#!/usr/bin/env bash

set -Eeuo pipefail

usage() {
  cat <<'EOF'
Build and publish a committed Compersion WebGL release locally on macOS or
Windows (through Git for Windows / Git Bash).

Usage:
  ./scripts/publish-webgl.sh [OPTIONS] [SOURCE_REF]

Options:
  --background  Detach from the terminal and write progress to Logs/WebGLPublish.
  --build-only  Build and validate locally without changing or pushing the website.
  --dry-run     Resolve the next release using local refs without building or fetching.
  --help        Show this help.

Examples:
  ./scripts/publish-webgl.sh
  ./scripts/publish-webgl.sh --background
  ./scripts/publish-webgl.sh --build-only
  ./scripts/publish-webgl.sh --background --build-only
  ./scripts/publish-webgl.sh 947348e

Environment overrides:
  SITE_REPO   Path to ramborngames.github.io.
              Default: a sibling of this repository.
  UNITY_BIN   Path to the Unity executable.
              Default: Unity Hub's standard installation for this OS,
              matching ProjectVersion.txt.

The next production number comes from successful publications on website
origin/main. The Unity build comes from an immutable commit in a temporary Git
worktree, so the open Editor and uncommitted project changes are not disturbed.
EOF
}

fail() {
  echo "error: $*" >&2
  exit 1
}

background=0
build_only=0
dry_run=0
while [[ "${1:-}" == --* ]]; do
  case "$1" in
    --background) background=1 ;;
    --build-only) build_only=1 ;;
    --dry-run) dry_run=1 ;;
    --help)
      usage
      exit 0
      ;;
    *)
      usage >&2
      fail "unknown option: $1"
      ;;
  esac
  shift
done

(( background == 0 || dry_run == 0 )) || fail "--background and --dry-run cannot be combined"
(( build_only == 0 || dry_run == 0 )) || fail "--build-only and --dry-run cannot be combined"

source_ref="${1:-HEAD}"
[[ -z "${2:-}" ]] || fail "expected at most one SOURCE_REF argument"

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(git -C "$script_dir/.." rev-parse --show-toplevel)"
source_sha="$(git -C "$repo_root" rev-parse --verify "${source_ref}^{commit}" 2>/dev/null)" \
  || fail "source ref '$source_ref' does not resolve to a commit"
source_short_sha="$(git -C "$repo_root" rev-parse --short "$source_sha")"

kernel_name="$(uname -s)"
case "$kernel_name" in
  Darwin)
    execution_platform="macOS"
    release_date="$(TZ=America/New_York date +%Y.%m.%d)"
    ;;
  MINGW*|MSYS*|CYGWIN*)
    execution_platform="Windows"
    command -v powershell.exe >/dev/null 2>&1 \
      || fail "PowerShell is required to resolve the New York release date on Windows"
    release_date="$(powershell.exe -NoProfile -Command \
      '[TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([DateTime]::UtcNow, "Eastern Standard Time").ToString("yyyy.MM.dd")' \
      | tr -d '\r')"
    ;;
  *)
    fail "unsupported operating system '$kernel_name'; publishing is supported on macOS and Windows and no GitHub Actions fallback was started"
    ;;
esac

IFS=. read -r release_year release_month release_day <<< "$release_date"
month_number=$((10#$release_month))
day_number=$((10#$release_day))

case "$month_number" in
  1) month_name=jan; maximum_day=31 ;;
  2)
    month_name=feb
    maximum_day=28
    if (( release_year % 400 == 0 || (release_year % 4 == 0 && release_year % 100 != 0) )); then
      maximum_day=29
    fi
    ;;
  3) month_name=mar; maximum_day=31 ;;
  4) month_name=apr; maximum_day=30 ;;
  5) month_name=may; maximum_day=31 ;;
  6) month_name=jun; maximum_day=30 ;;
  7) month_name=jul; maximum_day=31 ;;
  8) month_name=aug; maximum_day=31 ;;
  9) month_name=sep; maximum_day=30 ;;
  10) month_name=oct; maximum_day=31 ;;
  11) month_name=nov; maximum_day=30 ;;
  12) month_name=dec; maximum_day=31 ;;
  *) fail "invalid release month in $release_date" ;;
esac

(( day_number >= 1 && day_number <= maximum_day )) || fail "invalid release day in $release_date"

project_version="$(git -C "$repo_root" show "$source_sha:ProjectSettings/ProjectVersion.txt" \
  | awk -F': ' '/^m_EditorVersion:/{print $2; exit}')"
[[ -n "$project_version" ]] || fail "could not read the Unity version from ProjectSettings/ProjectVersion.txt"

