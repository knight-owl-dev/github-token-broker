#!/usr/bin/env bash
set -euo pipefail

#
# The .NET runtime identifier for this machine.
#
# `make publish` and the integration suite both need one, and a literal in
# either would be whichever machine wrote it down.
#
# Usage:
#   ./scripts/host-rid.sh [--linux]
#
# --linux keeps this machine's architecture but names Linux, for work that
# targets a container whatever the developer is sitting in front of.
#
# Exit codes:
#   0 - Printed the RID
#   1 - Unsupported architecture or operating system
#

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

LINUX_ONLY=false
if [[ $# -ge 1 && "$1" == "--linux" ]]; then
  LINUX_ONLY=true
  shift
fi

if [[ $# -ne 0 ]]; then
  echo "Usage: $(basename "$0") [--linux]" >&2
  exit 1
fi

HOST_ARCH="$(uname -m)"
case "${HOST_ARCH}" in
  x86_64)
    ARCH="x64"
    ;;
  aarch64 | arm64)
    ARCH="arm64"
    ;;
  *)
    echo "ERROR: unsupported host architecture: ${HOST_ARCH}" >&2
    exit 1
    ;;
esac

if [[ "${LINUX_ONLY}" == true ]]; then
  "${SCRIPT_DIR}/validate-rid.sh" --linux "linux-${ARCH}"
  exit 0
fi

HOST_OS="$(uname -s)"
case "${HOST_OS}" in
  Linux)
    "${SCRIPT_DIR}/validate-rid.sh" --linux "linux-${ARCH}"
    ;;
  Darwin)
    "${SCRIPT_DIR}/validate-rid.sh" "osx-${ARCH}"
    ;;
  *)
    echo "ERROR: unsupported host operating system: ${HOST_OS}" >&2
    exit 1
    ;;
esac
