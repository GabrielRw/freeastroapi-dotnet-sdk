#!/usr/bin/env bash
# Everything CI checks, in order: generated code is current, build (warnings as errors), tests,
# the synthetic-spec fixture build, and the NuGet package.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
scripts/generate.sh --check
dotnet build FreeAstroApi.slnx -c Release
dotnet test FreeAstroApi.slnx -c Release --no-build
scripts/check-fixture.sh
dotnet pack src/FreeAstroApi -c Release --no-build -o artifacts/packages
