#!/usr/bin/env bash
# Runs a command inside the pinned .NET SDK container, so no local .NET install is needed.
#
#   scripts/dotnet.sh dotnet test
#   SYNC_BACK="spec src/FreeAstroApi/Generated" scripts/dotnet.sh scripts/generate.sh
#
# The working tree is streamed into the container with tar instead of a bind mount, which also works
# with Colima, remote Docker daemons and external volumes that the Docker VM cannot see. Paths listed
# in SYNC_BACK are streamed back into the working tree after a successful run. NuGet packages are
# cached in the `freeastro-dotnet-nuget` Docker volume. DOCKER_RUN_ARGS adds `docker run` options, for
# example `--network ... -e PRISM_URL=...` to run the contract tests against a Prism container.
#
# On CI (or with .NET installed locally) run the inner commands directly instead.
set -euo pipefail

IMAGE="${DOTNET_SDK_IMAGE:-mcr.microsoft.com/dotnet/sdk:10.0.404@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SYNC_BACK="${SYNC_BACK:-}"
# shellcheck disable=SC2206
EXTRA_ARGS=(${DOCKER_RUN_ARGS:-})

if [ "$#" -eq 0 ]; then
  echo "usage: scripts/dotnet.sh <command> [args...]" >&2
  exit 64
fi

quoted=""
for arg in "$@"; do quoted+=" $(printf '%q' "$arg")"; done

inner="set -euo pipefail; mkdir -p /src; tar -x -C /src --no-same-owner 2>/dev/null; cd /src; git config --global --add safe.directory /src >/dev/null 2>&1 || true;"
inner+=" export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1;"
inner+=" (${quoted# }) 1>&2;"
if [ -n "$SYNC_BACK" ]; then
  inner+=" tar -c ${SYNC_BACK};"
fi

cd "$ROOT"
stream() {
  COPYFILE_DISABLE=1 tar -c --exclude=./artifacts --exclude='*/bin' --exclude='*/obj' --exclude=./.DS_Store . 2>/dev/null
}

if [ -n "$SYNC_BACK" ]; then
  stream | docker run --rm -i -v freeastro-dotnet-nuget:/root/.nuget/packages ${EXTRA_ARGS[@]+"${EXTRA_ARGS[@]}"} "$IMAGE" bash -c "$inner" | tar -x -C "$ROOT"
else
  stream | docker run --rm -i -v freeastro-dotnet-nuget:/root/.nuget/packages ${EXTRA_ARGS[@]+"${EXTRA_ARGS[@]}"} "$IMAGE" bash -c "$inner"
fi
