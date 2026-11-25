#!/usr/bin/env bash
# Two builds into separate directories must be byte-identical (ADR-0011 §2.8). Authority: Linux CI.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
cache="${SINGULARITY_MAP_CACHE:-$here/.cache}"
for i in 1 2; do
  if ! dotnet run -c Release --project "$here/MapBuilder" -- build --cache "$cache" --out "$tmp/out$i" --render "$tmp/render$i.png" > "$tmp/log$i.txt" 2>&1; then
    echo "build $i failed:" >&2; tail -n 40 "$tmp/log$i.txt" >&2; exit 1
  fi
done
diff -r "$tmp/out1" "$tmp/out2"
cmp "$tmp/render1.png" "$tmp/render2.png"
diff -r "$tmp/out1" "$here/../../mods/core/map" --exclude=.gitignore --exclude=arcs.bin --exclude=terrain.yaml
echo "determinism OK: two runs are byte-identical and match the committed tables"
