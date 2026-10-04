#!/usr/bin/env bash
set -euo pipefail

#
# Install the .deb, check what it put on the system, then purge it.
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

broker_stops_at_the_key() {
  local config status=0
  config="$(mktemp)"
  sed 's|/etc/github-token-broker/app.pem|/nonexistent/app.pem|' \
    /usr/share/github-token-broker/config.example.json > "${config}"
  github-token-broker --config "${config}" || status=$?
  rm -f "${config}"
  [[ ${status} -eq 66 ]]
}

# verify exits 0 on an unknown key, which systemd then ignores, so a misspelled
# hardening line would pass silently; any output fails the unit instead.
unit_verifies() {
  local output
  output="$(systemd-analyze verify /usr/lib/systemd/system/github-token-broker.service 2>&1)" || return 1
  [[ -z "${output}" ]]
}

unit_is_disabled() {
  [[ ! -e /etc/systemd/system/multi-user.target.wants/github-token-broker.service ]]
}

check "the github-token-broker account exists" getent passwd github-token-broker
check "the configuration directory exists" test -d /etc/github-token-broker
check "the unit is installed" test -f /usr/lib/systemd/system/github-token-broker.service
check "the unit is not enabled" unit_is_disabled
check "the example configuration parses, stopping at the missing key with 66" broker_stops_at_the_key

# systemd itself is not running here, so it is installed only to read the unit.
apt-get install -y -qq --no-install-recommends systemd > /dev/null
check "systemd-analyze verifies the unit without a warning" unit_verifies

unit_is_enabled() {
  [[ -L /etc/systemd/system/multi-user.target.wants/github-token-broker.service ]]
}

# Enabling works without a running systemd, as an image build does it, which
# also shows the not-enabled check above could have failed.
systemctl enable github-token-broker.service > /dev/null 2>&1
check "an offline enable links the unit" unit_is_enabled

apt-get remove -y -qq github-token-broker > /dev/null
apt-get install -y -qq "${DEB}" > /dev/null
check "a remove and reinstall keeps the unit enabled" unit_is_enabled

apt-get purge -y -qq github-token-broker > /dev/null
check "a purge removes the binaries" test ! -e /usr/bin/github-token-broker
check "a purge removes the unit's enablement" unit_is_disabled
check "a purge removes the empty configuration directory" test ! -e /etc/github-token-broker
check "a purge keeps the account" getent passwd github-token-broker

# An operator may have deleted the configuration directory before purging.
apt-get install -y -qq "${DEB}" > /dev/null
rm -rf /etc/github-token-broker
check "a purge succeeds with the configuration directory already gone" \
  apt-get purge -y -qq github-token-broker
