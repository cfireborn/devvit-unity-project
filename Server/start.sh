#!/bin/bash
set -e

# Start Cloudflare Tunnel in the background (wss://compersion.charliefeuerborn.com → ws://localhost:7771)
cloudflared tunnel --config /etc/cloudflared/config.yml run &

# Run the game server as PID 1 so Docker stop signals reach it
exec /root/build/ServerBuild -batchmode -nographics $UNITY_COMMANDLINE_ARGS
