#!/usr/bin/env python3
"""Makes sure mods/core/map/geo/arcs.bin exists and matches geo/manifest.json (ADR-0011 section 5: geo/ is generated, not stored in git).

  python3 tools/map/ensure_geo.py          build when missing or stale; exit 0 when the file is right

The build reads the pinned sources from SINGULARITY_MAP_CACHE (default ~/.cache/singularity/map, else tools/map/.cache). When the cache is
empty the sources are fetched once (network, SHA-256 checked). On any failure the script stops with a message that says what to do.
"""
from __future__ import annotations

import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GEO = ROOT / "mods" / "core" / "map" / "geo"


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def ok() -> bool:
    arcs = GEO / "arcs.bin"
    if not arcs.is_file():
        return False
    want = json.loads((GEO / "manifest.json").read_text())["files"]["arcs.bin"]["sha256"]
    return sha(arcs) == want


def dotnet() -> str:
    found = shutil.which("dotnet") or str(Path.home() / ".dotnet" / "dotnet")
    if not Path(found).exists():
        sys.exit("ensure_geo: dotnet not found (PATH or ~/.dotnet/dotnet)")
    return found


def main() -> int:
    if ok():
        return 0
    shared = Path.home() / ".cache" / "singularity" / "map"
    cache = Path(os.environ.get("SINGULARITY_MAP_CACHE") or (shared if shared.is_dir() else ROOT / "tools" / "map" / ".cache"))
    env = dict(os.environ, SINGULARITY_MAP_CACHE=str(cache), DOTNET_CLI_UI_LANGUAGE="en")
    proj = ["--project", str(ROOT / "tools" / "map" / "MapBuilder")]
    print(f"ensure_geo: building geo/arcs.bin (cache {cache}); about one minute", flush=True)
    if not cache.is_dir() or not any(cache.iterdir()):
        print("ensure_geo: the cache is empty, fetching the pinned sources (network)", flush=True)
        r = subprocess.run([dotnet(), "run", "-c", "Release", *proj, "--", "fetch", "--cache", str(cache)], cwd=ROOT, env=env)
        if r.returncode != 0:
            sys.exit(f"ensure_geo: fetch failed. Put the pinned sources into {cache} (see tools/map/README.md, command 'fetch') and run this script again.")
    with tempfile.TemporaryDirectory() as tmp:
        r = subprocess.run([dotnet(), "run", "-c", "Release", *proj, "--", "build", "--cache", str(cache), "--out", tmp],
                           cwd=ROOT, env=env, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        if r.returncode != 0:
            sys.exit("ensure_geo: the map build failed:\n" + "\n".join(r.stdout.splitlines()[-15:]) +
                     f"\nCheck that the cache {cache} holds the pinned sources ('MapBuilder fetch').")
        GEO.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(Path(tmp) / "geo" / "arcs.bin", GEO / "arcs.bin")
    if not ok():
        sys.exit("ensure_geo: the built arcs.bin differs from geo/manifest.json; the tables and the manifest are out of step. Rebuild with 'MapBuilder build' and commit the tables.")
    print("ensure_geo: geo/arcs.bin is ready")
    return 0


if __name__ == "__main__":
    sys.exit(main())
