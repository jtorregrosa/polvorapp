#!/usr/bin/env bash
# Runs one shard of the backend tests with coverage, as CI's backend-tests matrix does.
# Usage: scripts/backend-test-shard.sh <registry|catalog|rest> [extra `dotnet test` options]
#
# The first two shards are groups of PolvorApp.Api.Tests namespaces (each with its sub-namespaces);
# `rest` runs everything else in both test projects, so a new namespace can never be left out.
# Needs a Release build (`dotnet build backend/PolvorApp.slnx --configuration Release`) and Docker.
set -euo pipefail

shard=${1:?"usage: $0 <registry|catalog|rest> [dotnet test options]"}
shift

registry=(Registry Identity Privacy Images Badges)
catalog=(Catalog Orders Editions Billing Seeding)

# One filter for each namespace and one for its sub-namespaces.
filters() {
  local flag=$1
  shift
  for name in "$@"; do
    printf '%s\n' "$flag" "PolvorApp.Api.Tests.$name" "$flag" "PolvorApp.Api.Tests.$name.*"
  done
}

case "$shard" in
  registry)
    target=(--project tests/PolvorApp.Api.Tests)
    mapfile -t selection < <(filters --filter-namespace "${registry[@]}")
    ;;
  catalog)
    target=(--project tests/PolvorApp.Api.Tests)
    mapfile -t selection < <(filters --filter-namespace "${catalog[@]}")
    ;;
  rest)
    target=(--solution PolvorApp.slnx)
    mapfile -t selection < <(filters --filter-not-namespace "${registry[@]}" "${catalog[@]}")
    ;;
  *)
    echo "Unknown shard: $shard (expected registry, catalog or rest)" >&2
    exit 2
    ;;
esac

cd "$(dirname "$0")/../backend"
dotnet test "${target[@]}" --configuration Release --no-build \
  --coverlet --coverlet-output-format cobertura \
  --coverlet-include "[PolvorApp.*]*" --coverlet-exclude "[*Tests]*" \
  --results-directory TestResults \
  "${selection[@]}" "$@"
