# shellcheck shell=bash
#
# The App key, the configuration file, and the TCP client credential.
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

fixture_credential() {
  openssl rand -hex 32 > "$1"
}

# A port nothing is listening on. The broker needs one written into its
# configuration before it binds, so it cannot ask the kernel for an ephemeral
# one, and a fixed number would collide with whatever else the machine runs.
fixture_free_port() {
  python3 -c 'import socket
probe = socket.socket()
probe.bind(("127.0.0.1", 0))
print(probe.getsockname()[1])
probe.close()'
}

# fixture_config PATH KEY_PATH [OPTION...]
#
#   --socket PATH
#   --socket-mode MODE
#   --api-url URL
#   --tcp ADDRESS PORT CREDENTIAL_PATH
#
# Members are omitted rather than defaulted, so a case can exercise what the
# broker does when one is absent.
fixture_config() {
  local path="$1"
  local key_path="$2"
  shift 2

  local socket="" socket_mode="" api_url=""
  local tcp_address="" tcp_port="" tcp_credential=""

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
      --tcp)
        tcp_address="$2"
        tcp_port="$3"
        tcp_credential="$4"
        shift 4
        ;;
      *)
        echo "fixture_config: unknown option $1" >&2
        return 1
        ;;
    esac
  done

  local listen_members=()
  if [[ -n "${socket}" ]]; then
    listen_members+=("    \"unix_socket\": \"${socket}\"")
    mkdir -p "$(dirname "${socket}")"
  fi
  if [[ -n "${socket_mode}" ]]; then
    listen_members+=("    \"unix_socket_mode\": \"${socket_mode}\"")
  fi
  if [[ -n "${tcp_address}" ]]; then
    listen_members+=("    \"tcp\": {
      \"address\": \"${tcp_address}\",
      \"port\": ${tcp_port},
      \"client_credential_path\": \"${tcp_credential}\"
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
