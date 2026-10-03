#!/usr/bin/env bash
set -euo pipefail

#
# Install the .deb and check what it put on the system.
#
# Runs as root inside a Debian-family image: a CI job already in one, or
# test-package.sh's container. Each check prints its own line.
#
# Usage:
#   ./scripts/verify-deb-install.sh <path-to-deb>
#
# Exit codes:
#   0 - Installed and checked
#   1 - The install or a check failed
#

if [[ $# -ne 1 ]]; then
  echo "usage: $(basename "$0") <path-to-deb>" >&2
  exit 1
fi

DEB="$1"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VERSION="$("${SCRIPT_DIR}/get-version.sh")"

if [[ ! -f "${DEB}" ]]; then
  echo "ERROR: no package at ${DEB}" >&2
  exit 1
fi

# Container images are minimized to drop man pages on install. Removing the
# rule makes this a host as an operator has one, where the pages land.
rm -f /etc/dpkg/dpkg.cfg.d/excludes

export DEBIAN_FRONTEND=noninteractive
apt-get update -qq > /dev/null
apt-get install -y -qq --no-install-recommends man-db > /dev/null
# apt reads an argument as a file only when it holds a slash.
[[ "${DEB}" == */* ]] || DEB="./${DEB}"
apt-get install -y -qq "${DEB}" > /dev/null

check() {
  local description="$1"
  shift
  if "$@" > /dev/null 2>&1; then
    echo "OK: ${description}"
  else
    echo "ERROR: ${description}" >&2
    exit 1
  fi
}

version_matches() {
  local actual
  actual="$(github-token version)" || return 1
  [[ "${actual}" == "${VERSION}" ]]
}

broker_usage_status() {
  local status=0
  /usr/bin/github-token-broker || status=$?
  [[ ${status} -eq 64 ]]
}

check "github-token is installed in /usr/bin" test -x /usr/bin/github-token
check "github-token-broker is installed in /usr/bin" test -x /usr/bin/github-token-broker
check "github-token reports version ${VERSION}" version_matches
check "github-token-broker refuses an empty command line with 64" broker_usage_status
check "man finds github-token(1)" man -w 1 github-token
check "man finds github-token-broker-config(5)" man -w 5 github-token-broker-config
check "man finds github-token-broker(8)" man -w 8 github-token-broker
check "libssl.so.3 is on the loader path" sh -c 'ldconfig -p | grep -q "libssl\.so\.3 "'
check "the CA store is installed" test -s /etc/ssl/certs/ca-certificates.crt
