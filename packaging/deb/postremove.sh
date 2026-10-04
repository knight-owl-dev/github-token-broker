#!/bin/sh
# shellcheck shell=sh
set -e

#
# Forget the removed unit. A purge leaves the key and configuration, which are
# the operator's, and the account, which files it owns may still name.
#

if [ -d /run/systemd/system ]; then
  systemctl daemon-reload
fi

if [ "$1" = purge ]; then
  rmdir --ignore-fail-on-non-empty /etc/github-token-broker
fi
