#!/usr/bin/env python3
"""Knowledge base control: documents and decisions must agree with known facts.

  python3 tools/development/kb_check.py check            all living documents
  python3 tools/development/kb_check.py check --staged   staged Markdown files (pre-commit)
  python3 tools/development/kb_check.py check --file F   one file (PostToolUse hook)
  python3 tools/development/kb_check.py check --brief    one line (SessionStart hook)
  python3 tools/development/kb_check.py related "text"   known facts and earlier decisions on a topic
  python3 tools/development/kb_check.py stale [--days 30]  facts not reviewed for 30 days (for /handoff)
  python3 tools/development/kb_check.py decision-facts N  facts related to decision #N (kb_guard)
  python3 tools/development/kb_check.py landing --base B --head H [--message M]
                                                          land.py gate: a new decision needs its facts (#597)

Errors (exit 1):
  - an id that does not exist: #NNN above the last owner decision, ADR/TASK/EXP/HYP/RR/MS not in
    the registry, DEBT, RISK or O-NNN not in their tables;
  - a fact of development/FACTS.yaml whose probe no longer matches the repository (drift, #597);
    with --staged a warning unless development/FACTS.yaml itself is staged (landing and CI refuse it);
  - a fact whose source cites a decision marked SUPERSEDED in DECISIONS_LOG.md (#597);
  - a fact with a `reviewed:` value that is not a date.
Warnings (exit 0):
  - a fact whose source file changed in git after its `reviewed:` date (re-check it);
  - a fact whose source cites a decision that is partly superseded;
  - a stale pattern of a fact in a living document;
  - an ADR named with a status that differs from the registry;
  - a repository path in backticks that does not exist.

`related` is a retrieval aid, not a judge: before a decision is recorded or a question is put to
the owner, read what it prints and compare.
"""
from __future__ import annotations

import argparse
import datetime as dt
import re
import subprocess
import sys
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))
import registry  # noqa: E402

FACTS = ROOT / "development" / "FACTS.yaml"
DECISIONS = ROOT / "docs" / "decisions" / "DECISIONS_LOG.md"
ORCH = ROOT / "docs" / "decisions" / "ORCH_DECISIONS.md"
DEBT = ROOT / "development" / "TECH_DEBT.md"
RISKS = ROOT / "development" / "RISKS.md"

# Documents that describe the present state and must not carry outdated claims.
LIVING = ["CLAUDE.md", "CURRENT_STATE.md", "development/PLAN.md", "development/ROADMAP.md",
          "docs/architecture/ARCHITECTURE.md", "docs/vision/PROJECT_VISION.md",
          "docs/ui/UI_DESIGN_SYSTEM.md", "docs/game-design/M_PLAY_LOOP.md"]
LIVING_GLOBS = [".claude/rules/*.md", ".claude/agents/*.md", ".claude/skills/*/SKILL.md"]
PATH_ROOTS = ("src/", "tests/", "tools/", "docs/", "development/", "lab/", "mods/", ".claude/", ".github/")
ADR_STATUS = re.compile(r"\b(PROPOSED|ACCEPTED|SUPERSEDED|DEPRECATED|REJECTED)\b")


def living_files() -> list[Path]:
    out = [ROOT / p for p in LIVING if (ROOT / p).exists()]
    for g in LIVING_GLOBS:
        out += sorted(ROOT.glob(g))
    for it in registry.load_items([]):
        if it.type.prefix == "MS" and it.meta.get("status") != "RELEASED":
            out.append(it.path)
    return out


def load_facts() -> list[dict]:
    if not FACTS.exists():
        return []
    return (yaml.safe_load(FACTS.read_text(encoding="utf-8")) or {}).get("facts", [])


