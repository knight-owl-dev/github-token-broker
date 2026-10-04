#!/usr/bin/env bash
set -euo pipefail

#
# The case the socket's permission model exists for: whether another account can
# obtain a token comes down to the socket's mode and its group, and nothing here
# asserts that until a second account tries.
#
# Only the mode varies between the two scenarios. The socket directory is
# 0750 broker:brokers throughout — the shape a service unit deploys, and the one
# that leaves the socket itself as the deciding factor.
#

CASE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SUITE_DIR="$(cd "${CASE_DIR}/.." && pwd)"

# shellcheck source=../lib/assert.sh
source "${SUITE_DIR}/lib/assert.sh"
# shellcheck source=../lib/broker.sh
source "${SUITE_DIR}/lib/broker.sh"
# shellcheck source=../lib/fixture.sh
source "${SUITE_DIR}/lib/fixture.sh"

EFFECTIVE_UID="$(id -u)"
if [[ "${EFFECTIVE_UID}" -ne 0 ]]; then
  case_skip "dropping to another account needs root"
fi

if ! command -v runuser > /dev/null 2>&1; then
  case_skip "runuser is absent, so no account can be dropped to"
fi

for account in broker client outsider; do
  if ! id "${account}" > /dev/null 2>&1; then
    case_skip "the ${account} account is absent; prepare.sh creates it"
  fi
done

WORK="$(case_workspace)"
trap 'broker_stop; rm -rf "${WORK}"' EXIT

# Lays out a scenario the broker account owns, with a socket directory a member
# of brokers may traverse but not write.
scenario() {
  SCENARIO_ROOT="${WORK}/$1"
  SCENARIO_SOCKET_DIR="${SCENARIO_ROOT}/run"
  SCENARIO_SOCKET="${SCENARIO_SOCKET_DIR}/broker.sock"
  SCENARIO_CONFIG="${SCENARIO_ROOT}/config.json"
  SCENARIO_LOG="${SCENARIO_ROOT}/broker.log"

  mkdir -p "${SCENARIO_SOCKET_DIR}"
  fixture_key "${SCENARIO_ROOT}/app.pem"
  fixture_config "${SCENARIO_CONFIG}" "${SCENARIO_ROOT}/app.pem" \
    --socket "${SCENARIO_SOCKET}" "${@:2}"

  chown -R broker:brokers "${SCENARIO_ROOT}"
  chmod 0755 "${WORK}" "${SCENARIO_ROOT}"
  chmod 0750 "${SCENARIO_SOCKET_DIR}"

  # The group is the service unit's job in a real deployment, so it is set here
  # rather than by the broker.
  broker_start "${SCENARIO_CONFIG}" "${SCENARIO_LOG}" runuser -u broker -g brokers --
}

# check_as ACCOUNT — asks the broker whether it serves a repository, which
# needs no GitHub and mints nothing. Reaching the answer is the whole test.
check_as() {
  run_capture runuser -u "$1" -- env \
    "GITHUB_TOKEN_BROKER_ENDPOINT=unix://${SCENARIO_SOCKET}" \
    github-token check "${SERVED_REPOSITORY}"
}

fail_with_log() {
  local text
  text="$(broker_log)"
  fail "$1" "${text}"
}

# --- 0600: the socket is the broker's alone --------------------------------

scenario owner-only

if broker_wait_socket; then
  assert_mode "the socket is 0600" "600" "${SCENARIO_SOCKET}"

  check_as broker
  assert_eq "its own account reaches the broker" 0 "${RUN_STATUS}"
  assert_contains "and is told what it may do" "${RUN_STDOUT}" "${SERVED_REPOSITORY}"

  check_as client
  assert_eq "a group member is refused at 0600" 69 "${RUN_STATUS}"
  assert_empty "and receives no permissions" "${RUN_STDOUT}"

  check_as outsider
  assert_eq "an account outside the group is refused at 0600" 69 "${RUN_STATUS}"
