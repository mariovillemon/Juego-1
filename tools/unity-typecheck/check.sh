#!/usr/bin/env bash
# Type-checks the Unity C# scripts (runtime, editor, EditMode tests) without the editor. See README.md here.
set -euo pipefail
cd "$(dirname "$0")"
dotnet build Tests.csproj -c Release -nologo -v q
echo "Scripts de Unity: sin errores de tipos (runtime, editor y tests)."
