#!/usr/bin/env bash
set -euo pipefail

#
# The mint path from the far side: what the GitHub CLI is handed, how often
# GitHub is actually called, and what a refusal from GitHub looks like to a
# caller.
#
# The credential helper's end of this is case 03. What is left is the launcher,
# whose whole job is the child's environment, and the cache, which is only
# observable by counting what reached GitHub.
#

CASE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SUITE_DIR="$(cd "${CASE_DIR}/.." && pwd)"

# shellcheck source=../lib/assert.sh
source "${SUITE_DIR}/lib/assert.sh"
# shellcheck source=../lib/broker.sh
source "${SUITE_DIR}/lib/broker.sh"
# shellcheck source=../lib/fixture.sh
source "${SUITE_DIR}/lib/fixture.sh"

WORK="$(case_workspace)"
trap 'broker_stop; fixture_github_stop; rm -rf "${WORK}"' EXIT

TOKEN="ghs-integration-token"
fixture_key "${WORK}/app.pem"

# A second allowlisted repository, carrying an installation of its own. Its owner
# is taken from the first, since the canned GitHub answers under one owner and a
# grant naming another would fail the broker's own narrowing check.
ELSEWHERE_REPOSITORY="${SERVED_REPOSITORY%/*}/elsewhere-repo"
ELSEWHERE_INSTALLATION=345678

fail_with_log() {
  local text
  text="$(broker_log)"
  fail "$1" "${text}"
}

# The route the most recent mint was sent to.
last_mint_path() {
  grep -oE '/app/installations/[0-9]+/access_tokens' "${FAKE_GITHUB_LOG}" | tail -n 1
}

# start_broker STATUSES — a canned GitHub answering one comma-separated status per
# minting request, and a broker pointed at it. A single status is a run of one, so
# every case reads the same whether or not it needs the broker to attempt twice.
# Each call gets a fresh port, so the configuration is rewritten rather than reused.
start_broker() {
  local statuses="$1"
  local name="status-${statuses//,/-}"

  SCENARIO_ROOT="${WORK}/${name}"
  SCENARIO_SOCKET="${SCENARIO_ROOT}/run/broker.sock"
  mkdir -p "${SCENARIO_ROOT}"

  if ! fixture_github "${SCENARIO_ROOT}" \
    --status-sequence "${statuses}" --token "${TOKEN}"; then
    return 1
  fi

  fixture_config "${SCENARIO_ROOT}/config.json" "${WORK}/app.pem" \
    --socket "${SCENARIO_SOCKET}" --api-url "${FAKE_GITHUB_URL}"

  ENDPOINT="unix://${SCENARIO_SOCKET}"
  broker_start "${SCENARIO_ROOT}/config.json" "${SCENARIO_ROOT}/broker.log"
  broker_wait_socket
}

# A broker whose allowlist spreads two repositories over two installations. The
# configuration is written here rather than by fixture_config, which allowlists
# one repository on the default installation and is what every other case wants.
start_broker_installations() {
  SCENARIO_ROOT="${WORK}/installations"
  SCENARIO_SOCKET="${SCENARIO_ROOT}/run/broker.sock"
  mkdir -p "$(dirname "${SCENARIO_SOCKET}")"

  if ! fixture_github "${SCENARIO_ROOT}" --status-sequence 201 --token "${TOKEN}"; then
    return 1
  fi

  cat > "${SCENARIO_ROOT}/config.json" << CONFIG
{
  "github_host": "github.com",
  "api_url": "${FAKE_GITHUB_URL}",
  "app_id": ${FIXTURE_APP_ID},
  "installation_id": ${FIXTURE_INSTALLATION_ID},
  "private_key_path": "${WORK}/app.pem",
  "listen": {
    "unix_socket": {
      "path": "${SCENARIO_SOCKET}"
    }
  },
  "repositories": {
    "${SERVED_REPOSITORY}": {
      "permissions": {
        "contents": "write",
        "pull_requests": "write"
      }
    },
    "${ELSEWHERE_REPOSITORY}": {
      "installation_id": ${ELSEWHERE_INSTALLATION},
      "permissions": {
        "contents": "write",
        "pull_requests": "write"
      }
    }
  }
}
CONFIG

  ENDPOINT="unix://${SCENARIO_SOCKET}"
  broker_start "${SCENARIO_ROOT}/config.json" "${SCENARIO_ROOT}/broker.log"
  broker_wait_socket
}

# A stand-in for the GitHub CLI that records what it was handed. The launcher
# sets three variables and passes arguments through; everything else about the
# child is inherited, which is why the log is written where the case can read it.
GH_STUB="${WORK}/gh-stub"
GH_STUB_LOG="${WORK}/gh-stub.log"
export GH_STUB_LOG

