#!/usr/bin/env python3
"""Platform metrics in PostgreSQL (owner #593): where the work waits and what it costs.

  metrics.py collect [--quiet]                  read the sources and append new rows (idempotent)
  metrics.py report [--since D] [--until D]     one period (default: the last 7 days)
  metrics.py report --compare A..B C..D         two periods side by side (dates YYYY-MM-DD, end exclusive)

Tables (schema of the checkout, see kb_db.db_schema; created if missing, never dropped by kb_db build):
sessions, messages, llm_calls, landings, task_flow, owner_waits are journals: rows are only added, a row has
deleted_at for a soft delete, and a repeated collect adds nothing (keys are message, file, ledger, commit
ids). sessions (one aggregate per transcript file), messages (one row per API message: exact period sums) and
llm_calls (a ledger amount changes at settle) are refreshed when their source row changes. agent_runs and errors are the derived tables of kb_db.

Only numbers, ids, dates and short labels are stored. No prompt, answer, tool input or file text is read
into the database; a transcript is read for message id, time, model and usage only.
Sources: ~/.claude/projects/<project>/*.jsonl and <session>/subagents/*.jsonl (internal Claude Code
format, parsed tolerantly), tools/llm_worker/runs/*.json, the llm_worker ledger, .claude/.state/landings.jsonl
(written by land.py), git log of development/tasks, development/OPEN_QUESTIONS.md.
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import fcntl
import json
import math
import os
import re
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import kb_db  # noqa: E402

ROOT = kb_db.ROOT
DATE_RE = re.compile(r"\d{4}-\d{2}-\d{2}")

DDL = """create table if not exists sessions (key text primary key, kind text, session text, agent_id text, model text,
  start_t timestamptz, end_t timestamptz, msgs integer, input_tokens bigint, output_tokens bigint,
  cache_read_tokens bigint, cache_write_tokens bigint, deleted_at timestamptz, tasks text);
create table if not exists messages (key text primary key, kind text, session text, agent_id text, model text,
  t timestamptz, input_tokens bigint, output_tokens bigint, cache_read_tokens bigint, cache_write_tokens bigint,
  deleted_at timestamptz);
create table if not exists llm_calls (key text primary key, kind text, provider text, model text, task text,
  t timestamptz, input_tokens bigint, output_tokens bigint, usd numeric, verdict text, findings integer,
  round integer, deleted_at timestamptz);
create table if not exists landings (key text primary key, t timestamptz, branch text, task text, attempt integer,
  code integer, total_s numeric, verify_s numeric, steps text, deleted_at timestamptz);
create table if not exists task_flow (key text primary key, task text, t timestamptz, from_status text,
  to_status text, commit_id text, deleted_at timestamptz);
alter table sessions add column if not exists tasks text;
create table if not exists owner_waits (key text primary key, question text, asked date, answered date,
  deleted_at timestamptz);
