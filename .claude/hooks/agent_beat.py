#!/usr/bin/env python3
"""PostToolUse hook (all tools): an agent's heartbeat in Redis, owner #590.

Inside a subagent the hook input carries agent_id (absent in the main session): write
agent:<id>:beat (EX 600) and agent:<id>:seen through tools/development/redis_kv.py, so that
tools/development/watch.py --actions can tell a working agent from a silent one.
Fail-silent and fast: a 30 ms socket timeout, no output, exit 0 always.
"""
import json
import sys
from pathlib import Path


def main() -> None:
    try:
        data = json.load(sys.stdin)
    except ValueError:
        return
    agent = data.get("agent_id") if isinstance(data, dict) else None
    if not agent:
        return
    sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools" / "development"))
    import redis_kv
    redis_kv.beat(str(agent))


if __name__ == "__main__":
    try:
        main()
    except Exception:  # a heartbeat must never disturb the session
        pass