DECISION_ROW = re.compile(r"^\| (\d+) \| ([^|]*?) \| (.*) \| ([^|]*) \|\s*$", re.M)
FULLY_SUPERSEDED = re.compile(r"^\W*SUPERSEDED\b")
FACTS_OK = re.compile(r"facts-ok:\s*((?:#\d+[\s,]*)+)", re.I)
# Logs that change every day: a fact citing them is not re-checked on every new row.
NOT_SOURCES = {"docs/decisions/DECISIONS_LOG.md", "docs/decisions/ORCH_DECISIONS.md"}
STALE_DAYS = 30


def decision_rows(text: str) -> dict[int, str]:
    """Number -> text of every decision row of the decisions log (the only parser of its layout here).
    A row whose text runs over several lines (lists, blank lines inside the cell) is joined up to its closing pipe."""
    out: dict[int, str] = {}
    buf: list[str] = []
    for line in text.splitlines() + [""]:
        if buf and (re.match(r"^\| \d+ \|", line) or len(buf) > 200):
            buf = []  # the row never closed: not a table row
        if not buf and not re.match(r"^\| \d+ \|", line):
            continue
        if line.strip():
            buf.append(line.strip())
        if line.rstrip().endswith("|") and not line.rstrip().endswith("\\|"):
            m = DECISION_ROW.match(" ".join(buf))
            if m:
                out[int(m.group(1))] = m.group(3)
            buf = []
    return out


SHORT_KEYWORD = 4  # shorter keywords (cc, g4, m0, api, лет, тик, 400) match only as whole words
DECISION_MIN_HITS = 2  # landing and decision-facts: a fact relates to a decision only on two different keywords


def keyword_hits(text: str, fact: dict) -> set[str]:
    """The fact's keywords found in the text: a prefix match for long ones, whole words for short ones."""
    low = text.lower()
    hits = set()
    for k in fact.get("keywords") or []:
        k = str(k).lower()
        if not k:
            continue
        if len(k) < SHORT_KEYWORD:
            if re.search(r"(?<!\w)" + re.escape(k) + r"(?!\w)", low):
                hits.add(k)
        elif k in low:
            hits.add(k)
    return hits


def related_facts(text: str, facts: list[dict], min_hits: int = 1) -> list[dict]:
    """Facts with at least min_hits different keywords in the text (`related` uses 1)."""
    return [f for f in facts if len(keyword_hits(text, f)) >= min_hits]


def cited_decisions(fact: dict) -> list[int]:
    """Decision numbers named in the fact's source (#NNN; a range #a–#b names its two ends)."""
    return [int(n) for n in re.findall(r"#(\d{1,4})\b", str(fact.get("source", "")))]


def reviewed_date(fact: dict) -> dt.date | None:
    """The fact's reviewed: date; None when absent. Raises ValueError for a value that is not a date."""
    v = fact.get("reviewed")
    if v is None:
        return None
    if isinstance(v, dt.datetime):
        return v.date()
    if isinstance(v, dt.date):
        return v
    return dt.date.fromisoformat(str(v).strip())


def fact_source_paths(fact: dict, root: Path = ROOT) -> list[str]:
    """Repository files behind a fact: the probe file and the paths named in its source (globs expanded)."""
    out = []
    pr = fact.get("probe") or {}
    if pr.get("file"):
        out.append(pr["file"])
    for tok in re.split(r"[;,\s]+", str(fact.get("source", ""))):
        tok = tok.strip("`'\"()")
        if not tok.startswith(PATH_ROOTS):
            continue
        if "*" in tok:
            out += sorted(p.relative_to(root).as_posix() for p in root.glob(tok) if p.is_file())
        elif (root / tok).is_file():
            out.append(tok)
    return [p for p in dict.fromkeys(out) if p not in NOT_SOURCES]


def last_change(paths: list[str], root: Path = ROOT) -> dt.date | None:
    """Commit date of the last commit touching any of the paths; None when git cannot tell."""
    if not paths:
        return None
    try:
        r = subprocess.run(["git", "log", "-1", "--format=%cs", "--", *paths], cwd=root,
                           capture_output=True, text=True, timeout=10)
        return dt.date.fromisoformat(r.stdout.strip()) if r.returncode == 0 and r.stdout.strip() else None
    except (OSError, subprocess.SubprocessError, ValueError):
        return None


