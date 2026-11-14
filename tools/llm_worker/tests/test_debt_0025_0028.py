"""TASK-0048: ledger repair (DEBT-0025) and review record fields, bwrap order, related, robustness (DEBT-0028)."""

import hashlib
import json
import os
import tempfile
import time
import unittest
from decimal import Decimal
from pathlib import Path
from unittest import mock

from helpers import FakeProvider

from tools.llm_worker import __main__ as cli
from tools.llm_worker import codex, config, ledger as ledger_mod, report, review
from tools.llm_worker.ledger import Ledger
from test_debt_0026_0027 import rec, run_codex_with
from test_review import GOOD, make_repo


class LedgerRepairTest(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.dir = Path(self._tmp.name) / "ledger"
        self.led = Ledger(self.dir)
        self.led.init()

    def crash_after_line(self):
        """Reserve whose head write never happens: the line is on disk, the head is stale."""
        with mock.patch.object(Ledger, "_write_head", side_effect=OSError("killed")):
            with self.assertRaises(OSError):
                self.led.reserve("TASK-1", "m", Decimal("1"), Decimal("10"), Decimal("10"))

    def test_crash_between_line_and_head_then_repair(self):
        self.led.reserve("TASK-1", "m", Decimal("1"), Decimal("10"), Decimal("10"))
        self.crash_after_line()
        with self.assertRaises(ledger_mod.SpendRefused) as cm:
            self.led.summary()
        self.assertEqual(cm.exception.code, "ledger_corrupt")
        res = self.led.repair()
        self.assertEqual((res["status"], res["records"], res["added"]), ("repaired", 2, 1))
        self.assertEqual(self.led.summary()["total"], Decimal(2))  # the crashed reservation is counted
        self.assertEqual(self.led.repair()["status"], "ok")

    def test_damaged_tail_is_reported_not_deleted(self):
        self.led.reserve("TASK-1", "m", Decimal("1"), Decimal("10"), Decimal("10"))
        before = self.dir.joinpath("ledger.head").read_bytes()
        with open(self.led.path, "ab") as f:
            f.write(b'{"t": "reserve", "id": "x"')  # torn write, no newline
        size = self.led.path.stat().st_size
        res = self.led.repair()
        self.assertEqual(res["status"], "refused")
        self.assertEqual(self.led.path.stat().st_size, size)
        self.assertEqual(self.dir.joinpath("ledger.head").read_bytes(), before)

    def test_edited_or_truncated_ledger_is_not_repaired(self):
        for _ in range(2):
            self.led.reserve("TASK-1", "m", Decimal("1"), Decimal("10"), Decimal("10"))
        lines = self.led.path.read_bytes().splitlines(keepends=True)
        self.led.path.write_bytes(lines[0].replace(b'"1"', b'"0"') + lines[1])  # edited amount
        self.assertEqual(self.led.repair()["status"], "refused")
        self.led.path.write_bytes(lines[0])  # truncated below the head
        self.assertEqual(self.led.repair()["status"], "refused")
        with self.assertRaises(ledger_mod.SpendRefused):
            self.led.summary()

    def test_missing_ledger_and_cli(self):
        with self.assertRaises(ledger_mod.SpendRefused) as cm:
            Ledger(Path(self._tmp.name) / "none").repair()
        self.assertEqual(cm.exception.code, "ledger_missing")
        self.crash_after_line()
        with mock.patch.object(cli, "Ledger", return_value=self.led), mock.patch("builtins.print") as pr:
            self.assertEqual(cli.main(["ledger", "repair"]), 0)
        self.assertIn("repaired", pr.call_args[0][0])


class ReviewRecordTest(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.tmp = Path(self._tmp.name)
        self.addCleanup(self._tmp.cleanup)
        for target in ("scan_text", "scan_dir"):
            m = mock.patch(f"tools.llm_worker.gitleaks.{target}")
            m.start()
            self.addCleanup(m.stop)
        m = mock.patch.object(config, "WORKTREES_DIR", self.tmp / "wt")
        m.start()
        self.addCleanup(m.stop)
        self.repo, self.base, self.head = make_repo()

    def run_review(self, **kw):
        return review.run_review("TASK-0001", self.base, self.head, "deepseek", root=self.repo.root,
                                 runs_dir=self.tmp / "runs", attempts_dir=self.tmp / "att", log=lambda m: None,
                                 make_provider=lambda: FakeProvider([GOOD]), **kw)

    def test_created_at_round_and_no_overwrite(self):
        p1, _, r1 = self.run_review()
        p2, _, r2 = self.run_review()
        self.assertNotEqual(p1, p2)
        self.assertTrue(p1.is_file() and p2.is_file())
        self.assertEqual((r1["round"], r2["round"]), (1, 2))
        self.assertRegex(json.loads(p1.read_text())["created_at"], r"^\d{4}-\d\d-\d\dT.*\+00:00$")
        self.assertEqual(p2.name, p1.name[:-5] + "-n2.json")

    def test_invalid_record_names_stay_invalid_and_unique(self):
        for _ in range(2):
            with self.assertRaises(review.ReviewError):
                review.run_review("TASK-0001", self.base, self.head, "deepseek", root=self.repo.root,
                                  runs_dir=self.tmp / "runs", attempts_dir=self.tmp / "att", log=lambda m: None,
                                  make_provider=lambda: FakeProvider(["not json"]))
        names = sorted(p.name for p in (self.tmp / "runs").iterdir())
        self.assertEqual(len(names), 2)
        self.assertTrue(all(n.endswith("-invalid.json") for n in names))

    def test_confirm_marks_and_validates(self):
        path, _, _ = self.run_review()
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        self.assertEqual(review.mark_finding(path, 1, True), 2)
        review.mark_finding(path, 2, False)
        self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(), digest)  # record untouched
        side = review.confirm_path(path)
        self.assertEqual(json.loads(side.read_text())["confirmed"], {"1": True, "2": False})
        self.assertEqual(review.next_round(self.tmp / "runs", "TASK-0001"), 2)  # sidecar is not a round
        for n in (0, 3):
            with self.assertRaises(review.ReviewError):
                review.mark_finding(path, n, True)
        bad = self.tmp / "bad.json"
        bad.write_text("[]")
        with self.assertRaises(review.ReviewError):
            review.mark_finding(bad, 1, True)

    def test_confirm_cli(self):
        path, _, _ = self.run_review()
        with mock.patch("builtins.print"):
            self.assertEqual(cli.main(["review-confirm", str(path), "--finding", "2", "--rejected"]), 0)
            self.assertEqual(cli.main(["review-confirm", str(path), "--finding", "9", "--confirmed"]), 5)
        self.assertEqual(json.loads(review.confirm_path(path).read_text())["confirmed"], {"2": False})

    def test_sandbox_unavailable_is_logged(self):
        def runner(prompt, wt):
            raise codex.SandboxUnavailable("no bwrap")
        with self.assertRaises(review.ReviewError) as cm:
            review.run_review("TASK-0001", self.base, self.head, "codex", root=self.repo.root, codex_runner=runner,
                              runs_dir=self.tmp / "runs", attempts_dir=self.tmp / "att", log=lambda m: None)
        self.assertEqual(cm.exception.code, "sandbox_unavailable")
        att = [json.loads(x) for x in (self.tmp / "att" / codex.ATTEMPTS).read_text().splitlines()]
        self.assertEqual([a["outcome"] for a in att], ["sandbox_unavailable"])


class BwrapBeforeLimitTest(unittest.TestCase):
    def test_quota_in_bwrap_line_is_sandbox_unavailable(self):
        err = "bwrap: Can't create file at /x: Disk quota exceeded"
        with self.assertRaises(codex.SandboxUnavailable):
            run_codex_with(mock.Mock(returncode=1, stdout="", stderr=err))

    def test_real_limit_still_limit(self):
        with self.assertRaises(codex.CodexLimit):
            run_codex_with(mock.Mock(returncode=1, stdout="", stderr="rate limit exceeded"))


class RelatedBoolTest(unittest.TestCase):
    def test_true_gives_no_id(self):
        for v in ("true", "false", "yes"):
            ids, problem = review._related_ids(f"---\nrelated: {v}\n---\n")
            self.assertEqual(ids, [], v)
            self.assertTrue(problem, v)
        self.assertEqual(review._related_ids("---\nrelated: [true, ADR-0001]\n---\n")[0], ["ADR-0001"])


class MetricsFieldsTest(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.runs = Path(self._tmp.name)

    def put(self, name, data, age=0):
        p = self.runs / name
        p.write_text(json.dumps(data))
        os.utime(p, (time.time() - age, time.time() - age))

    def line(self):
        return next(x for x in report._metrics_section(self.runs, None, None) if "`TASK-0001`" in x)

    def test_round_and_confirmed_come_from_fields(self):
        a = {**rec("TASK-0001", "changes_requested", ["high", "low"]), "round": 1, "created_at": "2026-01-01T00:00:00+00:00",
             "confirmed": {"1": True, "2": False}}
        b = {**rec("TASK-0001", "accept", ["medium"]), "round": 2, "created_at": "2026-01-02T00:00:00+00:00"}
        # mtime order is the reverse of created_at order: created_at must win
        a.pop("confirmed")
        self.put("review-TASK-0001-aaaaaaaa-codex.json", b, 0)
        self.put("review-TASK-0001-bbbbbbbb-codex.json", a, 100)
        self.put("review-TASK-0001-bbbbbbbb-codex.json.confirm.json", {"confirmed": {"1": True, "2": False}}, 100)
        line = self.line()
        self.assertIn("до accept: 2", line)
        self.assertIn("вердикт 1-го раунда changes_requested", line)
        self.assertIn("подтверждено (high 1, medium 0, low 0)", line)
        self.assertIn("отклонено (high 0, medium 0, low 1)", line)
        self.assertIn("не отмечено (high 0, medium 1, low 0)", line)

    def test_nonstandard_findings_are_skipped_not_fatal(self):
        for i, findings in enumerate(("text", None, [1], [{"severity": "huge"}], {"a": 1})):
            self.put(f"review-TASK-0009-{i:08d}-codex.json", {"kind": "review", "task": "TASK-0009", "result": {
                "verdict": "accept", "findings": findings}})
        self.put("review-TASK-0009-zzzzzzzz-codex.json", {"task": "TASK-0009", "result": {"verdict": 5, "findings": []}})
        self.put("review-TASK-0001-aaaaaaaa-codex.json", rec("TASK-0001", "accept", []))
        self.assertEqual(sorted(report._review_rounds(self.runs)), ["TASK-0001"])


if __name__ == "__main__":
    unittest.main()
