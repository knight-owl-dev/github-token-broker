#!/usr/bin/env bash
set -euo pipefail

#
# One broker listening on both transports, driven with curl rather than the
# client, which cannot send a malformed body or an undeclared method.
#
# The property the two listeners exist for is only visible with both of them
# running: the credential is demanded on TCP and not on the socket. Around it,
# the surface no other case reaches. Nothing here mints, so no GitHub is needed.
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
CREDENTIAL_PATH="${WORK}/credential"
TCP_PORT="$(fixture_free_port)"

# The header carrying the credential, spelled out rather than read from the
# source: this is the wire contract, and a client the broker never sees has to
# hard-code it too.
CREDENTIAL_HEADER="X-GitHub-Token-Broker-Credential"

BODY_FILE="${WORK}/response.body"
HEADER_FILE="${WORK}/response.headers"

CHECK_BODY="{\"host\":\"github.com\",\"repository\":\"${SERVED_REPOSITORY}\"}"
JSON_HEADER="Content-Type: application/json"

fixture_key "${WORK}/app.pem"
fixture_credential "${CREDENTIAL_PATH}"
fixture_config "${CONFIG}" "${WORK}/app.pem" \
  --socket "${SOCKET}" \
  --tcp 127.0.0.1 "${TCP_PORT}" "${CREDENTIAL_PATH}"

# The broker strips the trailing newline `openssl rand` leaves, so the header
# has to carry the same value the broker compares against.
CREDENTIAL="$(< "${CREDENTIAL_PATH}")"

RESPONSE_BODY=""
RESPONSE_HEADERS=""

# request CURL_ARGUMENT... — RUN_STDOUT becomes the status code, RESPONSE_BODY
# and RESPONSE_HEADERS what came back. curl is never told to fail on a status,
# because the status is what is being asserted.
request() {
  run_capture curl --silent --show-error \
    --output "${BODY_FILE}" \
    --dump-header "${HEADER_FILE}" \
    --write-out '%{http_code}' \
    "$@"

  RESPONSE_BODY="$(< "${BODY_FILE}")"
  RESPONSE_HEADERS="$(< "${HEADER_FILE}")"
}

# over_socket PATH [CURL_ARGUMENT...]
over_socket() {
  local path="$1"
  shift
  request --unix-socket "${SOCKET}" "http://localhost${path}" "$@"
}

# over_tcp PATH [CURL_ARGUMENT...]
over_tcp() {
  local path="$1"
  shift
  request "http://127.0.0.1:${TCP_PORT}${path}" "$@"
}

# A check body whose repository name is COUNT characters long, so the size limit
# can be approached from either side without changing the request's shape.
padded_request() {
  local padding
  padding="$(printf '%*s' "$1" '' | tr ' ' 'a')"
  printf '{"host":"github.com","repository":"%s"}' "${padding}"
}

UNDER_LIMIT="$(padded_request 4000)"
OVER_LIMIT="$(padded_request 5000)"

broker_start "${CONFIG}" "${LOG}"

# The socket mode is applied once every listener is bound, so this waits for the
# TCP one too.
if ! broker_wait_socket; then
  BROKER_OUTPUT="$(broker_log)"
  fail "the broker listens on both transports" "${BROKER_OUTPUT}"
  case_summary
  exit
fi

# --- the credential follows the transport, not the route ----------------------

over_socket /health
assert_eq "the socket serves health with no credential" 200 "${RUN_STDOUT}"
assert_contains "and reports the allowlist size" "${RESPONSE_BODY}" '"repositories":1'

over_socket /v1/check -H "${JSON_HEADER}" --data "${CHECK_BODY}"
assert_eq "and answers a check with none either" 200 "${RUN_STDOUT}"

over_tcp /health
assert_eq "the same broker refuses health over TCP" 401 "${RUN_STDOUT}"
assert_contains "with the unversioned error body" "${RESPONSE_BODY}" '"error":"unauthorized"'

over_tcp /v1/check -H "${JSON_HEADER}" --data "${CHECK_BODY}"
assert_eq "and refuses the check the socket just answered" 401 "${RUN_STDOUT}"

over_tcp /health -H "${CREDENTIAL_HEADER}: ${CREDENTIAL}"
assert_eq "the credential opens health over TCP" 200 "${RUN_STDOUT}"

over_tcp /v1/check -H "${CREDENTIAL_HEADER}: ${CREDENTIAL}" \
  -H "${JSON_HEADER}" --data "${CHECK_BODY}"
assert_eq "and the check with it" 200 "${RUN_STDOUT}"
assert_contains "which reports the ceiling" \
  "${RESPONSE_BODY}" '"permissions":"contents:write;pull_requests:write"'

# The credential was accepted twice above, so it did reach the broker and its
# absence from the log is the logging's doing.
GATE_LOG="$(broker_log)"
assert_contains "a rejection is recorded" \
  "${GATE_LOG}" "without a valid client credential"
assert_not_contains "without the credential in it" "${GATE_LOG}" "${CREDENTIAL}"

# --- the surface itself -------------------------------------------------------
#
# Over the socket, where no credential stands between the request and the route.

over_socket /v1/check
assert_eq "a check that is not a POST is not allowed" 405 "${RUN_STDOUT}"

over_socket /health --request POST
assert_eq "health does not accept a POST" 405 "${RUN_STDOUT}"

over_socket /v1/nothing
assert_eq "a route the broker does not map is not found" 404 "${RUN_STDOUT}"

over_socket /v1/check -H "Content-Type: text/plain" --data "${CHECK_BODY}"
assert_eq "a content type that is not JSON is refused" 415 "${RUN_STDOUT}"
assert_contains "and says what was expected" \
  "${RESPONSE_BODY}" "expected application/json"

over_socket /v1/check -H "${JSON_HEADER}" --data '{"host":'
assert_eq "a truncated body is malformed" 400 "${RUN_STDOUT}"
assert_contains "and stays coarse about it" "${RESPONSE_BODY}" '"error":"malformed request"'

over_socket /v1/check -H "${JSON_HEADER}" \
  --data "{\"host\":\"github.com\",\"repository\":\"${SERVED_REPOSITORY}\",\"surprise\":true}"
assert_eq "a member the contract does not declare is malformed" 400 "${RUN_STDOUT}"

# The same request twice, differing only in length: a 400 on the larger one is
# the limit, since the smaller one was read far enough to be judged on content.
over_socket /v1/check -H "${JSON_HEADER}" --data "${UNDER_LIMIT}"
assert_eq "a body under the limit is read and refused on its contents" \
  403 "${RUN_STDOUT}"

over_socket /v1/check -H "${JSON_HEADER}" --data "${OVER_LIMIT}"
assert_eq "and one over the limit never gets that far" 400 "${RUN_STDOUT}"

# --- what the responses carry -------------------------------------------------

over_socket /health
assert_contains "a response declares itself JSON" "${RESPONSE_HEADERS}" "application/json"
assert_not_contains "and names no server" "${RESPONSE_HEADERS}" "Server:"

case_summary
