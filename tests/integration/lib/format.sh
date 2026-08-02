# shellcheck shell=bash
#
# One vocabulary for results: [ OK ] and [FAIL], same width, at every level
# from an assertion to a run image. Color is for terminals, so a redirected log
# or a CI pane stays plain text.
#
# Sourced by the host driver as well as the cases; shellcheck cannot see the
# use from here.
# shellcheck disable=SC2034

if [[ -t 1 && -z "${NO_COLOR:-}" ]]; then
  FORMAT_GREEN=$'\033[32m'
  FORMAT_RED=$'\033[31m'
  FORMAT_DIM=$'\033[2m'
  FORMAT_BOLD=$'\033[1m'
  FORMAT_RESET=$'\033[0m'
else
  FORMAT_GREEN=""
  FORMAT_RED=""
  FORMAT_DIM=""
  FORMAT_BOLD=""
  FORMAT_RESET=""
fi

LABEL_OK="${FORMAT_GREEN}[ OK ]${FORMAT_RESET}"
LABEL_FAIL="${FORMAT_RED}[FAIL]${FORMAT_RESET}"
LABEL_SKIP="${FORMAT_DIM}[SKIP]${FORMAT_RESET}"

# Width of a label, so continuation lines align under the text beside it.
LABEL_INDENT="      "

# count COUNT SINGULAR — "1 case", "3 cases"
count() {
  if [[ "$1" -eq 1 ]]; then
    printf '%d %s' "$1" "$2"
  else
    printf '%d %ss' "$1" "$2"
  fi
}

# report_ok INDENT TEXT
report_ok() {
  printf '%s%s %s\n' "$1" "${LABEL_OK}" "$2"
}

# report_skip INDENT TEXT
report_skip() {
  printf '%s%s %s\n' "$1" "${LABEL_SKIP}" "$2"
}

# report_fail INDENT TEXT [DETAIL...]
report_fail() {
  local indent="$1"
  printf '%s%s %s\n' "${indent}" "${LABEL_FAIL}" "$2"
  shift 2

  local line
  for line in "$@"; do
    printf '%s%s%s%s%s\n' \
      "${indent}" "${LABEL_INDENT}" "${FORMAT_DIM}" "${line}" "${FORMAT_RESET}"
  done
}