"""
COLS = {
    "sessions": ["key", "kind", "session", "agent_id", "model", "start_t", "end_t", "msgs", "input_tokens",
                 "output_tokens", "cache_read_tokens", "cache_write_tokens", "tasks"],
    "messages": ["key", "kind", "session", "agent_id", "model", "t", "input_tokens", "output_tokens",
                 "cache_read_tokens", "cache_write_tokens"],
    "llm_calls": ["key", "kind", "provider", "model", "task", "t", "input_tokens", "output_tokens", "usd",
                  "verdict", "findings", "round"],
    "landings": ["key", "t", "branch", "task", "attempt", "code", "total_s", "verify_s", "steps"],
    "task_flow": ["key", "task", "t", "from_status", "to_status", "commit_id"],
    "owner_waits": ["key", "question", "asked", "answered"],
}

# --------------------------------------------------------------------------
# Parsers (pure functions)
# --------------------------------------------------------------------------


def iso(t) -> str | None:
    """UTC ISO time from epoch seconds or an ISO string; None when it is neither."""
    if isinstance(t, bool):
        return None
    if isinstance(t, (int, float)):
        try:
            return dt.datetime.fromtimestamp(t, dt.timezone.utc).isoformat()
        except (ValueError, OverflowError, OSError):
            return None
    if isinstance(t, str):
        try:
            return dt.datetime.fromisoformat(t.replace("Z", "+00:00")).astimezone(dt.timezone.utc).isoformat()
        except (ValueError, OverflowError, OSError):
            return None
    return None


def real_date(s: str) -> bool:
    try:
        dt.date.fromisoformat(s)
        return True
    except ValueError:
        return False


def num(v) -> int:
    return v if isinstance(v, int) and not isinstance(v, bool) and v >= 0 else 0


def parse_transcript(lines, key: str, kind: str, session: str, agent_id: str = "") -> dict | None:
    """Aggregate of one Claude Code transcript, plus one row per message under 'messages' (exact period sums).
    One API message appears in several lines (one per content
    block) with the same id and usage: it is counted once. Bad lines and unknown fields are skipped."""
    msgs: dict[str, dict] = {}
    seen_at: dict[str, str | None] = {}
    first = last = None
    tasks = None  # TASK ids named in the first user record; only the ids are kept
    for line in lines:
        try:
            r = json.loads(line)
        except ValueError:
            continue
        if not isinstance(r, dict):
            continue
        if tasks is None and r.get("type") == "user":
            tasks = sorted(set(re.findall(r"TASK-\d{4}", line)))[:8]
        t = iso(r.get("timestamp"))
        if t:
            first = min(first, t) if first else t
            last = max(last, t) if last else t
        m = r.get("message")
        if r.get("type") != "assistant" or not isinstance(m, dict) or not isinstance(m.get("usage"), dict):
            continue
        mid, u = m.get("id"), m["usage"]
        if not isinstance(mid, str) or not mid or m.get("model") == "<synthetic>":
            continue
        seen_at.setdefault(mid, t)  # the first record of a message gives its time
        msgs[mid] = dict(model=str(m.get("model") or ""), i=num(u.get("input_tokens")), o=num(u.get("output_tokens")),
                         cr=num(u.get("cache_read_input_tokens")), cw=num(u.get("cache_creation_input_tokens")))
    if not msgs:
        return None
    counts: dict[str, int] = {}
    for m in msgs.values():
        counts[m["model"]] = counts.get(m["model"], 0) + 1
    model = sorted(counts, key=lambda k: (-counts[k], k))[0]  # most used, ties by name
    return dict(key=key, kind=kind, session=session, agent_id=agent_id, model=model, start_t=first, end_t=last,
                msgs=len(msgs), input_tokens=sum(m["i"] for m in msgs.values()),
                output_tokens=sum(m["o"] for m in msgs.values()),
                cache_read_tokens=sum(m["cr"] for m in msgs.values()),
                cache_write_tokens=sum(m["cw"] for m in msgs.values()), tasks=",".join(tasks or []),
                messages=[dict(key=f"{key}:{mid}", kind=kind, session=session, agent_id=agent_id, model=v["model"],
                               t=seen_at[mid], input_tokens=v["i"], output_tokens=v["o"], cache_read_tokens=v["cr"],
                               cache_write_tokens=v["cw"]) for mid, v in msgs.items()])


def parse_run_record(name: str, rec, mtime: float) -> dict | None:
    """One llm_worker run file (review, gate, research, sketch, visual) as an llm_calls row."""
    if not isinstance(rec, dict):
        return None
    usage = rec.get("usage") if isinstance(rec.get("usage"), dict) else {}
    res = rec.get("result") if isinstance(rec.get("result"), dict) else {}
    findings = res.get("findings", res.get("FINDINGS"))
    verdict = res.get("verdict", res.get("STATUS", rec.get("verdict", rec.get("status"))))
    task = rec.get("task") or rec.get("item") or rec.get("input") or ""
    tin = usage.get("input_tokens", usage.get("prompt_tokens"))
    tout = usage.get("output_tokens", usage.get("completion_tokens"))
    items = rec.get("items")
    if isinstance(items, list) and rec.get("kind") == "research":
        # a research record has one usage and one verdict per item: sum them, count the verdicts
        tin = tout = 0
        counts: dict[str, int] = {}
        for it in items:
            if not isinstance(it, dict):
                continue
            u = it.get("usage") if isinstance(it.get("usage"), dict) else {}
            tin += num(u.get("prompt_tokens")) or num(u.get("prompt_cache_hit_tokens")) + num(u.get("prompt_cache_miss_tokens"))
            tout += num(u.get("completion_tokens"))
            v = str(it.get("verdict") or "?")[:20]
            counts[v] = counts.get(v, 0) + 1
        verdict = ",".join(f"{k}={counts[k]}" for k in sorted(counts))
        findings = [1] * sum(n for k, n in counts.items() if k != "confirmed")  # items not confirmed
    when = (iso(rec.get("created_at")) or iso(rec.get("started_at")) or iso(rec.get("created"))
            or iso(rec.get("date")) or iso(mtime))
    return dict(key="run:" + name, kind=str(rec.get("kind") or name.split("-")[0]), provider=str(rec.get("provider") or ""),
                model=str(rec.get("model") or ""), task=str(task)[:120], t=when,
                input_tokens=num(tin), output_tokens=num(tout),
                usd=None, verdict=str(verdict)[:80] if verdict else None,
                findings=len(findings) if isinstance(findings, list) else None,
                round=rec.get("round") if isinstance(rec.get("round"), int) else None)


def parse_ledger(lines) -> list[dict]:
    """Ledger reserve/settle records as llm_calls rows of kind 'ledger' (usd = settled, else reserved)."""
    calls: dict[str, dict] = {}
    for line in lines:
        try:
            r = json.loads(line)
            rid, usd = r["id"], float(r["usd"])
        except (ValueError, KeyError, TypeError):
            continue
        if not math.isfinite(usd) or usd < 0 or not isinstance(rid, str):
            continue
        if r.get("t") == "reserve":
            calls[rid] = dict(key="ledger:" + rid, kind="ledger", provider="", model=str(r.get("model") or ""),
                              task=str(r.get("task") or "")[:120], t=iso(r.get("ts")), input_tokens=0,
                              output_tokens=0, usd=usd, verdict=None, findings=None, round=None)
        elif r.get("t") == "settle" and rid in calls:
            calls[rid]["usd"] = usd
    return list(calls.values())


def parse_landings(lines) -> list[dict]:
    rows = []
    for line in lines:
        line = line.strip()
        try:
            r = json.loads(line)
        except ValueError:
            continue
        if not isinstance(r, dict):
            continue
        steps = r.get("steps") if isinstance(r.get("steps"), dict) else {}
        rows.append(dict(key=hashlib.sha1(line.encode()).hexdigest()[:16], t=iso(r.get("t")),
                         branch=str(r.get("branch") or ""), task=str(r.get("task") or ""),
                         attempt=r.get("attempt") if isinstance(r.get("attempt"), int) else None,
                         code=r.get("code") if isinstance(r.get("code"), int) else None,
                         total_s=r.get("total_s") if isinstance(r.get("total_s"), (int, float)) else None,
                         verify_s=steps.get("verify") if isinstance(steps.get("verify"), (int, float)) else None,
                         steps=json.dumps(steps, sort_keys=True)))
    return rows


def parse_task_flow(log: str) -> list[dict]:
    """Status transitions from 'git log -p -U0 --format=@@%H %cI' over development/tasks."""
    rows, commit, when, task, old = [], "", None, None, None
    for line in log.splitlines():
        if line.startswith("@@") and not line.startswith("@@ "):  # "@@ -1 +1 @@" is a hunk header
            parts = line[2:].split(" ", 1)
            commit, when, task, old = parts[0], iso(parts[1].strip()) if len(parts) > 1 else None, None, None
        elif line.startswith("+++ "):
            m = re.search(r"(TASK-\d{4})", line)
            task, old = (m.group(1) if m else None), None
        elif line.startswith("-status:") and not line.startswith("---"):
            m = re.match(r"-status:\s*(\w+)", line)
            old = m.group(1) if m else None
        elif line.startswith("+status:") and task and commit:
            m = re.match(r"\+status:\s*(\w+)", line)
            if m:
                rows.append(dict(key=f"{commit[:12]}:{task}", task=task, t=when, from_status=old,
                                 to_status=m.group(1), commit_id=commit[:12]))
    return rows


def parse_question_history(log: str, decisions: list[dict]) -> list[dict]:
    """Owner waits from the git history of OPEN_QUESTIONS.md ('git log --reverse -p -U0 --format=@@%H %cI').
    A question is asked at the commit that adds its row. It counts as answered only when its row was removed or
    closed (strike-through, 'Закрыт <date>') AND there is an answer: a decision that names the question id (Q<n>)
    dated on or after the question, or a date in the closing text. The answer date is the earliest such date; a
    removed or closed row with no answer stays unresolved. Question tables of every kind are included."""
    asked: dict[str, str] = {}
    closed_at: dict[str, str] = {}
    text_dates: dict[str, str] = {}  # answer date written in a closing row

    def settle(day: str, plus: dict[str, str | None], minus: set[str]) -> None:
        for qid, closing in plus.items():
            if closing is not None:
                if qid in asked and qid not in closed_at:
                    closed_at[qid] = day
                    if closing:
                        text_dates[qid] = closing
            else:
                asked.setdefault(qid, day)
        for qid in minus - set(plus):
            if qid in asked and qid not in closed_at:
                closed_at[qid] = day

    day, plus, minus = None, {}, set()
    for line in log.splitlines():
        if line.startswith("@@") and not line.startswith("@@ "):
            if day:
                settle(day, plus, minus)
            when = iso(line[2:].split(" ", 1)[1].strip()) if " " in line else None
            day, plus, minus = (when[:10] if when else None), {}, set()
        elif day and line[:2] in ("+|", "-|"):
            cells = kb_db.split_row(line[1:])
            if len(cells) < 3 or not cells[0] or set(cells[0]) <= set("-: ") or cells[0] in ("#", "ID"):
                continue
            qid = cells[0].strip("~ ")
            qid = f"Q{qid}" if qid.isdigit() else qid
            if line[0] == "+":
                if kb_db.question_closed(cells):  # '' = closed with no date in the text
                    mm = re.search(r"(?:закрыт\w*|closed|answered)\W{0,12}(\d{4}-\d{2}-\d{2})", " | ".join(cells), re.I)
                    plus[qid] = mm.group(1) if mm and real_date(mm.group(1)) else ""
                else:
                    plus[qid] = None
            else:
                minus.add(qid)
    if day:
        settle(day, plus, minus)
    answered: dict[str, str] = {}
    for qid in closed_at:
        a = asked[qid]
        dates = [d["date"] for d in decisions if d.get("date") and d["date"] >= a
                 and re.search(rf"(?<![\w-]){re.escape(qid)}(?!\w)", d.get("text") or "")]
        if qid in text_dates and text_dates[qid] >= a:
            dates.append(text_dates[qid])
        if dates:
            answered[qid] = min(dates)
    return [dict(key=q, question=q, asked=asked[q], answered=answered[q]) for q in sorted(answered)]


# --------------------------------------------------------------------------
# Sources
# --------------------------------------------------------------------------


def claude_dir() -> Path:
    env = os.environ.get("SINGULARITY_CLAUDE_PROJECTS")
    if env:
        return Path(env)
    return Path.home() / ".claude" / "projects" / ("-" + str(kb_db.git_common_parent() or ROOT).strip("/").replace("/", "-"))


def read_lines(p: Path):
    try:
        with p.open(encoding="utf-8", errors="replace") as f:
            yield from f
    except OSError:
        return


def collect_sessions(base: Path) -> tuple[list[dict], list[dict]]:
    """(one aggregate per transcript file, one row per message)."""
    rows, msgs = [], []
    if not base.is_dir():
        return rows, msgs
    parsed = [(p, "session:" + p.stem, "session", p.stem, "") for p in sorted(base.glob("*.jsonl"))]
    parsed += [(p, "agent:" + p.stem.removeprefix("agent-"), "agent", p.relative_to(base).parts[0],
                p.stem.removeprefix("agent-")) for p in sorted(base.glob("*/subagents/**/*.jsonl"))]
    for p, key, kind, sess, aid in parsed:
        r = parse_transcript(read_lines(p), key, kind, sess, aid)
        if r:
            msgs += r.pop("messages")
            rows.append(r)
    return rows, msgs


def runs_dir() -> Path:
    # runs/ is untracked: a worktree has none, so read the main checkout's
    return Path(os.environ.get("SINGULARITY_LLM_RUNS") or (kb_db.git_common_parent() or ROOT) / "tools" / "llm_worker" / "runs")


def rejection_rows(since: str, until: str) -> list[dict]:
    """Share of new review findings that repeat a finding rejected earlier (rejection journal, #591)."""
    sys.path.insert(0, str(ROOT))
    from tools.llm_worker import rejections as rej
    try:
        entries = rej.load(rej.journal_path(ROOT))
    except (ValueError, OSError):
        entries = []
    s = rej.repeat_stats(entries, rej.load_records(runs_dir()), since, until)
    return [{"findings": s["findings"], "repeating_rejected": s["repeats"], "share_pct": s["share_pct"]}]


def ledger_path() -> Path:
    base = os.environ.get("XDG_DATA_HOME") or str(Path.home() / ".local" / "share")
    return Path(os.environ.get("SINGULARITY_LEDGER", Path(base) / "singularity-llm" / "ledger.jsonl"))


def collect_llm_calls() -> list[dict]:
    rows = []
    d = runs_dir()
    for p in sorted(d.glob("*.json")) if d.is_dir() else []:
        if p.name.endswith(".confirm.json"):
            continue
        try:
            rec = json.loads(p.read_text(encoding="utf-8"))
            r = parse_run_record(p.name, rec, p.stat().st_mtime)
        except (OSError, ValueError):
            continue
        if r:
            rows.append(r)
    rows += parse_ledger(read_lines(ledger_path()))
    return rows


def git_task_flow() -> list[dict]:
    try:
        r = subprocess.run(["git", "log", "--reverse", "-G^status:", "-p", "-U0", "--format=@@%H %cI", "--",
                            "development/tasks"], cwd=ROOT, text=True, capture_output=True, timeout=120)
    except (OSError, subprocess.SubprocessError):
        return []
    return parse_task_flow(r.stdout) if r.returncode == 0 else []


def git_owner_waits() -> list[dict]:
    try:
        r = subprocess.run(["git", "log", "--reverse", "-p", "-U0", "--format=@@%H %cI", "--",
                            "development/OPEN_QUESTIONS.md"], cwd=ROOT, text=True, capture_output=True, timeout=120)
    except (OSError, subprocess.SubprocessError):
        return []
    decisions = kb_db.parse_decisions(kb_db.read_text(ROOT / "docs/decisions/DECISIONS_LOG.md"))
    return parse_question_history(r.stdout, decisions) if r.returncode == 0 else []


def collect_all() -> dict[str, list[dict]]:
    land = kb_db.state_dir() / "landings.jsonl"
    sessions, messages = collect_sessions(claude_dir())
    return dict(sessions=sessions, messages=messages, llm_calls=collect_llm_calls(),
                landings=parse_landings(read_lines(land)), task_flow=git_task_flow(),
                owner_waits=git_owner_waits())


def sanitize(data: dict[str, list[dict]]) -> tuple[dict[str, list[dict]], list[str]]:
    """Drop rows that PostgreSQL would refuse (empty key, out-of-range numbers, NUL), so one bad journal record
    cannot abort the refresh. Returns the clean data and one warning per table with skipped rows."""
    out, notes = {}, []
    for table, cols in COLS.items():
        types = _types(table)
        good, bad = [], 0
        for r in data[table]:
            row, ok = dict(r), bool(r.get("key"))
            for c in cols:
                v, base = row.get(c), types[c].split()[0]
                if v is None:
                    continue
                if base in ("integer", "bigint"):
                    ok = ok and isinstance(v, int) and not isinstance(v, bool) and abs(v) < (2**31 if base == "integer" else 2**63)
                elif base == "numeric":
                    ok = ok and isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v) and abs(v) < 1e15
                else:
                    row[c] = str(v).replace("\x00", "")
            if ok:
                good.append(row)
            else:
                bad += 1
        out[table] = good
        if bad:
            notes.append(f"{table}: skipped {bad} malformed record(s)")
    return out, notes


