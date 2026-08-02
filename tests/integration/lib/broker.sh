# shellcheck shell=bash
#
# Starting and stopping the broker.
#
# Readiness is a log line, not a /health poll: the mode is applied after Kestrel
# binds, so health answers while the socket still carries whatever the umask
# left.
#
# The cases read these variables; shellcheck cannot see that from here.
# shellcheck disable=SC2034

BROKER_PID=""
BROKER_LOG=""
BROKER_CONFIG=""
BROKER_STATUS=0

# Signals the broker this case started, matched on its configuration path so a
# real broker on the same machine is never a candidate. runuser forks rather
# than execs on some builds, which is why $! alone will not do.
broker_signal() {
  if [[ -z "${BROKER_CONFIG}" ]]; then
    return 0
  fi

  pkill "$1" -f -- "${BROKER_CONFIG}" 2> /dev/null || true
}

# broker_start CONFIG LOG [LAUNCHER...] — LAUNCHER prefixes the command, as in
# `runuser -u broker -g brokers --`, and is empty to run as the current account.
broker_start() {
  local config="$1"
  local log="$2"
  shift 2

  BROKER_LOG="${log}"
  BROKER_CONFIG="${config}"
  "$@" github-token-broker --config "${config}" > "${log}" 2>&1 &
  BROKER_PID=$!
}

# broker_wait_log PATTERN [SECONDS] — waits for a startup line, giving up early
# if the process is already gone.
broker_wait_log() {
  local pattern="$1"
  local seconds="${2:-10}"

  local waited=0
  until grep -qE "${pattern}" "${BROKER_LOG}" 2> /dev/null; do
    if ! kill -0 "${BROKER_PID}" 2> /dev/null; then
      return 1
    fi

    if ((waited >= seconds * 10)); then
      return 1
    fi

    sleep 0.1
    waited=$((waited + 1))
  done
}

# Waits until the socket is bound and its mode has been applied.
broker_wait_socket() {
  broker_wait_log 'Listening on .* with mode '
}

# Sends one SIGTERM and records the status in BROKER_STATUS. broker_stop is the
# blunt version for teardown; this is for asserting how the broker exits.
broker_terminate() {
  if [[ -z "${BROKER_PID}" ]]; then
    return 0
  fi

  kill -TERM "${BROKER_PID}" 2> /dev/null || true

  BROKER_STATUS=0
  set +e
  wait "${BROKER_PID}"
  BROKER_STATUS=$?
  set -e
  BROKER_PID=""
  BROKER_CONFIG=""
}

broker_stop() {
  if [[ -z "${BROKER_PID}" ]]; then
    return 0
  fi

  kill -TERM "${BROKER_PID}" 2> /dev/null || true
  broker_signal -TERM

  wait "${BROKER_PID}" 2> /dev/null || true
  BROKER_PID=""
  BROKER_CONFIG=""
}

# Leaves the socket file behind, the way an unclean shutdown does.
broker_kill() {
  if [[ -z "${BROKER_PID}" ]]; then
    return 0
  fi

  broker_signal -KILL
  kill -KILL "${BROKER_PID}" 2> /dev/null || true
  wait "${BROKER_PID}" 2> /dev/null || true
  BROKER_PID=""
  BROKER_CONFIG=""
}

broker_log() {
  if [[ -n "${BROKER_LOG}" && -f "${BROKER_LOG}" ]]; then
    cat "${BROKER_LOG}"
  fi
}
