#!/usr/bin/env bash
set -euo pipefail

#
# Install the .deb in each Debian-family image images.json marks, and check it.
#
# The host environment runs the check here, for a CI job already inside one of
# those images. The container environment starts each image itself, for a
# developer; it tests the package matching this machine's architecture.
#
# Usage:
#   ./scripts/test-package.sh [--env host|container]
#
# Exit codes:
#   0 - Every image passed
#   1 - An image failed, or the package is missing
#

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
IMAGES_JSON="${REPO_ROOT}/tests/integration/container/images.json"

ENVIRONMENT="host"
if [[ $# -eq 2 && "$1" == "--env" ]]; then
  ENVIRONMENT="$2"
elif [[ $# -ne 0 ]]; then
  echo "usage: $(basename "$0") [--env host|container]" >&2
  exit 1
fi

VERSION="$("${SCRIPT_DIR}/get-version.sh")"
case "$("${SCRIPT_DIR}/host-rid.sh" --linux)" in
  linux-x64) ARCH="amd64" ;;
  linux-arm64) ARCH="arm64" ;;
esac
DEB="artifacts/release/github-token-broker_${VERSION}_${ARCH}.deb"

if [[ ! -f "${REPO_ROOT}/${DEB}" ]]; then
  echo "ERROR: no package at ${DEB}" >&2
  echo "  Run make package RID=linux-${ARCH/amd64/x64} first" >&2
  exit 1
fi

case "${ENVIRONMENT}" in
  host)
    exec "${SCRIPT_DIR}/verify-deb-install.sh" "${REPO_ROOT}/${DEB}"
    ;;
  container)
    selected="$(jq -r '.[] | select(.deb) | .image' "${IMAGES_JSON}")"
    mapfile -t images <<< "${selected}"
    for image in "${images[@]}"; do
      echo "Installing on ${image%%@*}"
      docker run --rm --volume "${REPO_ROOT}:/repo:ro" "${image}" \
        /repo/scripts/verify-deb-install.sh "/repo/${DEB}"
    done
    ;;
  *)
    echo "ERROR: --env must be host or container, not ${ENVIRONMENT}" >&2
    exit 1
    ;;
esac
