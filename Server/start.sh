#!/bin/bash
set -e

# Start stunnel SSL proxy in the background (wss://7772 → ws://localhost:7771)
stunnel /etc/stunnel/stunnel.conf &

# Run the game server as PID 1 so Docker stop signals reach it
exec /root/build/ServerBuild -batchmode -nographics $UNITY_COMMANDLINE_ARGS
