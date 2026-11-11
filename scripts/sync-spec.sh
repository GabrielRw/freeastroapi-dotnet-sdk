#!/usr/bin/env bash
# Fetches the production OpenAPI document into spec/freeastro.json (normalised by the shared
# scripts/spec_sync.py) and regenerates the SDK.
#   scripts/sync-spec.sh [url]
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
python3 scripts/spec_sync.py fetch "${1:-https://api.freeastroapi.com/openapi.json}" --out spec/freeastro.json
scripts/generate.sh
