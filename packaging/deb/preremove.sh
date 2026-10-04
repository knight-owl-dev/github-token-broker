#!/bin/sh
# shellcheck shell=sh
set -e

#
# A removal stops and disables the broker before its binary goes. An upgrade
# leaves it running, for postinstall to restart on the new binary.
#

if [ "$1" = remove ] && [ -d /run/systemd/system ]; then
  systemctl disable --now github-token-broker.service
fi
