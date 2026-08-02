#!/usr/bin/env bash
#
# Lint the man page sources with mandoc at style level.
#
# Style level also resolves every .Xr against the host's man database, so that
# one message is dropped: it reports what is installed here rather than anything
# about the sources, and would answer differently in the CI image.

set -euo pipefail

if [[ $# -eq 0 ]]; then
  echo "  no man pages yet"
  exit 0
fi

findings="$(mandoc -T lint -W style "$@" 2>&1 \
  | grep -v -e 'referenced manual not found' -e 'mandoc\.db' || true)"

if [[ -n ${findings} ]]; then
  echo "${findings}" >&2
  exit 1
fi
