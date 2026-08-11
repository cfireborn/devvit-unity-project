#!/bin/bash
# Re-applies the secured Dockerfile and post-upload dashboard behavior to the
# Edgegap plugin cache. Run after Unity reimports or updates packages.

set -Eeuo pipefail

shopt -s nullglob
plugin_dirs=(Library/PackageCache/com.edgegap.unity-servers-plugin@*/Editor)
if [ "${#plugin_dirs[@]}" -ne 1 ]; then
  echo "ERROR: Expected exactly one Edgegap plugin cache, found ${#plugin_dirs[@]}."
  echo "Open Unity and let it reimport packages, then re-run this script."
  exit 1
fi
plugin_dir="${plugin_dirs[0]}"
plugin_window="${plugin_dir}/EdgegapWindowV2.cs"
stable_version="26.08.11-watchdog-secure"

if grep -Eq 'cloudflare-credentials|cloudflare-tunnel\.yml|releases/latest' Server/Dockerfile; then
  echo "ERROR: Server/Dockerfile contains a legacy credential copy or mutable download."
  exit 1
fi

cp Server/Dockerfile "${plugin_dir}/Dockerfile"

python3 - "${plugin_window}" "${stable_version}" <<'PY'
from pathlib import Path
import re
import sys

path = Path(sys.argv[1])
stable_version = sys.argv[2]
text = path.read_text()
replacement = f'''OpenEdgegapURL(
                    $"{{EdgegapWindowMetadata.EDGEGAP_CREATE_APP_BASE_URL}}{{_createAppNameInput.value}}/versions/{stable_version}/details"
                );'''
pattern = re.compile(
    r'''OpenEdgegapURL\(\s*string\.Join\(\s*"",\s*new string\[\]\s*\{.*?\}\s*\)\s*\)\s*;\s*;''',
    re.DOTALL,
)
if stable_version in text and not pattern.search(text):
    pass
else:
    text, count = pattern.subn(replacement, text, count=1)
    if count != 1:
        raise SystemExit("ERROR: Edgegap upload flow changed; refusing an unsafe partial patch.")
    path.write_text(text)
PY

cmp -s Server/Dockerfile "${plugin_dir}/Dockerfile" || {
  echo "ERROR: Dockerfile verification failed."
  exit 1
}
grep -Fq "versions/${stable_version}/details" "${plugin_window}" || {
  echo "ERROR: Stable-version browser flow verification failed."
  exit 1
}

echo "Done — secured Dockerfile installed."
echo "After upload, Chrome opens ${stable_version}; select the new tag and click Save."