if [[ -n "${UNITY_BIN:-}" ]]; then
  unity_bin="$UNITY_BIN"
elif [[ "$execution_platform" == "macOS" ]]; then
  unity_bin="/Applications/Unity/Hub/Editor/$project_version/Unity.app/Contents/MacOS/Unity"
else
  command -v cygpath >/dev/null 2>&1 || fail "cygpath is required on Windows"
  windows_program_files="${PROGRAMFILES:-C:\\Program Files}"
  program_files_path="$(cygpath -u "$windows_program_files")"
  unity_bin="$program_files_path/Unity/Hub/Editor/$project_version/Editor/Unity.exe"
fi
site_repo_input="${SITE_REPO:-$(dirname "$repo_root")/ramborngames.github.io}"
[[ -d "$site_repo_input" ]] || fail "SITE_REPO does not exist: $site_repo_input"
site_repo="$(cd "$site_repo_input" && pwd -P)"
published_site_target="$site_repo/compersion"
build_profile="Assets/Settings/Build Profiles/Web - Mobile - Release.asset"

git -C "$repo_root" cat-file -e "${source_sha}:${build_profile}" 2>/dev/null \
  || fail "source commit '$source_short_sha' does not contain the release build profile"
[[ -x "$unity_bin" ]] || fail "Unity $project_version was not found at '$unity_bin' (set UNITY_BIN to override)"
[[ -f "$repo_root/$build_profile" ]] || fail "missing build profile: $build_profile"
[[ "$(git -C "$site_repo" rev-parse --show-toplevel 2>/dev/null || true)" == "$site_repo" ]] \
  || fail "SITE_REPO must be the root of a Git checkout: $site_repo"
[[ "$(git -C "$site_repo" rev-parse --is-shallow-repository)" == "false" ]] \
  || fail "SITE_REPO must have full history; run 'git fetch --unshallow origin' first"
site_origin="$(git -C "$site_repo" remote get-url origin 2>/dev/null || true)"
[[ "$site_origin" == *ramborngames.github.io* ]] \
  || fail "SITE_REPO origin does not look like ramborngames.github.io: $site_origin"