else
  fail_with_log "the broker starts as an unprivileged account"
fi

broker_stop

# --- 0660: the group decides -----------------------------------------------

scenario group-shared --socket-mode 0660

if broker_wait_socket; then
  assert_mode "the socket is 0660" "660" "${SCENARIO_SOCKET}"
  assert_group "and carries the shared group" "brokers" "${SCENARIO_SOCKET}"

  check_as client
  assert_eq "a group member reaches the broker at 0660" 0 "${RUN_STATUS}"
  assert_contains "and is told what it may do" "${RUN_STDOUT}" "${SERVED_REPOSITORY}"

  check_as outsider
  assert_eq "an account outside the group is still refused" 69 "${RUN_STATUS}"
  assert_empty "and receives no permissions" "${RUN_STDOUT}"
else
  fail_with_log "the broker starts with a shared group"
fi

broker_stop

# --- a socket directory another account owns -------------------------------
#
# Its owner can unlink the socket whatever the mode, which 0750 leaves narrow
# enough to pass. Without the check the bind fails too, on a directory the
# broker cannot write, so the message is what proves the refusal.

SCENARIO_ROOT="${WORK}/foreign-owner"
SCENARIO_SOCKET_DIR="${SCENARIO_ROOT}/run"
SCENARIO_SOCKET="${SCENARIO_SOCKET_DIR}/broker.sock"
SCENARIO_CONFIG="${SCENARIO_ROOT}/config.json"

mkdir -p "${SCENARIO_SOCKET_DIR}"
fixture_key "${SCENARIO_ROOT}/app.pem"
fixture_config "${SCENARIO_CONFIG}" "${SCENARIO_ROOT}/app.pem" --socket "${SCENARIO_SOCKET}"
chown -R broker:brokers "${SCENARIO_ROOT}"
chown outsider:brokers "${SCENARIO_SOCKET_DIR}"
chmod 0755 "${SCENARIO_ROOT}"
chmod 0750 "${SCENARIO_SOCKET_DIR}"

run_capture runuser -u broker -g brokers -- github-token-broker --config "${SCENARIO_CONFIG}"
assert_eq "a socket directory another account owns refuses startup" 73 "${RUN_STATUS}"
assert_contains "and says whose it is" "${RUN_STDERR}" "belongs to another account"
if [[ -e "${SCENARIO_SOCKET}" ]]; then
  fail "and binds nothing" "a socket appeared at ${SCENARIO_SOCKET}"
else
  pass "and binds nothing"
fi

# --- a link to the socket directory another account owns ----------------------
#
# The directory behind it is the broker's, so only the link's own owner refuses
# it. Without that check the broker serves, which run_capture's timeout ends.

SCENARIO_ROOT="${WORK}/foreign-link"
SCENARIO_SOCKET_DIR="${SCENARIO_ROOT}/run"
SCENARIO_LINK="${SCENARIO_ROOT}/linked"
SCENARIO_CONFIG="${SCENARIO_ROOT}/config.json"

mkdir -p "${SCENARIO_SOCKET_DIR}"
ln -s "${SCENARIO_SOCKET_DIR}" "${SCENARIO_LINK}"
fixture_key "${SCENARIO_ROOT}/app.pem"
fixture_config "${SCENARIO_CONFIG}" "${SCENARIO_ROOT}/app.pem" --socket "${SCENARIO_LINK}/broker.sock"
chown -R broker:brokers "${SCENARIO_ROOT}"
chown -h outsider "${SCENARIO_LINK}"
chmod 0755 "${SCENARIO_ROOT}"
chmod 0750 "${SCENARIO_SOCKET_DIR}"

run_capture runuser -u broker -g brokers -- github-token-broker --config "${SCENARIO_CONFIG}"
assert_eq "a link to the socket directory another account owns refuses startup" 73 "${RUN_STATUS}"
assert_contains "and says whose it is" "${RUN_STDERR}" "belongs to another account"

case_summary
