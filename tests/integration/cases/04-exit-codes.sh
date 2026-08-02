#!/usr/bin/env bash
set -euo pipefail

#
# The exit statuses are a contract a calling script depends on, so each one is
# reached here through the real executable.
#
# 78 from the client needs a GitHub that answers, and waits for the mint case.
# The breadth of configuration rejections stays in the unit suite; what is
# proven here is that a rejection becomes an exit status at all.
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

SOCKET="${WORK}/run/broker.sock"
CONFIG="${WORK}/config.json"
LOG="${WORK}/broker.log"
ENDPOINT="unix://${SOCKET}"

fixture_key "${WORK}/app.pem"
fixture_config "${CONFIG}" "${WORK}/app.pem" --socket "${SOCKET}"

# cli ARGUMENT... — the client, pointed at this case's broker.
cli() {
  run_capture env "GITHUB_TOKEN_BROKER_ENDPOINT=${ENDPOINT}" github-token "$@"
}

broker() {
  run_capture github-token-broker "$@"
}

# --- 64, the command line or the environment was wrong ------------------------

cli_no_endpoint() {
  run_capture env -u GITHUB_TOKEN_BROKER_ENDPOINT github-token "$@"
}

cli_no_endpoint check "${SERVED_REPOSITORY}"
assert_eq "an unset endpoint is a usage error" 64 "${RUN_STATUS}"
assert_contains "and names the variable" "${RUN_STDERR}" "GITHUB_TOKEN_BROKER_ENDPOINT"

run_capture github-token
assert_eq "no arguments is a usage error" 64 "${RUN_STATUS}"
assert_contains "and prints the usage" "${RUN_STDERR}" "usage:"

cli not-a-subcommand
assert_eq "an unknown subcommand is a usage error" 64 "${RUN_STATUS}"

cli check
assert_eq "check without a repository is a usage error" 64 "${RUN_STATUS}"

cli check "${SERVED_REPOSITORY}" extra
assert_eq "check with a second argument is a usage error" 64 "${RUN_STATUS}"

cli check "not a repository"
assert_eq "a malformed repository name is a usage error" 64 "${RUN_STATUS}"

cli gh "${SERVED_REPOSITORY}" status
assert_eq "gh without the -- separator is a usage error" 64 "${RUN_STATUS}"

run_capture env \
  "GITHUB_TOKEN_BROKER_ENDPOINT=http://127.0.0.1:1" \
  github-token check "${SERVED_REPOSITORY}"
assert_eq "an HTTP endpoint with no credential is a usage error" 64 "${RUN_STATUS}"

# --- 69, the broker could not be reached --------------------------------------

run_capture env \
  "GITHUB_TOKEN_BROKER_ENDPOINT=unix://${WORK}/absent.sock" \
  github-token check "${SERVED_REPOSITORY}"
assert_eq "an endpoint with nothing behind it is unavailable" 69 "${RUN_STATUS}"

# The wrapped message's wording comes from the platform's errno, so only the
# endpoint is asserted on.
assert_contains "and names the endpoint rather than its placeholder authority" \
  "${RUN_STDERR}" "unix://${WORK}/absent.sock"

# --- 0 and 77, against a running broker ---------------------------------------

broker_start "${CONFIG}" "${LOG}"

if broker_wait_socket; then
  cli version
  assert_eq "version succeeds" 0 "${RUN_STATUS}"
  assert_eq "and reports the version this was built from" \
    "${GTB_VERSION:-unknown}" "${RUN_STDOUT}"

  cli check "${SERVED_REPOSITORY}"
  assert_eq "a served repository succeeds" 0 "${RUN_STATUS}"

  cli check "${UNLISTED_REPOSITORY}"
  assert_eq "a repository the broker will not serve is not authorized" 77 "${RUN_STATUS}"
  assert_contains "and names the repository" "${RUN_STDERR}" "${UNLISTED_REPOSITORY}"
  assert_not_contains "rather than the credential it shares a status with" \
    "${RUN_STDERR}" "client credential"
  assert_empty "and no permissions are disclosed" "${RUN_STDOUT}"
