#!/bin/bash
set -Eeuo pipefail

# CF_TUNNEL_TOKEN must be injected as an Edgegap secret environment variable.
# It is deliberately never copied into the container image or source repository.
: "${CF_TUNNEL_TOKEN:?CF_TUNNEL_TOKEN must be provided at runtime}"
cloudflared tunnel --no-autoupdate run --token "${CF_TUNNEL_TOKEN}" &
cloudflared_pid=$!

# Supervise both critical processes. If either exits, terminate the other and let
# Edgegap restart/replace the container instead of leaving an unreachable server alive.
unity_args=(-batchmode -nographics)
if [[ -n "${UNITY_COMMANDLINE_ARGS:-}" ]]; then
    read -r -a extra_unity_args <<< "${UNITY_COMMANDLINE_ARGS}"
    unity_args+=("${extra_unity_args[@]}")
fi

/root/build/ServerBuild "${unity_args[@]}" &
server_pid=$!

shutdown() {
    trap - TERM INT EXIT
    kill -TERM "${server_pid}" "${cloudflared_pid}" 2>/dev/null || true
    wait "${server_pid}" "${cloudflared_pid}" 2>/dev/null || true
}

trap shutdown TERM INT EXIT
set +e
wait -n "${server_pid}" "${cloudflared_pid}"
exit_code=$?
set -e
shutdown
exit "${exit_code}"
