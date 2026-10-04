#!/usr/bin/env bash
set -euo pipefail

#
# Write SHA256 checksums for every release asset under a directory.
#
# checksums.txt is in sha256sum's own format, so `sha256sum -c` verifies it,
# and it is the file the apt repository and the Homebrew tap read.
#
# Usage:
#   ./scripts/generate-checksums.sh <dist-dir>
#
# Output:
#   artifacts/release/checksums.txt
#
# Exit codes:
#   0 - Written
#   1 - No directory, or no assets in it
#

if [[ $# -ne 1 ]]; then
  echo "usage: $(basename "$0") <dist-dir>" >&2
  exit 1
fi

if [[ ! -d "$1" ]]; then
  echo "ERROR: no directory at $1" >&2
  exit 1
fi

DIST_DIR="$(cd "$1" && pwd)"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)/artifacts/release"
mkdir -p "${OUT_DIR}"

shopt -s globstar nullglob
assets=("${DIST_DIR}"/**/*.tar.gz "${DIST_DIR}"/**/*.deb)

if [[ ${#assets[@]} -eq 0 ]]; then
  echo "ERROR: no release assets under ${DIST_DIR}" >&2
  exit 1
fi

# Basenames only: the release serves every asset from one flat list.
for asset in "${assets[@]}"; do
  (cd "$(dirname "${asset}")" && sha256sum "$(basename "${asset}")")
done | sort -k 2 > "${OUT_DIR}/checksums.txt"

cat "${OUT_DIR}/checksums.txt"
