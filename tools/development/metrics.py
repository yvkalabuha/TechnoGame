#!/usr/bin/env python3
"""Platform metrics in PostgreSQL (owner #593): where the work waits and what it costs.

  metrics.py collect [--quiet]                  read the sources and append new rows (idempotent)
  metrics.py report [--since D] [--until D]     one period (default: the last 7 days)
  metrics.py report --compare A..B C..D         two periods side by side (dates YYYY-MM-DD, end exclusive)

Tables (schema of the checkout, see kb_db.db_schema; created if missing, never dropped by kb_db build):
sessions, llm_calls, landings, task_flow, owner_waits are journals: rows are only added, a row has
deleted_at for a soft delete, and a repeated collect adds nothing (keys are message, file, ledger, commit
ids). sessions is the one exception: it holds one aggregate per transcript file and the aggregate is
refreshed while the file grows. agent_runs and errors are the derived tables of kb_db.

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
import json
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
  cache_read_tokens bigint, cache_write_tokens bigint, deleted_at timestamptz);
create table if not exists llm_calls (key text primary key, kind text, provider text, model text, task text,
  t timestamptz, input_tokens bigint, output_tokens bigint, usd numeric, verdict text, findings integer,
  round integer, deleted_at timestamptz);
create table if not exists landings (key text primary key, t timestamptz, branch text, task text, attempt integer,
  code integer, total_s numeric, verify_s numeric, steps text, deleted_at timestamptz);
create table if not exists task_flow (key text primary key, task text, t timestamptz, from_status text,
  to_status text, commit_id text, deleted_at timestamptz);
create table if not exists owner_waits (key text primary key, question text, asked date, answered date,
  deleted_at timestamptz);
"""
COLS = {
    "sessions": ["key", "kind", "session", "agent_id", "model", "start_t", "end_t", "msgs", "input_tokens",
                 "output_tokens", "cache_read_tokens", "cache_write_tokens"],
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
        return dt.datetime.fromtimestamp(t, dt.timezone.utc).isoformat()
    if isinstance(t, str):
        try:
            return dt.datetime.fromisoformat(t.replace("Z", "+00:00")).astimezone(dt.timezone.utc).isoformat()
        except ValueError:
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
    """Aggregate of one Claude Code transcript. One API message appears in several lines (one per content
    block) with the same id and usage: it is counted once. Bad lines and unknown fields are skipped."""
    msgs: dict[str, dict] = {}
    first = last = None
    for line in lines:
        try:
            r = json.loads(line)
        except ValueError:
            continue
        if not isinstance(r, dict):
            continue
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
                cache_write_tokens=sum(m["cw"] for m in msgs.values()))


