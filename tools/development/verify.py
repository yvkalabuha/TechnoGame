#!/usr/bin/env python3
"""Local equivalent of CI for the tree this script lives in.

  python3 tools/development/verify.py            registry, hooks, build, tests, lab selftest
  python3 tools/development/verify.py --full     + lab/analysis tests, llm_worker tests
  python3 tools/development/verify.py --map      + MapBuilder tests, package graph, determinism
  python3 tools/development/verify.py --only build,test

Run it in a clean worktree of the commit you want to check (land.py does that).
Exit code 0 only if every step passed. Each step prints one line; a failed
step prints the tail of its output.
"""
from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SHARED_MAP_CACHE = Path.home() / ".cache" / "singularity" / "map"


def dotnet() -> str:
    found = shutil.which("dotnet")
    if found:
        return found
    home = Path.home() / ".dotnet" / "dotnet"
    if home.exists():
        return str(home)
    sys.exit("dotnet not found (PATH or ~/.dotnet/dotnet)")


def steps(full: bool, with_map: bool) -> list[tuple[str, list[str]]]:
    dn = dotnet()
    py = sys.executable
    out: list[tuple[str, list[str]]] = [
        ("geo", [py, "tools/map/ensure_geo.py"]),   # arcs.bin is generated (ADR-0011 section 5); the client and its tests read it
        ("registry", [py, "tools/development/registry.py", "check"]),
        ("hooks", [py, "tools/development/test_hooks.py"]),
        ("kb_db", [py, "-m", "unittest", "tools/development/test_kb_db.py"]),  # database tests skip without PostgreSQL
        ("build", [dn, "build", "Singularity.sln", "-c", "Release", "-v", "q", "-nologo"]),
        ("test", [dn, "test", "Singularity.sln", "-c", "Release", "--no-build", "-v", "q", "-nologo"]),
        ("selftest", [dn, "src/Singularity.Lab/bin/Release/net10.0/Singularity.Lab.dll", "selftest"]),
    ]
    if full:
        out += [
            ("analysis", [py, "-m", "unittest", "discover", "-s", "lab/analysis", "-p", "test_*.py"]),
            ("llm_worker", [py, "-m", "unittest", "discover", "-s", "tools/llm_worker/tests"]),
        ]
    if with_map:
        out += [
            ("map-packages", ["bash", "tools/map/check_packages.sh"]),
            ("map-tests", [dn, "test", "tests/Singularity.MapBuilder.Tests", "-c", "Release", "-v", "q", "-nologo"]),
            ("map-determinism", ["bash", "tools/map/check_determinism.sh"]),
        ]
    return out


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--full", action="store_true", help="also lab/analysis and llm_worker tests")
    ap.add_argument("--map", action="store_true", help="also the map tooling job")
    ap.add_argument("--only", help="comma-separated step names")
    args = ap.parse_args()

    env = dict(os.environ)
    env.setdefault("DOTNET_CLI_UI_LANGUAGE", "en")
    if "SINGULARITY_MAP_CACHE" not in env and SHARED_MAP_CACHE.is_dir():
        env["SINGULARITY_MAP_CACHE"] = str(SHARED_MAP_CACHE)

    plan = steps(args.full, args.map)
    if args.only:
        wanted = set(args.only.split(","))
        plan = [s for s in plan if s[0] in wanted]
    failed: list[str] = []
    print(f"verify: {ROOT}")
    for name, cmd in plan:
        t0 = time.time()
        # DEBT-0060: one Lab selftest check is load-sensitive; retry it until the debt is closed.
        tries = 3 if name == "selftest" else 1
        for attempt in range(tries):
            r = subprocess.run(cmd, cwd=ROOT, env=env, text=True,
                               stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
            if r.returncode == 0:
                break
        took = time.time() - t0
        ok = r.returncode == 0
        print(f"  {'OK  ' if ok else 'FAIL'} {name:<16} {took:6.1f} s", flush=True)
        if not ok:
            failed.append(name)
            tail = [l for l in r.stdout.splitlines() if l.strip()][-25:]
            print("\n".join("       " + l for l in tail))
            if name == "build":
                break  # nothing after a failed build is meaningful
    if failed:
        print(f"verify: FAILED ({', '.join(failed)})")
        return 1
    print("verify: OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
