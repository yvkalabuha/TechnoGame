#!/usr/bin/env python3
"""PreToolUse hook for Edit|Write, configured in each subagent's frontmatter.

Usage: guard_zone.py --allow 'docs/research/**' --allow '...' [--deny '...']
Blocks any write outside the agent's write zone. Protects against mistakes,
not against deliberate bypass via Bash.
"""
import argparse

from _common import block, matches, read_input, rel_path


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--agent", default="agent")
    ap.add_argument("--allow", action="append", default=[])
    ap.add_argument("--deny", action="append", default=[])
    args = ap.parse_args()
    data = read_input()
    file_path = (data.get("tool_input") or {}).get("file_path")
    if not file_path:
        return
    rel = rel_path(data, file_path)
    zone = ", ".join(args.allow) or "(none)"
    if rel is None:
        block(f"Blocked: {args.agent} may not write outside the project ({file_path}).")
    if matches(rel, args.deny) or not matches(rel, args.allow):
        block(f"Blocked: {rel} is outside the write zone of {args.agent} ({zone}). "
              "Return the change to the Orchestrator instead.")


if __name__ == "__main__":
    main()
