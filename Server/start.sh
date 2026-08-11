#!/bin/bash
set -Eeuo pipefail

# CF_TUNNEL_TOKEN must be injected as an Edgegap secret environment variable.
# It is deliberately never copied into the container image or source repository.
: "${CF_TUNNEL_TOKEN:?CF_TUNNEL_TOKEN must be provided at runtime}"
token_file=/run/compersion-cloudflared.token
install -m 0600 -o tunnel -g tunnel /dev/null "${token_file}"
printf '%s' "${CF_TUNNEL_TOKEN}" > "${token_file}"
unset CF_TUNNEL_TOKEN

# Keep the credential out of command-line arguments and out of the Unity
# process. The file is readable only by the dedicated tunnel account.
setpriv --reuid=tunnel --regid=tunnel --init-groups \
    cloudflared tunnel --no-autoupdate run --token-file "${token_file}" &
cloudflared_pid=$!

# Supervise both critical processes. If either exits, terminate the other and let
# Edgegap restart/replace the container instead of leaving an unreachable server alive.
unity_args=(-batchmode -nographics)
if [[ -n "${UNITY_COMMANDLINE_ARGS:-}" ]]; then
    read -r -a extra_unity_args <<< "${UNITY_COMMANDLINE_ARGS}"
    unity_args+=("${extra_unity_args[@]}")
fi

setpriv --reuid=gameserver --regid=gameserver --init-groups \
    env HOME=/var/lib/compersion XDG_CONFIG_HOME=/var/lib/compersion/.config \
    /opt/compersion/ServerBuild "${unity_args[@]}" &
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
