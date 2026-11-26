#!/usr/bin/env python3
"""Tests for kb_db.py. Parsers run without a database; database tests use a
temporary database and are skipped when PostgreSQL is not available (CI)."""
import contextlib
import io
import json
import os
import re
import sys
import tempfile
import unittest
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import kb_db  # noqa: E402


class ParserTests(unittest.TestCase):
    def test_question_closed_only_by_strike_or_status_cell(self):
        text = ("## Прочие открытые вопросы\n| # | В | Кто | Когда | Ссылка |\n|---|---|---|---|---|\n"
                "| 1 | ~~Закрыт~~ (TASK-0009 DONE). Сжимает ли JIT | a | b | c |\n"
                "| 2 | The word closed appears here, still open | a | PHASE F | c |\n"
                "| 3 | q | a | Закрыт 2026-11-20 | c |\n")
        rows = {r["id"]: r["open"] for r in kb_db.parse_questions(text)}
        self.assertEqual(rows, {"Q1": False, "Q2": True, "Q3": False})
        real = kb_db.parse_questions((kb_db.ROOT / "development/OPEN_QUESTIONS.md").read_text(encoding="utf-8"))
        self.assertFalse({r["id"]: r for r in real}["Q1"]["open"])  # real row: struck through

    def test_every_limited_query_ends_in_the_primary_key(self):
        for table in kb_db.TABLES:
            sql = kb_db.build_query(table, ["limit=3"])
            self.assertRegex(sql, r"order by .*" + kb_db.PRIMARY_KEY[table][-1] + r" limit 3\) q_$", table)

    def test_split_row_ignores_pipes_in_backticks(self):
        self.assertEqual(kb_db.split_row("| a | `x | y` z | d |"), ["a", "`x | y` z", "d"])

    def test_every_debt_id_is_parsed(self):
        text = (kb_db.ROOT / "development/TECH_DEBT.md").read_text(encoding="utf-8")
        wanted = set(re.findall(r"^\| (DEBT-\d{4}) \|", text, re.M))
        self.assertEqual({r["id"] for r in kb_db.parse_debt(text)}, wanted)

    def test_split_row_keeps_escaped_pipe(self):
        self.assertEqual(kb_db.split_row(r"| a | b \| c | d |"), ["a", "b | c", "d"])

    def test_multiline_row_is_joined(self):
        text = "| # | D |\n|---|---|\n| 1 | first\nsecond |\n| 2 | x |\n"
        self.assertEqual(kb_db.table_rows(text), [["#", "D"], ["1", "first second"], ["2", "x"]])

    def test_decisions_superseded_and_source(self):
        text = ("| # | Дата | Решение | Ссылка |\n|---|---|---|---|\n"
                "| 205 | 2026-11-14 | **SUPERSEDED by #569 (2026-11-25).** old | RR-0008 |\n"
                "| 135 | 2026-11-03 | (Orchestrator, применение) x | - |\n")
        rows = {r["num"]: r for r in kb_db.parse_decisions(text)}
        self.assertEqual(rows[205]["superseded_by"], 569)
        self.assertEqual(rows[205]["source"], "owner")
        self.assertEqual(rows[135]["source"], "orchestrator")
        self.assertIsNone(rows[135]["superseded_by"])

    def test_debt_status_and_closed_date(self):
        text = ("| ID | Долг | Где | Почему | План | Статус |\n|---|---|---|---|---|---|\n"
                "| DEBT-0001 | a | env | w | p | открыт |\n"
                "| DEBT-0002 | b | Lab | w | p | закрыт 2026-11-14 (TASK-0049) |\n"
                "| DEBT-0003 | c | Lab | w | p | частично |\n"
                "| DEBT-0004 | no area | w | p | open |\n")
        rows = {r["id"]: r for r in kb_db.parse_debt(text)}
        self.assertEqual([rows[i]["status"] for i in sorted(rows)], ["open", "closed", "partial", "open"])
        self.assertEqual(rows["DEBT-0002"]["closed"], "2026-11-14")
        self.assertIsNone(rows["DEBT-0001"]["closed"])
        self.assertEqual(rows["DEBT-0004"]["area"], "")

    def test_debt_three_cell_multiline_row(self):
        text = ("| ID | Долг | Где | Почему | План | Статус |\n|---|---|---|---|---|---|\n"
                "| DEBT-0040 | TASK-0068 review (reviewer, low).\n- first item\n- second | Open |\n"
                "| DEBT-0041 | next | Закрыт 2026-11-20 |\n")
        rows = {r["id"]: r for r in kb_db.parse_debt(text)}
        self.assertEqual(rows["DEBT-0040"]["status"], "open")
        self.assertEqual(rows["DEBT-0040"]["area"], "")
        self.assertIn("second", rows["DEBT-0040"]["text"])
        self.assertEqual(rows["DEBT-0041"]["closed"], "2026-11-20")

    def test_orch_rows(self):
        text = "| № | Дата | Решение | Основание | Где |\n|---|---|---|---|---|\n| O-007 | 2026-11-19 | t | b | a |\n"
        self.assertEqual(kb_db.parse_orch(text)[0]["num"], 7)

    def test_questions_kinds_and_closed(self):
        text = ("## Очередь вопросов владельцу\n| # | В | К | Д |\n|---|---|---|---|\n| Q70 | q | ctx | 2026-11-20 |\n\n"
                "## Прочие открытые вопросы\n| # | В | Кто | Когда | Ссылка |\n|---|---|---|---|---|\n"
                "| 1 | ~~Закрыт~~ x | a | b | c |\n| 2 | open | a | PHASE F | c |\n\n"
                "## Очередь вопросов (autopilot)\n| # | В | Блок | С |\n|---|---|---|---|\n| A8 | q | blk | 2026-11-25 |\n")
        rows = {r["id"]: r for r in kb_db.parse_questions(text)}
        self.assertEqual(rows["Q70"]["kind"], "owner")
        self.assertEqual(rows["Q70"]["added"], "2026-11-20")
        self.assertFalse(rows["Q1"]["open"])
        extra = kb_db.parse_questions("## Очередь вопросов (autopilot)\n| # | В | Б | С |\n|---|---|---|---|\n"
                                      "| A1 | done | закрыт 2026-11-26 | c |\n| A2 | mentions closed in text | b | c |\n")
        self.assertEqual([r["open"] for r in extra], [False, True])
        self.assertEqual(rows["Q2"]["blocks"], "PHASE F")
        self.assertEqual(rows["A8"]["kind"], "autopilot")

    def test_errors_repeats(self):
        rows = kb_db.parse_errors({"errors": [{"id": "ERR-0001", "date": "2026-11-25", "area": "shots",
                                               "repeats": ["2026-11-26", "2026-11-27"], "status": "open"}]})
        self.assertEqual((rows[0]["repeats"], rows[0]["last_repeat"]), (2, "2026-11-27"))
        self.assertEqual(kb_db.parse_errors(None), [])

    def test_agent_runs_pair_launch_and_stop(self):
        lines = [json.dumps(r) for r in [
            {"t": 1000, "event": "launched", "agent_id": "a1", "agent_type": "developer", "description": "d", "tasks": ["TASK-1", "TASK-2"]},
            {"t": 2000, "event": "stop", "agent_id": "a1", "agent_type": "developer"},
            {"t": 1500, "event": "launched", "agent_id": "a2", "agent_type": "critic", "description": "e"},
            {"t": 1, "event": "start", "agent_type": "x"}]] + ["not json"]
        runs = {r["agent_id"]: r for r in kb_db.parse_agent_log(lines)}
        self.assertEqual(len(runs), 2)
        self.assertIsNotNone(runs["a1"]["stop_t"])
        self.assertIsNone(runs["a2"]["stop_t"])
        self.assertEqual(runs["a1"]["tasks"], "TASK-1,TASK-2")

    def test_facts_rows(self):
        import datetime as dt
        facts = [{"id": "a", "statement": "s", "source": "x", "keywords": [1945, "карта"], "reviewed": dt.date(2026, 11, 26)},
                 {"id": "b", "statement": "t", "reviewed": "never"}, {"statement": "no id"}, "junk"]
        rows = kb_db.parse_facts(facts, lambda f: "drifted" if f["id"] == "b" else None)
        self.assertEqual([r["id"] for r in rows], ["a", "b"])
        self.assertEqual((rows[0]["keywords"], rows[0]["reviewed"], rows[0]["drift"]), ("1945, карта", "2026-11-26", False))
        self.assertEqual((rows[1]["reviewed"], rows[1]["drift"], rows[1]["drift_text"]), (None, True, "drifted"))

    def test_facts_and_probe_files_are_in_the_fingerprint(self):
        files = {p.relative_to(kb_db.ROOT).as_posix() for p in kb_db.source_files() if kb_db.ROOT in p.parents}
        self.assertIn("development/FACTS.yaml", files)
        self.assertIn("src/Singularity.Session/Slice/SliceWorld.cs", files)  # probe of schema-version

    def test_journal_key_is_stable(self):
        line = json.dumps({"t": 5, "hook": "guard", "msg": "no"})
        a = kb_db.parse_journal([line], "hook")[0]
        self.assertEqual(a["key"], kb_db.parse_journal([line], "hook")[0]["key"])
        self.assertEqual((a["who"], a["msg"]), ("guard", "no"))


