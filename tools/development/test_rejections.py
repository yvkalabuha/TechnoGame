#!/usr/bin/env python3
"""Self-tests for rejections.py and its hooks in errors.py stats and metrics.py (stdlib unittest)."""
import contextlib
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import errors  # noqa: E402
import metrics  # noqa: E402
import rejections  # noqa: E402
from tools.llm_worker import rejections as core  # noqa: E402


class RejectionsCliTest(unittest.TestCase):
    def setUp(self):
        self.d = Path(tempfile.mkdtemp())
        self.j = self.d / "j.yaml"

    def run_cli(self, *argv):
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            rc = rejections.main(["--file", str(self.j), *argv])
        return rc, out.getvalue()

    def test_add_for_check(self):
        rc, _ = self.run_cli("add", "--provider", "claude", "--area", "src/a.cs", "--summary", "use float here",
                             "--reason", "floats are banned (ADR-0002)", "--ref", "ADR-0002")
        self.assertEqual(rc, 0)
        rc, out = self.run_cli("for", "src/a.cs")
        self.assertIn("floats are banned", out)
        self.assertEqual(self.run_cli("for", "src/b.cs")[1], "")
        self.assertEqual(self.run_cli("check")[0], 0)

    def test_check_fails_on_bad_entry(self):
        self.j.write_text("rejections:\n- id: x\n  provider: p\n")
        self.assertEqual(self.run_cli("check")[0], 1)

    def test_stats_line_counts_repeats(self):
        runs = self.d / "runs"
        runs.mkdir()
        rec = {"kind": "review", "created_at": "2026-11-20T10:00:00+00:00",
               "result": {"findings": [{"path": "a.py", "claim": "missing null check in parse"}]}}
        (runs / "review-T-1-codex.json").write_text(json.dumps(rec))
        entries = [{"id": "i", "provider": "codex", "area": "a.py", "summary": "missing null check in parse",
                    "reason": "r", "date": "2026-11-10"}]
        line = rejections.stats_line(entries, "2026-11-01", "2026-12-01", runs)
        self.assertIn("1 new, repeating a rejected one 1 (100.0%)", line)

    def test_metrics_rows_shape(self):
        rows = metrics.rejection_rows("2026-01-01", "2026-01-02")
        self.assertEqual(list(rows[0]), ["findings", "repeating_rejected", "share_pct"])

    def test_errors_stats_prints_rejection_lines(self):
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            errors.main(["--file", str(self.d / "e.yaml"), "stats"])
        self.assertIn("review findings", out.getvalue())


if __name__ == "__main__":
    unittest.main()
