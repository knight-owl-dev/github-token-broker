#!/usr/bin/env bash
set -euo pipefail

#
# Open a release PR: stamp the next version into Directory.Build.props, and the
# release month into each man page's .Dd, on a release/vX.Y.Z branch. Merging
# it tags vX.Y.Z (tag-release.yml), which publishes the release (publish.yml).
#
# A bump starts from the latest release tag, the source publish.yml checks the
# version against. An explicit version is for the first release, a jump, or a
# correction.
#
# Usage:
#   ./scripts/release.sh <major|minor|patch|X.Y.Z>
#
# Environment:
#   GH_TOKEN           In CI, the App token the push and PR run under, so the PR
#                      starts CI. Locally, the caller's own git and gh auth.
#   GITHUB_REPOSITORY  In CI, owner/repo for the token remote.
#   AUTOMERGE          Non-empty queues the PR to squash-merge once checks pass.
#
# Exit codes:
#   0 - Release PR opened
#   1 - Bad argument, a dirty tree, a tag that exists, or an open release PR
#

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
cd "${REPO_ROOT}"

if [[ $# -ne 1 ]]; then
  echo "usage: $(basename "$0") <major|minor|patch|X.Y.Z>" >&2
  exit 1
fi

git fetch --tags --quiet 2> /dev/null || true

case "$1" in
  major | minor | patch)
    tags="$(git tag --list 'v*')"
    latest="$(sed -n 's/^v\([0-9]*\.[0-9]*\.[0-9]*\)$/\1/p' <<< "${tags}" | sort -V | tail -n 1)"
    if [[ -z "${latest}" ]]; then
      echo "ERROR: no release tag to bump from; name the first version explicitly" >&2
      exit 1
    fi
    IFS=. read -r major minor patch <<< "${latest}"
    case "$1" in
      major) VERSION="$((major + 1)).0.0" ;;
      minor) VERSION="${major}.$((minor + 1)).0" ;;
      patch) VERSION="${major}.${minor}.$((patch + 1))" ;;
    esac
    echo "Latest release v${latest}, bumping $1 to v${VERSION}"
    ;;
  *)
    VERSION="$("${SCRIPT_DIR}/validate-version.sh" "${1#v}")"
    ;;
esac

if ! git diff --quiet || ! git diff --staged --quiet; then
  echo "ERROR: the working tree has changes; the stamp must be the release PR's only change" >&2
  exit 1
fi

if git rev-parse --quiet --verify "refs/tags/v${VERSION}" > /dev/null; then
  echo "ERROR: v${VERSION} is already released" >&2
  exit 1
fi

# Two open release PRs would both stamp from the same tag, and whichever merges
# second would release a version the first already moved past.
existing="$(gh pr list --state open --limit 1000 --json number,headRefName \
  --jq 'map(select(.headRefName | startswith("release/v"))) | .[0].number // empty')"
if [[ -n "${existing}" ]]; then
  echo "ERROR: release PR #${existing} is open; merge or close it first" >&2
  exit 1
fi

if [[ -n "${GH_TOKEN:-}" && -n "${GITHUB_REPOSITORY:-}" ]]; then
  git config user.name "github-actions[bot]"
  git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
  git remote set-url origin "https://x-access-token:${GH_TOKEN}@github.com/${GITHUB_REPOSITORY}.git"
fi

# From main as GitHub has it, whatever is checked out here, so the stamp is the
# PR's only change.
git fetch --quiet origin main
BRANCH="release/v${VERSION}"
git switch --quiet -c "${BRANCH}" origin/main

sed -i.bak "s|<Version>[^<]*</Version>|<Version>${VERSION}</Version>|" Directory.Build.props
rm Directory.Build.props.bak
stamped="$("${SCRIPT_DIR}/get-version.sh")"
if [[ "${stamped}" != "${VERSION}" ]]; then
  echo "ERROR: could not stamp ${VERSION} into Directory.Build.props" >&2
  exit 1
fi

# A page's .Dd is the month of the release that last shipped it. The first of
# the month keeps a rerun in the same month from moving it.
dated="$(LC_ALL=C date -u '+%B 1, %Y')"
pages=(docs/man/man*/*.[0-9])
sed -i.bak "s/^\.Dd .*/.Dd ${dated}/" "${pages[@]}"
for page in "${pages[@]}"; do rm "${page}.bak"; done

# Naming the current version in the month the pages already show stamps nothing;
# the empty commit is still what the PR and the tag hang on.
git add Directory.Build.props docs/man
git commit --allow-empty --quiet -m "Release v${VERSION}"
git push --quiet -u origin "${BRANCH}"

pr_url="$(gh pr create --base main --head "${BRANCH}" --title "Release v${VERSION}" \
  --body "Merging this PR tags v${VERSION}, which publishes the release.")"
echo "Opened ${pr_url}"

if [[ -n "${AUTOMERGE:-}" ]]; then
  gh pr merge --auto --squash "${pr_url}"
  echo "Queued to merge once checks pass"
fi
