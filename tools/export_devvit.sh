#!/bin/bash

set -euo pipefail

UNITY_DEFAULT="/Applications/Unity/Hub/Editor/6000.2.8f1/Unity.app/Contents/MacOS/Unity"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
UNITY_EXECUTABLE="${UNITY_PATH:-$UNITY_DEFAULT}"
OUTPUT_DIR="${PROJECT_DIR}/DevvitExports"
BUILD_NAME=""

usage() {
  cat <<'EOF'
Usage: export_devvit.sh [options]

Options:
  -o, --output <path>   Destination folder where build artifacts will be copied.
  -n, --name <name>     Base file name for copied artifacts (defaults to Unity project product name).
  -u, --unity <path>    Path to the Unity executable (defaults to $UNITY_EXECUTABLE or UNITY_PATH env).
  -h, --help            Show this help message.

Example:
  ./tools/export_devvit.sh -o ../devvit-starter/src/client/Public/Build -n DevvitBuild
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -o|--output)
      OUTPUT_DIR="$2"
      shift 2
      ;;
    -n|--name)
      BUILD_NAME="$2"
      shift 2
      ;;
    -u|--unity)
      UNITY_EXECUTABLE="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage
      exit 1
      ;;
  esac
done

if [[ ! -x "$UNITY_EXECUTABLE" ]]; then
  echo "Unity executable not found or not executable: $UNITY_EXECUTABLE" >&2
  exit 1
fi

mkdir -p "$OUTPUT_DIR"

ARGS=(-batchmode -quit -projectPath "$PROJECT_DIR" -executeMethod Devvit.Editor.DevvitExporter.ExportForDevvit -devvitOutput "$OUTPUT_DIR")
if [[ -n "$BUILD_NAME" ]]; then
  ARGS+=(-devvitBuildName "$BUILD_NAME")
fi

echo "Running Unity export..."
"$UNITY_EXECUTABLE" "${ARGS[@]}"

echo "Artifacts copied to: $OUTPUT_DIR"
