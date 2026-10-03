#!/usr/bin/env bash
# Copies the simulation sources and the game data into the Unity project (idempotent).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEST="$ROOT/Unity/Assets/Garage/Sim/Generated"
rm -rf "$DEST"
for p in Garage.Sim Garage.Data; do
  mkdir -p "$DEST/$p"
  (cd "$ROOT/src/$p" && find . -name '*.cs' -not -path './obj/*' -not -path './bin/*' -print0 | while IFS= read -r -d '' f; do
     mkdir -p "$DEST/$p/$(dirname "$f")"; cp "$f" "$DEST/$p/$f"; done)
done
DATA="$ROOT/Unity/Assets/StreamingAssets/data"
rm -rf "$DATA"; mkdir -p "$DATA"
cp -r "$ROOT/data/base" "$ROOT/data/schemas" "$DATA/"
echo "Sincronizado: $(find "$DEST" -name '*.cs' | wc -l) ficheros .cs y datos en StreamingAssets/data"
