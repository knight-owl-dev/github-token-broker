#!/bin/sh
# shellcheck shell=sh
set -e

#
# Create the account the unit runs as and the directory its configuration lives
# in, and register the unit without enabling it: the broker cannot start before
# an operator supplies a key and a configuration.
#
# The systemd steps follow dh_installsystemd's, so enablement survives a
# reinstall and a purge clears it, as for any Debian service.
#

UNIT=github-token-broker.service

case "$1" in
  configure | abort-upgrade | abort-deconfigure | abort-remove) ;;
  *) exit 0 ;;
esac

# Nothing logs in as the account, and it owns no home. An operator may have
# made the group first, to add clients to it, so an existing one is joined.
if ! getent passwd github-token-broker > /dev/null; then
  if getent group github-token-broker > /dev/null; then
    useradd --system --gid github-token-broker --no-create-home \
      --home-dir /nonexistent --shell /usr/sbin/nologin github-token-broker
  else
    useradd --system --user-group --no-create-home \
      --home-dir /nonexistent --shell /usr/sbin/nologin github-token-broker
  fi
fi

# The configuration is not secret; the key, mode 0400, is the operator's to own.
# Created only when missing, so an upgrade keeps a mode the operator tightened.
[ -d /etc/github-token-broker ] || install -d -m 0755 /etc/github-token-broker

# Re-enable a unit the operator had enabled before a remove and reinstall, and
# record its state for the purge to clean up.
if deb-systemd-helper debian-installed "${UNIT}"; then
  deb-systemd-helper unmask "${UNIT}" > /dev/null || true
  if deb-systemd-helper --quiet was-enabled "${UNIT}"; then
    deb-systemd-helper enable "${UNIT}" > /dev/null || true
  fi
fi
deb-systemd-helper update-state "${UNIT}" > /dev/null || true

if [ -z "${DPKG_ROOT:-}" ] && [ -d /run/systemd/system ]; then
  systemctl --system daemon-reload > /dev/null || true
  # An upgrade, or a reinstall after a remove, restarts an enabled broker on the
  # new binary; deb-systemd-invoke leaves a unit the operator never enabled
  # alone, and a first install starts nothing.
  if [ -n "$2" ]; then
    deb-systemd-invoke restart "${UNIT}" > /dev/null || true
  fi
fi

if [ ! -f /etc/github-token-broker/config.json ]; then
  cat << 'EOF'
github-token-broker is installed, not enabled. To run it, as root:
  install -o github-token-broker -m 0400 APP_KEY.pem /etc/github-token-broker/app.pem
  cp /usr/share/github-token-broker/config.example.json /etc/github-token-broker/config.json
  editor /etc/github-token-broker/config.json  # app_id, installation_id, repositories
  systemctl enable --now github-token-broker
  usermod -aG github-token-broker CLIENT_ACCOUNT  # for each account that mints
See github-token-broker(8)
EOF
fi
