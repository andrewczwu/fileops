#!/usr/bin/env bash
# Print the NUGET_PACKAGES assignment for the current environment (consumed via $GITHUB_ENV).
# On GitHub the cache lives in the workspace so actions/cache can restore it regardless of HOME;
# under act (local runs) the host's cache is mounted at /nuget-cache so builds work offline.
set -euo pipefail

if [[ -n "${ACT:-}" && -d /nuget-cache ]]; then
  echo "NUGET_PACKAGES=/nuget-cache"
else
  echo "NUGET_PACKAGES=${GITHUB_WORKSPACE:-$PWD}/.nuget/packages"
fi