class SafetyTests(unittest.TestCase):
    def test_quiet_build_without_postgres_is_silent(self):
        old = kb_db.available
        kb_db.available = lambda: False
        try:
            self.assertEqual(kb_db.build(quiet=True), 0)
        finally:
            kb_db.available = old

    def test_schema_is_in_fingerprint(self):
        a = kb_db.fingerprint()
        old = kb_db.SCHEMA
        kb_db.SCHEMA = old + 1
        try:
            self.assertNotEqual(a, kb_db.fingerprint())
        finally:
            kb_db.SCHEMA = old


class SchemaTests(unittest.TestCase):
    def test_schema_name(self):
        old = (kb_db.git_common_parent, kb_db.ROOT)
        try:
            kb_db.git_common_parent = lambda: kb_db.ROOT.resolve()
            self.assertEqual(kb_db.db_schema(), "main")
            kb_db.git_common_parent = lambda: Path("/other")
            self.assertRegex(kb_db.db_schema(), r"^wt_[0-9a-f]{8}$")
        finally:
            kb_db.git_common_parent, kb_db.ROOT = old


class QueryBuilderTests(unittest.TestCase):
    def test_filters_become_sql(self):
        sql = kb_db.build_query("debt", ["status=open", "area=Lab", "limit=5"])
        self.assertIn("lower(status) = lower('open')", sql)
        self.assertIn("area ilike '%Lab%'", sql)
        self.assertTrue(sql.endswith("limit 5) q_"))

    def test_text_search_and_since(self):
        sql = kb_db.build_query("decisions", ["text~наука", "since=2026-11-20"])
        self.assertIn("plainto_tsquery('russian'", sql)
        self.assertIn("date >= '2026-11-20'::date", sql)

    def test_quotes_are_escaped(self):
        sql = kb_db.build_query("items", ["title~x' or 1=1 --"])
        self.assertIn("'%x'' or 1=1 --%'", sql)

    def test_silent_and_errors(self):
        self.assertIn("stop_t is null", kb_db.build_query("agents", ["silent>2h"]))
        with self.assertRaises(ValueError):
            kb_db.build_query("items", ["nope=1"])
        with self.assertRaises(ValueError):
            kb_db.build_query("items", ["silent>2h"])
        with self.assertRaises(ValueError):
            kb_db.build_query("decisions", ["num=abc"])

    def test_journals_hide_deleted_by_default(self):
        self.assertIn("deleted_at is null", kb_db.build_query("refusals", []))
        self.assertNotIn("deleted_at", kb_db.build_query("refusals", ["deleted=1"]))


