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
import copy
import datetime as dt
import fcntl
import hashlib
import json
import os
import re
import sys
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parents[2]
ERRORS = ROOT / "development" / "errors.yaml"
STATE = ROOT / ".claude" / ".state"
DIGEST_DIR = ROOT / "docs" / "reviews"
SOURCES = ("hook_refusals.jsonl", "land_failures.jsonl")
MUTATING = ("add", "repeat", "close", "import")
IMPORT_STATE = "errors_import.json"  # byte offsets of the jsonl sources, seen CI run ids, import journal
CI_SEEN_KEEP = 50
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
          "prevention", "repeats", "status", "events")
STR_FIELDS = ("id", "date", "executor", "kind", "area", "symptom", "cause", "found_by", "cost", "fix", "status")
HEADER = ("# Platform error catalogue (owner #584). Edit with tools/development/errors.py; schema: errors.py check.\n"
          "# Auto drafts come from errors.py import (land.py, hook refusals, red CI, reviewer CHANGES).\n")


def norm_key(kind: str, area: str, symptom: str) -> str:
    words = re.sub(r"[\W_]+", " ", symptom.casefold()).split()  # Unicode-aware: Russian symptoms keep their letters
    return f"{kind}|{area.lower()}|{' '.join(words)}"  # the full text: a cut would merge distinct errors


def load(path: Path) -> list[dict]:
    """Raises ValueError for a malformed file: a wrong root or entry type is an error, never an empty catalogue."""
    if not path.exists():
        return []
    try:
        data = yaml.safe_load(path.read_text(encoding="utf-8"))
    except yaml.YAMLError as ex:
        raise ValueError(f"{path.name}: invalid YAML: {ex}") from None
    if data is None:
        return []
    if not isinstance(data, dict) or not isinstance(data.get("errors"), list):
        raise ValueError(f"{path.name}: the root must be a mapping with an 'errors' list")
    out = []
    for n, e in enumerate(data["errors"], 1):
        if not isinstance(e, dict):
            raise ValueError(f"{path.name}: entry #{n} is not a mapping")
        e = dict(e)
        e.pop("key", None)  # older files stored a dedupe key; it is computed from the fields now
        if isinstance(e.get("date"), dt.date):  # YAML turns an unquoted date into a date object
            e["date"] = e["date"].isoformat()
        reps = e.get("repeats") or []
        if not isinstance(reps, list):
            raise ValueError(f"{path.name}: entry #{n}: repeats must be a list")
        e["repeats"] = [fix_repeat(d) for d in reps]
        out.append(e)
    return out


def fix_repeat(d):
    """A repeat is a date string, or {date, executor?, detail?} when it came from another executor or has a detail."""
    if isinstance(d, dt.date):
        return d.isoformat()
    if isinstance(d, dict) and isinstance(d.get("date"), dt.date):
        return {**d, "date": d["date"].isoformat()}
    return d


def save(path: Path, entries: list[dict]) -> None:
    ordered = [{k: e[k] for k in FIELDS if k in e} for e in entries]
    body = yaml.safe_dump({"errors": ordered}, sort_keys=False, allow_unicode=True, width=110)
    path.parent.mkdir(parents=True, exist_ok=True)
    # Temp file in the same directory, fsync, then rename: any error leaves the old file whole.
    tmp = path.with_name(f".{path.name}.{os.getpid()}.tmp")
    try:
        with tmp.open("w", encoding="utf-8") as f:
            f.write(HEADER + body)
            f.flush()
            os.fsync(f.fileno())
        os.replace(tmp, path)
    except BaseException:
        tmp.unlink(missing_ok=True)
        raise


def valid_date(s) -> bool:
    if not isinstance(s, str) or not DATE_RE.match(s):
        return False
    try:
        dt.date.fromisoformat(s)
    except ValueError:
        return False
    return True


