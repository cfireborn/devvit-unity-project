#!/bin/bash
set -e

# Start nginx in the background (daemonizes itself)
nginx -c /etc/nginx/nginx.conf

# Run the game server as PID 1 so Docker stop signals reach it
exec /root/build/ServerBuild -batchmode -nographics $UNITY_COMMANDLINE_ARGS