site_git_dir_value="$(git -C "$site_repo" rev-parse --git-common-dir)"
case "$site_git_dir_value" in
  /*) site_git_dir="$site_git_dir_value" ;;
  *) site_git_dir="$(cd "$site_repo/$site_git_dir_value" && pwd -P)" ;;
esac
publish_lock="$site_git_dir/compersion-webgl-publish.lock"

find_highest_release_today() {
  highest_release=0
  release_date_pattern="${release_date//./\\.}"

  while IFS= read -r published_path; do
    published_file="${published_path##*/}"
    if [[ "$published_file" =~ ^${release_date_pattern}_build([1-9][0-9]*)_compersion2d\.loader\.js$ ]]; then
      candidate_release=$((10#${BASH_REMATCH[1]}))
      if (( candidate_release > highest_release )); then
        highest_release=$candidate_release
      fi
    fi
  done < <(git -C "$site_repo" log --name-only --pretty=format: origin/main -- compersion/Build)
}

resolve_release_metadata() {
  find_highest_release_today
  build_number=$((highest_release + 1))
  build_name="${release_date}_build${build_number}_compersion2d"
  commit_message="Updated to ${month_name} ${day_number} build ${build_number}"
}

show_release_plan() {
  echo "Execution:      local $execution_platform"
  echo "Unity:          $project_version"
  echo "Source:         $source_ref ($source_short_sha, pinned)"
  echo "Previous today: build$highest_release"
  echo "Next release:   build$build_number"
  echo "Build name:     $build_name"
  echo "Website target: $published_site_target"
  echo "Commit message: $commit_message"
}

if (( dry_run )); then
  git -C "$site_repo" rev-parse --verify origin/main >/dev/null 2>&1 \
    || fail "SITE_REPO has no local origin/main ref"
  resolve_release_metadata
  show_release_plan
  echo "Dry run complete using the local origin/main ref; no fetch, build, website change, commit, or push was performed."
  exit 0
fi

if (( background )); then
  git -C "$site_repo" rev-parse --verify origin/main >/dev/null 2>&1 \
    || fail "SITE_REPO has no local origin/main ref"
  resolve_release_metadata
  show_release_plan
  log_dir="$repo_root/Logs/WebGLPublish"
  mkdir -p "$log_dir"
  background_log="$log_dir/webgl-publisher-${release_date}-$$-${source_short_sha}.log"
  background_args=()
  if (( build_only )); then
    background_args+=("--build-only")
  fi
  background_args+=("$source_sha")

  nohup "$script_dir/$(basename "${BASH_SOURCE[0]}")" "${background_args[@]}" \
    > "$background_log" 2>&1 < /dev/null &
  publisher_pid=$!
  echo "Publisher started locally in the background as PID $publisher_pid."
  echo "Follow its output with: tail -f '$background_log'"
  exit 0
fi

if ! mkdir "$publish_lock" 2>/dev/null; then
  existing_pid="$(sed -n '1p' "$publish_lock/pid" 2>/dev/null || true)"
  if [[ "$existing_pid" =~ ^[0-9]+$ ]] && kill -0 "$existing_pid" 2>/dev/null; then
    fail "local publisher PID $existing_pid is already running"
  fi
  fail "stale publisher lock found at '$publish_lock'; verify no publisher is running, then remove that lock directory"
fi
echo "$$" > "$publish_lock/pid"

temp_parent="${TMPDIR:-/tmp}"
if [[ "$execution_platform" == "Windows" && "$temp_parent" =~ ^[A-Za-z]:[\\/] ]]; then
  temp_parent="$(cygpath -u "$temp_parent")"
fi
if ! temp_root="$(mktemp -d "$temp_parent/compersion-webgl-publish.XXXXXX")"; then
  rm -f -- "$publish_lock/pid"
  rmdir "$publish_lock" >/dev/null 2>&1 || true
  fail "could not create the temporary release directory"
fi
project_checkout="$temp_root/project"
site_checkout="$temp_root/website"
project_worktree_added=0
site_worktree_added=0
preserve_site_recovery=0

cleanup() {
  cleanup_status=$?
  trap - EXIT INT TERM

  if (( project_worktree_added )); then
    git -C "$repo_root" worktree remove --force "$project_checkout" >/dev/null 2>&1 || true
  fi

  if (( site_worktree_added && preserve_site_recovery == 0 )); then
    git -C "$site_repo" worktree remove --force "$site_checkout" >/dev/null 2>&1 || true
  fi

  if (( preserve_site_recovery == 0 )); then
    case "$temp_root" in
      "$temp_parent"/compersion-webgl-publish.*)
        rm -rf -- "$temp_root"
        ;;
      *)
        echo "warning: refusing to remove unexpected temporary path '$temp_root'" >&2
        ;;
    esac
  fi

  rm -f -- "$publish_lock/pid"
  rmdir "$publish_lock" >/dev/null 2>&1 || true
  exit "$cleanup_status"
}
trap cleanup EXIT INT TERM

echo "Synchronizing the website release history..."
git -C "$site_repo" fetch --quiet origin main
site_start_sha="$(git -C "$site_repo" rev-parse origin/main)"
resolve_release_metadata
show_release_plan

echo "Creating an isolated project checkout so the open Unity Editor is not disturbed..."
git -C "$repo_root" worktree add --detach "$project_checkout" "$source_sha"
project_worktree_added=1

main_version_path="$repo_root/Assets/Resources/BuildVersion.txt"
main_version_meta_path="$main_version_path.meta"
worktree_version_path="$project_checkout/Assets/Resources/BuildVersion.txt"
worktree_version_meta_path="$worktree_version_path.meta"

version_fingerprint() {
  if [[ -f "$1" ]]; then
    git hash-object --no-filters "$1"
  else
    echo missing
  fi
}

initial_version_fingerprint="$(version_fingerprint "$main_version_path")"
if [[ -f "$main_version_path" ]]; then
  mkdir -p "$(dirname "$worktree_version_path")"
  cp "$main_version_path" "$worktree_version_path"
  if [[ -f "$main_version_meta_path" ]]; then
    cp "$main_version_meta_path" "$worktree_version_meta_path"
  fi
  echo "Seeded the isolated build with Unity's current internal run counter."
fi

build_output="$temp_root/$build_name"
log_dir="$repo_root/Logs/WebGLPublish"
mkdir -p "$log_dir"
log_path="$log_dir/${build_name}-${source_short_sha}.log"

echo "Building WebGL locally. Full Unity log: $log_path"
"$unity_bin" \
  -batchmode \
  -nographics \
  -quit \
  -accept-apiupdate \
  -projectPath "$project_checkout" \
  -activeBuildProfile "$build_profile" \
  -build "$build_output" \
  -logFile "$log_path"

