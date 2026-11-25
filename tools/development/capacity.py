#!/usr/bin/env python3
"""Machine capacity for agent work (owner rule 2026-11-25: agents run as many at once as the CPU allows).

Prints cores, load, heavy processes (test runs, Godot, landings) and how many new heavy agents may start now.
Exit code 0 always; --json for scripts. Used by the Orchestrator (skill /autopilot) and by the hook guard_load.py.
"""
import json
import os
import subprocess
import sys

HEAVY = {"test": ("dotnet test", "testhost"), "godot": ("Godot_v",), "land": ("tools/development/land.py",)}
LOAD_START = 0.75   # start a new heavy agent only below this share of the cores (1-minute load)
LOAD_BLOCK = 1.25   # a new test run is refused above this share
MAX_TEST_RUNS = 3   # concurrent `dotnet test` runs started by agents


def procs():
    try:
        out = subprocess.run(["ps", "-eo", "args"], capture_output=True, text=True, timeout=5).stdout
    except (OSError, subprocess.SubprocessError):
        return {k: 0 for k in HEAVY}
    lines = out.splitlines()[1:]
    return {k: sum(1 for l in lines if any(p in l for p in pats) and "grep" not in l) for k, pats in HEAVY.items()}


def state():
    cores = os.cpu_count() or 1
    load1 = os.getloadavg()[0]
    p = procs()
    runs = sum(1 for l in subprocess.run(["ps", "-eo", "args"], capture_output=True, text=True).stdout.splitlines()
               if l.startswith(("dotnet test", "/usr/bin/dotnet test")) or " dotnet test " in f" {l} ")
    free = max(0, int((cores * LOAD_START - load1) // 3))
    return {"cores": cores, "load1": round(load1, 2), "heavy": p, "test_runs": runs, "agent_slots_free": free,
            "block_tests": load1 > cores * LOAD_BLOCK or runs >= MAX_TEST_RUNS}


if __name__ == "__main__":
    s = state()
    if "--json" in sys.argv:
        print(json.dumps(s))
    else:
        print(f"cores {s['cores']}  load1 {s['load1']}  test runs {s['test_runs']}  godot {s['heavy']['godot']}  "
              f"landings {s['heavy']['land']}  free agent slots {s['agent_slots_free']}"
              f"{'  TESTS BLOCKED' if s['block_tests'] else ''}")