@unittest.skipUnless(kb_db.available(), "PostgreSQL is not available")
class DatabaseTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.db = "kbtest_" + uuid.uuid4().hex[:10]
        cls.state = tempfile.TemporaryDirectory()
        s = Path(cls.state.name)
        (s / "agents.jsonl").write_text(json.dumps(
            {"t": 1000, "event": "launched", "agent_id": "zz1", "agent_type": "developer", "description": "old silent run"}) + "\n")
        (s / "hook_refusals.jsonl").write_text(json.dumps({"t": 5, "hook": "guard_zone", "msg": "refused one"}) + "\n")
        cls.old = {k: os.environ.get(k) for k in ("SINGULARITY_KB_DB", "SINGULARITY_STATE")}
        os.environ["SINGULARITY_KB_DB"] = cls.db
        os.environ["SINGULARITY_STATE"] = cls.state.name

    @classmethod
    def tearDownClass(cls):
        for k, v in cls.old.items():
            if v is None:
                os.environ.pop(k, None)
            else:
                os.environ[k] = v
        try:
            kb_db.psql("postgres", f"drop database if exists {cls.db}")
        finally:
            cls.state.cleanup()

    def run_q(self, *args):
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            rc = kb_db.main(list(args))
        self.assertEqual(rc, 0)
        return out.getvalue()

    def test_01_build_and_query(self):
        t0 = __import__("time").time()
        self.run_q("build")
        self.assertLess(__import__("time").time() - t0, 5.0)  # acceptance: full rebuild within 5 s
        rows = json.loads(self.run_q("q", "items", "id=TASK-0200", "--json"))
        self.assertEqual(rows[0]["agent"], "developer")
        self.assertTrue(json.loads(self.run_q("q", "decisions", "num=590", "--json")))
        self.assertIn("zz1", self.run_q("q", "agents", "silent>2h"))

    def test_02_forget_needs_an_exact_key(self):
        err = io.StringIO()
        for key in ("", "  "):
            with contextlib.redirect_stderr(err), contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(kb_db.main(["forget", "refusals", key]), 2)
        self.assertIn("exact key", err.getvalue())
        with contextlib.redirect_stdout(io.StringIO()) as out:
            kb_db.main(["forget", "refusals", "a"])  # a prefix of nothing: no wildcard
        self.assertIn("soft-deleted 0", out.getvalue())

    def test_02b_all_debt_ids_are_indexed(self):
        text = (kb_db.ROOT / "development/TECH_DEBT.md").read_text(encoding="utf-8")
        wanted = len(set(re.findall(r"^\| (DEBT-\d{4}) \|", text, re.M)))
        self.assertEqual(len(json.loads(self.run_q("q", "debt", "limit=1000", "--json"))), wanted)

    def test_03a_json_stays_valid_when_q_rebuilds(self):
        s = Path(self.state.name)
        with (s / "agents.jsonl").open("a") as f:
            f.write(json.dumps({"t": 1002, "event": "launched", "agent_id": "zz3", "agent_type": "critic"}) + "\n")
        os.utime(s / "agents.jsonl", ns=(2 * 10**18, 2 * 10**18))
        out = io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(kb_db.main(["q", "agents", "agent_id=zz3", "--json"]), 0)
        self.assertEqual(json.loads(out.getvalue())[0]["agent_id"], "zz3")

    def test_03_query_rebuilds_when_files_change(self):
        s = Path(self.state.name)
        with (s / "agents.jsonl").open("a") as f:
            f.write(json.dumps({"t": 1001, "event": "launched", "agent_id": "zz2", "agent_type": "critic", "description": "fresh"}) + "\n")
        os.utime(s / "agents.jsonl", ns=(10**18, 10**18 + 5))
        self.assertIn("zz2", self.run_q("q", "agents", "agent_id=zz2"))

    def test_04_soft_delete_survives_rebuild(self):
        self.assertIn("refused one", self.run_q("q", "refusals"))
        key = json.loads(self.run_q("q", "refusals", "--json"))[0]["key"]
        self.run_q("forget", "refusals", key)
        self.assertEqual(self.run_q("q", "refusals").strip(), "")
        self.run_q("build")
        self.assertEqual(self.run_q("q", "refusals").strip(), "")
        self.assertIn("refused one", self.run_q("q", "refusals", "deleted=1"))

    def test_05_text_search_forms(self):
        self.assertTrue(json.loads(self.run_q("q", "decisions", "text~наука", "--json")))

    def test_06_facts_table(self):
        rows = json.loads(self.run_q("q", "facts", "text~институт", "--json"))
        self.assertIn("institutes-count", {r["id"] for r in rows})
        self.assertEqual(json.loads(self.run_q("q", "facts", "drift=true", "--json")), [])  # kb_check: no drift
        self.assertTrue(json.loads(self.run_q("q", "facts", "id=schema-version", "--json")))


if __name__ == "__main__":
    unittest.main()
