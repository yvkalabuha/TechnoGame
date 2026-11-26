#!/usr/bin/env python3
"""PostToolUse hook: knowledge base control.

  kb_guard.py file      after Edit/Write of a Markdown document: run kb_check on that file and
                        hand the findings back to the model (unknown ids, claims that contradict
                        development/FACTS.yaml, wrong ADR statuses, missing paths). After a write of
                        the decisions log it also prints the facts related to each new row, to be
                        confirmed or updated before landing (#597).
  kb_guard.py answers   after AskUserQuestion: print the known facts and earlier owner decisions
                        related to the questions and answers, so that a new decision is compared
                        with them before it is recorded. At most 5, none shown earlier in the same
                        session (#598; state: .claude/.state/kb_guard/<session>.json).

Never blocks: the tool call already happened. Any error is swallowed.
"""
import json
import os
import re
import subprocess
import sys
import time
from pathlib import Path

from _common import project_dir, read_input, rel_path

MAX_CHARS = 2400
MAX_RELATED = 5  # facts and decisions per answer (#598)
STATE_DAYS = 7  # seen-lists of older sessions are removed
# The decisions log and its monthly and archive files (tools/development/kblog.py).
DECISIONS_FILE = re.compile(r"^docs/decisions/(DECISIONS_LOG|log/(archive-\d{4}-\d{4}|\d{4}-\d{2}))\.md$")
ROW = re.compile(r"^\| (\d+) \|", re.M)
ITEM = re.compile(r"^(FACT \S+?:|DECISION #\d+)")


def new_rows(root, ti: dict, rel: str) -> list[int]:
    """Decision numbers the write added: rows of new_string absent from old_string (Edit), or rows
    absent from the committed file (Write)."""
    if "new_string" in ti:
        old = set(ROW.findall(ti.get("old_string") or ""))
        return [int(n) for n in dict.fromkeys(ROW.findall(ti.get("new_string") or "")) if n not in old]
    try:
        r = subprocess.run(["git", "show", f"HEAD:{rel}"], cwd=root, capture_output=True,
                           text=True, timeout=10)
    except (OSError, subprocess.SubprocessError):
        return []
    old = set(ROW.findall(r.stdout)) if r.returncode == 0 else set()
    return [int(n) for n in dict.fromkeys(ROW.findall(ti.get("content") or "")) if n not in old]


def run(root, *args: str) -> str:
    try:
        r = subprocess.run([sys.executable, "tools/development/kb_check.py", *args], cwd=root,
                           capture_output=True, text=True, timeout=20)
    except (OSError, subprocess.SubprocessError):
        return ""
    return r.stdout.strip()


def seen_file(root, session: str) -> Path:
    base = Path(os.environ.get("SINGULARITY_STATE") or Path(root) / ".claude" / ".state")
    return base / "kb_guard" / (re.sub(r"[^\w.-]", "_", session or "nosession")[:80] + ".json")


def pick_unseen(out: str, path: Path, limit: int = MAX_RELATED) -> list[str]:
    """The first 'limit' FACT/DECISION lines of out not shown before in this session; records them as shown."""
    try:
        seen = set(json.loads(path.read_text(encoding="utf-8")))
    except (OSError, ValueError):
        seen = set()
    picked = []
    for line in out.splitlines():
        m = ITEM.match(line)
        if m and m.group(1) not in seen and len(picked) < limit:
            picked.append(line)
            seen.add(m.group(1))
    if picked:
        try:
            path.parent.mkdir(parents=True, exist_ok=True)
            tmp = path.with_suffix(".tmp")
            tmp.write_text(json.dumps(sorted(seen)), encoding="utf-8")
            tmp.replace(path)
            cutoff = time.time() - STATE_DAYS * 86400
            for old in path.parent.glob("*.json"):
                if old.stat().st_mtime < cutoff:
                    old.unlink()
        except OSError:
            pass
    return picked


def emit(text: str) -> None:
    print(json.dumps({"hookSpecificOutput": {"hookEventName": "PostToolUse",
                                             "additionalContext": text[:MAX_CHARS]}}, ensure_ascii=False))


def main() -> None:
    mode = sys.argv[1] if len(sys.argv) > 1 else "file"
    data = read_input()
    root = project_dir(data)
    ti = data.get("tool_input") or {}
    if mode == "file":
        path = ti.get("file_path") or ""
        rel = rel_path(data, path) if path else None
        if not rel or not rel.endswith(".md") or rel.startswith(("lab/", "tools/llm_worker/")):
            return
        out = run(root, "check", "--file", path)
        lines = [l for l in out.splitlines() if l.startswith(("ERROR", "WARNING"))]
        parts = []
        if lines:
            parts.append("kb_check on the document you just wrote (fix the document, or update "
                         "development/FACTS.yaml if the fact changed):\n" + "\n".join(lines[:12]))
        nums = new_rows(root, ti, rel) if DECISIONS_FILE.match(rel) else []
        if nums:
            facts = run(root, "decision-facts", *map(str, nums[:10]))
            if facts:
                parts.insert(0, facts)  # first: the context is cut at MAX_CHARS
        if parts:
            emit("\n\n".join(parts))
    elif mode == "answers":
        text = json.dumps(ti, ensure_ascii=False) + " " + json.dumps(data.get("tool_response") or "", ensure_ascii=False)
        out = run(root, "related", text[:6000], "--limit", "15", "--min", "4")
        picked = pick_unseen(out, seen_file(root, str(data.get("session_id") or "")))
        if picked:
            emit("Known facts and earlier owner decisions related to these answers (new in this session, "
                 f"at most {MAX_RELATED}). Compare before recording a decision; if an answer contradicts a fact, "
                 "tell the owner in plain words and ask again:\n" + "\n".join(picked))


if __name__ == "__main__":
    try:
        main()
    except Exception:
        pass
