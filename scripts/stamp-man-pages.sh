#!/usr/bin/env bash
#
# Copy the man pages to artifacts/man with @VERSION@ replaced.
#
# The sources carry the placeholder so Directory.Build.props stays the one home
# for the version. Everything that shows a page or ships one reads artifacts/man,
# so the placeholder is never what a person or a package sees.

set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source_root="${repository_root}/docs/man"
output_root="${repository_root}/artifacts/man"
build_props="${repository_root}/Directory.Build.props"

version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "${build_props}" | head -n 1)"

if [[ -z ${version} ]]; then
  echo "ERROR: No <Version> in ${build_props}." >&2
  exit 1
fi

shopt -s nullglob
rm -rf "${output_root}"
stamped=0

# The extension, not the source directory, decides the section man resolves by.
for page in "${source_root}"/man*/*.[0-9]; do
  section="${page##*.}"
  destination="${output_root}/man${section}"

  mkdir -p "${destination}"
  sed "s/@VERSION@/${version}/g" "${page}" > "${destination}/${page##*/}"
  stamped=$((stamped + 1))
done

if [[ ${stamped} -eq 0 ]]; then
  echo "ERROR: No man pages under ${source_root}." >&2
  exit 1
fi

echo "Stamped ${version} into ${output_root} (${stamped} pages)"