# --------------------------------------------------------------------------
# Database
# --------------------------------------------------------------------------


def load_script(data: dict[str, list[dict]]) -> str:
    sch = kb_db.ident(kb_db.db_schema())
    out = [f"begin;\nselect pg_advisory_xact_lock(593);\ncreate schema if not exists {sch};\nset local search_path = {sch};\n", DDL]
    for table, cols in COLS.items():
        types = {c: t for c, t in _types(table).items()}
        out.append(f"create temp table tmp_{table} ({', '.join(f'{c} {types[c]}' for c in cols)}) on commit drop;\n")
        out.append(kb_db.copy_block(f"tmp_{table}", cols, data[table]))
        names = ", ".join(cols)
        if table in ("sessions", "messages", "llm_calls"):  # growing file / reserve then settle: refresh if changed
            upd = ", ".join(f"{c} = excluded.{c}" for c in cols[1:])
            old = ", ".join(f"{table}.{c}" for c in cols[1:])
            new = ", ".join(f"excluded.{c}" for c in cols[1:])
            tail = (f"on conflict (key) do update set {upd} "
                    f"where {table}.deleted_at is null and ({old}) is distinct from ({new})")
        else:
            tail = "on conflict (key) do nothing"
        out.append(f"insert into {table} ({names}) select {names} from tmp_{table} {tail};\n")
    out.append("commit;\n")
    return "".join(out)


