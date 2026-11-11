#!/usr/bin/env bash
# Regenerates src/FreeAstroApi/Generated from spec/freeastro.json.
#   scripts/generate.sh            write the generated files
#   scripts/generate.sh --check    fail if they are out of date
# Without a local .NET SDK: SYNC_BACK=src/FreeAstroApi/Generated scripts/dotnet.sh scripts/generate.sh
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
dotnet run --project tools/FreeAstroApi.Generator -c Release -- generate "$@"
