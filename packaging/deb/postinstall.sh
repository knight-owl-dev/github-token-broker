#!/bin/sh
# shellcheck shell=sh
set -e

#
# Create the account the unit runs as and the directory its configuration lives
# in, then tell the operator what is left: the unit installs inert.
#
# dpkg runs this on every install and upgrade, so each step is idempotent.
#

# Nothing logs in as the account, and it owns no home.
if ! getent passwd github-token-broker > /dev/null; then
  useradd --system --user-group --no-create-home --home-dir /nonexistent \
    --shell /usr/sbin/nologin github-token-broker
fi

# The configuration is not secret; the key, mode 0400, is the operator's to own.
install -d -m 0755 /etc/github-token-broker

if [ -d /run/systemd/system ]; then
  systemctl daemon-reload
  # An upgrade restarts a running broker on the new binary; nothing else starts.
  systemctl try-restart github-token-broker.service
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
