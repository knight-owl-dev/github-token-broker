#!/usr/bin/env bash
set -euo pipefail

#
# What the broker leaves on disk, and what it refuses to bind.
#
# Unit tests cover the UnixFileMode arithmetic and the ConfigurationException
# wording. Neither observes a real socket: this does.
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
trap 'broker_stop; rm -rf "${WORK}"' EXIT

scenario() {
  SCENARIO_ROOT="${WORK}/$1"
  SCENARIO_SOCKET_DIR="${SCENARIO_ROOT}/run"
  SCENARIO_SOCKET="${SCENARIO_SOCKET_DIR}/broker.sock"
  SCENARIO_CONFIG="${SCENARIO_ROOT}/config.json"
  SCENARIO_LOG="${SCENARIO_ROOT}/broker.log"
  mkdir -p "${SCENARIO_ROOT}"
  fixture_key "${SCENARIO_ROOT}/app.pem"
}

fail_with_log() {
  local text
  text="$(broker_log)"
  fail "$1" "${text}"
}

# --- the mode the operator asked for reaches the socket -----------------------

scenario default-mode
fixture_config "${SCENARIO_CONFIG}" "${SCENARIO_ROOT}/app.pem" --socket "${SCENARIO_SOCKET}"
broker_start "${SCENARIO_CONFIG}" "${SCENARIO_LOG}"

if broker_wait_socket; then
  pass "an unconfigured socket mode starts"
  assert_mode "an unconfigured socket mode is 0600 on disk" "600" "${SCENARIO_SOCKET}"
  STARTUP_LOG="$(broker_log)"
  assert_contains "the log names the mode it applied" "${STARTUP_LOG}" "with mode 0600"
else
  fail_with_log "an unconfigured socket mode starts"
fi
broker_stop

scenario group-mode
fixture_config "${SCENARIO_CONFIG}" "${SCENARIO_ROOT}/app.pem" \
  --socket "${SCENARIO_SOCKET}" --socket-mode 0660
broker_start "${SCENARIO_CONFIG}" "${SCENARIO_LOG}"

if broker_wait_socket; then
  assert_mode "a configured 0660 reaches the socket" "660" "${SCENARIO_SOCKET}"
else
  fail_with_log "a configured 0660 reaches the socket"
fi
broker_stop

# --- the directory is the outer half of the same control ----------------------

scenario absent-directory
fixture_config "${SCENARIO_CONFIG}" "${SCENARIO_ROOT}/app.pem" --socket "${SCENARIO_SOCKET}"
rmdir "${SCENARIO_SOCKET_DIR}"
broker_start "${SCENARIO_CONFIG}" "${SCENARIO_LOG}"

if broker_wait_socket; then
  assert_mode "an absent socket directory is created 0700" "700" "${SCENARIO_SOCKET_DIR}"
else
  fail_with_log "an absent socket directory is created 0700"
fi
broker_stop

scenario group-writable-directory
fixture_config "${SCENARIO_CONFIG}" "${SCENARIO_ROOT}/app.pem" --socket "${SCENARIO_SOCKET}"
chmod 0770 "${SCENARIO_SOCKET_DIR}"
run_capture github-token-broker --config "${SCENARIO_CONFIG}"

assert_eq "a group-writable socket directory refuses startup" 78 "${RUN_STATUS}"
assert_contains "and says why" "${RUN_STDERR}" "writable beyond its owner"

scenario sticky-directory
fixture_config "${SCENARIO_CONFIG}" "${SCENARIO_ROOT}/app.pem" --socket "${SCENARIO_SOCKET}"
chmod 1777 "${SCENARIO_SOCKET_DIR}"
broker_start "${SCENARIO_CONFIG}" "${SCENARIO_LOG}"

if broker_wait_socket; then
  pass "a sticky socket directory starts"
else
  fail_with_log "a sticky socket directory starts"
fi
broker_stop

# --- an occupied socket path is refused unless it holds a dead socket ---------
#
# A connection cannot tell a dead socket from an ordinary file, so the file's
# type decides; see UnixSocketPreparation.

scenario regular-file
fixture_config "${SCENARIO_CONFIG}" "${SCENARIO_ROOT}/app.pem" --socket "${SCENARIO_SOCKET}"
echo "important" > "${SCENARIO_SOCKET}"
run_capture github-token-broker --config "${SCENARIO_CONFIG}"

assert_eq "a regular file at the socket path refuses startup" 78 "${RUN_STATUS}"
assert_contains "and says it is not a socket" "${RUN_STDERR}" "not a socket"

if [[ -f "${SCENARIO_SOCKET}" ]]; then
  REGULAR_FILE_CONTENTS="$(cat "${SCENARIO_SOCKET}")"
  assert_eq "and leaves the file untouched" "important" "${REGULAR_FILE_CONTENTS}"
else
  fail "and leaves the file untouched" "the broker deleted it"
fi

scenario symbolic-link
fixture_config "${SCENARIO_CONFIG}" "${SCENARIO_ROOT}/app.pem" --socket "${SCENARIO_SOCKET}"
touch "${SCENARIO_ROOT}/target"
ln -s "${SCENARIO_ROOT}/target" "${SCENARIO_SOCKET}"
run_capture github-token-broker --config "${SCENARIO_CONFIG}"

assert_eq "a symbolic link at the socket path refuses startup" 78 "${RUN_STATUS}"
assert_contains "and says so" "${RUN_STDERR}" "symbolic link"

scenario live-listener
fixture_config "${SCENARIO_CONFIG}" "${SCENARIO_ROOT}/app.pem" --socket "${SCENARIO_SOCKET}"
broker_start "${SCENARIO_CONFIG}" "${SCENARIO_LOG}"

if broker_wait_socket; then
  run_capture github-token-broker --config "${SCENARIO_CONFIG}"
  assert_eq "a second broker on a live socket refuses startup" 78 "${RUN_STATUS}"
  assert_contains "and names the listener" "${RUN_STDERR}" "already listening"
else
  fail_with_log "a second broker on a live socket refuses startup"
fi
broker_stop

scenario orphaned-socket
fixture_config "${SCENARIO_CONFIG}" "${SCENARIO_ROOT}/app.pem" --socket "${SCENARIO_SOCKET}"
broker_start "${SCENARIO_CONFIG}" "${SCENARIO_LOG}"

if broker_wait_socket; then
  broker_kill
  if [[ -S "${SCENARIO_SOCKET}" ]]; then
    broker_start "${SCENARIO_CONFIG}" "${SCENARIO_LOG}.restart"
    if broker_wait_socket; then
      pass "a socket a killed broker left is reclaimed"
      if broker_wait_log "Removed the dead socket at"; then
        pass "and the log says so"
      else
        fail_with_log "and the log says so"
      fi
    else
      fail_with_log "a socket a killed broker left is reclaimed"
    fi
    broker_stop
  else
    fail "an unclean shutdown leaves the socket behind" "nothing at ${SCENARIO_SOCKET}"
  fi
else
  fail_with_log "a socket a killed broker left is reclaimed"
fi

case_summary
