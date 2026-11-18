#!/usr/bin/env bash
# ADR-0011 §2.9: the package graph of tools/map must not contain LGPL modules of NetTopologySuite or ProjNet.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
out="$(dotnet list "$here/MapBuilder/MapBuilder.csproj" package --include-transitive)"
echo "$out"
if echo "$out" | grep -Ei 'NetTopologySuite\.IO\.ShapeFile|CoordinateSystems|ProjNet'; then
  echo "forbidden LGPL package in the graph" >&2
  exit 1
fi
echo "package graph OK"
