#!/usr/bin/env bash
# Two builds into separate directories must be byte-identical (ADR-0011 §2.8). Authority: Linux CI.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
cache="${SINGULARITY_MAP_CACHE:-$HOME/.cache/singularity/map}"
for i in 1 2; do
  if ! dotnet run -c Release --project "$here/MapBuilder" -- build --cache "$cache" --out "$tmp/out$i" --render "$tmp/render$i.png" > "$tmp/log$i.txt" 2>&1; then
    echo "build $i failed:" >&2; tail -n 40 "$tmp/log$i.txt" >&2; exit 1
  fi
done
diff -r "$tmp/out1" "$tmp/out2"
cmp "$tmp/render1.png" "$tmp/render2.png"
diff -r "$tmp/out1" "$here/../../mods/core/map" --exclude=.gitignore --exclude=arcs.bin --exclude=terrain.yaml
echo "determinism OK: two runs are byte-identical and match the committed tables"
# Opt-in (about 4 GB of terrain rasters in the cache, a few minutes per bake): TERRAIN_CHECK=1 bash tools/map/check_determinism.sh
# Two terrain bakes must be byte-identical and equal to the committed mods/core/map/terrain.yaml. The cache comes from
# SINGULARITY_MAP_CACHE or ~/.cache/singularity/map; fill it with: dotnet run --project tools/map/MapBuilder -- fetch --terrain
if [ "${TERRAIN_CHECK:-0}" = "1" ]; then
  tcache="${SINGULARITY_MAP_CACHE:-$HOME/.cache/singularity/map}"
  for i in 1 2; do
    mkdir -p "$tmp/terrain$i"
    if ! dotnet run -c Release --project "$here/MapBuilder" -- terrain --cache "$tcache" --out "$tmp/terrain$i" > "$tmp/tlog$i.txt" 2>&1; then
      echo "terrain bake $i failed:" >&2; tail -n 40 "$tmp/tlog$i.txt" >&2; exit 1
    fi
  done
  cmp "$tmp/terrain1/terrain.yaml" "$tmp/terrain2/terrain.yaml"
  cmp "$tmp/terrain1/terrain.yaml" "$here/../../mods/core/map/terrain.yaml"
  echo "terrain determinism OK: two bakes are byte-identical and match the committed terrain.yaml"
fi
