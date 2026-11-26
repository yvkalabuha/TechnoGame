#!/usr/bin/env python3
"""Autopilot switch (owner #583): the state file .claude/.state/autopilot.json.

  python3 tools/development/autopilot.py on       autopilot runs (default when the file is absent)
  python3 tools/development/autopilot.py stop     start no new agents: capacity.py reports 0 free slots,
                                                  running agents finish, nothing new lands without the owner
  python3 tools/development/autopilot.py status   print the state; --brief prints nothing when never touched

The path can be overridden with AUTOPILOT_STATE (tests). Used by capacity.py and the SessionStart hook.
"""
import fcntl
import json
import os
import sys
import time
from pathlib import Path

DEFAULT = Path(__file__).resolve().parents[2] / ".claude" / ".state" / "autopilot.json"


def state_path() -> Path:
    return Path(os.environ.get("AUTOPILOT_STATE") or DEFAULT)


def read() -> dict:
    """{} when never switched; an unreadable or corrupt file reads as stopped (fail safe)."""
    p = state_path()
    if not p.exists():
        return {}
    try:
        d = json.loads(p.read_text(encoding="utf-8"))
        if isinstance(d, dict) and d.get("state") in ("on", "stop"):
            return d
    except (OSError, ValueError):
        pass
    return {"state": "stop", "since": "?", "corrupt": True}


def is_stopped() -> bool:
    return read().get("state") == "stop"


def write(state: str) -> None:
    p = state_path()
    p.parent.mkdir(parents=True, exist_ok=True)
    fd = os.open(p.parent, os.O_RDONLY)  # lock the directory: two switches never interleave
    try:
        fcntl.flock(fd, fcntl.LOCK_EX)
        tmp = p.with_name(p.name + ".tmp")
        tmp.write_text(json.dumps({"state": state, "since": time.strftime("%Y-%m-%d %H:%M", time.localtime())}),
                       encoding="utf-8")
        os.replace(tmp, p)  # atomic: a reader never sees a half-written file
    finally:
        os.close(fd)


def describe() -> str:
    d = read()
    if not d:
        return "autopilot: on (never switched)"
    note = " (state file unreadable: treated as stopped)" if d.get("corrupt") else ""
    return f"autopilot: {d.get('state', '?')} since {d.get('since', '?')}{note}"


def main() -> int:
    cmd = sys.argv[1] if len(sys.argv) > 1 else "status"
    if cmd in ("on", "stop"):
        write(cmd)
    elif cmd != "status":
        print(__doc__)
        return 2
    elif "--brief" in sys.argv and not read():
        return 0
    print(describe())
    return 0


if __name__ == "__main__":
    sys.exit(main())
