#!/usr/bin/env python3
"""Self-tests for kb_db.py (stdlib unittest). Parsers and the query compiler need no database;
the database tests use a temporary database and are skipped when PostgreSQL is not reachable (CI).

Run: python3 tools/development/test_kb_db.py
"""
import json
import os
import subprocess
import sys
import unittest
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import kb_db  # noqa: E402

DEC = """| # | Дата | Решение | Ссылка |
|---|---|---|---|
| 1 | 2026-11-02 | Старое решение про науку | ADR-0001 |
| 2 | 2026-11-05 | Новое правило, отменяет #1 и содержит | в тексте | doc.md |
| bad | row |
"""
ORCH = "| O-001 | 2026-11-18 | Порог тика | ADR-0023 | TASK-0107 |\n| O-002 | - | text | b | a |\n"
DEBT = """| ID | Долг | Где | Почему | План | Статус |
| DEBT-0001 | A one | Lab/x | why | plan | закрыт 2026-11-14 |
| DEBT-0002 | B two | Core | why | plan | открыт |
| DEBT-0003 | C three | Core | why | plan | частично |
| DEBT-0004 | short row | Core | средний | закрыт |
"""
QUESTIONS = """| # | Вопрос | Контекст | Добавлен |
|---|---|---|---|
| Q70 | Правило бюджетов | data | 2026-11-22 |
| 1 | ~~Закрыт~~ вопрос | Кто | Когда | ADR |
"""
ERRORS = """errors:
- id: ERR-0001
  date: '2026-11-25'
  area: shots
  symptom: Old client
  repeats: ['2026-11-26', '2026-11-27']
  status: open
"""
AGENTS = "\n".join(json.dumps(r) for r in [
    {"t": 1000.0, "event": "start", "agent_type": "developer"},
    {"t": 1001.0, "event": "launched", "agent_id": "a1", "agent_type": "developer", "description": "d", "tasks": ["TASK-0001"]},
    {"t": 1500.0, "event": "stop", "agent_id": "a1", "agent_type": "developer"},
    {"t": 2000.0, "event": "launched", "agent_id": "a2", "agent_type": "critic", "description": "e"},
]) + "\nnot json\n"


class ParserTest(unittest.TestCase):
    def test_decisions_text_with_pipe_and_supersede(self):
        rows = kb_db.parse_decisions(DEC)
        self.assertEqual([r["num"] for r in rows], [1, 2])
        self.assertEqual(rows[1]["text"], "Новое правило, отменяет #1 и содержит | в тексте")
        self.assertEqual(rows[1]["ref"], "doc.md")
        self.assertEqual(rows[0]["superseded_by"], 2)
        self.assertIsNone(rows[1]["superseded_by"])

    def test_orch(self):
        rows = kb_db.parse_orch(ORCH)
        self.assertEqual(rows[0]["applied"], "TASK-0107")
        self.assertIsNone(rows[1]["date"])

    def test_debt_status_and_short_row(self):
        rows = {r["id"]: r for r in kb_db.parse_debt(DEBT)}
        self.assertEqual([rows[k]["status"] for k in sorted(rows)], ["closed", "open", "partial", "closed"])
        self.assertEqual(rows["DEBT-0001"]["area"], "Lab/x")
        self.assertEqual(rows["DEBT-0004"]["area"], "Core")

    def test_questions(self):
        rows = kb_db.parse_questions(QUESTIONS)
        self.assertEqual([(r["id"], r["open"]) for r in rows], [("Q70", True), ("N1", False)])
        self.assertEqual(rows[0]["added"], "2026-11-22")

    def test_errors_repeats(self):
        rows = kb_db.parse_errors(ERRORS)
        self.assertEqual(rows[0]["repeats"], 2)
        self.assertEqual(rows[0]["date"], "2026-11-25")

    def test_agents_join_and_foreground_skipped(self):
        runs = kb_db.parse_agents(AGENTS)
        self.assertEqual([r["id"] for r in runs], ["a1", "a2"])
        self.assertIsNotNone(runs[0]["stop"])
        self.assertIsNone(runs[1]["stop"])
        self.assertEqual(runs[0]["tasks"], ["TASK-0001"])


