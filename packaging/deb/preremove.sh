#!/bin/sh
# shellcheck shell=sh
set -e

#
# A removal stops the broker before its binary goes. Its enablement stays, so a
# reinstall starts it again; the purge clears it. An upgrade leaves it running,
# for postinstall to restart on the new binary.
#

if [ -z "${DPKG_ROOT:-}" ] && [ "$1" = remove ] && [ -d /run/systemd/system ]; then
  deb-systemd-invoke stop github-token-broker.service > /dev/null || true
fi
