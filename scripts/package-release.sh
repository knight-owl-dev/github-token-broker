#!/usr/bin/env bash
set -euo pipefail

#
# Package one runtime's release assets: a tarball for every RID, and a .deb for
# a Linux one.
#
# Reads the binaries `make publish` leaves and the man pages `make man-build`
# stamps. The tarball is flat, which is the shape the Homebrew formula installs
# from; the .deb is laid out by nfpm.yaml.
#
# Usage:
#   ./scripts/package-release.sh <rid>
#
# Output:
#   artifacts/release/github-token-broker_<version>_<rid>.tar.gz
#   artifacts/release/github-token-broker_<version>_<arch>.deb, for a Linux RID
#
# Exit codes:
#   0 - Packaged
#   1 - Invalid RID, a missing input, or nfpm absent for a Linux RID
#

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
cd "${REPO_ROOT}"

if [[ $# -ne 1 ]]; then
  echo "usage: $(basename "$0") <rid>" >&2
  exit 1
fi

RID="$("${SCRIPT_DIR}/validate-rid.sh" "$1")"
VERSION="$("${SCRIPT_DIR}/get-version.sh")"
TFM="$("${SCRIPT_DIR}/get-tfm.sh")"

OUT_DIR="artifacts/release"
PACKAGE="github-token-broker"

SERVICE="artifacts/bin/KnightOwl.GitHubTokenBroker.Service/Release/${TFM}/${RID}/publish/github-token-broker"
CLIENT="artifacts/bin/KnightOwl.GitHubTokenBroker.Cli/Release/${TFM}/${RID}/publish/github-token"
MAN_PAGES=(
  artifacts/man/man1/github-token.1
  artifacts/man/man5/github-token-broker-config.5
  artifacts/man/man8/github-token-broker.8
)

for input in "${SERVICE}" "${CLIENT}" "${MAN_PAGES[@]}" LICENSE; do
  if [[ ! -f "${input}" ]]; then
    echo "ERROR: missing ${input}" >&2
    echo "  Run make publish RID=${RID} and make man-build first" >&2
    exit 1
  fi
done

mkdir -p "${OUT_DIR}"

ARCHIVE="${OUT_DIR}/${PACKAGE}_${VERSION}_${RID}.tar.gz"
STAGE="$(mktemp -d)"
trap 'rm -rf "${STAGE}"' EXIT
cp "${SERVICE}" "${CLIENT}" "${MAN_PAGES[@]}" LICENSE "${STAGE}/"
tar -C "${STAGE}" -czf "${ARCHIVE}" github-token-broker github-token \
  "${MAN_PAGES[@]##*/}" LICENSE
echo "Packaged ${ARCHIVE}"

if [[ "${RID}" != linux-* ]]; then
  exit 0
fi

if ! command -v nfpm > /dev/null 2>&1; then
  echo "ERROR: nfpm is not installed" >&2
  echo "  On macOS: brew install nfpm" >&2
  exit 1
fi

case "${RID}" in
  linux-x64) ARCH="amd64" ;;
  linux-arm64) ARCH="arm64" ;;
esac

DEB="${OUT_DIR}/${PACKAGE}_${VERSION}_${ARCH}.deb"
ARCH="${ARCH}" VERSION="${VERSION}" RID="${RID}" TFM="${TFM}" \
  nfpm package --config nfpm.yaml --packager deb --target "${DEB}" > /dev/null
echo "Packaged ${DEB}"