def _types(table: str) -> dict[str, str]:
    m = re.search(rf"create table if not exists {table} \((.*?)\);", DDL, re.S)
    types = {}
    for part in re.split(r",\s*(?=\w+ \w)", m.group(1)):
        name, typ = part.split(None, 1)
        types[name] = typ.replace("primary key", "").strip()
    return types


def refresh() -> tuple[str | None, str]:
    """Read all sources and load them; (error or None, summary and warnings). One writer at a time: the flock is
    held while reading the sources, so an older snapshot cannot overwrite a newer one."""
    if not kb_db.available():
        return "PostgreSQL is not available", ""
    try:
        kb_db.ensure_db()
        state = kb_db.state_dir()
        state.mkdir(parents=True, exist_ok=True)
        with (state / "metrics.lock").open("a") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            data, notes = sanitize(collect_all())
            kb_db.psql(kb_db.db_name(), load_script(data))
        base = claude_dir()
        if not data["sessions"] and base.is_dir() and any(base.glob("*.jsonl")):
            notes.append("transcript files exist but none could be parsed (format changed?)")
        msg = "read " + ", ".join(f"{k}={len(v)}" for k, v in data.items())
        return None, msg + "".join(f"\nwarning: {n}" for n in notes)
    except Exception as ex:  # metrics must never break the caller
        return f"collect failed: {ex}", ""


