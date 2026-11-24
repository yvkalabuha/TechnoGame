#!/usr/bin/env python3
"""PreToolUse hook for Bash (all sessions and agents): client shot runs only in small batches.

Owner 2026-11-24: launch the game for frames in batches, never the full set for one fix.
Blocks ./run-globe-spike.sh with --shots / --time-shots / --map-frames unless the command
names the frames with --only, or starts with SHOTS_FULL=1 (a deliberate full set, Orchestrator only).
"""
import re

from _common import block, read_input

RUN = re.compile(r"run-globe-spike\.sh\b")
SHOT_FLAGS = re.compile(r"(^|\s)--(shots|time-shots|map-frames)(\s|$)")


def main() -> None:
    data = read_input()
    cmd = ((data.get("tool_input") or {}).get("command") or "")
    if not RUN.search(cmd) or not SHOT_FLAGS.search(cmd):
        return
    if re.search(r"(^|\s)--only(\s|=)", cmd) or re.search(r"(^|[\s;&(])SHOTS_FULL=1\s", cmd):
        return
    block("Blocked: a client shot run must name its frames with --only a,b,c* (owner rule: batches, no full set "
          "for one fix). A full set needs SHOTS_FULL=1 in front and is for the Orchestrator only.")


if __name__ == "__main__":
    main()
