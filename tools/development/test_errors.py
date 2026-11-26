#!/usr/bin/env python3
"""Self-tests for errors.py and the land.py failure log (stdlib unittest).

Run: python3 tools/development/test_errors.py
"""
import contextlib
import datetime as dt
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import errors  # noqa: E402
import land  # noqa: E402

BASE = ["--executor", "orchestrator", "--kind", "process", "--area", "shots", "--symptom", "Old version on frames"]


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
        self.assertIn("by executor: orchestrator 3", thirty)

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
        self.assertEqual((self.state / "land_failures.jsonl").read_text(), "")
        _, out = self.run_cli("import")
        self.assertIn("0 event(s)", out)

    def test_from_review_only_for_changes(self):
        ok = self.tmp / "TASK-0007-review.md"
        ok.write_text("Verdict: APPROVE\n- fine\n", encoding="utf-8")
        self.assertIn("nothing added", self.run_cli("add", "--from-review", str(ok))[1])
        bad = self.tmp / "TASK-0007-review.md"
        bad.write_text("Verdict: CHANGES\n- tick path allocates\n", encoding="utf-8")
        self.run_cli("add", "--from-review", str(bad))
        e = errors.load(self.file)
        self.assertEqual((e[0]["area"], e[0]["found_by"], e[0]["symptom"]), ("TASK-0007", "reviewer", "tick path allocates"))

    def test_digest_dry_run_scans_and_sends_nothing(self):
        self.run_cli("add", *BASE)
        rc, out = self.run_cli("digest", "--dry-run", "--out", str(self.tmp / "d"))
        self.assertIn("nothing sent" if rc == 0 else "digest refused", out)
        self.assertFalse((self.tmp / "d").exists())

    def test_digest_writes_report_with_injected_provider(self):
        self.run_cli("add", *BASE)
        sys.path.insert(0, str(errors.ROOT))
        from tools.llm_worker import gitleaks
        try:
            gitleaks.scan_text("x")
        except gitleaks.GitleaksUnavailable:
            self.skipTest("gitleaks not installed")

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

    def test_land_failure_log(self):
        land.log_failure(self.tmp, 1, "agent-x")
        rec = json.loads((self.tmp / ".claude" / ".state" / "land_failures.jsonl").read_text())
        self.assertEqual((rec["code"], rec["branch"]), (1, "agent-x"))


if __name__ == "__main__":
    unittest.main()
