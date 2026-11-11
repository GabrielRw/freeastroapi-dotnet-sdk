#!/usr/bin/env bash
# Generates the SDK from tests/fixtures/synthetic-spec.json and compiles it (net8.0 and netstandard2.0,
# warnings as errors), proving the pipeline handles spec features production does not use yet.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
rm -rf artifacts/fixture-generated
dotnet run --project tools/FreeAstroApi.Generator -c Release -- generate \
  --spec tests/fixtures/synthetic-spec.json --out artifacts/fixture-generated
dotnet build tests/FreeAstroApi.FixtureBuild -c Release