def parse_run_record(name: str, rec, mtime: float) -> dict | None:
    """One llm_worker run file (review, gate, research, sketch, visual) as an llm_calls row."""
    if not isinstance(rec, dict):
        return None
    usage = rec.get("usage") if isinstance(rec.get("usage"), dict) else {}
    res = rec.get("result") if isinstance(rec.get("result"), dict) else {}
    findings = res.get("findings", res.get("FINDINGS"))
    verdict = res.get("verdict", res.get("STATUS", rec.get("verdict", rec.get("status"))))
    task = rec.get("task") or rec.get("item") or ""
    when = (iso(rec.get("created_at")) or iso(rec.get("started_at")) or iso(rec.get("created"))
            or iso(rec.get("date")) or iso(mtime))
    return dict(key="run:" + name, kind=str(rec.get("kind") or name.split("-")[0]), provider=str(rec.get("provider") or ""),
                model=str(rec.get("model") or ""), task=str(task)[:120], t=when,
                input_tokens=num(usage.get("input_tokens", usage.get("prompt_tokens"))),
                output_tokens=num(usage.get("output_tokens", usage.get("completion_tokens"))),
                usd=None, verdict=str(verdict)[:40] if verdict else None,
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


def parse_owner_waits(text: str) -> list[dict]:
    """Question rows with an asked date and a 'closed <date>' (Закрыт/closed/answered) date, by dates only."""
    rows = []
    for cells in kb_db.table_rows(text):
        if len(cells) < 3 or not cells[0] or cells[0] in ("#", "ID"):
            continue
        qid = cells[0].strip("~ ")
        qid = f"Q{qid}" if qid.isdigit() else qid
        row = " | ".join(cells)
        closed = re.search(r"(?:закрыт\w*|closed|answered|ответ\w*)\W{0,12}(\d{4}-\d{2}-\d{2})", row, re.I)
        dates = DATE_RE.findall(" | ".join(cells[2:]))
        asked = next((d for d in dates if not closed or d != closed.group(1)), None)
        if asked and closed and real_date(asked) and real_date(closed.group(1)):
            rows.append(dict(key=qid, question=qid, asked=asked, answered=closed.group(1)))
    seen: set[str] = set()
    return [r for r in rows if not (r["key"] in seen or seen.add(r["key"]))]


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


def collect_sessions(base: Path) -> list[dict]:
    rows = []
    if not base.is_dir():
        return rows
    for p in sorted(base.glob("*.jsonl")):
        r = parse_transcript(read_lines(p), "session:" + p.stem, "session", p.stem)
        if r:
            rows.append(r)
    for p in sorted(base.glob("*/subagents/**/*.jsonl")):
        aid = p.stem.removeprefix("agent-")
        sess = p.relative_to(base).parts[0]
        r = parse_transcript(read_lines(p), "agent:" + aid, "agent", sess, aid)
        if r:
            rows.append(r)
    return rows


def runs_dir() -> Path:
    # runs/ is untracked: a worktree has none, so read the main checkout's
    return Path(os.environ.get("SINGULARITY_LLM_RUNS") or (kb_db.git_common_parent() or ROOT) / "tools" / "llm_worker" / "runs")


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


def collect_all() -> dict[str, list[dict]]:
    land = kb_db.state_dir() / "landings.jsonl"
    oq = ROOT / "development" / "OPEN_QUESTIONS.md"
    return dict(sessions=collect_sessions(claude_dir()), llm_calls=collect_llm_calls(),
                landings=parse_landings(read_lines(land)), task_flow=git_task_flow(),
                owner_waits=parse_owner_waits(kb_db.read_text(oq)))


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
        if table == "sessions":  # aggregate of a growing file: refresh it unless soft-deleted
            upd = ", ".join(f"{c} = excluded.{c}" for c in cols[1:])
            tail = f"on conflict (key) do update set {upd} where {table}.deleted_at is null"
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


def collect(quiet: bool = False) -> int:
    if not kb_db.available():
        if not quiet:
            print("metrics: PostgreSQL is not available", file=sys.stderr)
        return 0 if quiet else 1
    try:
        kb_db.ensure_db()
        data = collect_all()
        kb_db.psql(kb_db.db_name(), load_script(data))
    except Exception as ex:  # metrics must never break the caller
        if not quiet:
            print(f"metrics: collect failed: {ex}", file=sys.stderr)
        return 0 if quiet else 1
    if not quiet:
        print("metrics: read " + ", ".join(f"{k}={len(v)}" for k, v in data.items()))
    return 0


def rows(sql: str) -> list[dict]:
    out = kb_db.psql(kb_db.db_name(), f"select row_to_json(q_) from ({sql}) q_", "-At", schema=True)
    return [json.loads(l) for l in out.splitlines() if l.strip()]


# --------------------------------------------------------------------------
# Report
# --------------------------------------------------------------------------


def period_sql(col: str, since: str, until: str) -> str:
    return f"{col} >= {kb_db.cast('date', since)} and {col} < {kb_db.cast('date', until)}"


def report_data(since: str, until: str) -> dict:
    p = lambda col: period_sql(col, since, until)
    return dict(
        by_agent=rows(f"""select a.type, count(*) runs, round(avg(extract(epoch from a.stop_t - a.start_t)) / 60) avg_min,
            coalesce(sum(s.input_tokens + s.cache_write_tokens), 0) in_tokens, coalesce(sum(s.output_tokens), 0) out_tokens
            from agent_runs a left join sessions s on s.kind = 'agent' and s.agent_id = a.agent_id and s.deleted_at is null
            where {p('a.start_t')} group by a.type order by out_tokens desc, a.type"""),
        by_task=rows(f"""select t.task, count(*) runs, round(sum(extract(epoch from a.stop_t - a.start_t)) / 60) minutes,
            coalesce(sum(s.output_tokens), 0) out_tokens
            from agent_runs a cross join lateral unnest(string_to_array(nullif(a.tasks, ''), ',')) t(task)
            left join sessions s on s.kind = 'agent' and s.agent_id = a.agent_id and s.deleted_at is null
            where {p('a.start_t')} group by t.task order by out_tokens desc, t.task limit 15"""),
        llm=rows(f"""select provider, kind, count(*) calls, coalesce(sum(input_tokens), 0) in_tokens,
            coalesce(sum(output_tokens), 0) out_tokens, coalesce(sum(usd), 0) usd
            from llm_calls where deleted_at is null and {p('t')} group by provider, kind order by calls desc, provider, kind"""),
        rounds=rows(f"""select task, count(*) reviews, coalesce(max(round), 0) max_round,
            count(*) filter (where verdict like 'changes%') changes
            from llm_calls where deleted_at is null and kind = 'review' and {p('t')} group by task
            order by reviews desc, task limit 15"""),
        errors=rows(f"""select coalesce(executor, '-') executor, count(*) errors, coalesce(sum(repeats), 0) repeats
            from errors where {p('date')} group by executor order by errors desc, executor"""),
        stalls=rows(f"""with x as (select task, to_status, t, lead(t) over (partition by task order by t, key) nxt from task_flow
            where deleted_at is null) select to_status status, count(*) entries,
            round(sum(extract(epoch from nxt - t)) / 3600) hours, round(max(extract(epoch from nxt - t)) / 3600) max_hours
            from x where nxt is not null and {p('t')} group by to_status order by hours desc nulls last, status"""),
        owner=rows(f"""select count(*) answered, round(avg(answered - asked), 1) avg_days, max(answered - asked) max_days
            from owner_waits where deleted_at is null and {p('answered')}"""),
        landings=rows(f"""select code, count(*) landings, round(avg(verify_s)) avg_verify_s, round(sum(verify_s)) cpu_s
            from landings where deleted_at is null and {p('t')} group by code order by code"""))


def summary(d: dict) -> dict[str, float]:
    s = lambda k, c: sum(float(r.get(c) or 0) for r in d[k])
    land = s("landings", "landings")
    ok = sum(float(r["landings"]) for r in d["landings"] if r["code"] == 0)
    reviews = s("rounds", "reviews")
    return {"agent runs": s("by_agent", "runs"), "agent output tokens": s("by_agent", "out_tokens"),
            "agent input tokens": s("by_agent", "in_tokens"), "llm calls": s("llm", "calls"),
            "llm usd": round(s("llm", "usd"), 4), "review records": reviews,
            "reviews with changes requested": s("rounds", "changes"), "errors": s("errors", "errors"),
            "landings": land, "landings failed": land - ok, "verify cpu s": s("landings", "cpu_s"),
            "hours in task statuses": s("stalls", "hours"), "owner answers": s("owner", "answered")}


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
           table("Agents by type (tokens from subagent transcripts)", d["by_agent"]),
           table("Tasks by agent output tokens", d["by_task"]),
           table("LLM calls by provider and kind (usd from the ledger)", d["llm"]),
           table("Review rounds per task", d["rounds"]),
           table("Errors by executor (errors.yaml)", d["errors"]),
           table("Where tasks stand: time spent in a status, from status changes", d["stalls"]),
           table("Owner wait (question to answer)", d["owner"]),
           table("Landings by exit code (verify time is the CPU wait)", d["landings"])]
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
        if collect(quiet=True) != 0:
            return 1
        if args.compare:
            print(render_compare(pa, pb, summary(report_data(*pa)), summary(report_data(*pb))), end="")
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
