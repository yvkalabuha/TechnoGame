#!/usr/bin/env python3
"""PreToolUse hook for Bash, configured in read-only agents' frontmatter.

Allows only commands that start with an allowed prefix and contain no shell
chaining, redirection or substitution.
Usage: guard_bash.py --agent reviewer --allow 'git diff' --allow 'dotnet test'
"""
import argparse
import re

from _common import block, read_input

FORBIDDEN = re.compile(r"[;&|<>`]|\$\(")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--agent", default="agent")
    ap.add_argument("--allow", action="append", default=[])
    args = ap.parse_args()
    data = read_input()
    cmd = ((data.get("tool_input") or {}).get("command") or "").strip()
    if FORBIDDEN.search(cmd):
        block(f"Blocked: {args.agent} may not use shell operators (; & | < > ` $()).")
    if not any(cmd == p or cmd.startswith(p + " ") for p in args.allow):
        block(f"Blocked: {args.agent} may run only: {', '.join(args.allow)}.")


if __name__ == "__main__":
    main()