def known_ids() -> dict[str, set[str] | int]:
    items = {i.id for i in registry.load_items([])}
    dec = [int(m) for m in re.findall(r"^\| (\d+) \|", DECISIONS.read_text(encoding="utf-8"), re.M)]
    def table(path: Path, pat: str) -> set[str]:
        return set(re.findall(pat, path.read_text(encoding="utf-8"), re.M)) if path.exists() else set()
    return {"items": items, "max_decision": max(dec) if dec else 0,
            "debt": table(DEBT, r"^\| (DEBT-\d{4}) "), "risk": table(RISKS, r"^\| (RISK-\d{4}) "),
            "orch": table(ORCH, r"^\| (O-\d{3}) ")}


def rel(p: Path) -> str:
    try:
        return p.resolve().relative_to(ROOT).as_posix()
    except ValueError:
        return str(p)


def added_lines(path: Path) -> set[int]:
    """Line numbers added or changed in the staged version of the file."""
    out = subprocess.run(["git", "diff", "--cached", "-U0", "--", rel(path)], cwd=ROOT,
                         capture_output=True, text=True).stdout
    lines: set[int] = set()
    for m in re.finditer(r"^@@ -\S+ \+(\d+)(?:,(\d+))? @@", out, re.M):
        start, count = int(m.group(1)), int(m.group(2) or 1)
        lines.update(range(start, start + count))
    return lines


def check_file(path: Path, ids: dict, facts: list[dict], living: bool,
               errors: list[str], warnings: list[str], only: set[int] | None = None) -> None:
    try:
        text = path.read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError):
        return
    where = rel(path)
    statuses = {i.id: i.meta.get("status") for i in registry.load_items([]) if i.type.prefix == "ADR"}
    for n, line in enumerate(text.splitlines(), 1):
        if only is not None and n not in only:
            continue  # pre-commit judges only the lines this commit adds: old logs are history
        for m in re.finditer(r"(?<![\w/&])#(\d{2,4})\b", line):
            if int(m.group(1)) > ids["max_decision"] and not re.search(r"[0-9A-Fa-f]{6}", line[m.start():m.start() + 8]):
                errors.append(f"{where}:{n}: decision #{m.group(1)} does not exist (last is #{ids['max_decision']})")
        for m in re.finditer(r"\b((?:ADR|TASK|EXP|HYP|RR)-\d{4}|MS-M\d+)\b", line):
            if m.group(1) not in ids["items"]:
                errors.append(f"{where}:{n}: {m.group(1)} is not in the registry")
        for m in re.finditer(r"\b(DEBT-\d{4})\b", line):
            if m.group(1) not in ids["debt"]:
                errors.append(f"{where}:{n}: {m.group(1)} is not in TECH_DEBT.md")
        for m in re.finditer(r"\b(RISK-\d{4})\b", line):
            if m.group(1) not in ids["risk"]:
                errors.append(f"{where}:{n}: {m.group(1)} is not in RISKS.md")
        for m in re.finditer(r"\b(O-\d{3})\b", line):
            if m.group(1) not in ids["orch"]:
                errors.append(f"{where}:{n}: {m.group(1)} is not in ORCH_DECISIONS.md")
        if not living:
            continue
        for f in facts:
            for pat in f.get("stale_patterns", []):
                if re.search(pat, line) and "superseded" not in line.lower() and "отмен" not in line.lower():
                    warnings.append(f"{where}:{n}: contradicts fact '{f['id']}' ({f['statement'][:80]}): /{pat}/")
        st = ADR_STATUS.findall(line)
        if len(set(st)) == 1:
            for adr in re.findall(r"\bADR-(\d{4})\b", line):
                real = statuses.get(f"ADR-{adr}")
                if real and real != st[0]:
                    warnings.append(f"{where}:{n}: ADR-{adr} is {real} in the registry, the line says {st[0]}")
        for m in re.finditer(r"`([\w.\-/]+)`", line):
            p = m.group(1)
            if p.startswith(PATH_ROOTS) and "/" in p.rstrip("/") and not (ROOT / p).exists() \
                    and not re.search(r"NNNN|XXXX|<|\*", p):
                warnings.append(f"{where}:{n}: path `{p}` does not exist")


