#!/usr/bin/env python3
"""Rejection memory (owner #591): findings that were checked and rejected, and why. development/review-rejections.yaml.

  rejections.py for PATH... | TASK-NNNN [--limit N]   the block "Ранее отклонено и почему" for a prompt
  rejections.py add --provider P --area A --summary S --reason R [--ref REF]   a manual entry (an agent finding)
  rejections.py stats [--since D --until D]           share of new review findings that repeat a rejected one
  rejections.py check                                 schema check

Entries from Codex and DeepSeek reviews and gates are written by `python3 -m tools.llm_worker review-confirm
RECORD --finding N --rejected --reason R [--ref REF]`. The Orchestrator puts the output of `for` into the prompt
of a Claude agent (/dispatch, /recheck). A path argument matches entries of that file or its directory; for a task
the files named in the task file and its title words are used.
"""
from __future__ import annotations

import argparse
import datetime as dt
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
from tools.llm_worker import rejections as core  # noqa: E402

JOURNAL = core.journal_path(ROOT)
TASK_RE = re.compile(r"TASK-\d{4}\Z")
PATH_IN_TEXT = re.compile(r"(?<![\w/.-])((?:[\w.-]+/)+[\w.-]+\.\w+)")


def task_scope(root: Path, task: str) -> tuple[list[str], str]:
    """(files named in the task file, topic = its title). ValueError if there is no such task."""
    found = sorted((root / "development" / "tasks").glob(f"{task}-*.md"))
    if not found:
        raise ValueError(f"no task file for {task}")
    text = found[0].read_text(encoding="utf-8")
    title = next((l for l in text.splitlines() if l.startswith("title:")), "")
    return sorted(set(PATH_IN_TEXT.findall(text))), title.removeprefix("title:").strip(" \"'")


def block_for(entries: list[dict], targets: list[str], limit: int, root: Path = ROOT) -> str:
    files, topic, refs = [], "", set()
    for t in targets:
        if TASK_RE.match(t):
            f, topic_t = task_scope(root, t)
            files += f
            topic += " " + topic_t
            refs.add(t)
        else:
            files.append(t)
    hit = core.relevant(entries, files, topic, limit)
    extra = [e for e in entries if e.get("ref") in refs and e not in hit]  # rejected under this very task
    return core.text_block((hit + extra)[:limit])


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--file", type=Path, default=JOURNAL, help=argparse.SUPPRESS)
    sub = ap.add_subparsers(dest="cmd", required=True)
    f = sub.add_parser("for")
    f.add_argument("targets", nargs="+")
    f.add_argument("--limit", type=int, default=core.MAX_LINES)
    a = sub.add_parser("add")
    for k in ("provider", "area", "summary", "reason"):
        a.add_argument("--" + k, required=True)
    a.add_argument("--ref", default="")
    s = sub.add_parser("stats")
    s.add_argument("--since")
    s.add_argument("--until")
    sub.add_parser("check")
    args = ap.parse_args(argv)
    try:
        if args.cmd == "add":
            e = core.add(args.file, args.provider, args.area, args.summary, args.reason, args.ref)
            print(f"recorded: {e['id']}")
            return 0
        entries = core.load(args.file)
        if args.cmd == "check":
            errs = core.validate(entries)
            print("\n".join("ERROR  " + x for x in errs))
            print(f"rejections.py: {len(entries)} entries, {len(errs)} error(s)")
            return 1 if errs else 0
        if args.cmd == "for":
            out = block_for(entries, args.targets, args.limit, args.file.parents[1] if args.file != JOURNAL else ROOT)
            if out:
                print(out)
            return 0
        today = dt.date.today()
        until = args.until or (today + dt.timedelta(days=1)).isoformat()
        since = args.since or (dt.date.fromisoformat(until) - dt.timedelta(days=30)).isoformat()
        print(stats_line(entries, since, until))
        return 0
    except (ValueError, OSError) as ex:
        print(f"rejections.py: {ex}", file=sys.stderr)
        return 1


def runs_dir() -> Path:
    import os
    return Path(os.environ.get("SINGULARITY_LLM_RUNS") or ROOT / "tools" / "llm_worker" / "runs")


def stats_line(entries: list[dict], since: str, until: str, runs: Path | None = None) -> str:
    s = core.repeat_stats(entries, core.load_records(runs or runs_dir()), since, until)
    share = f"{s['share_pct']}%" if s["share_pct"] is not None else "-"
    return f"review findings {since}..{until}: {s['findings']} new, repeating a rejected one {s['repeats']} ({share})"


if __name__ == "__main__":
    sys.exit(main())
