#!/usr/bin/env bash
set -euo pipefail

#
# Extract the project version from Directory.Build.props.
#
# Reads the <Version> element and outputs the value (e.g., "0.1.0"). Both
# executables inherit it, so there is one version and one place it is written.
#
# Usage:
#   ./scripts/get-version.sh
#
# Output:
#   Prints the version to stdout (e.g., "0.1.0")
#
# Exit codes:
#   0 - Success
#   1 - Could not read Version from Directory.Build.props
#

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

PROPS_PATH="${REPO_ROOT}/Directory.Build.props"

VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "${PROPS_PATH}" | head -n 1)"

if [[ -z "${VERSION}" ]]; then
  echo "ERROR: could not read <Version> from ${PROPS_PATH}" >&2
  exit 1
fi

echo "${VERSION}"
