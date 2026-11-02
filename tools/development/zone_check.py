#!/usr/bin/env python3
"""Check that files changed in the working tree are inside an agent's write zone.

Hooks on Edit/Write cannot see writes made through Bash (sed -i, cat >>, ...).
The Orchestrator runs this after every agent that can write files:

  python3 tools/development/zone_check.py developer [test-engineer ...] [--base HEAD] [--ignore PATH ...]

Several agents = union of their zones (agents that ran in parallel).

Zones are read from the guard_zone.py hook command in .claude/agents/<agent>.md,
so they have one source of truth. Exit code 1 if any changed file is outside
the zone. --ignore excludes files the Orchestrator itself changed meanwhile.
"""
import argparse
import re
import shlex
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / ".claude" / "hooks"))
from _common import matches  # noqa: E402  (same glob semantics as the hook)


def zone_of(agent: str) -> tuple[list[str], list[str]]:
    text = (ROOT / ".claude" / "agents" / f"{agent}.md").read_text(encoding="utf-8")
    m = re.search(r"guard_zone\.py\"?([^'\n]*)", text)
    if not m:
        sys.exit(f"{agent} has no guard_zone hook (read-only agent?)")
    allow, deny = [], []
    args = shlex.split(m.group(1))
    for flag, value in zip(args, args[1:]):
        if flag == "--allow":
            allow.append(value)
        elif flag == "--deny":
            deny.append(value)
    return allow, deny


def changed_files(base: str) -> list[str]:
    tracked = subprocess.run(["git", "diff", "--name-only", base], cwd=ROOT,
                             capture_output=True, text=True, check=True).stdout.split()
    untracked = subprocess.run(["git", "ls-files", "--others", "--exclude-standard"],
                               cwd=ROOT, capture_output=True, text=True, check=True).stdout.split()
    return sorted(set(tracked) | set(untracked))


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("agents", nargs="+")
    ap.add_argument("--base", default="HEAD")
    ap.add_argument("--ignore", action="append", default=[])
    args = ap.parse_args()
    zones = [zone_of(a) for a in args.agents]

    def inside(f: str) -> bool:
        return any(matches(f, allow) and not matches(f, deny) for allow, deny in zones)

    outside = [f for f in changed_files(args.base) if f not in args.ignore and not inside(f)]
    name = "+".join(args.agents)
    if outside:
        print(f"{name}: {len(outside)} changed file(s) outside the write zone:")
        for f in outside:
            print("  -", f)
        return 1
    print(f"{name}: all changed files are inside the write zone")
    return 0


if __name__ == "__main__":
    sys.exit(main())
