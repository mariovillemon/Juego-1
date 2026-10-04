#!/usr/bin/env bash
# Builds the Windows x64 player without opening the editor UI.
# Usage: UNITY=/path/to/Unity tools/build-windows.sh [--development] [output dir]
# Output (default): Builds/Windows/Taller.exe. Log: Builds/build-windows.log
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="${UNITY:-}"
if [ -z "$UNITY" ]; then
  for c in "$HOME"/Unity/Hub/Editor/6000.0.*/Editor/Unity /Applications/Unity/Hub/Editor/6000.0.*/Unity.app/Contents/MacOS/Unity; do
    [ -x "$c" ] && UNITY="$c"
  done
fi
[ -n "$UNITY" ] || { echo "No encuentro Unity 6000.0: define la variable UNITY con la ruta del ejecutable." >&2; exit 2; }
DEV=()
if [ "${1:-}" = "--development" ]; then DEV=(-development); shift; fi
OUT="${1:-$ROOT/Builds/Windows}"
"$ROOT/tools/sync-sim-to-unity.sh"
mkdir -p "$ROOT/Builds"
"$UNITY" -batchmode -quit -projectPath "$ROOT/Unity" -buildTarget Win64 \
  -executeMethod Garage.Unity.EditorTools.BuildWindows.CommandLine -buildPath "$OUT" "${DEV[@]}" \
  -logFile "$ROOT/Builds/build-windows.log"
echo "Build lista: $OUT/Taller.exe"