def probe_drift(fact: dict, root: Path = ROOT, read=None) -> str | None:
    """Why the fact's probe disagrees with the repository, or None (no probe, or it matches).
    read(path) -> text or None replaces the working tree (landing reads the branch head)."""
    pr = fact.get("probe")
    if not pr:
        return None
    if read is None:
        target = root / pr["file"]
        text = target.read_text(encoding="utf-8", errors="replace") if target.exists() else None
    else:
        text = read(pr["file"])
    if text is None:
        return f"probe file {pr['file']} is missing"
    if "count" in pr:
        got = str(len(re.findall(pr["count"], text, re.M)))
    else:
        m = re.search(pr["regex"], text, re.S)
        got = m.group(1) if m else "no match"
    if got != str(pr["expect"]):
        return f"{pr['file']} gives {got}, the fact says {pr['expect']}"
    return None


def check_facts(facts: list[dict], errors: list[str], warnings: list[str], decisions: dict[int, str] | None = None,
                fresh: bool = True, drift_is_error: bool = True) -> None:
    """Drift and superseded sources are errors (#597); a source changed after `reviewed:` is a warning.
    drift_is_error=False (pre-commit without FACTS.yaml staged) reports drift as a warning: CI and landing judge it."""
    if decisions is None:
        decisions = decision_rows(DECISIONS.read_text(encoding="utf-8")) if DECISIONS.exists() else {}
    for f in facts:
        why = probe_drift(f)
        if why:
            (errors if drift_is_error else warnings).append(f"FACTS.yaml: fact '{f['id']}' drifted: {why}. Update the fact and the documents "
                          "that state it, or fix the code.")
        for n in cited_decisions(f):
            body = decisions.get(n)
            if body is None:
                continue
            if FULLY_SUPERSEDED.match(body):
                errors.append(f"FACTS.yaml: fact '{f['id']}' cites decision #{n}, which is SUPERSEDED: "
                              f"{body[:120]}. Cite the decision in force.")
            elif "SUPERSEDED" in body:
                warnings.append(f"FACTS.yaml: fact '{f['id']}' cites decision #{n}, which is partly superseded: "
                                f"{body[:120]}")
        try:
            rev = reviewed_date(f)
        except ValueError:
            errors.append(f"FACTS.yaml: fact '{f['id']}': reviewed: {f.get('reviewed')!r} is not a YYYY-MM-DD date")
            continue
        if rev is None or not fresh:
            continue
        changed = last_change(fact_source_paths(f))
        if changed and changed > rev:
            warnings.append(f"FACTS.yaml: fact '{f['id']}': its source changed on {changed}, after reviewed: {rev}; "
                            "re-check the fact and bump reviewed:")


