#!/usr/bin/env bash
set -euo pipefail

#
# Resolve the pinned SDK image that stands in for a Linux host.
#
# global.json holds both the SDK version and the image that provides it, pinned
# by digest because the tag is rebuilt whenever its base takes CVE fixes. They
# are required to agree here: a digest left behind on a version bump would
# otherwise pull the older SDK and fail later, reported as something else.
#
# Usage:
#   ./tests/integration/container/build-image.sh
#
# Output:
#   The full image reference, tag and digest
#
# Exit codes:
#   0 - Success
#   1 - global.json is missing either value, or they disagree
#

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../../.." && pwd)"

GLOBAL_JSON="${REPO_ROOT}/global.json"

SDK_VERSION="$(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' \
  "${GLOBAL_JSON}" | head -n 1)"
BUILD_IMAGE="$(sed -n 's/.*"buildImage"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' \
  "${GLOBAL_JSON}" | head -n 1)"

if [[ -z "${SDK_VERSION}" || -z "${BUILD_IMAGE}" ]]; then
  echo "ERROR: could not read sdk.version and knightOwl.buildImage from ${GLOBAL_JSON}" >&2
  exit 1
fi

if [[ "${BUILD_IMAGE}" != *":${SDK_VERSION}-"* ]]; then
  echo "ERROR: knightOwl.buildImage does not carry sdk.version ${SDK_VERSION}" >&2
  echo "  ${BUILD_IMAGE}" >&2
  echo "  Refresh it with: docker buildx imagetools inspect IMAGE:TAG" >&2
  exit 1
fi

echo "${BUILD_IMAGE}"
