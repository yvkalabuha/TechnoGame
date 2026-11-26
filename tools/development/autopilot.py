#!/usr/bin/env python3
"""Autopilot switch (owner #583): the state file .claude/.state/autopilot.json.

  python3 tools/development/autopilot.py on       autopilot runs (default when the file is absent)
  python3 tools/development/autopilot.py stop     start no new agents: capacity.py reports 0 free slots,
                                                  running agents finish, nothing new lands without the owner
  python3 tools/development/autopilot.py status   print the state; --brief prints nothing when never touched

The path can be overridden with AUTOPILOT_STATE (tests). Used by capacity.py and the SessionStart hook.
"""
import json
import os
import sys
import time
from pathlib import Path

DEFAULT = Path(__file__).resolve().parents[2] / ".claude" / ".state" / "autopilot.json"


def state_path() -> Path:
    return Path(os.environ.get("AUTOPILOT_STATE") or DEFAULT)


def read() -> dict:
    try:
        d = json.loads(state_path().read_text(encoding="utf-8"))
        return d if isinstance(d, dict) else {}
    except (OSError, ValueError):
        return {}


def is_stopped() -> bool:
    return read().get("state") == "stop"


def write(state: str) -> None:
    p = state_path()
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(json.dumps({"state": state, "since": time.strftime("%Y-%m-%d %H:%M", time.localtime())}),
                 encoding="utf-8")


def describe() -> str:
    d = read()
    if not d:
        return "autopilot: on (never switched)"
    return f"autopilot: {d.get('state', '?')} since {d.get('since', '?')}"


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
