#!/usr/bin/env bash
set -uo pipefail

#
# Run the cases. Invoked inside the container by run.sh, and directly by the
# native path.
#
# Each case is a separate process with its own workspace and its own trap, so a
# leaked broker or a leftover socket cannot reach the next one.
#
# Usage:
#   ./tests/integration/run-cases.sh [CASE_NAME]...
#
# Exit codes:
#   0 - Every case passed
#   1 - A case failed, or none matched
#

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# shellcheck source=lib/format.sh
source "${SCRIPT_DIR}/lib/format.sh"

# A developer running this has a shell configured for their own broker. Every
# case names the endpoint it means, so inheriting one can only mislead.
unset GITHUB_TOKEN_BROKER_ENDPOINT
unset GITHUB_TOKEN_BROKER_GH

# The terminal answers for Git too: an askpass helper supplies a credential
# where GIT_TERMINAL_PROMPT only closes the prompt, and GIT_CONFIG_COUNT
# injects settings past any config file a case redirects.
unset GIT_ASKPASS SSH_ASKPASS GIT_CONFIG_COUNT

selected=("$@")

cases=()
for path in "${SCRIPT_DIR}"/cases/*.sh; do
  name="$(basename "${path}" .sh)"

  if [[ "${#selected[@]}" -gt 0 ]]; then
    wanted=false
    for want in "${selected[@]}"; do
      if [[ "${name}" == *"${want}"* ]]; then
        wanted=true
        break
      fi
    done
    if [[ "${wanted}" == false ]]; then
      continue
    fi
  fi

  cases+=("${path}")
done

if [[ "${#cases[@]}" -eq 0 ]]; then
  echo "ERROR: no cases matched" >&2
  exit 1
fi

started="$(elapsed_mark)"

passed=0
failed=0
skipped=0
failures=()

# A case reports itself as not applicable with this status rather than passing
# without asserting anything.
CASE_SKIPPED=3

for path in "${cases[@]}"; do
  name="$(basename "${path}" .sh)"
  printf '  %s%s%s\n' "${FORMAT_BOLD}" "${name}" "${FORMAT_RESET}"

  status=0
  bash "${path}" || status=$?

  case "${status}" in
    0)
      passed=$((passed + 1))
      ;;
    "${CASE_SKIPPED}")
      skipped=$((skipped + 1))
      ;;
    *)
      failed=$((failed + 1))
      failures+=("${name}")
      ;;
  esac
done

echo ""

took="$(elapsed_since "${started}")"

if [[ "${failed}" -eq 0 ]]; then
  summary="$(count "${passed}" case) passed"
  if [[ "${skipped}" -gt 0 ]]; then
    summary="${summary}, ${skipped} skipped"
  fi

  report_ok "" "${summary} in ${took}"
  exit 0
fi

total="$(count $((passed + failed + skipped)) case)"
report_fail "" "${failed} of ${total} failed in ${took}" "${failures[@]}"
exit 1
