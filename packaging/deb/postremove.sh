#!/bin/sh
# shellcheck shell=sh
set -e

#
# Forget the removed unit, and on a purge its enablement too. A purge leaves the
# key and configuration, which are the operator's, and the account, which files
# it owns may still name.
#

if [ "$1" = remove ] && [ -d /run/systemd/system ]; then
  systemctl --system daemon-reload > /dev/null || true
fi

if [ "$1" = purge ]; then
  if [ -x /usr/bin/deb-systemd-helper ]; then
    deb-systemd-helper purge github-token-broker.service > /dev/null || true
  fi
  if [ -d /etc/github-token-broker ]; then
    rmdir --ignore-fail-on-non-empty /etc/github-token-broker
  fi
fi
