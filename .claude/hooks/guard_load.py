#!/usr/bin/env python3
"""PreToolUse hook for Bash (all sessions and agents): do not start a test or build run when the CPU is full.

Owner rule 2026-11-25: tests run in batches, agents as many as the CPU allows. Refuses `dotnet test`, `dotnet build`
and `verify.py` while the 1-minute load is above 1.25 x cores or 3 test runs are already going. The agent should
wait (`sleep 90`) and retry, at most 10 times. land.py runs its own verify outside this hook.
"""
import re
import sys
from pathlib import Path

from _common import block, read_input

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools" / "development"))
import capacity  # noqa: E402

HEAVY = re.compile(r"(dotnet\s+(test|build)\b|tools/development/verify\.py)")
QUOTED = re.compile(r"'[^']*'|\"(?:[^\"\\\\]|\\\\.)*\"", re.S)


def main() -> None:
    cmd = ((read_input().get("tool_input") or {}).get("command") or "")
    if not HEAVY.search(QUOTED.sub("''", cmd)):
        return
    s = capacity.state()
    if s["block_tests"]:
        block(f"CPU busy: load {s['load1']} on {s['cores']} cores, {s['test_runs']} test runs going. "
              "Wait (sleep 90) and retry; run only the tests of your module, once, at the end of a change set.")


if __name__ == "__main__":
    main()