def valid_repeat(d) -> bool:
    if isinstance(d, str):
        return valid_date(d)
    if isinstance(d, dict):
        return (valid_date(d.get("date")) and set(d) <= {"date", "executor", "detail", "event"}
                and (d.get("executor") is None or (isinstance(d["executor"], str) and bool(EXECUTOR_RE.match(d["executor"]))))
                and all(isinstance(d.get(k), (str, type(None))) for k in ("detail", "event")))
    return False


def validate(entries: list[dict]) -> list[str]:
    """Every value that a command would trip over is an error: field types, enums, dates that parse."""
    errs, seen = [], set()
    for n, e in enumerate(entries, 1):
        who = e.get("id", f"#{n}") if isinstance(e.get("id", f"#{n}"), str) else f"#{n}"
        miss = [k for k in FIELDS if k not in e and k not in ("prevention", "events")]
        if miss:
            errs.append(f"{who}: missing fields {miss}")
            continue
        wrong = [k for k in STR_FIELDS if not isinstance(e[k], str)]
        if wrong:
            errs.append(f"{who}: {wrong} must be strings")
            continue
        if not ID_RE.match(e["id"]):
            errs.append(f"{who}: id must look like ERR-0001")
        if e["id"] in seen:
            errs.append(f"{who}: duplicate id")
        seen.add(e["id"])
        if not valid_date(e["date"]):
            errs.append(f"{who}: date must be a real date YYYY-MM-DD")
        if not EXECUTOR_RE.match(e["executor"]):
            errs.append(f"{who}: executor '{e['executor']}' is not orchestrator|agent:<type>|tool:<name>|model:<name>")
        for field, allowed in (("kind", KINDS), ("found_by", FOUND_BY), ("cost", COSTS), ("status", STATUSES)):
            if e[field] not in allowed:  # strings only here, checked above
                errs.append(f"{who}: {field} '{e[field]}' not in {list(allowed)}")
        for field in ("area", "symptom"):
            if not e[field].strip():
                errs.append(f"{who}: {field} is empty")
        if not isinstance(e["repeats"], list) or not all(valid_repeat(d) for d in e["repeats"]):
            errs.append(f"{who}: repeats must be real dates YYYY-MM-DD or {{date, executor, detail}} mappings")
        if "events" in e and (not isinstance(e["events"], list) or not all(isinstance(x, str) for x in e["events"])):
            errs.append(f"{who}: events must be a list of strings")
        p = e.get("prevention")
        if p is not None:
            if (not isinstance(p, dict) or p.get("kind") not in PREVENTION_KINDS
                    or not isinstance(p.get("ref"), str) or not p["ref"].strip()):
                errs.append(f"{who}: prevention needs kind {list(PREVENTION_KINDS)} and a ref")
        if e["status"] == "prevented" and not p:
            errs.append(f"{who}: status prevented needs a prevention")
    return errs


def next_id(entries: list[dict]) -> str:
    nums = [int(e["id"][4:]) for e in entries if ID_RE.match(str(e.get("id", "")))]
    return f"ERR-{max(nums, default=0) + 1:04d}"


def add_entry(entries: list[dict], today: str, **f) -> tuple[dict, bool]:
    """(entry, is_new). The same key adds today to repeats of the existing entry instead of a new one.
    Every event counts: repeats may hold the same date many times. The key is recomputed from the fields."""
    key = norm_key(f["kind"], f["area"], f["symptom"])
    detail, event = f.get("detail"), f.get("event")
    for e in entries:
        if norm_key(e["kind"], e["area"], e["symptom"]) == key:
            if f["executor"] == e["executor"] and not detail:
                e["repeats"].append(today)
            else:  # the repeat keeps who made it and where (a branch), so stats attribute it correctly
                e["repeats"].append({k: v for k, v in (("date", today), ("executor", f["executor"]),
                                                       ("detail", detail)) if v})
            if event:
                e.setdefault("events", []).append(event)
            return e, False
    cause = f.get("cause") or "unknown (draft)"
    e = {"id": next_id(entries), "date": today, "executor": f["executor"], "kind": f["kind"], "area": f["area"],
         "symptom": f["symptom"].strip(), "cause": f"{cause}; {detail}" if detail else cause,
         "found_by": f.get("found_by") or "self", "cost": f.get("cost") or "low", "fix": f.get("fix") or "",
         "repeats": [], "status": "open"}
    if event:
        e["events"] = [event]
    entries.append(e)
    return e, True


