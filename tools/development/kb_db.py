#!/usr/bin/env python3
"""Platform knowledge index in PostgreSQL (owner #590). Files in git stay the source of truth;
the database is a derived index that can be dropped and rebuilt at any time.

  kb_db.py build                      rebuild the index from files (full rebuild, a few seconds)
  kb_db.py q TABLE [FILTER...]        short filter language, one line per record (rebuilds when files are newer)
  kb_db.py sql "SELECT ..."           raw SQL through psql
  kb_db.py status                     connection, row counts, freshness

Tables for q: items, links, decisions, orch, debt, questions, errors, agents, refusals, landfails.
Filters: key=v  key!=v  key>=v  key<=v  key>v  key<v  key~text (full text)  since=DATE  until=DATE
         agents silent>2h (started, never stopped, older than the duration: m, h, d)
Options: --json  --limit N (default 50)  --full (no text truncation)
Examples:
  kb_db.py q debt status=open area=Lab
  kb_db.py q decisions text~наука since=2026-11-20
  kb_db.py q items status=REVIEW agent=developer
  kb_db.py q agents silent>2h

Connection: PostgreSQL 16 on the local socket, peer authentication of the current user, database
singularity_kb (created on first build). No passwords. Env: SINGULARITY_KB_DB, SINGULARITY_KB_SOCKET.
The service journals (hook_refusals, land_failures) are not rebuilt: they keep rows and soft deletes
(deleted_at); q hides deleted rows. The driver is the psql command (no new dependencies).
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))

DECISIONS = ROOT / "docs" / "decisions" / "DECISIONS_LOG.md"
ORCH = ROOT / "docs" / "decisions" / "ORCH_DECISIONS.md"
DEBT = ROOT / "development" / "TECH_DEBT.md"
QUESTIONS = ROOT / "development" / "OPEN_QUESTIONS.md"
ERRORS = ROOT / "development" / "errors.yaml"
AGENTS = ROOT / ".claude" / ".state" / "agents.jsonl"

DB_NAME = "singularity_kb"
SOCKET = "/var/run/postgresql"
DATE_RE = re.compile(r"^\d{4}-\d{2}-\d{2}$")


class KbError(Exception):
    pass


# --------------------------------------------------------------------------
# Parsers (no database)
# --------------------------------------------------------------------------

def table_cells(line: str) -> list[str]:
    """Cells of a Markdown table row; ' | ' inside a cell is kept by the caller via merging."""
    s = line.strip()
    if s.startswith("|"):
        s = s[1:]
    if s.endswith("|"):
        s = s[:-1]
    return [c.strip() for c in re.split(r"\s\|\s", " " + s + " ")] if s else []


def _merge(cells: list[str], head: int, tail: int) -> tuple[list[str], str, list[str]]:
    """Split cells into head columns, a free text (joined) and tail columns."""
    if len(cells) < head + tail + 1:
        cells = cells + [""] * (head + tail + 1 - len(cells))
    return cells[:head], " | ".join(cells[head:len(cells) - tail]), cells[len(cells) - tail:]


SUPERSEDE_RE = re.compile(r"(?:отменя\w*|отмен[аы]\w*|заменя\w*|вместо|supersed\w*|пересматрива\w*)\s+#(\d+)", re.I)


def parse_decisions(text: str) -> list[dict]:
    rows: list[dict] = []
    for line in text.splitlines():
        m = re.match(r"^\| (\d+) \| (\d{4}-\d{2}-\d{2}) \|", line)
        if not m:
            continue
        head, body, tail = _merge(table_cells(line), 2, 1)
        rows.append({"num": int(head[0]), "date": head[1], "text": body, "ref": tail[0],
                     "source": "orchestrator" if body.startswith("(Orchestrator") else "owner",
                     "superseded_by": None})
    by_num = {r["num"]: r for r in rows}
    for r in rows:
        for m in SUPERSEDE_RE.finditer(r["text"]):
            target = by_num.get(int(m.group(1)))
            if target is not None and target["num"] != r["num"] and target["superseded_by"] is None:
                target["superseded_by"] = r["num"]
    return rows


def parse_orch(text: str) -> list[dict]:
    rows = []
    for line in text.splitlines():
        if re.match(r"^\| O-\d{3} \|", line):
            head, body, tail = _merge(table_cells(line), 2, 2)
            rows.append({"id": head[0], "date": head[1] if DATE_RE.match(head[1]) else None,
                         "text": body, "basis": tail[0], "applied": tail[1]})
    return rows


def debt_status(cell: str) -> str:
    c = cell.strip().lower()
    if c.startswith("закрыт"):
        return "closed"
    if c.startswith("частично"):
        return "partial"
    return "open"


def parse_debt(text: str) -> list[dict]:
    rows = []
    for line in text.splitlines():
        if re.match(r"^\| DEBT-\d{4} \|", line):
            cells = table_cells(line)
            if len(cells) >= 6:
                head, body, tail = _merge(cells, 1, 4)
                area, status_cell = tail[0], tail[3]
            else:  # a short row: id, text, where, ..., status
                head, body, tail = _merge(cells, 1, 2)
                area, status_cell = (cells[2] if len(cells) > 2 else ""), tail[1]
            rows.append({"id": head[0], "status": debt_status(status_cell), "area": area,
                         "text": body, "status_text": status_cell})
    return rows


def parse_questions(text: str) -> list[dict]:
    """Rows of the question tables: id, text, blocks, added; ~~struck~~ rows are closed."""
    rows = []
    for line in text.splitlines():
        m = re.match(r"^\| (Q?\d+) \| (.*)$", line)
        if not m:
            continue
        cells = table_cells(line)
        if len(cells) < 2:
            continue
        qid = cells[0] if cells[0].startswith("Q") else "N" + cells[0]
        blocks = cells[2] if len(cells) > 2 else ""
        added = ""
        for c in reversed(cells[2:]):
            if DATE_RE.match(c):
                added = c
                break
        rows.append({"id": qid, "text": cells[1], "blocks": blocks,
                     "added": added if DATE_RE.match(added) else None,
                     "open": not cells[1].startswith("~~")})
    return rows


def parse_errors(text: str) -> list[dict]:
    data = yaml.safe_load(text) or {}
    rows = []
    for e in data.get("errors", []) or []:
        rep = [str(x) for x in (e.get("repeats") or [])]
        rows.append({"id": str(e.get("id", "")), "date": str(e.get("date") or "") or None,
                     "executor": e.get("executor"), "kind": e.get("kind"), "area": e.get("area"),
                     "symptom": e.get("symptom"), "cause": e.get("cause"), "found_by": e.get("found_by"),
                     "cost": e.get("cost"), "fix": e.get("fix"), "repeats": len(rep),
                     "status": e.get("status"), "key": e.get("key")})
    return rows


def parse_agents(text: str) -> list[dict]:
    """Background agents from the hook log: launched/stop records joined by agent_id."""
    runs: dict[str, dict] = {}
    order: list[str] = []
    for line in text.splitlines():
        try:
            r = json.loads(line)
        except ValueError:
            continue
        aid = r.get("agent_id") or ""
        if not aid or r.get("event") not in ("launched", "stop"):
            continue
        if aid not in runs:
            runs[aid] = {"id": aid, "type": None, "description": None, "tasks": [], "start": None, "stop": None}
            order.append(aid)
        run = runs[aid]
        t = dt.datetime.fromtimestamp(float(r.get("t", 0)), dt.timezone.utc).isoformat()
        if r["event"] == "launched":
            run["start"] = t
            run["type"] = r.get("agent_type") or run["type"]
            run["description"] = r.get("description")
            run["tasks"] = r.get("tasks") or []
        else:
            run["stop"] = t
            run["type"] = run["type"] or r.get("agent_type") or None
    return [runs[a] for a in order]


# --------------------------------------------------------------------------
# Schema and SQL generation
# --------------------------------------------------------------------------

def tsv(col: str) -> str:
    return (f"tsv_ru tsvector GENERATED ALWAYS AS (to_tsvector('russian'::regconfig, coalesce({col},''))) STORED, "
            f"tsv_simple tsvector GENERATED ALWAYS AS (to_tsvector('simple'::regconfig, coalesce({col},''))) STORED")


DERIVED_DDL = f"""
DROP TABLE IF EXISTS item_links, items, decisions, orch_decisions, debt, questions, errors, agent_runs CASCADE;
CREATE TABLE items (id text PRIMARY KEY, type text, status text, level text, milestone text, agent text,
  category text, priority text, needs_owner boolean, approved_by text, approved_at date, approval_ref text,
  path text, title text, updated date, {tsv('title')});
