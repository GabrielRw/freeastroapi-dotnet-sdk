#!/usr/bin/env bash
# Everything CI checks, in order: generated code is current, build (warnings as errors; net10.0, net8.0
# and netstandard2.0), unit tests on net10.0, the synthetic-spec fixture build, and the NuGet package.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
scripts/generate.sh --check
dotnet build FreeAstroApi.slnx -c Release
dotnet test tests/FreeAstroApi.Tests -c Release --no-build --framework net10.0
scripts/check-fixture.sh
dotnet pack src/FreeAstroApi -c Release --no-build -o artifacts/packages
