#!/bin/bash
# Re-applies our custom Dockerfile to the Edgegap plugin's expected location.
# Run this if Unity reimports packages (Library/ gets wiped) or if the plugin updates.

PLUGIN_DIR="Library/PackageCache/com.edgegap.unity-servers-plugin@35356e28ab54/Editor"

if [ ! -d "$PLUGIN_DIR" ]; then
  echo "ERROR: Edgegap plugin not found at $PLUGIN_DIR"
  echo "Open Unity and let it reimport packages, then re-run this script."
  exit 1
fi

cp Server/Dockerfile "$PLUGIN_DIR/Dockerfile"
echo "Done — Dockerfile copied to $PLUGIN_DIR/Dockerfile"
