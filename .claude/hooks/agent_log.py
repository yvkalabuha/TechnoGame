#!/usr/bin/env python3
"""Log agent starts and stops into .claude/.state/agents.jsonl for
tools/development/watch.py (control of silent agents).

Configured for PreToolUse and PostToolUse on the Agent tool and for SubagentStop.
Never blocks and never fails: any error is swallowed.
Usage: agent_log.py start | launched | stop
"""
import json
import re
import sys
import time

from _common import project_dir, read_input


def main() -> None:
    event = sys.argv[1] if len(sys.argv) > 1 else "event"
    data = read_input()
    ti = data.get("tool_input") or {}
    rec = {"t": time.time(), "event": event, "session": data.get("session_id")}
    if event == "stop":
        rec["agent_id"] = data.get("agent_id") or data.get("subagent_id") or ""
        rec["agent_type"] = data.get("agent_type") or data.get("subagent_type") or ""
    else:
        rec["agent_type"] = ti.get("subagent_type") or "general-purpose"
        rec["description"] = (ti.get("description") or "")[:120]
        tasks = sorted(set(re.findall(r"TASK-\d{4}", ti.get("prompt") or "")))
        if tasks:
            rec["tasks"] = tasks
        if event == "launched":
            m = re.search(r'agentId\\?"?:\s*\\?"?(\w+)', json.dumps(data.get("tool_response") or ""))
            rec["agent_id"] = m.group(1) if m else ""
            if not rec["agent_id"]:
                return  # foreground agent: it finished inside the call
    path = project_dir(data) / ".claude" / ".state" / "agents.jsonl"
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as f:
        f.write(json.dumps(rec, ensure_ascii=False) + "\n")


if __name__ == "__main__":
    try:
        main()
    except Exception:  # a logging hook must never disturb the session
        pass
