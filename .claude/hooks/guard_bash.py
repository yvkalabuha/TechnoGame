#!/usr/bin/env python3
"""PreToolUse hook for Bash.

Allow-list mode (read-only agents' frontmatter): allows only commands that start with an allowed prefix and
contain no shell chaining, redirection or substitution.
  guard_bash.py --agent reviewer --allow 'git diff' --allow 'dotnet test'

--deny-db mode (settings.json, every Bash call): inside a subagent (the hook input carries agent_id) refuses a
direct psql or redis-cli (owner #597: agents read PostgreSQL and Redis only through kb_db.py and metrics.py, Redis
is written only by hooks and tools). The main session is not checked. A psql on a database named kbtest_* passes
when prefixed with SINGULARITY_KB_TEST=1 (tests on a temporary database) or run while the hook's own environment
sets it.
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
SEPS = {";", "&&", "||", "|", "&", "|&", ";;", "(", ")"}
SHELLS = {"bash", "sh", "dash", "zsh"}
SHELL_C = re.compile(r"^-[a-zA-Z]*c[a-zA-Z]*$")
ASSIGN = re.compile(r"^[A-Za-z_]\w*=")
KEYWORDS = {"if", "then", "else", "elif", "while", "until", "do", "!", "{", "}", "function"}
SUBST = re.compile(r"\$\(([^()]*)\)|`([^`]*)`")
KB_TEST = re.compile(r"^SINGULARITY_KB_TEST=.+")
HEREDOC = re.compile(r"(?<!<)<<(?!<)(-?)\s*(\\?)(['\"]?)([A-Za-z_][\w.-]*)\3")

# Command wrappers that run their arguments as a command: options that take a separate value (short letters and long
# names) and the number of positional arguments before the command (timeout DURATION, chrt PRIORITY, taskset MASK).
# Unknown options count as flags. A "-N" number (nice -10) is a flag.
WRAPPERS: dict[str, tuple[str, set[str], int]] = {
    "nice": ("n", {"--adjustment"}, 0),
    "timeout": ("sk", {"--signal", "--kill-after"}, 1),
    "xargs": ("adEILnPs", {"--arg-file", "--delimiter", "--max-lines", "--max-args", "--max-procs", "--max-chars",
                          "--process-slot-var"}, 0),
    "env": ("uCS", {"--unset", "--chdir", "--split-string"}, 0),
    "sudo": ("ughpCDrtUTR", {"--user", "--group", "--host", "--prompt", "--close-from", "--chdir", "--role", "--type",
                             "--other-user", "--command-timeout", "--chroot"}, 0),
    "stdbuf": ("ioe", {"--input", "--output", "--error"}, 0),
    "ionice": ("cnt", {"--class", "--classdata"}, 0),
    "chrt": ("TPD", {"--sched-runtime", "--sched-period", "--sched-deadline"}, 1),
    "taskset": ("", set(), 1),
    "exec": ("a", set(), 0),
    "time": ("fo", {"--format", "--output"}, 0),
    "nohup": ("", set(), 0),
    "setsid": ("", set(), 0),
    "builtin": ("", set(), 0),
    "command": ("", set(), 0),
    "watch": ("nd", {"--interval", "--differences"}, 0),
}


def skip_wrapper(ws: list[str], i: int) -> tuple[int, str | None]:
    """ws[i] is a wrapper: index of the word after its options and positional arguments, and the string of
    "env -S STRING" (run as a command line) if any. A wrapper that does not run a command (command -v) returns
    len(ws)."""
    name = os.path.basename(ws[i])
    short, long_, npos = WRAPPERS[name]
    nested = None
    i += 1
    while i < len(ws):
        w = ws[i]
        if w == "--":
            i += 1
            break
        if w.startswith("--"):
            key, eq, val = w.partition("=")
            if key in long_ and not eq:
                val = ws[i + 1] if i + 1 < len(ws) else ""
                i += 1
            if name == "env" and key == "--split-string":
                nested = val
            i += 1
        elif w.startswith("-") and len(w) > 1 and not w[1:].isdigit():
            if name == "command" and ("v" in w or "V" in w):
                return len(ws), None
            k = next((j for j in range(1, len(w)) if w[j] in short), None)
            if k is not None:
                val = w[k + 1:]
                if not val:
                    val = ws[i + 1] if i + 1 < len(ws) else ""
                    i += 1
                if name == "env" and w[k] == "S":
                    nested = val
            i += 1
        elif w.startswith("-") and len(w) > 1:
            i += 1  # nice -10
        elif name == "env" and ASSIGN.match(w):
            i += 1
        else:
            break
    while npos and i < len(ws) and not ws[i] == "--":
        i += 1
        npos -= 1
    return i, nested


def strip_heredocs(raw: str) -> tuple[str, str]:
    """Cut here-document bodies out of a command line: (the lines the shell parses as commands, the text to scan
    for $() and backticks: the commands plus bodies with an unquoted delimiter, which the shell expands)."""
    cmd_lines: list[str] = []
    expanded: list[str] = []
    pending: list[tuple[str, bool, bool]] = []  # (delimiter, strip leading tabs, expanded)
    for line in raw.split("\n"):
        if pending:
            delim, tabs, exp = pending[0]
            if (line.lstrip("\t") if tabs else line) == delim:
                pending.pop(0)
            elif exp:
                expanded.append(line)
            continue
        cmd_lines.append(line)
        for m in HEREDOC.finditer(line):
            pending.append((m.group(4), m.group(1) == "-", not (m.group(2) or m.group(3))))
    main = "\n".join(cmd_lines)
    return main, "\n".join([main] + expanded)


PSQL_ARG = set("cdfvPhpUoLTFR")
PSQL_LONG_ARG = {"--command", "--dbname", "--file", "--set", "--variable", "--pset", "--host", "--port", "--username",
                 "--output", "--log-file", "--table-attr", "--field-separator", "--record-separator"}


def psql_database(args: list[str]) -> str | None:
    """The database a psql command line connects to (-d, --dbname, first positional, a URI or dbname=)."""
    db = None
    i = 0
    while i < len(args):
        w = args[i]
        if w.startswith("--"):
            key, eq, val = w.partition("=")
            if key in PSQL_LONG_ARG and not eq:
                val = args[i + 1] if i + 1 < len(args) else ""
                i += 1
            if key == "--dbname":
                db = val
        elif w.startswith("-") and len(w) > 1:
            if w[1] in PSQL_ARG:
                val = w[2:]
                if not val:
                    val = args[i + 1] if i + 1 < len(args) else ""
                    i += 1
                if w[1] == "d":
                    db = val
        elif db is None:
            db = w
        i += 1
    if not db:
        return None
    if "://" in db:
        db = db.split("://", 1)[1].split("?", 1)[0]
        return db.rsplit("/", 1)[1] if "/" in db else None
    if "=" in db:
        m = re.search(r"(?:^|\s)dbname\s*=\s*'?([^'\s]+)", db)
        return m.group(1) if m else None
    return db


def kbtest_db(args: list[str]) -> bool:
    db = psql_database(args)
    return bool(db and re.fullmatch(r"kbtest_\w+", db))


def db_commands(raw: str, depth: int = 0, kb_env: bool = False) -> list[str]:
    """Names of psql/redis-cli run as a command in a shell command line: tokenised like the shell, here-document
    bodies cut, leading keywords, assignments and wrappers (with their options) skipped, only the real command word
    checked; "bash -c '...'", "env -S '...'", eval, $(...) and backticks read inside (also within quotes: fail
    closed). An unreadable line falls back to a plain word search (fail closed). A psql on a kbtest_* database passes
    when its leading assignments set SINGULARITY_KB_TEST or kb_env is true (the hook's own environment sets it)."""
    found: list[str] = []
    main, scan = strip_heredocs(raw)
    if depth < 3:
        for m in SUBST.finditer(scan):
            found += db_commands(m.group(1) if m.group(1) is not None else m.group(2), depth + 1, kb_env)
    try:
        lex = shlex.shlex(re.sub(r"\\\n", " ", main).replace("\n", " ; "), posix=True, punctuation_chars=True)
        lex.whitespace_split = True
        lex.commenters = ""
        tokens = list(lex)
    except ValueError:
        return found + [m.group(2) for m in re.finditer(r"(^|[^\w.-])(psql|redis-cli)([^\w.-]|$)", main)]
    cmds: list[list[str]] = [[]]
    for t in tokens:
        if t in SEPS:
            cmds.append([])
        else:
            cmds[-1].append(t)
    for ws in cmds:
        while ws and ws[0] in KEYWORDS:
            ws = ws[1:]
        kb_test = kb_env
        i = 0
        while i < len(ws):
            if ASSIGN.match(ws[i]):
                kb_test = kb_test or bool(KB_TEST.match(ws[i]))
                i += 1
            elif os.path.basename(ws[i]) in WRAPPERS:
                if os.path.basename(ws[i]) == "env":
                    kb_test = kb_test or any(KB_TEST.match(w) for w in ws[i + 1:] if ASSIGN.match(w))
                i, nested = skip_wrapper(ws, i)
                if nested is not None and depth < 3:
                    found += db_commands(nested, depth + 1, kb_env)
            else:
                break
        if i >= len(ws):
            continue
        name = os.path.basename(ws[i])
        if name in DB_TOOLS:
            if not (name == "psql" and kb_test and kbtest_db(ws[i + 1:])):
                found.append(name)
        elif depth < 3 and name in SHELLS:
            rest = ws[i + 1:]
            k = next((j for j, w in enumerate(rest) if SHELL_C.match(w)), None)
            if k is not None and k + 1 < len(rest):
                found += db_commands(rest[k + 1], depth + 1, kb_env)
        elif depth < 3 and name == "eval":
            found += db_commands(" ".join(ws[i + 1:]), depth + 1, kb_env)
    return found


def deny_db(data: dict) -> None:
    if not data.get("agent_id"):
        return
    cmd = (data.get("tool_input") or {}).get("command") or ""
    found = db_commands(cmd, kb_env=bool(os.environ.get("SINGULARITY_KB_TEST")))
    if not found:
        return
    block(f"Blocked (owner #597): agents do not run {', '.join(sorted(set(found)))} directly. Read through "
          "python3 tools/development/kb_db.py or metrics.py; Redis is written only by hooks and tools. "
          "A psql test on a temporary kbtest_* database is prefixed with SINGULARITY_KB_TEST=1.")


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