cat > "${GH_STUB}" << 'STUB'
#!/usr/bin/env bash
{
  echo "argv=$*"
  echo "GH_TOKEN=${GH_TOKEN-<unset>}"
  echo "GH_REPO=${GH_REPO-<unset>}"
  echo "GITHUB_TOKEN=${GITHUB_TOKEN-<unset>}"
} > "${GH_STUB_LOG}"
exit 42
STUB
chmod +x "${GH_STUB}"

# --- what the GitHub CLI is handed -------------------------------------------

if start_broker 201; then
  # GITHUB_TOKEN is set here so its removal is a change the launcher made,
  # rather than something that was never there.
  run_capture env \
    "GITHUB_TOKEN_BROKER_ENDPOINT=${ENDPOINT}" \
    "GITHUB_TOKEN_BROKER_GH=${GH_STUB}" \
    "GITHUB_TOKEN=inherited-and-should-not-survive" \
    github-token gh "${SERVED_REPOSITORY}" -- pr create --fill

  assert_eq "the child's status is returned verbatim" 42 "${RUN_STATUS}"

  CHILD="$(< "${GH_STUB_LOG}")"
  assert_contains "arguments after -- reach the child" "${CHILD}" "argv=pr create --fill"
  assert_contains "the minted token arrives as GH_TOKEN" "${CHILD}" "GH_TOKEN=${TOKEN}"
  assert_contains "the child is pinned to the repository" \
    "${CHILD}" "GH_REPO=${SERVED_REPOSITORY}"
  assert_contains "and an inherited GITHUB_TOKEN is removed" \
    "${CHILD}" "GITHUB_TOKEN=<unset>"

  # --- the cache ---------------------------------------------------------------
  #
  # Only countable at the far end: a second request that mints again is
  # indistinguishable from one that did not, from the client's side.

  BEFORE="$(fixture_github_requests)"
  assert_eq "the first request did reach GitHub" 1 "${BEFORE}"

  run_capture env \
    "GITHUB_TOKEN_BROKER_ENDPOINT=${ENDPOINT}" \
    "GITHUB_TOKEN_BROKER_GH=${GH_STUB}" \
    github-token gh "${SERVED_REPOSITORY}" -- run list
  assert_eq "a second request succeeds" 42 "${RUN_STATUS}"

  AFTER="$(fixture_github_requests)"
  assert_eq "and is served without calling GitHub again" "${BEFORE}" "${AFTER}"

  SECOND_CHILD="$(< "${GH_STUB_LOG}")"
  assert_contains "with the same token" "${SECOND_CHILD}" "GH_TOKEN=${TOKEN}"

  # Asserted here rather than beside a refusal, where there would be no token
  # to leak and the absence would prove nothing.
  MINTING_LOG="$(broker_log)"
  assert_contains "the broker recorded the mint" "${MINTING_LOG}" "Minted a token"
  assert_not_contains "without the token in it" "${MINTING_LOG}" "${TOKEN}"
else
  fail_with_log "the broker starts against a canned GitHub"
fi

broker_stop
fixture_github_stop

# --- the real GitHub CLI -------------------------------------------------------
#
# The stub proves what the launcher passes; only the real binary proves the
# launcher can find and start one.

if ! command -v gh > /dev/null 2>&1; then
  skip_note="the GitHub CLI is not installed"
else
  skip_note=""
fi

if [[ -z "${skip_note}" ]] && start_broker 201; then
  run_capture env "GITHUB_TOKEN_BROKER_ENDPOINT=${ENDPOINT}" \
    github-token gh "${SERVED_REPOSITORY}" -- --version

  assert_eq "the real GitHub CLI runs" 0 "${RUN_STATUS}"
  assert_contains "and reports itself" "${RUN_STDOUT}" "gh version"
  broker_stop
  fixture_github_stop
elif [[ -n "${skip_note}" ]]; then
  report_skip "    " "the real GitHub CLI runs (${skip_note})"
fi

# --- which installation a repository is minted against -------------------------
#
# The allowlist binds each entry to one. Both are exercised against the same
# broker, so the two routes have to differ: a mint ignoring the entry would send
# each of them to the same place and fail one assertion or the other.