def cmd_check(args) -> int:
    ids, facts = known_ids(), load_facts()
    errors: list[str] = []
    warnings: list[str] = []
    living = {p.resolve() for p in living_files()}
    if args.file:
        targets = [Path(args.file) if Path(args.file).is_absolute() else ROOT / args.file]
    elif args.staged:
        out = subprocess.run(["git", "diff", "--cached", "--name-only", "--diff-filter=ACM"], cwd=ROOT,
                             capture_output=True, text=True).stdout.split()
        targets = [ROOT / f for f in out if f.endswith((".md", ".yaml")) and not f.startswith("lab/")]
        staged_facts = rel(FACTS) in out
    else:
        targets = sorted(living)
    for t in targets:
        if t.suffix == ".md" and t.exists():
            check_file(t, ids, facts, t.resolve() in living, errors, warnings,
                       added_lines(t) if args.staged else None)
    if not args.file:
        check_facts(facts, errors, warnings, drift_is_error=not args.staged or staged_facts)
        import errors as catalogue  # development/errors.yaml schema (owner #584)
        try:
            errors += [f"errors.yaml: {e}" for e in catalogue.validate(catalogue.load(catalogue.ERRORS))]
        except ValueError as ex:
            errors.append(f"errors.yaml: {ex}")
    if args.brief:
        if errors or warnings:
            print(f"{len(errors)} error(s), {len(warnings)} warning(s) (python3 tools/development/kb_check.py check)")
        return 0
    for e in errors:
        print("ERROR  ", e)
    if not args.errors_only:
        for w in warnings:
            print("WARNING", w)
    print(f"kb_check: {len(errors)} error(s), {len(warnings)} warning(s)")
    return 1 if errors else 0


def cmd_related(args) -> int:
    text = args.text.lower()
    words = {w for w in re.findall(r"[\w-]{4,}", text)}
    nums = set(re.findall(r"#(\d{1,4})", args.text)) | set(re.findall(r"\b(?:19|20)\d\d\b", args.text))
    shown = 0
    for f in related_facts(text, load_facts()):
        print(f"FACT {f['id']}: {f['statement']}  [{f.get('source', '')}]")
        shown += 1
    rows = re.findall(r"^\| (\d+) \| (\S+) \| (.*?) \| [^|]*\|\s*$", DECISIONS.read_text(encoding="utf-8"), re.M)
    scored = []
    for num, date, body in rows:
        low = body.lower()
        score = sum(1 for w in words if w in low) + (5 if num in nums else 0) + sum(2 for n in nums if n in body)
        if score >= args.min:
            scored.append((score, int(num), date, body))
    for score, num, date, body in sorted(scored, key=lambda r: (-r[0], -r[1]))[:args.limit]:
        print(f"DECISION #{num} ({date}): {body[:260]}")
        shown += 1
    if not shown:
        print("nothing related found in FACTS.yaml or DECISIONS_LOG.md")
    return 0


def cmd_stale(args) -> int:
    """Facts never reviewed or reviewed more than --days ago: the list /handoff carries."""
    today = dt.date.fromisoformat(args.today) if args.today else dt.date.today()
    n = 0
    for f in load_facts():
        try:
            rev = reviewed_date(f)
        except ValueError:
            print(f"STALE {f['id']}: reviewed: {f.get('reviewed')!r} is not a date")
            n += 1
            continue
        if rev is None:
            print(f"STALE {f['id']}: never reviewed")
            n += 1
        elif (today - rev).days > args.days:
            print(f"STALE {f['id']}: reviewed {rev} ({(today - rev).days} days ago)")
            n += 1
    print(f"kb_check stale: {n} fact(s) to re-check (older than {args.days} days or never reviewed)")
    return 0


def cmd_decision_facts(args) -> int:
    """Facts related to the given decision rows (kb_guard after a write of the decisions log)."""
    rows = decision_rows(DECISIONS.read_text(encoding="utf-8")) if DECISIONS.exists() else {}
    facts = load_facts()
    for n in args.nums:
        rel_ = related_facts(rows.get(n, ""), facts, DECISION_MIN_HITS)
        if not rel_:
            continue
        print(f"DECISION #{n}: confirm or update these facts in development/FACTS.yaml (or put 'facts-ok: #{n}' "
              "in the commit message when they still hold):")
        for f in rel_:
            print(f"FACT {f['id']}: {f['statement']}  [{f.get('source', '')}]")
    return 0


def git_blob(repo: Path, rev: str, path: str) -> str | None:
    """File content at a revision; None when the file is not there."""
    r = subprocess.run(["git", "show", f"{rev}:{path}"], cwd=repo, capture_output=True, text=True, timeout=30,
                       errors="replace")
    return r.stdout if r.returncode == 0 else None


