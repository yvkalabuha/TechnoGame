#!/usr/bin/env python3
"""PreToolUse hook for Bash (all sessions and agents): the game client starts only with the owner's permission.

Owner rules 2026-11-24/25: launch the game only with permission, in batches; never the full shot set for one fix.
- Any launch of run-globe-spike.sh / run-client.sh / play.sh / the Godot binary (not --headless) needs a live permit:
  ~/.cache/singularity/client-launch-ok holding an expiry time (unix seconds). The Orchestrator writes it after the
  owner allows a batch (tools/development/client_permit.py).
- A shot run (--shots / --map-frames) must also name its frames with --only, unless the command starts with SHOTS_FULL=1.
"""
import re
import time
from pathlib import Path

from _common import block, read_input

PERMIT = Path.home() / ".cache" / "singularity" / "client-launch-ok"
LAUNCH = re.compile(r"(run-globe-spike\.sh|run-client\.sh|play\.sh|Godot_v[\w.-]+)")
QUOTED = re.compile(r"'[^']*'|\"(?:[^\"\\\\]|\\\\.)*\"", re.S)
SHOT_FLAGS = re.compile(r"(^|\s)--(shots|time-shots|map-frames)(\s|$)")


def permit_live() -> bool:
    try:
        return float(PERMIT.read_text().strip()) > time.time()
    except (OSError, ValueError):
        return False


def main() -> None:
    cmd = QUOTED.sub("''", ((read_input().get("tool_input") or {}).get("command") or ""))
    m = LAUNCH.search(cmd)
    if not m:
        return
    if m.group(1).startswith("Godot_v") and re.search(r"--headless\b", cmd):
        return  # imports and builds without a window
    if not permit_live():
        block("Blocked: the game client starts only with the owner's permission (no live permit). "
              "Finish code and tests, then report the frames you need; the Orchestrator asks the owner and runs one batch.")
    if SHOT_FLAGS.search(cmd) and not re.search(r"(^|\s)--only(\s|=)", cmd) and not re.search(r"(^|[\s;&(])SHOTS_FULL=1\s", cmd):
        block("Blocked: a shot run must name its frames with --only a,b,c* (one batch, no full set for one fix).")


if __name__ == "__main__":
    main()