else
  BROKER_OUTPUT="$(broker_log)"
  fail "the broker starts" "${BROKER_OUTPUT}"
fi

broker_stop

# --- 77 over TCP, a credential the broker rejects -----------------------------
#
# Shares its status with the refused repository above, so the message is
# asserted to carry which one it was.

TCP_CONFIG="${WORK}/tcp-config.json"
TCP_LOG="${WORK}/tcp-broker.log"
TCP_PORT="$(fixture_free_port)"
fixture_credential "${WORK}/credential"
fixture_config "${TCP_CONFIG}" "${WORK}/app.pem" \
  --tcp 127.0.0.1 "${TCP_PORT}" "${WORK}/credential"

broker_start "${TCP_CONFIG}" "${TCP_LOG}"

if broker_wait_log 'Now listening on'; then
  run_capture env \
    "GITHUB_TOKEN_BROKER_ENDPOINT=http://127.0.0.1:${TCP_PORT}" \
    "GITHUB_TOKEN_BROKER_CREDENTIAL_FILE=${WORK}/credential" \
    github-token check "${SERVED_REPOSITORY}"
  assert_eq "the right credential succeeds over TCP" 0 "${RUN_STATUS}"

  run_capture env \
    "GITHUB_TOKEN_BROKER_ENDPOINT=http://127.0.0.1:${TCP_PORT}" \
    "GITHUB_TOKEN_BROKER_CREDENTIAL=wrong-credential-long-enough-to-send" \
    github-token check "${SERVED_REPOSITORY}"
  assert_eq "a rejected credential is not authorized" 77 "${RUN_STATUS}"
  assert_contains "and names the credential rather than the repository" \
    "${RUN_STDERR}" "client credential"
  assert_empty "and no permissions are disclosed" "${RUN_STDOUT}"
else
  BROKER_OUTPUT="$(broker_log)"
  fail "the broker listens on TCP" "${BROKER_OUTPUT}"
fi

broker_stop

# --- the broker's own statuses ------------------------------------------------

broker
assert_eq "the broker with no arguments is a usage error" 64 "${RUN_STATUS}"
assert_contains "and prints the usage" "${RUN_STDERR}" "usage:"

broker --config
assert_eq "a --config with no path is a usage error" 64 "${RUN_STATUS}"

broker --config "${CONFIG}" extra
assert_eq "a trailing argument is a usage error" 64 "${RUN_STATUS}"

broker --not-config "${CONFIG}"
assert_eq "an unknown flag is a usage error" 64 "${RUN_STATUS}"

broker --config relative/config.json
assert_eq "a relative configuration path is a configuration error" 78 "${RUN_STATUS}"

broker --config "${WORK}/absent.json"
assert_eq "an unreadable configuration is a configuration error" 78 "${RUN_STATUS}"
assert_contains "and says so" "${RUN_STDERR}" "configuration error"

# One per validator.
reject() {
  local name="$1"
  local description="$2"
  local path="${WORK}/${name}.json"
  cat > "${path}"

  broker --config "${path}"
  assert_eq "${description}" 78 "${RUN_STATUS}"
}

reject unknown-member "an unrecognized member is refused" << JSON
{
  "github_host": "github.com",
  "app_id": 1,
  "installation_id": 2,
  "private_key_path": "${WORK}/app.pem",
  "surprise": true,
  "listen": { "unix_socket": "${SOCKET}" },
  "repositories": { "${SERVED_REPOSITORY}": { "permissions": { "contents": "read" } } }
}
JSON

reject empty-allowlist "an empty allowlist is refused" << JSON
{
  "github_host": "github.com",
  "app_id": 1,
  "installation_id": 2,
  "private_key_path": "${WORK}/app.pem",
  "listen": { "unix_socket": "${SOCKET}" },
  "repositories": {}
}
JSON

reject workflows "the workflows permission is refused" << JSON
{
  "github_host": "github.com",
  "app_id": 1,
  "installation_id": 2,
  "private_key_path": "${WORK}/app.pem",
  "listen": { "unix_socket": "${SOCKET}" },
  "repositories": { "${SERVED_REPOSITORY}": { "permissions": { "workflows": "write" } } }
}
JSON