def git_show(repo: Path, rev: str, path: str) -> str:
    """File content at a revision; '' when the file is not there."""
    return git_blob(repo, rev, path) or ""


def facts_at(repo: Path, rev: str) -> dict[str, dict]:
    data = yaml.safe_load(git_show(repo, rev, rel(FACTS))) or {}
    return {str(f.get("id")): f for f in (data.get("facts") or []) if isinstance(f, dict)}


def landing_problems(repo: Path, base: str, head: str, message: str = "") -> list[str]:
    """#597: every decision row the branch adds needs its related facts changed on the branch, or
    'facts-ok: #N' in the landing message or a commit message of base..head. The facts of the head tree must not
    drift from the head tree (pre-commit only warns about drift)."""
    after = facts_at(repo, head)
    out = []
    for f in after.values():
        why = probe_drift(f, read=lambda p: git_blob(repo, head, p))
        if why:
            out.append(f"fact '{f.get('id')}' drifts on the branch head: {why}. Update development/FACTS.yaml "
                       "or the code before landing")
    old = decision_rows(git_show(repo, base, rel(DECISIONS)))
    added = {n: b for n, b in decision_rows(git_show(repo, head, rel(DECISIONS))).items() if n not in old}
    if not added:
        return out
    before = facts_at(repo, base)
    changed = {i for i, f in after.items() if before.get(i) != f}
    log = subprocess.run(["git", "log", "--format=%B", f"{base}..{head}"], cwd=repo, capture_output=True,
                         text=True, timeout=30, check=True).stdout
    ok = {int(n) for grp in FACTS_OK.findall(message + "\n" + log) for n in re.findall(r"#(\d+)", grp)}
    for n, body in sorted(added.items()):
        missing = [f["id"] for f in related_facts(body, list(after.values()), DECISION_MIN_HITS)
                   if f["id"] not in changed]
        if missing and n not in ok:
            out.append(f"decision #{n} relates to facts {', '.join(missing)}, unchanged on the branch: update them "
                       f"(statement, probe or reviewed:) or confirm them with 'facts-ok: #{n}' in the commit message")
    return out


def cmd_landing(args) -> int:
    """Exit 0 passed, 1 refused, 2 the check itself failed (land.py names it apart)."""
    try:
        problems = landing_problems(ROOT, args.base, args.head, args.message)
    except Exception as ex:  # git, YAML or a bad regex in a probe: not a verdict on the branch
        print(f"kb_check landing: facts gate failed internally: {type(ex).__name__}: {ex}")
        return 2
    for p in problems:
        print("ERROR  ", p)
    print(f"kb_check landing: {len(problems)} problem(s) (new decisions without confirmed facts, drifting facts)")
    return 1 if problems else 0


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    c = sub.add_parser("check")
    c.add_argument("--staged", action="store_true")
    c.add_argument("--file")
    c.add_argument("--brief", action="store_true")
    c.add_argument("--errors-only", action="store_true")
    c.set_defaults(func=cmd_check)
    r = sub.add_parser("related")
    r.add_argument("text")
    r.add_argument("--limit", type=int, default=8)
    r.add_argument("--min", type=int, default=3)
    r.set_defaults(func=cmd_related)
    s = sub.add_parser("stale")
    s.add_argument("--days", type=int, default=STALE_DAYS)
    s.add_argument("--today", help="YYYY-MM-DD (tests)")
    s.set_defaults(func=cmd_stale)
    d = sub.add_parser("decision-facts")
    d.add_argument("nums", type=int, nargs="+")
    d.set_defaults(func=cmd_decision_facts)
    ld = sub.add_parser("landing")
    ld.add_argument("--base", required=True)
    ld.add_argument("--head", required=True)
    ld.add_argument("--message", default="")
    ld.set_defaults(func=cmd_landing)
    args = ap.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
