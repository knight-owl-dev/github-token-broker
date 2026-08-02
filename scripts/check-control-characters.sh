#!/usr/bin/env bash
set -euo pipefail

#
# Refuse a source file holding a control character written as a raw byte.
#
# A NUL makes git treat the file as binary: every diff on it reads "Bin 3356 ->
# 3544 bytes" and shows nothing at all. The rest are invisible in an editor and
# in a pull request, which is reason enough for source to spell them as escapes.
#
# Uses tr alone. The linters run in an image that carries no interpreter beyond
# the shell, and detection here is byte counting, which tr does everywhere.
#
# Scoped by extension, as `make lint-spelling` is, so a binary asset added later
# is not a false positive.
#
# Usage:
#   ./scripts/check-control-characters.sh
#
# Exit codes:
#   0 - No raw control characters
#   1 - At least one file holds one
#

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

cd "${REPO_ROOT}"

# What a source file may hold: tab, newline, carriage return, printable ASCII,
# and every byte above 0x7f, which is the UTF-8 this repository's prose is full
# of. Deleting all of it leaves exactly what should not be there.
PERMITTED='\11\12\15\40-\176\200-\377'

FINDINGS=0

# Listed into a file rather than a process substitution, so a failure to list is
# the script's failure rather than an empty loop that reports success.
TRACKED="$(mktemp)"
trap 'rm -f "${TRACKED}"' EXIT
git ls-files -z > "${TRACKED}"

while IFS= read -r -d '' file; do
  case "${file}" in
    *.cs | *.sh | *.md | *.json | *.props | *.yml | *.yaml | *.slnx | *.py) ;;
    *) continue ;;
  esac

  if [[ ! -f "${file}" ]]; then
    continue
  fi

  # wc pads its count on BSD, so the arithmetic comparison does the trimming.
  remaining="$(LC_ALL=C tr -d "${PERMITTED}" < "${file}" | wc -c)"
  if [[ "${remaining}" -ne 0 ]]; then
    echo "${file}: ${remaining} raw control character(s)" >&2
    FINDINGS=$((FINDINGS + 1))
  fi
done < "${TRACKED}"

if [[ "${FINDINGS}" -ne 0 ]]; then
  echo "Write them as escapes. To locate one: cat -v FILE | less" >&2
  exit 1
fi