class QueryCompilerTest(unittest.TestCase):
    def sql(self, table, *f):
        return kb_db.compile_query(table, list(f), 10)[0]

    def test_filters(self):
        s = self.sql("debt", "status=open", "area=Lab")
        self.assertIn("lower(status) IN ('open')", s)
        self.assertIn("area ILIKE '%Lab%'", s)

    def test_fulltext_and_since(self):
        s = self.sql("decisions", "text~наука", "since=2026-11-20")
        self.assertIn("plainto_tsquery('russian'", s)
        self.assertIn("plainto_tsquery('simple'", s)
        self.assertIn("date >= '2026-11-20'", s)

    def test_silent(self):
        self.assertIn("stop IS NULL AND start < now() - interval '2 hours'", self.sql("agents", "silent>2h"))
        with self.assertRaises(kb_db.KbError):
            self.sql("items", "silent>2h")

    def test_injection_and_unknown_column(self):
        s = self.sql("items", "title=x'; DROP TABLE items; --")
        self.assertIn("x''; DROP", s)  # quoted as a literal
        with self.assertRaises(kb_db.KbError):
            self.sql("items", "nope=1")
        with self.assertRaises(kb_db.KbError):
            self.sql("nope")
        with self.assertRaises(kb_db.KbError):
            self.sql("items", "garbage")

    def test_journals_hide_soft_deleted(self):
        self.assertIn("deleted_at IS NULL", self.sql("refusals"))

    def test_list_value_and_limit(self):
        s = self.sql("items", "status=REVIEW,DONE")
        self.assertIn("IN ('review','done')", s)
        self.assertTrue(s.endswith("LIMIT 10"))


def pg_available() -> bool:
    try:
        kb_db.psql("SELECT 1;", db="postgres")
        return True
    except Exception:
        return False


@unittest.skipUnless(pg_available(), "PostgreSQL is not reachable")
class DatabaseTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.old = os.environ.get("SINGULARITY_KB_DB")
        cls.name = "kb_test_" + uuid.uuid4().hex[:8]
        os.environ["SINGULARITY_KB_DB"] = cls.name
        cls.counts = kb_db.build()

    @classmethod
    def tearDownClass(cls):
        kb_db.psql(f'DROP DATABASE IF EXISTS "{cls.name}";', db="postgres")
        if cls.old is None:
            os.environ.pop("SINGULARITY_KB_DB", None)
        else:
            os.environ["SINGULARITY_KB_DB"] = cls.old

    def test_build_fills_tables(self):
        self.assertGreater(self.counts["items"], 50)
        self.assertGreater(self.counts["decisions"], 100)
        self.assertGreater(self.counts["debt"], 20)

    def test_query_and_json(self):
        out = kb_db.run_query("items", ["id=TASK-0200"], 5, True, False)
        rows = json.loads(out)
        self.assertEqual(rows[0]["id"], "TASK-0200")

    def test_fulltext_russian_stem(self):
        out = kb_db.run_query("decisions", ["text~наука"], 5, False, False)
        self.assertTrue(out)

    def test_fresh_and_stale(self):
        kb_db.ensure_fresh()
        self.assertTrue(kb_db.is_fresh())
        kb_db.psql("UPDATE kb_meta SET value = 'x' WHERE key = 'fingerprint';")
        self.assertFalse(kb_db.is_fresh())
        kb_db.ensure_fresh()
        self.assertTrue(kb_db.is_fresh())

    def test_soft_delete_survives_rebuild(self):
        kb_db.psql("INSERT INTO hook_refusals (hook, reason) VALUES ('h1', 'r1'), ('h2', 'r2');")
        kb_db.psql("UPDATE hook_refusals SET deleted_at = now() WHERE hook = 'h2';")
        kb_db.build()
        out = kb_db.run_query("refusals", [], 10, False, False)
        self.assertIn("h1", out)
        self.assertNotIn("h2", out)
        self.assertEqual(kb_db.psql("SELECT count(*) FROM hook_refusals;").strip(), "2")

    def test_cli_status_and_bad_filter(self):
        env = dict(os.environ)
        p = subprocess.run([sys.executable, str(Path(kb_db.__file__)), "q", "debt", "nope=1"],
                           capture_output=True, text=True, env=env)
        self.assertEqual(p.returncode, 2)


if __name__ == "__main__":
    unittest.main()