CREATE TABLE item_links (src text, dst text, kind text);
CREATE TABLE decisions (num int PRIMARY KEY, date date, text text, source text, ref text, superseded_by int,
  {tsv('text')});
CREATE TABLE orch_decisions (id text PRIMARY KEY, date date, text text, basis text, applied text, {tsv('text')});
CREATE TABLE debt (id text PRIMARY KEY, status text, area text, text text, status_text text, {tsv('text')});
CREATE TABLE questions (id text, text text, blocks text, added date, open boolean, {tsv('text')});
CREATE TABLE errors (id text PRIMARY KEY, date date, executor text, kind text, area text, symptom text,
  cause text, found_by text, cost text, fix text, repeats int, status text, key text, {tsv("symptom || ' ' || coalesce(cause,'')")});
CREATE TABLE agent_runs (id text PRIMARY KEY, type text, description text, tasks text, start timestamptz, stop timestamptz);
CREATE INDEX ON items (status); CREATE INDEX ON items (agent); CREATE INDEX ON items (type); CREATE INDEX ON items (priority);
CREATE INDEX ON items USING gin (tsv_ru); CREATE INDEX ON items USING gin (tsv_simple);
CREATE INDEX ON item_links (src); CREATE INDEX ON item_links (dst);
CREATE INDEX ON decisions (date); CREATE INDEX ON decisions USING gin (tsv_ru); CREATE INDEX ON decisions USING gin (tsv_simple);
CREATE INDEX ON orch_decisions (date); CREATE INDEX ON orch_decisions USING gin (tsv_ru);
CREATE INDEX ON debt (status); CREATE INDEX ON debt (area); CREATE INDEX ON debt USING gin (tsv_ru); CREATE INDEX ON debt USING gin (tsv_simple);
CREATE INDEX ON questions (open); CREATE INDEX ON errors (status); CREATE INDEX ON errors (area); CREATE INDEX ON errors (date);
CREATE INDEX ON agent_runs (type); CREATE INDEX ON agent_runs (start);
"""

# Journals are kept between rebuilds: they are not in git, deleted_at is a soft delete.
JOURNAL_DDL = """
CREATE TABLE IF NOT EXISTS kb_meta (key text PRIMARY KEY, value text);
CREATE TABLE IF NOT EXISTS hook_refusals (id bigserial PRIMARY KEY, at timestamptz DEFAULT now(), hook text,
  agent text, target text, reason text, deleted_at timestamptz);