if start_broker_installations; then
  run_capture env \
    "GITHUB_TOKEN_BROKER_ENDPOINT=${ENDPOINT}" \
    "GITHUB_TOKEN_BROKER_GH=${GH_STUB}" \
    github-token gh "${SERVED_REPOSITORY}" -- pr create --fill

  assert_eq "an entry naming no installation mints" 42 "${RUN_STATUS}"

  ROUTE="$(last_mint_path)"
  assert_eq "against the top-level one" \
    "/app/installations/${FIXTURE_INSTALLATION_ID}/access_tokens" "${ROUTE}"

  run_capture env \
    "GITHUB_TOKEN_BROKER_ENDPOINT=${ENDPOINT}" \
    "GITHUB_TOKEN_BROKER_GH=${GH_STUB}" \
    github-token gh "${ELSEWHERE_REPOSITORY}" -- pr create --fill

  assert_eq "an entry carrying its own installation mints" 42 "${RUN_STATUS}"

  ROUTE="$(last_mint_path)"
  assert_eq "against that one instead" \
    "/app/installations/${ELSEWHERE_INSTALLATION}/access_tokens" "${ROUTE}"
else
  fail_with_log "the broker starts with two installations allowlisted"
fi

broker_stop
fixture_github_stop

# --- what GitHub's refusal costs the caller ------------------------------------
#
# The cases above mint against the same fixture, so a status here is the refusal.
# Which status is the whole of what a caller learns, every body being the same
# coarse message. See DIAGNOSTICS in github-token-broker(8) for the classes.

# 404: allowlisted here, absent from the App installation.
if start_broker 404; then
  run_capture env "GITHUB_TOKEN_BROKER_ENDPOINT=${ENDPOINT}" \
    github-token gh "${SERVED_REPOSITORY}" -- --version

  assert_eq "a repository the installation lacks is a configuration error" \
    78 "${RUN_STATUS}"
  assert_contains "and the client says to go and look" \
    "${RUN_STDERR}" "installation"
else
  fail_with_log "the broker starts against a refusing GitHub"
fi

broker_stop
fixture_github_stop

# 401: the App credential itself, which mostly waits on an operator.
if start_broker 401; then
  run_capture env "GITHUB_TOKEN_BROKER_ENDPOINT=${ENDPOINT}" \
    github-token gh "${SERVED_REPOSITORY}" -- --version

  assert_eq "a rejected App credential is a configuration error" 78 "${RUN_STATUS}"
  assert_contains "and the client says the credential may be why" \
    "${RUN_STDERR}" "credential"
else
  fail_with_log "the broker starts against a GitHub rejecting the credential"
fi

broker_stop
fixture_github_stop

# 500: GitHub reachable and unusable, the one class a retry can clear.
if start_broker 500; then
  run_capture env "GITHUB_TOKEN_BROKER_ENDPOINT=${ENDPOINT}" \
    github-token gh "${SERVED_REPOSITORY}" -- --version

  assert_eq "GitHub being unusable is retryable rather than a mistake" \
    75 "${RUN_STATUS}"
  assert_contains "and the client says retrying can help" "${RUN_STDERR}" "Retrying"
else
  fail_with_log "the broker starts against an unusable GitHub"
fi

broker_stop
fixture_github_stop

# 429: rate limited, which waiting clears.
if start_broker 429; then
  run_capture env "GITHUB_TOKEN_BROKER_ENDPOINT=${ENDPOINT}" \
    github-token gh "${SERVED_REPOSITORY}" -- --version

  assert_eq "a rate limit is retryable rather than a broker fault" 75 "${RUN_STATUS}"
  assert_contains "and the client says retrying can help" "${RUN_STDERR}" "Retrying"
else
  fail_with_log "the broker starts against a rate-limiting GitHub"
fi

broker_stop
fixture_github_stop

# 400: the broker and whatever answered it disagreeing about the API, which an
# operator reads rather than retries.
if start_broker 400; then
  run_capture env "GITHUB_TOKEN_BROKER_ENDPOINT=${ENDPOINT}" \
    github-token gh "${SERVED_REPOSITORY}" -- --version

  assert_eq "a status with no reading is the broker's to report" 70 "${RUN_STATUS}"
  assert_contains "and the client sends the reader to the log" \
    "${RUN_STDERR}" "broker log"
else
  fail_with_log "the broker starts against a GitHub answering nonsense"
fi

broker_stop
fixture_github_stop

# --- what a retry buys ---------------------------------------------------------
#
# The 500 case above is the same failure with no recovery behind it, so the two differ
# only in what GitHub does second.

if start_broker 500,201; then
  run_capture env "GITHUB_TOKEN_BROKER_ENDPOINT=${ENDPOINT}" \
    github-token gh "${SERVED_REPOSITORY}" -- --version

  assert_eq "a mint that failed for want of GitHub is attempted again" 0 "${RUN_STATUS}"

  RETRY_LOG="$(broker_log)"
  assert_contains "and the log records the attempt it made again" \
    "${RETRY_LOG}" "again"
else
  fail_with_log "the broker starts against a GitHub that recovers"
fi

broker_stop

case_summary