reject public-listener "a listen address beyond this host's network is refused" << JSON
{
  "github_host": "github.com",
  "app_id": 1,
  "installation_id": 2,
  "private_key_path": "${WORK}/app.pem",
  "listen": {
    "tcp": {
      "address": "0.0.0.0",
      "port": 8765,
      "client_credential_path": "${WORK}/credential"
    }
  },
  "repositories": { "${SERVED_REPOSITORY}": { "permissions": { "contents": "read" } } }
}
JSON

reject remote-api "a plaintext api_url that is not loopback is refused" << JSON
{
  "github_host": "github.com",
  "api_url": "http://api.example.com",
  "app_id": 1,
  "installation_id": 2,
  "private_key_path": "${WORK}/app.pem",
  "listen": { "unix_socket": "${SOCKET}" },
  "repositories": { "${SERVED_REPOSITORY}": { "permissions": { "contents": "read" } } }
}
JSON

# --- 66, the private key ------------------------------------------------------
#
# Paired with the same configuration once the key is there. Without that, 66
# would pass for any startup failure at all.

LATE_KEY="${WORK}/late.pem"
LATE_CONFIG="${WORK}/late-config.json"
LATE_SOCKET="${WORK}/run/late.sock"
fixture_config "${LATE_CONFIG}" "${LATE_KEY}" --socket "${LATE_SOCKET}"

broker --config "${LATE_CONFIG}"
assert_eq "an unreadable private key is its own status" 66 "${RUN_STATUS}"
assert_contains "and names the key rather than the configuration" \
  "${RUN_STDERR}" "private key"

# The false green this replaced: the broker used to serve /health and answer
# check on a key it could not read.
if [[ -e "${LATE_SOCKET}" ]]; then
  fail "and nothing is left listening" "${LATE_SOCKET} is still there"
else
  pass "and nothing is left listening"
fi

fixture_key "${LATE_KEY}"
broker_start "${LATE_CONFIG}" "${WORK}/late-broker.log"

if broker_wait_socket; then
  pass "the same configuration starts once the key is readable"
else
  BROKER_OUTPUT="$(broker_log)"
  fail "the same configuration starts once the key is readable" "${BROKER_OUTPUT}"
fi

broker_stop

# --- 78, a listener that cannot bind ------------------------------------------
#
# The TCP counterpart of an occupied socket path. Kestrel binds when the host
# starts rather than when it is built, so this used to abort with a stack trace.

BUSY_CONFIG="${WORK}/busy-config.json"
BUSY_PORT="$(fixture_free_port)"
fixture_config "${BUSY_CONFIG}" "${WORK}/app.pem" \
  --tcp 127.0.0.1 "${BUSY_PORT}" "${WORK}/credential"

broker_start "${BUSY_CONFIG}" "${WORK}/busy-broker.log"

if broker_wait_log 'Now listening on'; then
  broker --config "${BUSY_CONFIG}"
  assert_eq "a port already in use is a configuration error" 78 "${RUN_STATUS}"
  assert_contains "and reports rather than aborting" "${RUN_STDERR}" "configuration error"
  assert_contains "naming the address it could not have" "${RUN_STDERR}" "${BUSY_PORT}"
else
  BROKER_OUTPUT="$(broker_log)"
  fail "the first broker binds the port" "${BROKER_OUTPUT}"
fi

broker_stop

# --- 0, a broker asked to stop ------------------------------------------------

broker_start "${CONFIG}" "${LOG}"

if broker_wait_socket; then
  broker_terminate
  assert_eq "a broker told to stop exits cleanly" 0 "${BROKER_STATUS}"

  if [[ -e "${SOCKET}" ]]; then
    fail "and takes its socket with it" "${SOCKET} is still there"
  else
    pass "and takes its socket with it"
  fi
else
  BROKER_OUTPUT="$(broker_log)"
  fail "the broker starts" "${BROKER_OUTPUT}"
fi

case_summary
