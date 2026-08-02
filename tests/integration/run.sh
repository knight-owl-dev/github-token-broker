#!/usr/bin/env bash
set -euo pipefail

#
# Run the integration cases, on this host or in the container matrix.
#
# The host environment is what CI uses: a job already running on the platform
# under test, with the published binaries on PATH. The container environment is
# for a developer whose machine is not the platform under test — it builds a run
# image per distro and runs the same cases inside.
#
# Usage:
#   ./tests/integration/run.sh [--env host|container] [--all-images] [--case NAME]...
#
# Examples:
#   ./tests/integration/run.sh
#   ./tests/integration/run.sh --env container
#   ./tests/integration/run.sh --env container --all-images
#   ./tests/integration/run.sh --case 01-socket-mode
#
# The run images live in container/images.json, pinned by digest, which the CI
# matrix reads directly. Only the floor runs here unless --all-images says so.
#
# Requirements:
#   - Docker and jq, for --env container
#
# Exit codes:
#   0 - Every case passed
#   1 - A case failed, or an image could not be built
#

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
CONTAINER_DIR="${SCRIPT_DIR}/container"

# shellcheck source=lib/format.sh
source "${SCRIPT_DIR}/lib/format.sh"

ENVIRONMENT="host"
ALL_IMAGES=false
IMAGES=()
CASES=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --env)
      ENVIRONMENT="$2"
      shift 2
      ;;
    --all-images)
      ALL_IMAGES=true
      shift
      ;;
    --case)
      CASES+=("$2")
      shift 2
      ;;
    *)
      echo "Usage: $(basename "$0") [--env host|container] [--all-images] [--case NAME]..." >&2
      exit 1
      ;;
  esac
done

if [[ "${ENVIRONMENT}" != "host" && "${ENVIRONMENT}" != "container" ]]; then
  echo "ERROR: --env must be host or container, not ${ENVIRONMENT}" >&2
  exit 1
fi

# Refused rather than ignored: a host run would report success for a smaller
# thing than was asked for.
if [[ "${ALL_IMAGES}" == true && "${ENVIRONMENT}" != "container" ]]; then
  echo "ERROR: --all-images selects among the container run images," >&2
  echo "       and the host environment tests this machine alone" >&2
  exit 1
fi

# container/images.json is the one list; the CI matrix reads the same file. A local run
# takes the floor alone, since the other images answer questions that only
# matter once something has changed under them.
if [[ "${ENVIRONMENT}" == "container" ]]; then
  IMAGES_JSON="${CONTAINER_DIR}/images.json"
  selector='.[] | select(.floor) | .image'
  if [[ "${ALL_IMAGES}" == true ]]; then
    selector='.[].image'
  fi

  if ! selected="$(jq -r "${selector}" "${IMAGES_JSON}")"; then
    echo "ERROR: Could not read ${IMAGES_JSON}" >&2
    exit 1
  fi

  while IFS= read -r line; do
    if [[ -n "${line}" ]]; then
      IMAGES+=("${line}")
    fi
  done <<< "${selected}"

  if [[ "${#IMAGES[@]}" -eq 0 ]]; then
    echo "ERROR: No images selected from ${IMAGES_JSON}" >&2
    exit 1
  fi
fi

HOST_OS="$(uname -s)"

# The host environment tests this platform; the container environment always
# tests Linux, whatever the developer is sitting in front of.
if [[ "${ENVIRONMENT}" == "container" ]]; then
  RID="$("${REPO_ROOT}/scripts/host-rid.sh" --linux)"
else
  RID="$("${REPO_ROOT}/scripts/host-rid.sh")"
fi

TFM="$("${REPO_ROOT}/scripts/get-tfm.sh")"

# The cases assert that the binary reports this, and the repository is not
# visible from inside a run image.
GTB_VERSION="$("${REPO_ROOT}/scripts/get-version.sh")"
export GTB_VERSION

SERVICE_DIR="${REPO_ROOT}/artifacts/bin/KnightOwl.GitHubTokenBroker.Service/Release/${TFM}/${RID}/publish"
CLI_DIR="${REPO_ROOT}/artifacts/bin/KnightOwl.GitHubTokenBroker.Cli/Release/${TFM}/${RID}/publish"
SERVICE_BINARY="${SERVICE_DIR}/github-token-broker"
CLI_BINARY="${CLI_DIR}/github-token"

echo "Integration suite: ${ENVIRONMENT}, ${RID}, ${TFM}"