def collect(quiet: bool = False) -> int:
    """CLI collect. --quiet (for hooks) prints nothing and always exits 0."""
    err, msg = refresh()
    if err:
        if not quiet:
            print(f"metrics: {err}", file=sys.stderr)
        return 0 if quiet else 1
    if not quiet:
        print("metrics: " + msg.replace("\nwarning: ", "\nmetrics: warning: "))
    return 0


def rows(sql: str) -> list[dict]:
    out = kb_db.psql(kb_db.db_name(), f"select row_to_json(q_) from ({sql}) q_", "-At", schema=True)
    return [json.loads(l) for l in out.splitlines() if l.strip()]


# --------------------------------------------------------------------------
# Report
# --------------------------------------------------------------------------


def period_sql(col: str, since: str, until: str) -> str:
    return f"{col} >= {kb_db.cast('date', since)} and {col} < {kb_db.cast('date', until)}"


def report_data(since: str, until: str, limit: int | None = 15) -> dict:
    """Report sections. limit cuts the per-task lists for display; the comparison asks for all rows."""
    p = lambda col: period_sql(col, since, until)
    lim = f"limit {limit}" if limit else ""
    day = lambda d: f"({kb_db.cast('date', d)})::timestamptz"
    both = ("agent_runs a full join (select * from sessions where kind = 'agent' and deleted_at is null) s "
            "on s.agent_id = a.agent_id")  # foreground agents have a transcript but no launch record
    secs = "extract(epoch from coalesce(a.stop_t - a.start_t, s.end_t - s.start_t))"
    return dict(
        by_agent=rows(f"""select coalesce(a.type, 'unknown') type, count(*) runs, round(avg({secs}) / 60) avg_min,
            coalesce(sum(s.input_tokens + s.cache_write_tokens), 0) in_tokens,
            coalesce(sum(s.cache_read_tokens), 0) cache_read_tokens, coalesce(sum(s.output_tokens), 0) out_tokens
            from {both} where {p('coalesce(a.start_t, s.start_t)')} group by 1 order by out_tokens desc, 1"""),
        by_task=rows(f"""select t.task, count(*) runs, round(sum({secs}) / 60) minutes,
            coalesce(sum(s.output_tokens), 0) out_tokens
            from {both} cross join lateral unnest(string_to_array(nullif(coalesce(nullif(a.tasks, ''), s.tasks), ''), ',')) t(task)
            where {p('coalesce(a.start_t, s.start_t)')} group by t.task order by out_tokens desc, t.task {lim}"""),
        tokens=rows(f"""select model, count(*) messages, coalesce(sum(input_tokens + cache_write_tokens), 0) in_tokens,
            coalesce(sum(cache_read_tokens), 0) cache_read_tokens, coalesce(sum(output_tokens), 0) out_tokens
            from messages where deleted_at is null and {p('t')} group by model order by out_tokens desc, model"""),
        llm=rows(f"""select provider, kind, count(*) calls, coalesce(sum(input_tokens), 0) in_tokens,
            coalesce(sum(output_tokens), 0) out_tokens
            from llm_calls where deleted_at is null and kind <> 'ledger' and {p('t')}
            group by provider, kind order by calls desc, provider, kind"""),
        spend=rows(f"""select model, count(*) entries, coalesce(sum(usd), 0) usd
            from llm_calls where deleted_at is null and kind = 'ledger' and {p('t')} group by model order by usd desc, model"""),
        rounds=rows(f"""select task, count(*) reviews, coalesce(max(round), 0) max_round,
            count(*) filter (where verdict like 'changes%') changes
            from llm_calls where deleted_at is null and kind = 'review' and {p('t')} group by task
            order by reviews desc, task {lim}"""),
        errors=rows(f"""select coalesce(executor, '-') executor, count(*) errors, coalesce(sum(repeats), 0) repeats
            from errors where {p('date')} group by executor order by errors desc, executor"""),
        stalls=rows(f"""with x as (select task, to_status, t, lead(t) over w nxt from task_flow where deleted_at is null
              window w as (partition by task order by t, key)),
            y as (select to_status, nxt is null ongoing, greatest(t, {day(since)}) a, least(coalesce(nxt, now()), {day(until)}, now()) b
              from x where to_status not in ('DONE', 'REJECTED', 'DEFERRED') and t < {day(until)}
              and coalesce(nxt, now()) > {day(since)})
            select to_status status, count(*) intervals, count(*) filter (where ongoing) ongoing,
              round(sum(extract(epoch from b - a)) / 3600) hours, round(max(extract(epoch from b - a)) / 3600) max_hours
            from y where b > a group by to_status order by hours desc nulls last, status"""),
        owner=rows(f"""select count(*) answered, round(avg(answered - asked), 1) avg_days, max(answered - asked) max_days
            from owner_waits where deleted_at is null and {p('answered')}"""),
        landings=rows(f"""select code, count(*) landings, round(avg(verify_s)) avg_verify_s, round(sum(verify_s)) verify_wall_s
            from landings where deleted_at is null and {p('t')} group by code order by code"""),
        verify_steps=rows(f"""select e.k step, count(*) runs, round(avg(e.v::numeric)) avg_s, round(max(e.v::numeric)) max_s
            from landings l cross join lateral jsonb_each_text(l.steps::jsonb) e(k, v)
            where l.deleted_at is null and e.k like 'verify:%' and {p('l.t')}
            group by e.k order by sum(e.v::numeric) desc, e.k {lim}"""),
        rejections=rejection_rows(since, until))