def occ_events(e: dict) -> list[tuple[str, str]]:
    """(date, executor) of the first occurrence and of each repeat, each repeat with its own executor."""
    out = [(str(e["date"]), e["executor"])]
    for d in e["repeats"]:
        out.append((d["date"], d.get("executor") or e["executor"]) if isinstance(d, dict) else (str(d), e["executor"]))
    return out


def occurrences(e: dict) -> list[str]:
    return [d for d, _ in occ_events(e)]


def has_event(entries: list[dict], event: str) -> bool:
    return any(event in e.get("events", []) for e in entries)


def window_stats(entries: list[dict], today: dt.date, days: int) -> dict:
    lo = (today - dt.timedelta(days=days)).isoformat()
    total = repeats = 0
    by_kind: dict[str, int] = {}  # the leaderboards rank repeats, not first occurrences
    by_exec: dict[str, int] = {}
    for e in entries:
        n = 0
        for i, (d, who) in enumerate(occ_events(e)):
            if lo < d <= today.isoformat():
                total += 1
                if i:
                    n += 1
                    by_exec[who] = by_exec.get(who, 0) + 1
        repeats += n
        if n:
            by_kind[e["kind"]] = by_kind.get(e["kind"], 0) + n
    return {"total": total, "repeats": repeats, "kind": by_kind, "executor": by_exec}


def top(d: dict[str, int], n: int = 5) -> str:
    items = sorted(d.items(), key=lambda kv: (-kv[1], kv[0]))[:n]
    return ", ".join(f"{k} {v}" for k, v in items) or "-"


def parse_prevention(s: str) -> dict:
    kind, _, ref = s.partition(":")
    return {"kind": kind.strip(), "ref": ref.strip()}


def review_drafts(path: Path) -> list[dict]:
    """Drafts from a reviewer file with verdict CHANGES; [] otherwise.
    Two shapes: a llm_worker review record (JSON, one draft per finding) or a markdown review."""
    text = path.read_text(encoding="utf-8")
    if text.lstrip().startswith("{"):
        try:
            rec = json.loads(text)
        except ValueError:
            return []
        res = rec.get("result") or rec
        if res.get("verdict") != "changes_requested":
            return []
        area = str(rec.get("task") or path.stem)
        found = res.get("findings") or []
        return [{"kind": "code", "area": area, "found_by": "reviewer",
                 "symptom": f"[{f.get('severity', '?')}] {f.get('path', '')}:{f.get('line', '')} {f.get('claim', '')}"[:200]}
                for f in found] or [{"kind": "code", "area": area, "found_by": "reviewer",
                                     "symptom": str(res.get("summary", "changes requested"))[:200]}]
    lines = text.splitlines()
    for i, l in enumerate(lines):
        if re.search(r"(?i)(verdict|вердикт|status)\W+\**\s*changes", l):
            rest = [x.strip(" -*#") for x in lines[i + 1:] if x.strip(" -*#")]
            task = re.search(r"TASK-\d{4}", path.name + " " + " ".join(lines[:30]))
            return [{"kind": "code", "area": task.group(0) if task else path.stem,
                     "symptom": (rest[0] if rest else l.strip())[:200], "found_by": "reviewer"}]
    return []


def read_jsonl(path: Path) -> list[dict]:
    out = []
    if path.exists():
        for line in path.read_text(encoding="utf-8").splitlines():
            try:
                out.append(json.loads(line))
            except ValueError:
                continue
    return out