[[ -s "$build_output/index.html" ]] || fail "Unity did not produce index.html; inspect $log_path"
[[ -d "$build_output/Build" ]] || fail "Unity did not produce the WebGL Build directory; inspect $log_path"
[[ -d "$build_output/TemplateData" ]] || fail "Unity did not produce TemplateData; inspect $log_path"
[[ -d "$build_output/StreamingAssets" ]] || fail "Unity did not produce StreamingAssets; inspect $log_path"
[[ -s "$build_output/ServiceWorker.js" ]] || fail "Unity did not produce ServiceWorker.js; inspect $log_path"
[[ -s "$build_output/manifest.webmanifest" ]] || fail "Unity did not produce manifest.webmanifest; inspect $log_path"
[[ -s "$build_output/Build/$build_name.loader.js" ]] || fail "Unity did not produce the expected loader; inspect $log_path"
[[ -s "$build_output/Build/$build_name.data.unityweb" ]] || fail "Unity did not produce the expected data archive; inspect $log_path"
[[ -s "$build_output/Build/$build_name.framework.js.unityweb" ]] || fail "Unity did not produce the expected framework; inspect $log_path"
[[ -s "$build_output/Build/$build_name.wasm.unityweb" ]] || fail "Unity did not produce the expected WebAssembly binary; inspect $log_path"
grep -q "$build_name.loader.js" "$build_output/index.html" \
  || fail "index.html does not reference the expected loader; inspect $log_path"

[[ -f "$worktree_version_path" ]] || fail "the build did not generate its internal BuildVersion resource"
built_internal_version="$(sed -n '1p' "$worktree_version_path")"
current_version_fingerprint="$(version_fingerprint "$main_version_path")"
if [[ "$current_version_fingerprint" == "$initial_version_fingerprint" ]]; then
  cp "$worktree_version_path" "$main_version_path"
  if [[ ! -f "$main_version_meta_path" && -f "$worktree_version_meta_path" ]]; then
    cp "$worktree_version_meta_path" "$main_version_meta_path"
  fi
  echo "Advanced Unity's local internal counter to $built_internal_version"
else
  echo "warning: the open Editor advanced BuildVersion.txt during the build; its newer local counter was preserved" >&2
fi

echo "Validated the complete local WebGL/PWA output for $build_name."
if (( build_only )); then
  echo "Build-only verification succeeded; the website was not changed, committed, or pushed."
  exit 0
fi

echo "Confirming no other publisher changed the website while Unity was building..."
git -C "$site_repo" fetch --quiet origin main
current_remote_site_sha="$(git -C "$site_repo" rev-parse origin/main)"
[[ "$current_remote_site_sha" == "$site_start_sha" ]] \
  || fail "website origin/main changed during the build; publish again to allocate the next number"

find_highest_release_today
(( highest_release == build_number - 1 )) \
  || fail "another release claimed build$build_number during this build; publish again to allocate the next number"

echo "Preparing the website commit in a separate temporary worktree..."
git -C "$site_repo" worktree add --detach "$site_checkout" "$site_start_sha"
site_worktree_added=1
site_publish_target="$site_checkout/compersion"
mkdir -p "$site_publish_target"
touch "$site_publish_target/.nojekyll"
find "$site_publish_target" -mindepth 1 -maxdepth 1 ! -name '.nojekyll' -exec rm -rf -- {} +
cp -R "$build_output/." "$site_publish_target/"
find "$site_publish_target" -type f -name '.DS_Store' -delete
touch "$site_publish_target/.nojekyll"

git -C "$site_checkout" add -A -- compersion
if git -C "$site_checkout" diff --cached --quiet -- compersion; then
  echo "The generated WebGL site is unchanged; nothing to commit or push."
  exit 0
fi

git -C "$site_checkout" commit -m "$commit_message" -- compersion
publish_commit="$(git -C "$site_checkout" rev-parse HEAD)"
if ! git -C "$site_checkout" push origin HEAD:main; then
  preserve_site_recovery=1
  fail "push failed; release commit $publish_commit is preserved. Retry with: git -C '$site_checkout' push origin HEAD:main"
fi

if [[ "$(git -C "$site_repo" branch --show-current)" == "main" ]] \
  && [[ -z "$(git -C "$site_repo" status --porcelain)" ]] \
  && [[ "$(git -C "$site_repo" rev-parse main)" == "$site_start_sha" ]]; then
  if git -C "$site_repo" merge --ff-only "$publish_commit" >/dev/null; then
    echo "Fast-forwarded the local website checkout to the published commit."
  else
    echo "warning: production was pushed, but the local website checkout could not be fast-forwarded" >&2
  fi
else
  echo "The local website checkout changed during the build and was left untouched; origin/main contains the release."
fi

echo "Published $build_name successfully from this Mac."
