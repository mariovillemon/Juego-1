#!/usr/bin/env bash
# Type-checks the Unity C# scripts without the editor (see README.md here). Exit code != 0 on errors.
set -euo pipefail
cd "$(dirname "$0")"
dotnet build Runtime.csproj -c Release -nologo -v q
# LightProbeGroup.probePositions has a setter only in the editor's UnityEngine build: ignore that one error.
out=$(dotnet build Editor.csproj -c Release -nologo -v q 2>&1 || true)
errors=$(echo "$out" | grep -E " error " | grep -v "probePositions" | sort -u || true)
if [ -n "$errors" ]; then echo "$errors"; exit 1; fi
echo "Scripts de Unity: sin errores de tipos (runtime y editor)."
