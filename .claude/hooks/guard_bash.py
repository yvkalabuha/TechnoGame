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
# A leading "nice -n N" (lower CPU priority, owner rule 2026-11-24) is allowed in front of any allowed command.
NICE = re.compile(r"^nice\s+(-n\s*)?-?\d+\s+")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--agent", default="agent")
    ap.add_argument("--allow", action="append", default=[])
    args = ap.parse_args()
    data = read_input()
    cmd = ((data.get("tool_input") or {}).get("command") or "").strip()
    cmd = NICE.sub("", cmd, count=1)
    if FORBIDDEN.search(cmd):
        block(f"Blocked: {args.agent} may not use shell operators (; & | < > ` $()).")
    if not any(cmd == p or cmd.startswith(p + " ") for p in args.allow):
        block(f"Blocked: {args.agent} may run only: {', '.join(args.allow)}.")


if __name__ == "__main__":
    main()