def summary(d: dict) -> dict[str, float]:
    s = lambda k, c: sum(float(r.get(c) or 0) for r in d[k])
    land = s("landings", "landings")
    ok = sum(float(r["landings"]) for r in d["landings"] if r["code"] == 0)
    return {"agent runs": s("by_agent", "runs"), "claude output tokens": s("tokens", "out_tokens"),
            "claude input tokens": s("tokens", "in_tokens"), "claude cache read tokens": s("tokens", "cache_read_tokens"), "llm calls (run records)": s("llm", "calls"),
            "ledger entries": s("spend", "entries"), "llm usd (ledger)": round(s("spend", "usd"), 4),
            "review records": s("rounds", "reviews"), "review findings": s("rejections", "findings"),
            "findings repeating a rejected one": s("rejections", "repeating_rejected"), "reviews with changes requested": s("rounds", "changes"),
            "errors": s("errors", "errors"), "landings": land, "landings failed": land - ok,
            "verify wall s": s("landings", "verify_wall_s"), "hours in task statuses": s("stalls", "hours"),
            "owner answers": s("owner", "answered")}


def table(title: str, items: list[dict]) -> str:
    if not items:
        return f"{title}\n  (no data)\n"
    cols = list(items[0])
    cells = [[("-" if r[c] is None else str(r[c])) for c in cols] for r in items]
    w = [max(len(c), *(len(x[i]) for x in cells)) for i, c in enumerate(cols)]
    lines = ["  " + "  ".join(c.ljust(w[i]) for i, c in enumerate(cols))]
    lines += ["  " + "  ".join(x[i].ljust(w[i]) for i in range(len(cols))) for x in cells]
    return title + "\n" + "\n".join(lines) + "\n"


