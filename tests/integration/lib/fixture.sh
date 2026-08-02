# shellcheck shell=bash
#
# The App key and the configuration file.
#
# Nothing here is a real secret: the key is generated per case, and no token it
# produces is valid anywhere.
#
# The cases read these variables; shellcheck cannot see that from here.
# shellcheck disable=SC2034

# The one repository every configuration allowlists, and one that is never
# allowlisted anywhere, so a refusal is unambiguous.
SERVED_REPOSITORY="integration-owner/served-repo"
UNLISTED_REPOSITORY="integration-owner/unlisted-repo"

# The App a JWT must be attributed to, in the configuration and in the fake.
FIXTURE_APP_ID=123456

FIXTURE_LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

fixture_key() {
  openssl genrsa -out "$1" 2048 2> /dev/null
}

# fixture_github WORKSPACE [ARGUMENT...] — starts the canned GitHub and sets
# FAKE_GITHUB_URL once it is listening. Extra arguments reach fake-github.py.
fixture_github() {
  local workspace="$1"
  shift

  FAKE_GITHUB_PORT_FILE="${workspace}/github.port"
  FAKE_GITHUB_LOG="${workspace}/github.requests"
  rm -f "${FAKE_GITHUB_PORT_FILE}" "${FAKE_GITHUB_LOG}"

  python3 "${FIXTURE_LIB_DIR}/fake-github.py" \
    --port-file "${FAKE_GITHUB_PORT_FILE}" \
    --request-log "${FAKE_GITHUB_LOG}" \
    --app-id "${FIXTURE_APP_ID}" \
    "$@" &
  FAKE_GITHUB_PID=$!

  if ! wait_for 10 test -s "${FAKE_GITHUB_PORT_FILE}"; then
    return 1
  fi

  local port
  port="$(< "${FAKE_GITHUB_PORT_FILE}")"
  FAKE_GITHUB_URL="http://127.0.0.1:${port}"
}

fixture_github_stop() {
  if [[ -n "${FAKE_GITHUB_PID:-}" ]]; then
    kill -TERM "${FAKE_GITHUB_PID}" 2> /dev/null || true
    wait "${FAKE_GITHUB_PID}" 2> /dev/null || true
    FAKE_GITHUB_PID=""
  fi
}

# How many times the broker has called the canned GitHub.
fixture_github_requests() {
  if [[ -f "${FAKE_GITHUB_LOG}" ]]; then
    grep -c . "${FAKE_GITHUB_LOG}" || true
  else
    echo 0
  fi
}

# fixture_config PATH KEY_PATH [OPTION...]
#
#   --socket PATH
#   --socket-mode MODE
#   --api-url URL
#
# Members are omitted rather than defaulted, so a case can exercise what the
# broker does when one is absent.
fixture_config() {
  local path="$1"
  local key_path="$2"
  shift 2

  local socket="" socket_mode="" api_url=""

  while [[ $# -gt 0 ]]; do
    case "$1" in
      --socket)
        socket="$2"
        shift 2
        ;;
      --socket-mode)
        socket_mode="$2"
        shift 2
        ;;
      --api-url)
        api_url="$2"
        shift 2
        ;;
      *)
        echo "fixture_config: unknown option $1" >&2
        return 1
        ;;
    esac
  done

  local socket_members=()
  if [[ -n "${socket}" ]]; then
    socket_members+=("      \"path\": \"${socket}\"")
    mkdir -p "$(dirname "${socket}")"
  fi
  if [[ -n "${socket_mode}" ]]; then
    socket_members+=("      \"mode\": \"${socket_mode}\"")
  fi

  local listen_members=()
  if [[ ${#socket_members[@]} -gt 0 ]]; then
    listen_members+=("    \"unix_socket\": {
$(join_members "${socket_members[@]}")
    }")
  fi

  local top_members=()
  top_members+=("  \"github_host\": \"github.com\"")
  if [[ -n "${api_url}" ]]; then
    top_members+=("  \"api_url\": \"${api_url}\"")
  fi
  top_members+=("  \"app_id\": ${FIXTURE_APP_ID}")
  top_members+=("  \"installation_id\": 789012")
  top_members+=("  \"private_key_path\": \"${key_path}\"")
  top_members+=("  \"listen\": {
$(join_members "${listen_members[@]}")
  }")
  top_members+=("  \"repositories\": {
    \"${SERVED_REPOSITORY}\": {
      \"permissions\": {
        \"contents\": \"write\",
        \"pull_requests\": \"write\"
      }
    }
  }")

  {
    echo "{"
    join_members "${top_members[@]}"
    echo "}"
  } > "${path}"
}

# Joins pre-indented JSON members with the separating commas.
join_members() {
  if [[ $# -eq 0 ]]; then
    return 0
  fi

  printf '%s' "$1"
  shift

  local member
  for member in "$@"; do
    printf ',\n%s' "${member}"
  done
  printf '\n'
}
