#!/usr/bin/env bash
set -euo pipefail

#
# Make a Linux environment able to run the cases: packages, the GitHub CLI, and
# the accounts the socket cases need.
#
# Run once, as root, by whatever produced that environment: the run image's
# build, or a CI job already inside one.
#
# Where the package name does not say what it provides:
#
#   procps      pkill
#   util-linux  runuser
#   shadow      useradd, groupadd
#
# make is here because a CI job running inside one of these images invokes the
# recipe, the same way a developer does.
#
# Exit codes:
#   0 - Ready
#   1 - No supported package manager, or an unsupported architecture
#

GH_VERSION="2.102.0"

if command -v apt-get > /dev/null 2>&1; then
  export DEBIAN_FRONTEND=noninteractive
  apt-get update
  apt-get install --yes --no-install-recommends \
    ca-certificates \
    curl \
    git \
    make \
    openssl \
    passwd \
    procps \
    python3 \
    tar \
    util-linux
  rm -rf /var/lib/apt/lists/*
elif command -v dnf > /dev/null 2>&1; then
  dnf install --assumeyes \
    ca-certificates \
    curl \
    git \
    make \
    openssl \
    procps-ng \
    python3 \
    shadow-utils \
    tar \
    util-linux
  dnf clean all
else
  echo "ERROR: no supported package manager (looked for apt-get and dnf)" >&2
  exit 1
fi

# The GitHub CLI is in no distro repository here, so a pinned release tarball is
# the one source that works the same on all of them.
MACHINE="$(uname -m)"
case "${MACHINE}" in
  x86_64)
    GH_ARCH="amd64"
    ;;
  aarch64 | arm64)
    GH_ARCH="arm64"
    ;;
  *)
    echo "ERROR: unsupported architecture: ${MACHINE}" >&2
    exit 1
    ;;
esac

GH_TARBALL="gh_${GH_VERSION}_linux_${GH_ARCH}"
curl --fail --silent --show-error --location \
  "https://github.com/cli/cli/releases/download/v${GH_VERSION}/${GH_TARBALL}.tar.gz" \
  | tar --extract --gzip --directory /tmp
mv "/tmp/${GH_TARBALL}/bin/gh" /usr/local/bin/gh
rm -rf "/tmp/${GH_TARBALL:?}"

# Root bypasses the permission check the socket rests on, so a case that runs as
# root passes vacuously. The multi-user case runs as these instead: broker
# serves, client shares its group, outsider does not.
groupadd --system brokers
useradd --create-home --groups brokers broker
useradd --create-home --groups brokers client
useradd --create-home outsider