def render(d: dict, since: str, until: str) -> str:
    out = [f"Platform metrics {since} .. {until} (end exclusive)\n",
           table("Claude tokens by model, exact for the period (per message; in_tokens = input + cache writes)", d["tokens"]),
           table("Agent runs started in the period by type (tokens of the whole run, wherever its messages fall)", d["by_agent"]),
           table("Tasks by agent output tokens (a run with several tasks counts fully for each)", d["by_task"]),
           table("LLM calls by provider and kind (run records; they carry no cost)", d["llm"]),
           table("LLM spend by model (ledger; run records and ledger share no id, so they are not joined)", d["spend"]),
           table("Review rounds per task", d["rounds"]),
           table("Review findings that repeat a finding rejected earlier (rejection journal, heuristic match)", d["rejections"]),
           table("Errors by executor (errors.yaml)", d["errors"]),
           table("Where tasks stand: time in a status clipped to the period, ongoing included (DONE, REJECTED, DEFERRED excluded)", d["stalls"]),
           table("Owner wait (question to answer)", d["owner"]),
           table("Landings by exit code (verify_wall_s: wall time of verify, includes waiting for CPU)", d["landings"]),
           table("Slowest verify steps", d["verify_steps"])]
    return "\n".join(out)


def render_compare(a: tuple[str, str], b: tuple[str, str], sa: dict, sb: dict) -> str:
    w = max(len(k) for k in sa)
    lines = [f"{'metric'.ljust(w)}  {a[0]}..{a[1]}  {b[0]}..{b[1]}  change"]
    for k in sa:
        x, y = sa[k], sb[k]
        pct = f"{(y - x) / x * 100:+.0f}%" if x else ("n/a" if y else "0%")
        lines.append(f"{k.ljust(w)}  {x:>21g}  {y:>21g}  {pct}")
    return "\n".join(lines) + "\n"