def import_state_path(state: Path, catalogue: Path) -> Path:
    if catalogue.resolve() == ERRORS.resolve():
        return state / IMPORT_STATE
    return state / f"errors_import-{hashlib.sha1(str(catalogue.resolve()).encode()).hexdigest()[:10]}.json"


def read_import_state(path: Path) -> dict:
    try:
        st = json.loads(path.read_text(encoding="utf-8"))
        return {"offsets": dict(st.get("offsets") or {}), "ci_seen": [int(x) for x in st.get("ci_seen") or []],
                "journal": st.get("journal") if isinstance(st.get("journal"), dict) else None}
    except (OSError, ValueError, TypeError, AttributeError):
        return {"offsets": {}, "ci_seen": [], "journal": None}


def write_import_state(path: Path, st: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(f".{path.name}.{os.getpid()}.tmp")
    try:
        tmp.write_text(json.dumps(st, sort_keys=True), encoding="utf-8")
        os.replace(tmp, path)
    except BaseException:
        tmp.unlink(missing_ok=True)
        raise


def read_new_lines(path: Path, offset: int) -> tuple[list[tuple[str, dict]], int]:
    """Complete lines after `offset` and the new offset. The sources are append-only and never renamed or
    truncated, so a hook writing during the import cannot lose a line; a half-written last line waits for the next
    import. A shorter file than the offset means someone truncated it: start again from 0."""
    if not path.exists():
        return [], offset
    if path.stat().st_size < offset:
        offset = 0
    with path.open("rb") as f:
        f.seek(offset)
        data = f.read()
    cut = data.rfind(b"\n") + 1
    recs, pos = [], offset
    for line in data[:cut].split(b"\n")[:-1]:
        try:
            rec = json.loads(line)
        except ValueError:
            rec = None
        if isinstance(rec, dict):
            recs.append((f"{path.name}:{pos}", rec))  # byte offset of the line: a stable id inside an append-only file
        elif line.strip():
            print(f"warning: {path.name}@{pos}: line skipped (not a JSON object)", file=sys.stderr)
        pos += len(line) + 1
    return recs, offset + cut


def state_drafts(records: dict[str, list[tuple[str, dict]]]) -> tuple[list[dict], int]:
    """Drafts from hook refusals and land failures, and the number of ignored lines."""
    drafts, skipped = [], 0
    hooks = records.get("hook_refusals.jsonl", [])
    lands = records.get("land_failures.jsonl", [])
    for eid, r in hooks:
        hook = str(r.get("hook", "hook"))
        if hook in IGNORED_HOOKS:
            skipped += 1
            continue
        drafts.append({"date": day(r.get("t")), "executor": f"tool:{hook}", "kind": "process", "area": "hooks",
                       "symptom": f"{hook} refused: {str(r.get('msg', ''))[:120]}", "found_by": "hook", "event": eid})
    for eid, r in lands:
        code = r.get("code")
        why = "second review missing or not passed" if code == 6 else "verify failed on the merged tree"
        drafts.append({"date": day(r.get("t")), "executor": "tool:land.py", "kind": "process" if code == 6 else "code",
                       "area": "land", "symptom": f"land exit {code}: {why}",
                       "detail": f"branch {str(r.get('branch', '?'))[:80]}", "found_by": "ci", "event": eid})
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
    if args.cmd in MUTATING:
        # One writer at a time: concurrent add/import would lose each other's entries (read-modify-write of the file).
        args.state.mkdir(parents=True, exist_ok=True)
        with (args.state / "errors_import.lock").open("a") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            return execute(args)
    return execute(args)


def execute(args) -> int:
    today = args.today or dt.date.today().isoformat()
    try:
        entries = load(args.file)
    except ValueError as ex:
        print(f"ERROR   {ex}")
        return 1
    if args.cmd != "check":
        bad = validate(entries)
        if bad:  # a value of the wrong type would crash the command: refuse with the list
            print("\n".join(bad))
            return 1

    if args.cmd == "check":
        errs = validate(entries)
        for e in errs:
            print("ERROR  ", e)
        print(f"errors.py: {len(entries)} entries, {len(errs)} error(s)")
        return 1 if errs else 0
    if args.cmd == "add":
        if args.from_review:
            drafts = review_drafts(args.from_review)
            if not drafts:
                print("not a CHANGES verdict: nothing added")
                return 0
            touched = []
            for dft in drafts:
                e, new = add_entry(entries, today, **dft, executor=args.executor or "agent:developer")
                touched.append(f"{e['id']} {'added' if new else 'repeat recorded'}")
            bad = validate(entries)
            if bad:
                print("\n".join(bad))
                return 2
            save(args.file, entries)
            print("\n".join(touched))
            return 0
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
            print(f"  repeats by kind: {top(s['kind'])}")
            print(f"  repeats by executor: {top(s['executor'])}")
        try:  # rejection memory (#591): do reviewers still repeat rejected findings
            sys.path.insert(0, str(Path(__file__).resolve().parent))
            import rejections
            from tools.llm_worker import rejections as rej_core
            journal = rej_core.load(rej_core.journal_path(ROOT))
            for days in (7, 30):
                lo = (t - dt.timedelta(days=days)).isoformat()
                print(f"rejection memory {days}d: " + rejections.stats_line(journal, lo, (t + dt.timedelta(days=1)).isoformat()))
        except (ValueError, OSError, ImportError) as ex:
            print(f"rejections: not available ({ex})")
        return 0
    if args.cmd in ("repeat", "close"):
        e = next((x for x in entries if x["id"] == args.id), None)
        if e is None:
            print(f"unknown {args.id}")
            return 2
        if args.cmd == "repeat":
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
        spath = import_state_path(args.state, args.file)  # an alternate catalogue has its own cursors
        st = read_import_state(spath)
        j = st["journal"]
        # An interrupted import left its events in the journal: they are applied again by event id (the ones the
        # catalogue already holds are skipped), whatever happened to the catalogue in between.
        pending = list(j["events"]) if j else []
        base_off = j["offsets"] if j else st["offsets"]
        base_seen = j["ci_seen"] if j else st["ci_seen"]
        records, new_off = {}, {}
        for name in SOURCES:
            records[name], new_off[name] = read_new_lines(args.state / name, int(base_off.get(name, 0)))
        drafts, skipped = state_drafts(records)
        seen, fetched = set(base_seen), []
        if args.ci:
            sys.path.insert(0, str(Path(__file__).resolve().parent))
            import watch
            events, fetched = watch.red_ci_events(seen)
            for ev in events:
                drafts.append({"date": ev["date"], "executor": "tool:ci", "kind": "code", "area": "ci",
                               "symptom": f"red CI on main: {ev['title']}", "found_by": "ci", "event": f"ci:{ev['id']}"})
        new_seen = sorted(seen | set(fetched))[-CI_SEEN_KEEP:]
        todo = pending + drafts
        applied = new = 0
        for dft in todo:
            if dft.get("event") and has_event(entries, dft["event"]):
                continue
            before = copy.deepcopy(entries)
            e, is_new = add_entry(entries, dft["date"], **{k: v for k, v in dft.items() if k != "date"})
            bad = validate([e])
            if bad:  # one odd event must not block the rest: skip it with a warning
                entries[:] = before
                print(f"warning: event {dft.get('event', '?')} skipped: {'; '.join(bad)}", file=sys.stderr)
                continue
            applied += 1
            new += is_new
        if todo:
            st["journal"] = {"events": todo, "offsets": new_off, "ci_seen": new_seen}
            write_import_state(spath, st)
            save(args.file, entries)
        st.update(offsets=new_off, ci_seen=new_seen, journal=None)
        write_import_state(spath, st)
        print(f"import: {applied} event(s), {new} new draft(s), {skipped} ignored")
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
