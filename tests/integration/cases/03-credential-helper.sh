#!/usr/bin/env bash
set -euo pipefail

#
# Git's own implementation drives the helper, because the wire format is Git's
# and a hand-written reader agrees with itself by construction.
#
# What Git cannot show is the exit status: it reads a helper's output and
# ignores what the helper returned, so declining and failing look identical
# through `git credential fill`. The statuses are asserted by calling the helper
# directly, which is also the only way to see that a decline said nothing.
#

CASE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SUITE_DIR="$(cd "${CASE_DIR}/.." && pwd)"

# shellcheck source=../lib/assert.sh
source "${SUITE_DIR}/lib/assert.sh"
# shellcheck source=../lib/broker.sh
source "${SUITE_DIR}/lib/broker.sh"
# shellcheck source=../lib/fixture.sh
source "${SUITE_DIR}/lib/fixture.sh"

if ! command -v git > /dev/null 2>&1; then
  case_skip "git drives this case"
fi

WORK="$(case_workspace)"
trap 'broker_stop; fixture_github_stop; rm -rf "${WORK}"' EXIT

SOCKET="${WORK}/run/broker.sock"
CONFIG="${WORK}/config.json"
LOG="${WORK}/broker.log"
TOKEN="ghs-integration-token"

fixture_key "${WORK}/app.pem"

if ! fixture_github "${WORK}" --token "${TOKEN}"; then
  fail "the canned GitHub starts"
  case_summary
  exit
fi

fixture_config "${CONFIG}" "${WORK}/app.pem" \
  --socket "${SOCKET}" --api-url "${FAKE_GITHUB_URL}"

# A private configuration, so the machine running this keeps its own.
export GIT_CONFIG_GLOBAL="${WORK}/gitconfig"
export GIT_CONFIG_SYSTEM=/dev/null
export GIT_TERMINAL_PROMPT=0
export GITHUB_TOKEN_BROKER_ENDPOINT="unix://${SOCKET}"

git config --global credential.helper '!github-token credential'
git config --global credential.https://github.com.useHttpPath true

# fill REPOSITORY_PATH — what Git makes of a credential request for a URL.
fill() {
  RUN_INPUT="protocol=https
host=github.com
path=$1

"
  run_capture git credential fill
}

# helper OPERATION REQUEST — the helper alone, where the exit status survives.
helper() {
  local operation="$1"
  shift
  RUN_INPUT="$*"
  run_capture github-token credential "${operation}"
}

broker_start "${CONFIG}" "${LOG}"

if ! broker_wait_socket; then
  BROKER_OUTPUT="$(broker_log)"
  fail "the broker starts" "${BROKER_OUTPUT}"
  case_summary
  exit
fi

# --- a served repository ------------------------------------------------------

fill "${SERVED_REPOSITORY}.git"
assert_eq "git accepts the credential" 0 "${RUN_STATUS}"
assert_contains "and is given the installation username" \
  "${RUN_STDOUT}" "username=x-access-token"
assert_contains "and the minted token as the password" \
  "${RUN_STDOUT}" "password=${TOKEN}"

# The fake answers 401 to a JWT it will not accept, so a mint proves one was
# sent. Asserted anyway, since a check that never runs also never complains.
GITHUB_REQUESTS="$(< "${FAKE_GITHUB_LOG}")"
assert_contains "having proved which App asked" \
  "${GITHUB_REQUESTS}" "jwt=ok iss=${FIXTURE_APP_ID}"

# --- a repository the broker does not serve -----------------------------------
#
# Git carries on unauthenticated, which for a public upstream is right. With
# prompts disabled that surfaces as Git failing to find a username.

fill "${UNLISTED_REPOSITORY}.git"
assert_eq "git is left without a credential" 128 "${RUN_STATUS}"
assert_contains "and says which repository it wanted one for" \
  "${RUN_STDERR}" "${UNLISTED_REPOSITORY}"
assert_not_contains "while the helper stays quiet" "${RUN_STDERR}" "github-token:"

helper get "protocol=https
host=github.com
path=${UNLISTED_REPOSITORY}.git
"
assert_eq "and declining is a success" 0 "${RUN_STATUS}"
assert_empty "with nothing written to Git" "${RUN_STDOUT}"

# --- requests the helper does not serve ---------------------------------------

helper get "protocol=https
host=gitlab.example.com
path=owner/repository.git
"
assert_eq "another host is declined" 0 "${RUN_STATUS}"
assert_empty "silently" "${RUN_STDOUT}"

helper get "protocol=ssh
host=github.com
path=${SERVED_REPOSITORY}.git
"
assert_eq "another protocol is declined" 0 "${RUN_STATUS}"
assert_empty "silently" "${RUN_STDOUT}"

# --- a request that cannot name a repository ----------------------------------
#
# Reported rather than declined: it means useHttpPath is off, and staying quiet
# would leave one repository's token satisfying a request for another.

helper get "protocol=https
host=github.com
"
assert_eq "a request with no path still succeeds" 0 "${RUN_STATUS}"
assert_empty "without a credential" "${RUN_STDOUT}"
assert_contains "and names the setting to turn on" "${RUN_STDERR}" "useHttpPath"

# --- store and erase ----------------------------------------------------------

BEFORE="$(fixture_github_requests)"

helper store "protocol=https
host=github.com
path=${SERVED_REPOSITORY}.git
username=x-access-token
password=whatever
"
assert_eq "store succeeds" 0 "${RUN_STATUS}"
assert_empty "and answers nothing" "${RUN_STDOUT}"

helper erase "protocol=https
host=github.com
path=${SERVED_REPOSITORY}.git
"
assert_eq "erase succeeds" 0 "${RUN_STATUS}"

AFTER="$(fixture_github_requests)"
assert_eq "and neither mints" "${BEFORE}" "${AFTER}"

# --- the broker gone ----------------------------------------------------------
#
# Loud, unlike a decline: a private repository degrading into an anonymous
# failure is the confusing outcome this avoids.

broker_stop

helper get "protocol=https
host=github.com
path=${SERVED_REPOSITORY}.git
"
assert_eq "an unreachable broker is unavailable" 69 "${RUN_STATUS}"
assert_empty "with no credential written" "${RUN_STDOUT}"
assert_contains "and a diagnostic" "${RUN_STDERR}" "github-token:"

case_summary