def parse_period(s: str) -> tuple[str, str]:
    m = re.fullmatch(r"(\d{4}-\d{2}-\d{2})\.\.(\d{4}-\d{2}-\d{2})", s)
    if not m or not real_date(m.group(1)) or not real_date(m.group(2)) or m.group(1) >= m.group(2):
        raise ValueError(f"bad period {s!r}: use YYYY-MM-DD..YYYY-MM-DD with the start before the end")
    return m.group(1), m.group(2)


def cmd_report(args) -> int:
    try:
        if args.compare:
            pa, pb = parse_period(args.compare[0]), parse_period(args.compare[1])
        else:
            until = args.until or (dt.date.today() + dt.timedelta(days=1)).isoformat()
            since = args.since or (dt.date.fromisoformat(until) - dt.timedelta(days=7)).isoformat()
            pa = (since, until)
            if not (real_date(since) and real_date(until)):
                raise ValueError("dates are YYYY-MM-DD")
    except ValueError as ex:
        print(f"metrics: {ex}", file=sys.stderr)
        return 2
    if not kb_db.available():
        print("metrics: PostgreSQL is not available", file=sys.stderr)
        return 1
    try:
        kb_db.ensure_fresh()
        err, msg = refresh()
        if err:
            print(f"metrics: {err}", file=sys.stderr)  # a report on stale data would mislead
            return 1
        for w in msg.splitlines()[1:]:
            print("metrics: " + w, file=sys.stderr)
        if args.compare:
            print(render_compare(pa, pb, summary(report_data(*pa, limit=None)), summary(report_data(*pb, limit=None))), end="")
        else:
            print(render(report_data(*pa), *pa), end="")
    except kb_db.PgError as ex:
        print(f"metrics: {ex}", file=sys.stderr)
        return 1
    return 0


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    c = sub.add_parser("collect")
    c.add_argument("--quiet", action="store_true")
    r = sub.add_parser("report")
    r.add_argument("--since")
    r.add_argument("--until")
    r.add_argument("--compare", nargs=2, metavar=("A..B", "C..D"))
    args = ap.parse_args(argv)
    return collect(args.quiet) if args.cmd == "collect" else cmd_report(args)


if __name__ == "__main__":
    sys.exit(main())