# Publishing runs every time, not only when the binaries are missing: both this
# and the container build are incremental, and a suite that silently tests a
# stale artifact reports on code that is no longer there. A CI job holding a
# downloaded artifact has no SDK and keeps what it was given.
echo ""
if [[ "${RID}" == linux-* && "${HOST_OS}" != "Linux" ]]; then
  # Native AOT links with the host's toolchain, so macOS cannot target Linux.
  # The same recipe runs in a container that can.
  printf '%sPublishing %s in a container, since %s cannot target it%s\n' \
    "${FORMAT_BOLD}" "${RID}" "${HOST_OS}" "${FORMAT_RESET}"

  # Resolved first so a drift between global.json's SDK version and its pinned
  # image stops the run, rather than being swallowed by the argument list.
  BUILD_IMAGE="$("${CONTAINER_DIR}/build-image.sh")"

  docker build \
    --file "${CONTAINER_DIR}/Dockerfile.publish" \
    --target export \
    --output "type=local,dest=${REPO_ROOT}" \
    --build-arg "BUILD_IMAGE=${BUILD_IMAGE}" \
    --build-arg "RID=${RID}" \
    --build-arg "TFM=${TFM}" \
    "${REPO_ROOT}"
elif command -v dotnet > /dev/null 2>&1; then
  printf '%sPublishing %s%s\n' "${FORMAT_BOLD}" "${RID}" "${FORMAT_RESET}"
  make -C "${REPO_ROOT}" publish RID="${RID}"
else
  printf '%sNo SDK here, keeping the artifact already published%s\n' \
    "${FORMAT_BOLD}" "${FORMAT_RESET}"
fi

for binary in "${SERVICE_BINARY}" "${CLI_BINARY}"; do
  if [[ ! -x "${binary}" ]]; then
    echo "ERROR: Missing executable after publish: ${binary}" >&2
    exit 1
  fi
done

if [[ "${ENVIRONMENT}" == "host" ]]; then
  echo ""
  printf '%sRunning cases on this host%s\n' "${FORMAT_BOLD}" "${FORMAT_RESET}"

  # The two projects publish to their own directories, so both are on PATH.
  PATH="${SERVICE_DIR}:${CLI_DIR}:${PATH}"
  export PATH
  exec "${SCRIPT_DIR}/run-cases.sh" "${CASES[@]}"
fi

# The run image's context holds only what it copies, so a case edit or a distro
# change never re-sends the repository.
CONTEXT="${REPO_ROOT}/artifacts/integration/context"
rm -rf "${CONTEXT}"
mkdir -p "${CONTEXT}"
cp "${SERVICE_BINARY}" "${CLI_BINARY}" "${CONTAINER_DIR}/prepare.sh" "${CONTEXT}/"

# Cases are mounted rather than copied, so editing one does not rebuild the
# image. The socket and the key stay inside the container filesystem, where Unix
# modes are reported faithfully. A terminal is forwarded so the labels keep
# their color.
DOCKER_RUN_FLAGS=(--rm --volume "${SCRIPT_DIR}:/suite:ro" --env "GTB_VERSION=${GTB_VERSION}")
if [[ -t 1 ]]; then
  DOCKER_RUN_FLAGS+=(--tty)
fi

IMAGES_STARTED="$(elapsed_mark)"
FAILED=0
FAILED_IMAGES=()

for image in "${IMAGES[@]}"; do
  # Images arrive as name:tag@sha256:… so the file still reads as the distro it
  # names. Only the readable half belongs in a local tag or a message.
  name="${image%%@*}"
  tag="github-token-broker-integration:${name//[^a-zA-Z0-9]/-}"

  echo ""
  printf '%sBuilding %s on %s%s\n' "${FORMAT_BOLD}" "${tag}" "${name}" "${FORMAT_RESET}"
  if ! docker build \
    --file "${CONTAINER_DIR}/Dockerfile" \
    --build-arg "RUN_IMAGE=${image}" \
    --tag "${tag}" \
    "${CONTEXT}"; then
    report_fail "" "${name}" "the image could not be built"
    FAILED=1
    FAILED_IMAGES+=("${name}")
    continue
  fi

  echo ""
  printf '%sRunning cases on %s%s\n' "${FORMAT_BOLD}" "${name}" "${FORMAT_RESET}"

  if docker run "${DOCKER_RUN_FLAGS[@]}" "${tag}" \
    bash /suite/run-cases.sh "${CASES[@]}"; then
    report_ok "" "${name}"
  else
    report_fail "" "${name}"
    FAILED=1
    FAILED_IMAGES+=("${name}")
  fi
done

echo ""
TOTAL_IMAGES="$(count "${#IMAGES[@]}" image)"
IMAGES_TOOK="$(elapsed_since "${IMAGES_STARTED}")"

if [[ "${FAILED}" -eq 0 ]]; then
  report_ok "" "${TOTAL_IMAGES} passed in ${IMAGES_TOOK}"
  exit 0
fi

report_fail "" "${#FAILED_IMAGES[@]} of ${TOTAL_IMAGES} failed in ${IMAGES_TOOK}" "${FAILED_IMAGES[@]}"
exit 1
