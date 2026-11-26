#!/usr/bin/env python3
"""Platform error catalogue (owner #584): development/errors.yaml.

  errors.py add --executor E --kind K --area A --symptom S [--cause C --found-by F --cost low|medium|high --fix X]
  errors.py add --from-review FILE [--executor E]   draft from a reviewer file with verdict CHANGES
  errors.py list [--status S --kind K --executor E] [--brief]
  errors.py stats                                   top repeats by kind and executor, 7 and 30 days
  errors.py repeat ERR-N                            another occurrence today
  errors.py close ERR-N --prevention hook|test|skill|rule|memory:REF   (or --wontfix --reason R)
  errors.py import [--ci]                           drafts from .claude/.state/{hook_refusals,land_failures}.jsonl
  errors.py digest [--dry-run]                      weekly DeepSeek review of repeats -> docs/reviews/errors-digest-YYYY-WW.md
  errors.py check                                   schema check of errors.yaml

An entry with the same key (kind, area, normalised symptom) is never duplicated: the date goes to "repeats".
Automatic sources only write drafts (status open, found_by hook|ci|reviewer). land.py and the hooks append to
.claude/.state/*.jsonl (git-ignored) and never touch errors.yaml, so a landing cannot dirty main.
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import re
import sys
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parents[2]
ERRORS = ROOT / "development" / "errors.yaml"
STATE = ROOT / ".claude" / ".state"
DIGEST_DIR = ROOT / "docs" / "reviews"
SOURCES = ("hook_refusals.jsonl", "land_failures.jsonl")
IGNORED_HOOKS = {"guard_load"}  # a busy CPU is an expected back-off, not an error

KINDS = ("process", "code", "data", "ui", "comms", "tooling", "determinism")
FOUND_BY = ("owner", "reviewer", "codex", "ci", "hook", "self")
COSTS = ("low", "medium", "high")
STATUSES = ("open", "prevented", "wontfix")
PREVENTION_KINDS = ("hook", "test", "skill", "rule", "memory")
EXECUTOR_RE = re.compile(r"(orchestrator|agent:[\w-]+|tool:[\w.-]+|model:[\w.-]+)\Z")
ID_RE = re.compile(r"ERR-\d{4}\Z")
DATE_RE = re.compile(r"\d{4}-\d{2}-\d{2}\Z")
FIELDS = ("id", "date", "executor", "kind", "area", "symptom", "cause", "found_by", "cost", "fix",
          "prevention", "repeats", "status", "key")
HEADER = ("# Platform error catalogue (owner #584). Edit with tools/development/errors.py; schema: errors.py check.\n"
          "# Auto drafts come from errors.py import (land.py, hook refusals, red CI, reviewer CHANGES).\n")


def norm_key(kind: str, area: str, symptom: str) -> str:
    words = re.sub(r"[^a-z0-9]+", " ", symptom.lower().replace("_", " ")).split()
    return f"{kind}|{area.lower()}|{' '.join(words)[:60]}"


def load(path: Path) -> list[dict]:
    if not path.exists():
        return []
    data = yaml.safe_load(path.read_text(encoding="utf-8")) or {}
    items = data.get("errors") if isinstance(data, dict) else None
    out = []
    for e in items or []:
        e = dict(e)
        for k in ("date",):
            if k in e:
                e[k] = str(e[k])
        e["repeats"] = [str(d) for d in (e.get("repeats") or [])]
        out.append(e)
    return out


def save(path: Path, entries: list[dict]) -> None:
    ordered = [{k: e[k] for k in FIELDS if k in e} for e in entries]
    body = yaml.safe_dump({"errors": ordered}, sort_keys=False, allow_unicode=True, width=110)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(HEADER + body, encoding="utf-8")


def validate(entries: list[dict]) -> list[str]:
    errs, seen = [], set()
    for n, e in enumerate(entries, 1):
        who = e.get("id", f"#{n}")
        miss = [k for k in FIELDS if k not in e and k not in ("key", "prevention")]
        if miss:
            errs.append(f"{who}: missing fields {miss}")
            continue
        if not ID_RE.match(str(e["id"])):
            errs.append(f"{who}: id must look like ERR-0001")
        if e["id"] in seen:
            errs.append(f"{who}: duplicate id")
        seen.add(e["id"])
        if not DATE_RE.match(str(e["date"])):
            errs.append(f"{who}: date must be YYYY-MM-DD")
        if not EXECUTOR_RE.match(str(e["executor"])):
            errs.append(f"{who}: executor '{e['executor']}' is not orchestrator|agent:<type>|tool:<name>|model:<name>")
        for field, allowed in (("kind", KINDS), ("found_by", FOUND_BY), ("cost", COSTS), ("status", STATUSES)):
            if e[field] not in allowed:
                errs.append(f"{who}: {field} '{e[field]}' not in {list(allowed)}")
        for field in ("area", "symptom"):
            if not str(e[field]).strip():
                errs.append(f"{who}: {field} is empty")
        if not isinstance(e["repeats"], list) or not all(DATE_RE.match(str(d)) for d in e["repeats"]):
            errs.append(f"{who}: repeats must be a list of YYYY-MM-DD")
        p = e.get("prevention")
        if p is not None:
            if not isinstance(p, dict) or p.get("kind") not in PREVENTION_KINDS or not str(p.get("ref", "")).strip():
                errs.append(f"{who}: prevention needs kind {list(PREVENTION_KINDS)} and a ref")
        if e["status"] == "prevented" and not p:
            errs.append(f"{who}: status prevented needs a prevention")
    return errs


def next_id(entries: list[dict]) -> str:
    nums = [int(e["id"][4:]) for e in entries if ID_RE.match(str(e.get("id", "")))]
    return f"ERR-{max(nums, default=0) + 1:04d}"


def add_entry(entries: list[dict], today: str, **f) -> tuple[dict, bool]:
    """(entry, is_new). The same key adds today to repeats of the existing entry instead of a new one."""
    key = norm_key(f["kind"], f["area"], f["symptom"])
    for e in entries:
        if (e.get("key") or norm_key(e["kind"], e["area"], e["symptom"])) == key:
            if today != e["date"] and today not in e["repeats"]:
                e["repeats"].append(today)
            return e, False
    e = {"id": next_id(entries), "date": today, "executor": f["executor"], "kind": f["kind"], "area": f["area"],
         "symptom": f["symptom"].strip(), "cause": f.get("cause") or "unknown (draft)",
         "found_by": f.get("found_by") or "self", "cost": f.get("cost") or "low", "fix": f.get("fix") or "",
         "repeats": [], "status": "open", "key": key}
    entries.append(e)
    return e, True


def occurrences(e: dict) -> list[str]:
    return [str(e["date"])] + [str(d) for d in e["repeats"]]


def window_stats(entries: list[dict], today: dt.date, days: int) -> dict:
    lo = (today - dt.timedelta(days=days)).isoformat()
    total = repeats = 0
    by_kind: dict[str, int] = {}
    by_exec: dict[str, int] = {}
    for e in entries:
        n = 0
        for i, d in enumerate(occurrences(e)):
            if lo < d <= today.isoformat():
                n += 1
                if i:
                    repeats += 1
        total += n
        if n:
            by_kind[e["kind"]] = by_kind.get(e["kind"], 0) + n
            by_exec[e["executor"]] = by_exec.get(e["executor"], 0) + n
    return {"total": total, "repeats": repeats, "kind": by_kind, "executor": by_exec}


def top(d: dict[str, int], n: int = 5) -> str:
    items = sorted(d.items(), key=lambda kv: (-kv[1], kv[0]))[:n]
    return ", ".join(f"{k} {v}" for k, v in items) or "-"


def parse_prevention(s: str) -> dict:
    kind, _, ref = s.partition(":")
    return {"kind": kind.strip(), "ref": ref.strip()}


def review_draft(path: Path) -> dict | None:
    """Fields of a draft from a reviewer file, or None when the verdict is not CHANGES."""
    lines = path.read_text(encoding="utf-8").splitlines()
    for i, l in enumerate(lines):
        if re.search(r"(?i)(verdict|вердикт|status)\W+\**\s*changes", l):
            rest = [x.strip(" -*#") for x in lines[i + 1:] if x.strip(" -*#")]
            task = re.search(r"TASK-\d{4}", path.name + " " + " ".join(lines[:30]))
            return {"kind": "code", "area": task.group(0) if task else path.stem,
                    "symptom": (rest[0] if rest else l.strip())[:200], "found_by": "reviewer"}
    return None


def read_jsonl(path: Path) -> list[dict]:
    out = []
    if path.exists():
        for line in path.read_text(encoding="utf-8").splitlines():
            try:
                out.append(json.loads(line))
            except ValueError:
                continue
    return out


def state_drafts(state: Path) -> tuple[list[dict], int]:
    """Drafts from hook refusals and land failures, and the number of ignored lines."""
    drafts, skipped = [], 0
    for r in read_jsonl(state / "hook_refusals.jsonl"):
        hook = str(r.get("hook", "hook"))
        if hook in IGNORED_HOOKS:
            skipped += 1
            continue
        drafts.append({"date": day(r.get("t")), "executor": f"tool:{hook}", "kind": "process", "area": "hooks",
                       "symptom": f"{hook} refused: {str(r.get('msg', ''))[:120]}", "found_by": "hook"})
    for r in read_jsonl(state / "land_failures.jsonl"):
        code = r.get("code")
        why = "second review missing or not passed" if code == 6 else "verify failed on the merged tree"
        drafts.append({"date": day(r.get("t")), "executor": "tool:land.py", "kind": "process" if code == 6 else "code",
                       "area": "land", "symptom": f"land exit {code}: {why} ({r.get('branch', '?')})",
                       "found_by": "ci"})
    return drafts, skipped


def day(t) -> str:
    try:
        return dt.datetime.fromtimestamp(float(t), dt.timezone.utc).date().isoformat()
    except (TypeError, ValueError, OverflowError):
        return dt.date.today().isoformat()


DIGEST_SYSTEM = (
    "You review a catalogue of errors made on a software platform run by AI agents. Group the entries into "
    "recurring patterns. Each FINDINGS item is one pattern: item = short name, reason = which entries (ids) and why "
    "they repeat, proposal = the cheapest prevention in the order hook > test > skill > rule > memory. "
    "SUMMARY: the share of repeats and the top cause. STATUS: changes_requested if any pattern lacks a "
    "prevention, else approve. Use only the given text; invent no ids.")


def digest_text(entries: list[dict], today: dt.date) -> str:
    lo = (today - dt.timedelta(days=30)).isoformat()
    keep = [e for e in entries if e["status"] == "open" or any(d > lo for d in occurrences(e))]
    rows = [{k: e[k] for k in ("id", "date", "executor", "kind", "area", "symptom", "cause", "cost", "fix",
                                "prevention", "repeats", "status") if k in e} for e in keep]
    return f"Today: {today.isoformat()}\n" + yaml.safe_dump(rows, sort_keys=False, allow_unicode=True, width=110)


def run_digest(entries, today: dt.date, out_dir: Path, dry_run=False, make_provider=None) -> Path | str:
    """Path of the written report, or a one-line size report on dry run. The text is scanned by gitleaks first."""
    sys.path.insert(0, str(ROOT))
    from tools.llm_worker import review as rv
    text = digest_text(entries, today)
    rv._scan(text)
    if dry_run:
        return f"dry-run: {len(text.encode())} bytes, scanned by gitleaks, nothing sent"
    provider = (make_provider or (lambda: rv.paid_provider(rv.DEFAULT_PROFILE, "errors-digest")))()
    answer, _usage, _think = rv._deepseek_call(provider, DIGEST_SYSTEM, text, rv.GATE_SCHEMA, "gate",
                                               rv.max_tokens_for(False, 6000))
    data = rv.parse_answer(answer, rv.GATE_SCHEMA, "gate")
    iso = today.isocalendar()
    lines = [f"# Errors digest {iso[0]}-W{iso[1]:02d}", "", f"[Факт] DeepSeek, entries: {len(entries)}. {data['SUMMARY']}", ""]
    for f in data["FINDINGS"]:
        lines += [f"- **{f['item']}** ({f['severity']}): {f['reason']} -> {f['proposal']}"]
    out = out_dir / f"errors-digest-{iso[0]}-{iso[1]:02d}.md"
    out_dir.mkdir(parents=True, exist_ok=True)
    out.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return out


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--file", type=Path, default=ERRORS)
    ap.add_argument("--state", type=Path, default=STATE)
    ap.add_argument("--today", default=None, help="YYYY-MM-DD (tests)")
    sub = ap.add_subparsers(dest="cmd", required=True)
    a = sub.add_parser("add")
    a.add_argument("--executor")
    a.add_argument("--kind", choices=KINDS)
    a.add_argument("--area")
    a.add_argument("--symptom")
    a.add_argument("--cause")
    a.add_argument("--found-by", choices=FOUND_BY)
    a.add_argument("--cost", choices=COSTS)
    a.add_argument("--fix")
    a.add_argument("--from-review", type=Path)
    l = sub.add_parser("list")
    l.add_argument("--status", choices=STATUSES)
    l.add_argument("--kind", choices=KINDS)
    l.add_argument("--executor")
    l.add_argument("--brief", action="store_true")
    sub.add_parser("stats")
    r = sub.add_parser("repeat")
    r.add_argument("id")
    c = sub.add_parser("close")
    c.add_argument("id")
    c.add_argument("--prevention")
    c.add_argument("--wontfix", action="store_true")
    c.add_argument("--reason")
    i = sub.add_parser("import")
    i.add_argument("--ci", action="store_true", help="also read a red CI on main through gh")
    d = sub.add_parser("digest")
    d.add_argument("--dry-run", action="store_true")
    d.add_argument("--out", type=Path, default=DIGEST_DIR)
    sub.add_parser("check")
    args = ap.parse_args(argv)
    today = args.today or dt.date.today().isoformat()
    entries = load(args.file)

    if args.cmd == "check":
        errs = validate(entries)
        for e in errs:
            print("ERROR  ", e)
        print(f"errors.py: {len(entries)} entries, {len(errs)} error(s)")
        return 1 if errs else 0
    if args.cmd == "add":
        if args.from_review:
            draft = review_draft(args.from_review)
            if draft is None:
                print("not a CHANGES verdict: nothing added")
                return 0
            f = {**draft, "executor": args.executor or "agent:developer"}
        else:
            f = {k: getattr(args, k) for k in ("executor", "kind", "area", "symptom", "cause", "found_by", "cost", "fix")}
            if not (f["executor"] and f["kind"] and f["area"] and f["symptom"]):
                print("add needs --executor --kind --area --symptom (or --from-review)")
                return 2
        e, new = add_entry(entries, today, **f)
        bad = validate([e])
        if bad:
            print("\n".join(bad))
            return 2
        save(args.file, entries)
        print(f"{e['id']} {'added' if new else 'repeat recorded'}")
        return 0
    if args.cmd == "list":
        rows = [e for e in entries if (not args.status or e["status"] == args.status)
                and (not args.kind or e["kind"] == args.kind)
                and (not args.executor or e["executor"] == args.executor)]
        if args.brief:
            op = [e for e in entries if e["status"] == "open"]
            if op:
                print(f"{len(op)} open error(s), {sum(1 for e in op if e['repeats'])} repeated (python3 tools/development/errors.py list --status open)")
            return 0
        for e in rows:
            print(f"{e['id']} [{e['status']:<9}] {e['date']} x{1 + len(e['repeats'])} {e['kind']:<11} {e['executor']:<22} {e['area']}: {e['symptom'][:80]}")
        return 0
    if args.cmd == "stats":
        t = dt.date.fromisoformat(today)
        for days in (7, 30):
            s = window_stats(entries, t, days)
            share = f"{100 * s['repeats'] // s['total']}%" if s["total"] else "-"
            print(f"{days} days: {s['total']} occurrence(s), repeats {s['repeats']} ({share})")
            print(f"  by kind: {top(s['kind'])}")
            print(f"  by executor: {top(s['executor'])}")
        return 0
    if args.cmd in ("repeat", "close"):
        e = next((x for x in entries if x["id"] == args.id), None)
        if e is None:
            print(f"unknown {args.id}")
            return 2
        if args.cmd == "repeat":
            if today != e["date"] and today not in e["repeats"]:
                e["repeats"].append(today)
        elif args.wontfix:
            if not args.reason:
                print("--wontfix needs --reason")
                return 2
            e["status"], e["fix"] = "wontfix", args.reason
        else:
            if not args.prevention:
                print("close needs --prevention kind:ref (hook|test|skill|rule|memory) or --wontfix --reason")
                return 2
            e["prevention"], e["status"] = parse_prevention(args.prevention), "prevented"
        bad = validate([e])
        if bad:
            print("\n".join(bad))
            return 2
        save(args.file, entries)
        print(f"{e['id']} {e['status']}")
        return 0
    if args.cmd == "import":
        drafts, skipped = state_drafts(args.state)
        if args.ci:
            sys.path.insert(0, str(Path(__file__).resolve().parent))
            import watch
            ev = watch.red_ci_event()
            if ev:
                drafts.append({"date": ev["date"], "executor": "tool:ci", "kind": "code", "area": "ci",
                               "symptom": f"red CI on main: {ev['title']}", "found_by": "ci"})
        new = 0
        for dft in drafts:
            _e, is_new = add_entry(entries, dft.pop("date"), **dft)
            new += is_new
        if drafts:
            save(args.file, entries)
        for name in SOURCES:
            p = args.state / name
            if p.exists():
                p.write_text("", encoding="utf-8")
        print(f"import: {len(drafts)} event(s), {new} new draft(s), {skipped} ignored")
        return 0
    if args.cmd == "digest":
        try:
            res = run_digest(entries, dt.date.fromisoformat(today), args.out, dry_run=args.dry_run)
        except Exception as ex:  # ReviewError, SpendRefused, KeyFileError: the CLI of llm_worker prints them the same way
            print(f"digest refused: {type(ex).__name__}: {ex}")
            return 4
        print(res)
        return 0
    return 2


if __name__ == "__main__":
    sys.exit(main())
