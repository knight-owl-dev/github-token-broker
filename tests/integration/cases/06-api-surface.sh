#!/usr/bin/env bash
set -euo pipefail

#
# The broker driven with curl rather than the client, which cannot send a
# malformed body or an undeclared method. This is the surface no other case
# reaches. Nothing here mints, so no GitHub is needed.
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
BODY_FILE="${WORK}/response.body"
HEADER_FILE="${WORK}/response.headers"

CHECK_BODY="{\"host\":\"github.com\",\"repository\":\"${SERVED_REPOSITORY}\"}"
JSON_HEADER="Content-Type: application/json"

fixture_key "${WORK}/app.pem"
fixture_config "${CONFIG}" "${WORK}/app.pem" --socket "${SOCKET}"

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

if ! broker_wait_socket; then
  BROKER_OUTPUT="$(broker_log)"
  fail "the broker listens" "${BROKER_OUTPUT}"
  case_summary
  exit
fi

# --- health and check, neither of which needs a secret ------------------------

over_socket /health
assert_eq "the socket serves health" 200 "${RUN_STDOUT}"
assert_contains "and reports the allowlist size" "${RESPONSE_BODY}" '"repositories":1'

over_socket /v1/check -H "${JSON_HEADER}" --data "${CHECK_BODY}"
assert_eq "and answers a check" 200 "${RUN_STDOUT}"
assert_contains "which reports the ceiling" \
  "${RESPONSE_BODY}" '"permissions":"contents:write;pull_requests:write"'

# --- the surface itself -------------------------------------------------------

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
