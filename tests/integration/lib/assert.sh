# shellcheck shell=bash
#
# Assertions, and what differs between Linux and macOS.
#
# A case sources this, asserts, and ends with case_summary. Failures are
# counted rather than fatal, so one broken expectation does not hide the rest.
#
# The cases read these variables and call these functions; shellcheck cannot
# see that from inside the library.
# shellcheck disable=SC2034

ASSERT_LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=format.sh
source "${ASSERT_LIB_DIR}/format.sh"

ASSERT_UNAME="$(uname -s)"

# Exit status a case uses to report itself as not applicable.
CASE_SKIPPED=3

ASSERT_FAILURES=0
ASSERT_TOTAL=0

# Output of the last run_capture.
RUN_STATUS=0
RUN_STDOUT=""
RUN_STDERR=""

# Fed to the next run_capture on standard input, then cleared so it cannot reach
# a later command. Set it rather than redirecting into run_capture: the command
# runs in the background to be bounded by a timeout, and a background job takes
# its standard input from /dev/null unless something redirects it.
RUN_INPUT=""

# Longest a captured command may take before it is killed and reported as 124.
RUN_TIMEOUT_SECONDS=15

pass() {
  ASSERT_TOTAL=$((ASSERT_TOTAL + 1))
  report_ok "    " "$1"
}

fail() {
  ASSERT_TOTAL=$((ASSERT_TOTAL + 1))
  ASSERT_FAILURES=$((ASSERT_FAILURES + 1))
  report_fail "    " "$@"
}

# assert_eq DESCRIPTION EXPECTED ACTUAL
assert_eq() {
  if [[ "$2" == "$3" ]]; then
    pass "$1"
  else
    fail "$1" "expected: $2" "actual:   $3"
  fi
}

# assert_contains DESCRIPTION HAYSTACK NEEDLE
assert_contains() {
  if [[ "$2" == *"$3"* ]]; then
    pass "$1"
  else
    fail "$1" "expected to contain: $3" "actual: $2"
  fi
}

# assert_not_contains DESCRIPTION HAYSTACK NEEDLE
assert_not_contains() {
  if [[ "$2" != *"$3"* ]]; then
    pass "$1"
  else
    fail "$1" "expected not to contain: $3" "actual: $2"
  fi
}

# assert_empty DESCRIPTION VALUE
assert_empty() {
  if [[ -z "$2" ]]; then
    pass "$1"
  else
    fail "$1" "expected nothing" "actual: $2"
  fi
}

# assert_mode DESCRIPTION EXPECTED_OCTAL PATH
assert_mode() {
  if [[ ! -e "$3" ]]; then
    fail "$1" "no such path: $3"
    return
  fi

  local actual
  actual="$(file_mode "$3")"
  assert_eq "$1" "$2" "${actual}"
}

# assert_group DESCRIPTION EXPECTED_GROUP PATH
assert_group() {
  if [[ ! -e "$3" ]]; then
    fail "$1" "no such path: $3"
    return
  fi

  local actual
  actual="$(file_group "$3")"
  assert_eq "$1" "$2" "${actual}"
}

# Runs a command, capturing status, stdout, and stderr into RUN_*. Never fatal:
# a non-zero status is the thing under test, not an error here.
run_capture() {
  local out err input pid waited
  out="$(mktemp)"
  err="$(mktemp)"
  input="$(mktemp)"

  printf '%s' "${RUN_INPUT}" > "${input}"
  RUN_INPUT=""

  "$@" > "${out}" 2> "${err}" < "${input}" &
  pid=$!

  RUN_STATUS=0
  waited=0
  while kill -0 "${pid}" 2> /dev/null; do
    if ((waited >= RUN_TIMEOUT_SECONDS * 10)); then
      kill -KILL "${pid}" 2> /dev/null || true
      RUN_STATUS=124
      break
    fi
    sleep 0.1
    waited=$((waited + 1))
  done

  if [[ "${RUN_STATUS}" -ne 124 ]]; then
    set +e
    wait "${pid}"
    RUN_STATUS=$?
    set -e
  else
    wait "${pid}" 2> /dev/null || true
  fi

  RUN_STDOUT="$(< "${out}")"
  RUN_STDERR="$(< "${err}")"
  rm -f "${out}" "${err}" "${input}"
}

# wait_for SECONDS COMMAND... — polls until the command succeeds. `timeout` is
# GNU and absent on the macOS native path, so this is a plain loop.
wait_for() {
  local seconds="$1"
  shift

  local waited=0
  until "$@"; do
    if ((waited >= seconds * 10)); then
      return 1
    fi
    sleep 0.1
    waited=$((waited + 1))
  done
}

file_mode() {
  if [[ "${ASSERT_UNAME}" == "Darwin" ]]; then
    stat -f '%Lp' "$1"
  else
    stat -c '%a' "$1"
  fi
}

file_group() {
  if [[ "${ASSERT_UNAME}" == "Darwin" ]]; then
    stat -f '%Sg' "$1"
  else
    stat -c '%G' "$1"
  fi
}

# A workspace under a short root: the socket path it holds has to fit inside
# sockaddr_un, and macOS caps that at 104 bytes with a long $TMPDIR.
case_workspace() {
  local root="${GTB_WORK_ROOT:-/tmp/gtb}"
  mkdir -p "${root}"
  mktemp -d "${root}/XXXXXX"
}

# Ends the case as not applicable here. A case that cannot run must say so:
# passing with nothing asserted is indistinguishable from passing.
case_skip() {
  report_skip "    " "$1"
  exit "${CASE_SKIPPED}"
}

# Reports the tally and sets the case's exit status.
case_summary() {
  local total
  total="$(count "${ASSERT_TOTAL}" assertion)"

  if [[ "${ASSERT_FAILURES}" -eq 0 ]]; then
    printf '    %s%s passed%s\n' "${FORMAT_DIM}" "${total}" "${FORMAT_RESET}"
    return 0
  fi

  printf '    %s%d of %s failed%s\n' \
    "${FORMAT_DIM}" "${ASSERT_FAILURES}" "${total}" "${FORMAT_RESET}"
  return 1
}
