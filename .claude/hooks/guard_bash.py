#!/usr/bin/env python3
"""PreToolUse hook for Bash.

Allow-list mode (read-only agents' frontmatter): allows only commands that start with an allowed prefix and
contain no shell chaining, redirection or substitution.
  guard_bash.py --agent reviewer --allow 'git diff' --allow 'dotnet test'

--deny-db mode (settings.json, every Bash call): inside a subagent (the hook input carries agent_id) refuses a
direct psql or redis-cli (owner #597: agents read PostgreSQL and Redis only through kb_db.py and metrics.py, Redis
is written only by hooks and tools). The main session is not checked. A psql prefixed with
SINGULARITY_KB_TEST=1 (tests on a temporary database), or run while the hook's own environment sets it, passes.
  guard_bash.py --deny-db
"""
import argparse
import os
import re
import shlex

from _common import block, read_input

FORBIDDEN = re.compile(r"[;&|<>`]|\$\(")
# A leading "nice -n N" (lower CPU priority, owner rule 2026-11-24) is allowed in front of any allowed command.
NICE = re.compile(r"^nice\s+(-n\s*)?-?\d+\s+")

DB_TOOLS = {"psql", "redis-cli"}
DB_WORD = re.compile(r"(^|[^\w.-])(psql|redis-cli)([^\w.-]|$)")
SEPS = {";", "&&", "||", "|", "&", "|&", ";;", "(", ")"}
WRAPPERS = {"nice", "env", "exec", "time", "timeout", "nohup", "setsid", "stdbuf", "xargs", "command", "sudo",
            "ionice", "chrt", "taskset", "watch", "builtin"}
SHELLS = {"bash", "sh", "dash", "zsh"}
SHELL_C = re.compile(r"^-[a-zA-Z]*c[a-zA-Z]*$")
ASSIGN = re.compile(r"^[A-Za-z_]\w*=")
KEYWORDS = {"if", "then", "else", "elif", "while", "until", "do", "!", "{", "}", "function"}
SUBST = re.compile(r"\$\(([^()]*)\)|`([^`]*)`")
KB_TEST = re.compile(r"^SINGULARITY_KB_TEST=.+")


def db_commands(raw: str, depth: int = 0) -> list[str]:
    """Names of psql/redis-cli run as a command in a shell command line: tokenised like the shell, leading
    keywords, assignments and wrappers skipped, "bash -c '...'", eval, $(...) and backticks read inside (also within
    quotes: fail closed). An unreadable line falls back to a plain word search (fail closed). A psql whose own
    leading assignments set SINGULARITY_KB_TEST (a test on a temporary database) is not counted."""
    found: list[str] = []
    if depth < 3:
        for m in SUBST.finditer(raw):
            found += db_commands(m.group(1) if m.group(1) is not None else m.group(2), depth + 1)
    try:
        lex = shlex.shlex(re.sub(r"\\\n", " ", raw).replace("\n", " ; "), posix=True, punctuation_chars=True)
        lex.whitespace_split = True
        lex.commenters = ""
        tokens = list(lex)
    except ValueError:
        return found + [m.group(2) for m in DB_WORD.finditer(raw)]
    cmds: list[list[str]] = [[]]
    for t in tokens:
        if t in SEPS:
            cmds.append([])
        else:
            cmds[-1].append(t)
    for ws in cmds:
        while ws and ws[0] in KEYWORDS:
            ws = ws[1:]
        kb_test = any(KB_TEST.match(w) for w in ws[:next((j for j, w in enumerate(ws) if not ASSIGN.match(w)), len(ws))])
        i = 0
        while i < len(ws) and (ASSIGN.match(ws[i]) or os.path.basename(ws[i]) in WRAPPERS
                               or (i and (ws[i].startswith("-") or ws[i].replace(".", "").isdigit()))):
            i += 1
        if i >= len(ws):
            continue
        name = os.path.basename(ws[i])
        if name in DB_TOOLS:
            if not (name == "psql" and kb_test):
                found.append(name)
        elif i and any(os.path.basename(w) in DB_TOOLS for w in ws[i:]):
            # a wrapper with its own arguments (sudo -u postgres psql, env -u X redis-cli): fail closed
            found += [os.path.basename(w) for w in ws[i:] if os.path.basename(w) in DB_TOOLS]
        elif depth < 3 and name in SHELLS:
            rest = ws[i + 1:]
            k = next((j for j, w in enumerate(rest) if SHELL_C.match(w)), None)
            if k is not None and k + 1 < len(rest):
                found += db_commands(rest[k + 1], depth + 1)
        elif depth < 3 and name == "eval":
            found += db_commands(" ".join(ws[i + 1:]), depth + 1)
    return found


def deny_db(data: dict) -> None:
    if not data.get("agent_id"):
        return
    cmd = (data.get("tool_input") or {}).get("command") or ""
    found = db_commands(cmd)
    if os.environ.get("SINGULARITY_KB_TEST"):
        found = [f for f in found if f != "psql"]
    if not found:
        return
    block(f"Blocked (owner #597): agents do not run {', '.join(sorted(set(found)))} directly. Read through "
          "python3 tools/development/kb_db.py or metrics.py; Redis is written only by hooks and tools. "
          "A psql test on a temporary database is prefixed with SINGULARITY_KB_TEST=1.")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--agent", default="agent")
    ap.add_argument("--allow", action="append", default=[])
    ap.add_argument("--deny-db", action="store_true")
    args = ap.parse_args()
    data = read_input()
    if args.deny_db:
        deny_db(data)
        if not args.allow:
            return
    cmd = ((data.get("tool_input") or {}).get("command") or "").strip()
    cmd = NICE.sub("", cmd, count=1)
    if FORBIDDEN.search(cmd):
        block(f"Blocked: {args.agent} may not use shell operators (; & | < > ` $()).")
    if not any(cmd == p or cmd.startswith(p + " ") for p in args.allow):
        block(f"Blocked: {args.agent} may run only: {', '.join(args.allow)}.")


if __name__ == "__main__":
    main()
