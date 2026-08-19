#!/usr/bin/env bash

set -Eeuo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(git -C "$script_dir/.." rev-parse --show-toplevel)"
hook_path="$repo_root/.githooks/post-commit"
publisher_path="$repo_root/scripts/publish-webgl.sh"

[[ -f "$hook_path" ]] || { echo "error: missing $hook_path" >&2; exit 1; }
[[ -f "$publisher_path" ]] || { echo "error: missing $publisher_path" >&2; exit 1; }

chmod +x "$hook_path" "$publisher_path" "$repo_root/scripts/install-publish-hook.sh"

echo "Checking this computer's local Unity and website-repository setup..."
"$publisher_path" --dry-run

git -C "$repo_root" config --local core.hooksPath .githooks

configured_path="$(git -C "$repo_root" config --local --get core.hooksPath)"
[[ "$configured_path" == ".githooks" ]] || { echo "error: Git hook configuration did not stick" >&2; exit 1; }

echo "Installed the Compersion [publish] trigger for this clone."
echo "A commit containing [publish] will now start its exact SHA as a local background release."
echo "Ordinary commits do nothing, and unsupported operating systems never fall back to GitHub Actions."
