#!/usr/bin/env bash
set -euo pipefail

#
# Validate a version for safe use in file names and tags.
#
# Accepts MAJOR.MINOR.PATCH alone: a prerelease would reach apt and Homebrew
# users, who have no channel to opt out of it. Prints the version back, so a
# caller assigns the validated value rather than the one it was given.
#
# Usage:
#   ./scripts/validate-version.sh <version>
#
# Exit codes:
#   0 - Valid
#   1 - Invalid, or no argument
#

if [[ $# -ne 1 ]]; then
  echo "usage: $(basename "$0") <version>" >&2
  exit 1
fi

if [[ ! "$1" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "ERROR: invalid version: $1" >&2
  echo "  Expected MAJOR.MINOR.PATCH, such as 1.4.0" >&2
  exit 1
fi

echo "$1"