CREATE TABLE IF NOT EXISTS land_failures (id bigserial PRIMARY KEY, at timestamptz DEFAULT now(), branch text,
  step text, reason text, deleted_at timestamptz);
CREATE INDEX IF NOT EXISTS hook_refusals_at ON hook_refusals (at);
CREATE INDEX IF NOT EXISTS land_failures_at ON land_failures (at);
"""


def lit(v) -> str:
    if v is None:
        return "NULL"
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, (int, float)):
        return str(v)
    return "'" + str(v).replace("\x00", "").replace("'", "''") + "'"


def insert(table: str, cols: list[str], rows: list[list]) -> str:
    out = []
    for i in range(0, len(rows), 200):
        vals = ",\n".join("(" + ",".join(lit(v) for v in r) + ")" for r in rows[i:i + 200])
        out.append(f"INSERT INTO {table} ({','.join(cols)}) VALUES\n{vals};")
    return "\n".join(out)


def as_date(v) -> str | None:
    s = str(v) if v is not None else ""
    return s[:10] if DATE_RE.match(s[:10]) else None


def collect_items() -> tuple[list[list], list[list]]:
    import registry
    rows, links = [], []
    for it in registry.load_items([]):
        m = it.meta
        path = it.path.resolve().relative_to(ROOT).as_posix()
        try:
            upd = dt.datetime.fromtimestamp(it.path.stat().st_mtime).date().isoformat()
        except OSError:
            upd = None
        rows.append([it.id, it.type.prefix, m.get("status"), m.get("level"), m.get("milestone"), m.get("agent"),
                     m.get("category"), m.get("priority"), m.get("needs_owner"), m.get("approved_by"),
                     as_date(m.get("approved_at")), m.get("approval_ref"), path, m.get("title"),
                     as_date(m.get("updated")) or upd])
        for kind in ("depends_on", "related"):
            for dst in registry.as_list(m.get(kind)):
                links.append([it.id, dst, kind])
    return rows, links


ITEM_COLS = ["id", "type", "status", "level", "milestone", "agent", "category", "priority", "needs_owner",
             "approved_by", "approved_at", "approval_ref", "path", "title", "updated"]


def build_script() -> str:
    def read(p: Path) -> str:
        return p.read_text(encoding="utf-8") if p.exists() else ""
    items, links = collect_items()
    dec = parse_decisions(read(DECISIONS))
    orch = parse_orch(read(ORCH))
    debt = parse_debt(read(DEBT))
    qs = parse_questions(read(QUESTIONS))
    errs = parse_errors(read(ERRORS)) if ERRORS.exists() else []
    runs = parse_agents(read(AGENTS))
    parts = ["BEGIN;", JOURNAL_DDL, DERIVED_DDL,
             insert("items", ITEM_COLS, items), insert("item_links", ["src", "dst", "kind"], links),
             insert("decisions", ["num", "date", "text", "source", "ref", "superseded_by"],
                    [[r["num"], r["date"], r["text"], r["source"], r["ref"], r["superseded_by"]] for r in dec]),
             insert("orch_decisions", ["id", "date", "text", "basis", "applied"],
                    [[r["id"], r["date"], r["text"], r["basis"], r["applied"]] for r in orch]),
             insert("debt", ["id", "status", "area", "text", "status_text"],
                    [[r["id"], r["status"], r["area"], r["text"], r["status_text"]] for r in debt]),
             insert("questions", ["id", "text", "blocks", "added", "open"],
                    [[r["id"], r["text"], r["blocks"], r["added"], r["open"]] for r in qs]),
             insert("errors", ["id", "date", "executor", "kind", "area", "symptom", "cause", "found_by", "cost",
                               "fix", "repeats", "status", "key"],
                    [[r[c] for c in ["id", "date", "executor", "kind", "area", "symptom", "cause", "found_by",
                                     "cost", "fix", "repeats", "status", "key"]] for r in errs]),
             insert("agent_runs", ["id", "type", "description", "tasks", "start", "stop"],
                    [[r["id"], r["type"], r["description"], ",".join(r["tasks"]), r["start"], r["stop"]] for r in runs]),
             f"INSERT INTO kb_meta VALUES ('fingerprint', {lit(fingerprint())}) "
             "ON CONFLICT (key) DO UPDATE SET value = excluded.value;",
             "COMMIT;"]
    return "\n".join(p for p in parts if p)


def source_files() -> list[Path]:
    import registry
    files = [it.path for it in registry.load_items([])]
    return sorted(files) + [DECISIONS, ORCH, DEBT, QUESTIONS, ERRORS, AGENTS]


def fingerprint() -> str:
    h = hashlib.sha256()
    for p in source_files():
        try:
            st = p.stat()
            h.update(f"{p.name}:{st.st_mtime_ns}:{st.st_size};".encode())
        except OSError:
            h.update(f"{p.name}:-;".encode())
    return h.hexdigest()


# --------------------------------------------------------------------------
# Query language
# --------------------------------------------------------------------------

# kind: eq (exact, case-insensitive), sub (substring), num, date, ts (timestamp), bool, fts (full text)
TABLES: dict[str, dict] = {
    "items": {"sql": "items", "cols": {"id": "eq", "type": "eq", "status": "eq", "level": "eq", "milestone": "eq",
              "agent": "eq", "category": "eq", "priority": "eq", "needs_owner": "bool", "approved_by": "eq",
              "approved_at": "date", "path": "sub", "title": "sub", "updated": "date"},
              "show": ["id", "status", "agent", "priority", "title"], "date": "updated", "fts": "title"},
    "links": {"sql": "item_links", "cols": {"src": "eq", "dst": "eq", "kind": "eq"}, "show": ["src", "kind", "dst"]},
    "decisions": {"sql": "decisions", "cols": {"num": "num", "date": "date", "text": "sub", "source": "eq",
                  "ref": "sub", "superseded_by": "num"}, "show": ["num", "date", "text"], "date": "date", "fts": "text"},
    "orch": {"sql": "orch_decisions", "cols": {"id": "eq", "date": "date", "text": "sub", "basis": "sub", "applied": "sub"},
             "show": ["id", "date", "text"], "date": "date", "fts": "text"},
    "debt": {"sql": "debt", "cols": {"id": "eq", "status": "eq", "area": "sub", "text": "sub"},
             "show": ["id", "status", "area", "text"], "fts": "text"},
    "questions": {"sql": "questions", "cols": {"id": "eq", "text": "sub", "blocks": "sub", "added": "date", "open": "bool"},
                  "show": ["id", "added", "text"], "date": "added", "fts": "text"},
    "errors": {"sql": "errors", "cols": {"id": "eq", "date": "date", "executor": "eq", "kind": "eq", "area": "eq",
               "found_by": "eq", "cost": "eq", "status": "eq", "repeats": "num", "key": "sub", "symptom": "sub"},
               "show": ["id", "date", "area", "status", "symptom"], "date": "date", "fts": "symptom"},
    "agents": {"sql": "agent_runs", "cols": {"id": "eq", "type": "eq", "description": "sub", "tasks": "sub",
               "start": "ts", "stop": "ts"}, "show": ["id", "type", "start", "stop", "description", "tasks"],
               "date": "start", "fts": "description"},
    "refusals": {"sql": "hook_refusals", "cols": {"id": "num", "at": "ts", "hook": "eq", "agent": "eq",
                 "target": "sub", "reason": "sub"}, "show": ["id", "at", "hook", "agent", "reason"],
                 "date": "at", "live": True},
    "landfails": {"sql": "land_failures", "cols": {"id": "num", "at": "ts", "branch": "eq", "step": "eq",
                  "reason": "sub"}, "show": ["id", "at", "branch", "step", "reason"], "date": "at", "live": True},
}
OPS = {"=": "=", "!=": "<>", ">=": ">=", "<=": "<=", ">": ">", "<": "<"}
FILTER_RE = re.compile(r"^([A-Za-z_]+)(!=|>=|<=|=|~|>|<)(.*)$")
DURATION_RE = re.compile(r"^(\d+)([mhd])$")


def like(v: str) -> str:
    return "%" + v.replace("\\", "\\\\").replace("%", "\\%").replace("_", "\\_") + "%"


def compile_filter(table: str, token: str) -> str:
    spec = TABLES[table]
    m = FILTER_RE.match(token)
    if not m:
        raise KbError(f"bad filter '{token}' (use key=value, key>=value, key~text)")
    key, op, val = m.group(1), m.group(2), m.group(3)
    if key == "silent":
        d = DURATION_RE.match(val)
        if table != "agents" or op != ">" or not d:
            raise KbError("silent>2h works for 'agents' only (units m, h, d)")
        unit = {"m": "minutes", "h": "hours", "d": "days"}[d.group(2)]
        return f"(stop IS NULL AND start < now() - interval '{int(d.group(1))} {unit}')"
    if key in ("since", "until"):
        col = spec.get("date")
        if not col or op != "=" or not DATE_RE.match(val):
            raise KbError(f"{key}=YYYY-MM-DD is not available for '{table}'")
        return f"{col} {'>=' if key == 'since' else '<'} {lit(val)}" + ("::date + 1" if key == "until" else "")
    if key == "text" and op == "~" or op == "~":
        fts = spec.get("fts")
        if key not in spec["cols"]:
            raise KbError(f"unknown column '{key}' for '{table}'")
        if not fts or key != fts:
            return f"{key} ILIKE {lit(like(val))}"
        return (f"(tsv_ru @@ plainto_tsquery('russian'::regconfig, {lit(val)}) "
                f"OR tsv_simple @@ plainto_tsquery('simple'::regconfig, {lit(val)}))")
    kind = spec["cols"].get(key)
    if kind is None:
        raise KbError(f"unknown column '{key}' for '{table}' (known: {', '.join(spec['cols'])})")
    if kind == "bool":
        if val.lower() not in ("true", "false", "1", "0", "yes", "no") or op not in ("=", "!="):
            raise KbError(f"'{key}' takes true/false")
        v = "true" if val.lower() in ("true", "1", "yes") else "false"
        return f"{key} {OPS[op]} {v}"
    if kind == "num":
        if not re.match(r"^-?\d+$", val):
            raise KbError(f"'{key}' takes a number")
        return f"{key} {OPS[op]} {int(val)}"
    if kind in ("date", "ts"):
        if not re.match(r"^\d{4}-\d{2}-\d{2}([ T]\d{2}:\d{2}(:\d{2})?)?$", val):
            raise KbError(f"'{key}' takes a date YYYY-MM-DD")
        return f"{key} {OPS[op]} {lit(val)}"
    if kind == "sub" and op in ("=", "!="):
        return f"{key} {'NOT ' if op == '!=' else ''}ILIKE {lit(like(val))}"
    vals = [x for x in val.split(",") if x != ""]
    if op in ("=", "!=") and vals:
        inner = ",".join(lit(x.lower()) for x in vals)
        return f"lower({key}) {'NOT ' if op == '!=' else ''}IN ({inner})"
    return f"{key} {OPS[op]} {lit(val)}"


def compile_query(table: str, filters: list[str], limit: int) -> tuple[str, list[str]]:
    if table not in TABLES:
        raise KbError(f"unknown table '{table}' (known: {', '.join(TABLES)})")
    spec = TABLES[table]
    where = [compile_filter(table, f) for f in filters]
    if spec.get("live"):
        where.append("deleted_at IS NULL")
    cols = spec["show"]
    order = spec["show"][0] if table not in ("agents", "refusals", "landfails") else spec["date"]
    desc = " DESC" if table in ("agents", "refusals", "landfails") else ""
    sql = (f"SELECT {', '.join(cols)} FROM {spec['sql']}"
           + (" WHERE " + " AND ".join(where) if where else "")
           + f" ORDER BY {order}{desc} LIMIT {int(limit)}")
    return sql, cols


# --------------------------------------------------------------------------
# psql driver
# --------------------------------------------------------------------------

def db_name() -> str:
    return os.environ.get("SINGULARITY_KB_DB", DB_NAME)


def psql(sql: str, db: str | None = None, *, tuples: bool = True, timeout: int = 60, extra: list[str] | None = None) -> str:
    exe = shutil.which("psql")
    if not exe:
        raise KbError("psql not found")
    cmd = [exe, "-X", "-q", "-h", os.environ.get("SINGULARITY_KB_SOCKET", SOCKET), "-d", db or db_name(),
           "-v", "ON_ERROR_STOP=1", "-f", "-"] + (["-A", "-t", "-F", "\x1f"] if tuples else []) + (extra or [])
    env = dict(os.environ, PGCONNECT_TIMEOUT="3", PGOPTIONS="-c client_min_messages=warning")
    try:
        p = subprocess.run(cmd, input=sql, capture_output=True, text=True, timeout=timeout, env=env)
    except subprocess.TimeoutExpired:
        raise KbError("psql timed out")
    if p.returncode != 0:
        raise KbError((p.stderr or p.stdout).strip().splitlines()[-1] if (p.stderr or p.stdout).strip() else "psql failed")
    return p.stdout


def ensure_db() -> None:
    name = db_name()
    if not re.match(r"^[A-Za-z_][A-Za-z0-9_]*$", name):
        raise KbError("bad database name")
    have = psql(f"SELECT 1 FROM pg_database WHERE datname = {lit(name)};", db="postgres").strip()
    if not have:
        psql(f'CREATE DATABASE "{name}";', db="postgres")


def build() -> dict:
    ensure_db()
    script = build_script()
    psql(script, tuples=False)
    return counts()


def counts() -> dict:
    tabs = ["items", "item_links", "decisions", "orch_decisions", "debt", "questions", "errors", "agent_runs"]
    sql = " UNION ALL ".join(f"SELECT '{t}', count(*) FROM {t}" for t in tabs)
    return {k: int(v) for k, v in (r.split("\x1f") for r in psql(sql).strip().splitlines())}


def is_fresh() -> bool:
    try:
        got = psql("SELECT value FROM kb_meta WHERE key = 'fingerprint';").strip()
    except KbError:
        return False
    return got == fingerprint()


def ensure_fresh() -> None:
    try:
        ok = is_fresh()
    except KbError:
        ok = False
    if not ok:
        build()


def run_query(table: str, filters: list[str], limit: int, as_json: bool, full: bool) -> str:
    sql, cols = compile_query(table, filters, limit)
    ensure_fresh()
    rows = [r.split("\x1f") for r in psql(sql).splitlines() if r != ""]
    if as_json:
        return json.dumps([dict(zip(cols, r)) for r in rows], ensure_ascii=False)
    out = []
    for r in rows:
        cells = [c if full or len(c) <= 160 else c[:157] + "..." for c in (x.replace("\n", " ") for x in r)]
        out.append(" | ".join(cells))
    return "\n".join(out)


def cmd_status() -> int:
    try:
        c = counts()
    except KbError as e:
        print(f"not available: {e}")
        return 2
    print("database:", db_name(), "| fresh:", "yes" if is_fresh() else "no (q rebuilds it)")
    print(", ".join(f"{k}={v}" for k, v in c.items()))
    return 0


def build_quiet() -> None:
    """For registry.py index: never fails, never prints."""
    try:
        build()
    except Exception:
        pass


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0], formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("build")
    sub.add_parser("status")
    q = sub.add_parser("q")
    q.add_argument("table")
    q.add_argument("filters", nargs="*")
    q.add_argument("--json", action="store_true")
    q.add_argument("--limit", type=int, default=50)
    q.add_argument("--full", action="store_true")
    s = sub.add_parser("sql")
    s.add_argument("query")
    args = ap.parse_args(argv)
    try:
        if args.cmd == "build":
            t = dt.datetime.now()
            c = build()
            print(f"built in {(dt.datetime.now() - t).total_seconds():.1f}s:", ", ".join(f"{k}={v}" for k, v in c.items()))
        elif args.cmd == "status":
            return cmd_status()
        elif args.cmd == "q":
            print(run_query(args.table, args.filters, args.limit, args.json, args.full))
        else:
            ensure_fresh()
            sys.stdout.write(psql(args.query, tuples=False, extra=["-A"]))
    except KbError as e:
        print(f"kb_db: {e}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
