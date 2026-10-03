#!/usr/bin/env bash
#
# Render one stamped man page. Reads artifacts/man rather than docs/man, so what
# is shown is what a package installs.

set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
man_root="${repository_root}/artifacts/man"
name="${1:-github-token}"

page="$(find "${man_root}" -type f -name "${name}.[0-9]" | head -n 1)"

if [[ -z ${page} ]]; then
  available="$(find "${man_root}" -type f -name '*.[0-9]' -exec basename {} \; \
    | sed 's/\.[0-9]$//' | sort | tr '\n' ' ')"

  echo "ERROR: no man page \"${name}\". Available: ${available}" >&2
  exit 1
fi

mandoc -a "${page}"
