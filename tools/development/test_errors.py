#!/usr/bin/env python3
"""Self-tests for errors.py and the land.py failure log (stdlib unittest).

Run: python3 tools/development/test_errors.py
"""
import contextlib
import datetime as dt
import io
import json
import sys
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import errors  # noqa: E402
import watch  # noqa: E402
import land  # noqa: E402

BASE = ["--executor", "orchestrator", "--kind", "process", "--area", "shots", "--symptom", "Old version on frames"]


def has_gitleaks() -> bool:
    return bool(shutil.which("gitleaks") or (Path.home() / ".local" / "bin" / "gitleaks").exists())


class ErrorsTest(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.file = self.tmp / "errors.yaml"
        self.state = self.tmp / "state"
        self.state.mkdir()

    def run_cli(self, *argv, today="2026-11-26"):
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            rc = errors.main(["--file", str(self.file), "--state", str(self.state), "--today", today, *argv])
        return rc, out.getvalue()

    def test_add_validates_and_dedupes_by_key(self):
        self.assertEqual(self.run_cli("add", *BASE)[0], 0)
        rc, out = self.run_cli("add", *BASE[:-1], "old VERSION on frames!", today="2026-11-27")
        self.assertIn("repeat recorded", out)
        e = errors.load(self.file)
        self.assertEqual(len(e), 1)
        self.assertEqual(e[0]["repeats"], ["2026-11-27"])
        self.assertEqual(errors.validate(e), [])

    def test_add_rejects_bad_executor(self):
        rc, _ = self.run_cli("add", "--executor", "bob", *BASE[2:])
        self.assertEqual(rc, 2)
        self.assertFalse(self.file.exists())

    def test_close_needs_prevention_and_check_catches_bad_schema(self):
        self.run_cli("add", *BASE)
        self.assertEqual(self.run_cli("close", "ERR-0001")[0], 2)
        self.assertEqual(self.run_cli("close", "ERR-0001", "--prevention", "hook:guard_shots")[0], 0)
        e = errors.load(self.file)
        self.assertEqual(e[0]["status"], "prevented")
        e[0]["kind"] = "nonsense"
        self.assertTrue(errors.validate(e))

    def test_stats_windows_and_repeat_share(self):
        self.run_cli("add", *BASE, today="2026-11-01")
        self.run_cli("repeat", "ERR-0001", today="2026-11-25")
        self.run_cli("repeat", "ERR-0001", today="2026-11-26")
        _, out = self.run_cli("stats")
        seven, thirty = out.split("30 days")
        self.assertIn("7 days: 2 occurrence(s), repeats 2 (100%)", seven)
        self.assertIn("3 occurrence(s), repeats 2 (66%)", thirty)
        self.assertIn("repeats by executor: orchestrator 2", thirty)

    def test_leaderboard_ranks_repeats_not_first_occurrences(self):
        self.run_cli("add", "--executor", "agent:a", "--kind", "code", "--area", "x", "--symptom", "one", today="2026-11-25")
        self.run_cli("add", "--executor", "agent:b", "--kind", "ui", "--area", "x", "--symptom", "two", today="2026-11-25")
        self.run_cli("add", "--executor", "agent:c", "--kind", "ui", "--area", "x", "--symptom", "three", today="2026-11-25")
        self.run_cli("repeat", "ERR-0001", today="2026-11-26")
        self.run_cli("repeat", "ERR-0001", today="2026-11-27")
        _, out = self.run_cli("stats", today="2026-11-27")
        self.assertIn("repeats by kind: code 2", out.split("30 days")[0])
        self.assertNotIn("ui", out.split("30 days")[0].split("repeats by kind:")[1].splitlines()[0])

    def test_russian_symptoms_are_distinct_keys(self):
        for s in ("Агент запущен до коммита задачи", "Кадры сняты на старой версии"):
            self.run_cli("add", "--executor", "orchestrator", "--kind", "process", "--area", "a", "--symptom", s)
        self.assertEqual(len(errors.load(self.file)), 2)
        self.run_cli("add", "--executor", "orchestrator", "--kind", "process", "--area", "a",
                     "--symptom", "агент запущен ДО коммита задачи!", today="2026-11-27")
        e = errors.load(self.file)
        self.assertEqual((len(e), e[0]["repeats"]), (2, ["2026-11-27"]))

    def test_malformed_root_is_an_error(self):
        for body in ("- a\n- b\n", "errors: 5\n", "errors:\n  - just text\n", "just text\n"):
            self.file.write_text(body, encoding="utf-8")
            rc, out = self.run_cli("check")
            self.assertEqual(rc, 1, body)
            self.assertIn("ERROR", out)

    def append(self, name, *recs, raw=""):
        with (self.state / name).open("a", encoding="utf-8") as f:
            for r in recs:
                f.write(json.dumps(r) + "\n")
            f.write(raw)

    def test_import_never_rotates_sources_so_late_lines_are_kept(self):
        t = dt.datetime(2026, 11, 26, 12, tzinfo=dt.timezone.utc).timestamp()
        src = self.state / "land_failures.jsonl"
        self.append("land_failures.jsonl", {"t": t, "code": 1, "branch": "b1"})
        inode = src.stat().st_ino
        # A hook that opened the file earlier writes while the import runs: the import saves, then the line lands.
        real = errors.save

        def save_then_append(*a, **k):
            real(*a, **k)
            self.append("land_failures.jsonl", {"t": t, "code": 6, "branch": "b2"}, raw='{"t": 1, "code"')  # + half line

        errors.save = save_then_append
        try:
            _, out = self.run_cli("import")
        finally:
            errors.save = real
        self.assertIn("1 event(s), 1 new draft(s)", out)
        self.assertEqual(src.stat().st_ino, inode)  # same file: nothing was renamed or truncated
        _, out = self.run_cli("import")
        self.assertIn("1 event(s)", out)  # the late line is read; the half line waits
        _, out = self.run_cli("import")
        self.assertIn("0 event(s)", out)

    def test_interrupted_import_does_not_double_count(self):
        t = dt.datetime(2026, 11, 26, 12, tzinfo=dt.timezone.utc).timestamp()
        self.append("land_failures.jsonl", {"t": t, "code": 1, "branch": "b1"})
        real = errors.write_import_state
        calls = []

        def die_on_cursor_update(*a, **k):
            calls.append(1)
            if len(calls) == 2:  # journal written, catalogue saved, now the cursors
                raise KeyboardInterrupt
            return real(*a, **k)

        errors.write_import_state = die_on_cursor_update
        try:
            with self.assertRaises(KeyboardInterrupt):
                self.run_cli("import")
        finally:
            errors.write_import_state = real
        self.run_cli("import")
        e = errors.load(self.file)
        self.assertEqual((len(e), e[0]["repeats"]), (1, []))  # one event, one occurrence

    def test_import_interrupted_before_save_is_redone(self):
        t = dt.datetime(2026, 11, 26, 12, tzinfo=dt.timezone.utc).timestamp()
        self.append("land_failures.jsonl", {"t": t, "code": 1, "branch": "b1"})
        real = errors.save
        errors.save = lambda *a, **k: (_ for _ in ()).throw(KeyboardInterrupt)
        try:
            with self.assertRaises(KeyboardInterrupt):
                self.run_cli("import")
        finally:
            errors.save = real
        self.assertIn("1 event(s), 1 new draft(s)", self.run_cli("import")[1])

    def test_schema_rejects_values_that_crash_commands(self):
        good = {"id": "ERR-0001", "date": "2026-11-26", "executor": "orchestrator", "kind": "code", "area": "a",
                "symptom": "s", "cause": "c", "found_by": "self", "cost": "low", "fix": "", "repeats": [], "status": "open"}
        for patch in ({"symptom": ["x"]}, {"date": "2026-13-45"}, {"repeats": ["2026-02-30"]}, {"id": 5},
                      {"kind": ["code"]}, {"prevention": {"kind": "hook", "ref": 3}}, {"area": None}):
            self.assertTrue(errors.validate([{**good, **patch}]), patch)
        self.assertEqual(errors.validate([good]), [])
        errors.save(self.file, [{**good, "symptom": ["x"]}])
        for cmd in ("list", "stats"):
            rc, out = self.run_cli(cmd)
            self.assertEqual(rc, 1, cmd)
            self.assertIn("ERR-0001", out)

    def test_stored_key_is_dropped(self):
        self.run_cli("add", *BASE)
        self.assertNotIn("key:", self.file.read_text(encoding="utf-8"))
        text = self.file.read_text(encoding="utf-8").replace("status: open", "status: open\n  key: old|key")
        self.file.write_text(text, encoding="utf-8")
        self.assertNotIn("key", errors.load(self.file)[0])
        self.run_cli("repeat", "ERR-0001")
        self.assertNotIn("key:", self.file.read_text(encoding="utf-8"))

    def test_from_review_record_json(self):
        rec = self.tmp / "review-TASK-0009-abc-codex.json"
        rec.write_text(json.dumps({"kind": "review", "task": "TASK-0009", "result": {
            "verdict": "changes_requested", "summary": "s", "findings": [
                {"severity": "high", "path": "a.py", "line": 5, "claim": "key drops letters", "evidence": ""},
                {"severity": "low", "path": "b.py", "line": 1, "claim": "naming", "evidence": ""}]}}), encoding="utf-8")
        self.run_cli("add", "--from-review", str(rec))
        e = errors.load(self.file)
        self.assertEqual([x["area"] for x in e], ["TASK-0009", "TASK-0009"])
        self.assertIn("a.py:5 key drops letters", e[0]["symptom"])
        rec.write_text(json.dumps({"kind": "review", "task": "TASK-0009", "result": {
            "verdict": "accept", "summary": "s", "findings": []}}), encoding="utf-8")
        self.assertIn("nothing added", self.run_cli("add", "--from-review", str(rec))[1])

    def test_import_drafts_ignores_guard_load_and_empties_sources(self):
        t = dt.datetime(2026, 11, 26, 12, tzinfo=dt.timezone.utc).timestamp()
        (self.state / "hook_refusals.jsonl").write_text("\n".join(json.dumps(r) for r in [
            {"t": t, "hook": "guard_shots", "msg": "Blocked: no live permit"},
            {"t": t, "hook": "guard_shots", "msg": "Blocked: no live permit"},
            {"t": t, "hook": "guard_load", "msg": "CPU busy"}]) + "\nnot json\n", encoding="utf-8")
        (self.state / "land_failures.jsonl").write_text(json.dumps({"t": t, "code": 6, "branch": "b1"}) + "\n", encoding="utf-8")
        _, out = self.run_cli("import")
        self.assertIn("3 event(s), 2 new draft(s), 1 ignored", out)
        e = errors.load(self.file)
        self.assertEqual({x["status"] for x in e}, {"open"})
        self.assertEqual(errors.validate(e), [])
        _, out = self.run_cli("import")
        self.assertIn("0 event(s)", out)  # the cursor is past the lines; the source file itself stays

    def test_from_review_only_for_changes(self):
        ok = self.tmp / "TASK-0007-review.md"
        ok.write_text("Verdict: APPROVE\n- fine\n", encoding="utf-8")
        self.assertIn("nothing added", self.run_cli("add", "--from-review", str(ok))[1])
        bad = self.tmp / "TASK-0007-review.md"
        bad.write_text("Verdict: CHANGES\n- tick path allocates\n", encoding="utf-8")
        self.run_cli("add", "--from-review", str(bad))
        e = errors.load(self.file)
        self.assertEqual((e[0]["area"], e[0]["found_by"], e[0]["symptom"]), ("TASK-0007", "reviewer", "tick path allocates"))

    @unittest.skipUnless(has_gitleaks(), "gitleaks not installed")
    def test_digest_dry_run_scans_and_sends_nothing(self):
        self.run_cli("add", *BASE)
        calls = []
        out = errors.run_digest(errors.load(self.file), dt.date(2026, 11, 26), self.tmp / "d", dry_run=True,
                                make_provider=lambda: calls.append(1))
        self.assertIn("nothing sent", out)
        self.assertEqual(calls, [])  # the provider is never even built
        self.assertFalse((self.tmp / "d").exists())

    @unittest.skipUnless(has_gitleaks(), "gitleaks not installed")
    def test_digest_writes_report_with_injected_provider(self):
        self.run_cli("add", *BASE)
        sys.path.insert(0, str(errors.ROOT))

        class Comp:
            usage = {}
            reasoning_chars = 0
            reasoning_tokens = None
            finish_reason = "stop"
            content = json.dumps({"STATUS": "changes_requested", "SUMMARY": "one pattern", "OPEN": [], "FINDINGS": [
                {"severity": "high", "item": "stale client", "reason": "ERR-0001", "proposal": "hook"}]})

        class Prov:
            thinking = False

            def complete(self, *a, **k):
                return Comp()

        out = errors.run_digest(errors.load(self.file), dt.date(2026, 11, 26), self.tmp / "d", make_provider=Prov)
        self.assertEqual(out.name, "errors-digest-2026-48.md")
        self.assertIn("stale client", out.read_text(encoding="utf-8"))

    def test_long_symptoms_with_same_prefix_stay_distinct(self):
        pre = "x" * 70
        for tail in ("alpha", "beta"):
            self.run_cli("add", "--executor", "orchestrator", "--kind", "code", "--area", "a", "--symptom", pre + tail)
        self.assertEqual(len(errors.load(self.file)), 2)

    def test_same_day_repeats_are_counted(self):
        self.run_cli("add", *BASE)
        self.run_cli("repeat", "ERR-0001")
        self.run_cli("repeat", "ERR-0001")
        self.assertEqual(errors.load(self.file)[0]["repeats"], ["2026-11-26", "2026-11-26"])
        self.assertIn("repeats 2", self.run_cli("stats")[1])

    def test_two_processes_do_not_lose_entries(self):
        script = str(Path(errors.__file__))
        procs = []
        for p in range(2):
            for n in range(6):
                procs.append(subprocess.Popen([sys.executable, script, "--file", str(self.file), "--state", str(self.state),
                                               "add", "--executor", "orchestrator", "--kind", "code", "--area", "a",
                                               "--symptom", f"proc {p} item {n}"], stdout=subprocess.DEVNULL))
        for pr in procs:
            self.assertEqual(pr.wait(), 0)
        e = errors.load(self.file)
        self.assertEqual((len(e), len({x["id"] for x in e})), (12, 12))

    def test_from_review_entries_are_schema_valid(self):
        bad = self.tmp / "TASK-0007-review.md"
        bad.write_text("Verdict: CHANGES\n- x\n", encoding="utf-8")
        self.assertEqual(self.run_cli("add", "--from-review", str(bad), "--executor", "bob")[0], 2)
        self.assertFalse(self.file.exists())
        self.run_cli("add", "--from-review", str(bad))
        self.assertEqual(errors.validate(errors.load(self.file)), [])

    def test_ci_import_reports_superseded_failure_once(self):
        runs = [{"databaseId": 7, "conclusion": "success", "displayTitle": "green", "createdAt": "2026-11-26T10:00:00Z"},
                {"databaseId": 6, "conclusion": "failure", "displayTitle": "broke", "createdAt": "2026-11-26T09:00:00Z"},
                {"databaseId": 3, "conclusion": "failure", "displayTitle": "old", "createdAt": "2026-11-25T09:00:00Z"}]
        real = watch.subprocess.run
        watch.subprocess.run = lambda *a, **k: subprocess.CompletedProcess(a, 0, json.dumps(runs), "")
        try:
            (self.state / errors.IMPORT_STATE).write_text(json.dumps({"ci_seen": [7, 3]}))
            _, out = self.run_cli("import", "--ci")
            self.assertIn("1 event(s), 1 new draft(s)", out)  # run 6 finished late with an older id than 7: not skipped
            self.assertEqual(json.loads((self.state / errors.IMPORT_STATE).read_text())["ci_seen"], [3, 6, 7])
            _, out = self.run_cli("import", "--ci")
            self.assertIn("0 event(s)", out)
        finally:
            watch.subprocess.run = real
        self.assertIn("broke", errors.load(self.file)[0]["symptom"])

    def test_save_is_atomic_and_keeps_old_file_on_error(self):
        self.run_cli("add", *BASE)
        before = self.file.read_text(encoding="utf-8")
        real = errors.os.replace
        errors.os.replace = lambda *a: (_ for _ in ()).throw(OSError("disk"))
        try:
            with self.assertRaises(OSError):
                errors.save(self.file, [{"id": "ERR-0009"}])
        finally:
            errors.os.replace = real
        self.assertEqual(self.file.read_text(encoding="utf-8"), before)
        self.assertEqual([p.name for p in self.tmp.iterdir() if p.suffix == ".tmp" or p.name.startswith(".")], [])

    def test_land_failure_log(self):
        land.log_failure(self.tmp, 1, "agent-x")  # branch comes from the parsed args (run(info))
        rec = json.loads((self.tmp / ".claude" / ".state" / "land_failures.jsonl").read_text())
        self.assertEqual((rec["code"], rec["branch"]), (1, "agent-x"))


if __name__ == "__main__":
    unittest.main()
