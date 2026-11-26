"""Rejection memory (#591): journal, relevance, stats, reader CLI. No network."""

import sys
import tempfile
import unittest
from pathlib import Path

from tools.llm_worker import rejections as rej

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "development"))
import rejections as cli  # noqa: E402


class CoreTest(unittest.TestCase):
    def setUp(self):
        self.d = Path(tempfile.mkdtemp())
        self.j = self.d / "development" / "review-rejections.yaml"

    def test_add_dedupes_and_refreshes(self):
        rej.add(self.j, "codex", "a.py:3", "Missing null check here", "handled upstream", "#1", today="2026-11-01")
        rej.add(self.j, "codex", "a.py", "missing null check here", "handled upstream v2", "#2", today="2026-11-02")
        es = rej.load(self.j)
        self.assertEqual((len(es), es[0]["reason"], es[0]["area"]), (1, "handled upstream v2", "a.py"))
        self.assertEqual(rej.validate(es), [])

    def test_relevant_by_file_dir_topic_and_limit(self):
        for i in range(12):
            rej.add(self.j, "codex", "src/x.py", f"finding number{i} alpha", "r", today=f"2026-11-{i + 1:02d}")
        rej.add(self.j, "codex", "tools/", "dir wide thing", "r")
        rej.add(self.j, "deepseek", "docs/z.md", "carbon pricing model unclear", "r")
        es = rej.load(self.j)
        got = rej.relevant(es, ["src/x.py"])
        self.assertEqual(len(got), rej.MAX_LINES)
        self.assertEqual(got[0]["summary"], "finding number11 alpha")  # newest first
        self.assertEqual(len(rej.relevant(es, ["tools/a/b.py"])), 1)
        self.assertEqual(len(rej.relevant(es, ["other.py"])), 0)
        self.assertEqual(len(rej.relevant(es, [], "carbon pricing")), 1)
        self.assertEqual(len(rej.relevant(es, [], "carbon")), 0)

    def test_repeat_stats(self):
        rej.add(self.j, "codex", "a.py", "missing null check in parse", "r", source="old.json#1", today="2026-11-01")
        recs = [{"name": "new.json", "created_at": "2026-11-05T10:00:00", "findings": [
            {"path": "a.py", "claim": "parse is missing a null check"}, {"path": "a.py", "claim": "unrelated race"},
            {"path": "b.py", "claim": "missing null check in parse"}]},
                {"name": "old.json", "created_at": "2026-11-01T10:00:00", "findings": [
                    {"path": "a.py", "claim": "missing null check in parse"}]}]
        s = rej.repeat_stats(rej.load(self.j), recs, "2026-11-01", "2026-12-01")
        self.assertEqual((s["findings"], s["repeats"]), (4, 1))  # the source record does not count against itself
        self.assertEqual(rej.repeat_stats([], recs, "2026-11-01", "2026-12-01")["repeats"], 0)

    def test_malformed_journal_raises_and_load_relevant_warns(self):
        self.j.parent.mkdir(parents=True)
        self.j.write_text("rejections: 5\n")
        with self.assertRaises(ValueError):
            rej.load(self.j)
        msgs = []
        self.assertEqual(rej.load_relevant(self.d, ["a.py"], log=msgs.append), [])
        self.assertEqual(len(msgs), 1)

    def test_cli_for_paths_task_and_empty(self):
        rej.add(self.j, "codex", "tools/x.py", "bad name", "naming is fixed", "#5", today="2026-11-01")
        rej.add(self.j, "deepseek", "docs/y.md", "vague", "intended", "TASK-0001", today="2026-11-02")
        task = self.d / "development" / "tasks" / "TASK-0001-t.md"
        task.parent.mkdir(parents=True)
        task.write_text('---\ntitle: "Rework tools/x.py"\n---\nText\n')
        es = rej.load(self.j)
        out = cli.block_for(es, ["tools/x.py"], 8, self.d)
        self.assertTrue(out.startswith("Ранее отклонено и почему:"))
        self.assertIn("naming is fixed [#5]", out)
        out = cli.block_for(es, ["TASK-0001"], 8, self.d)
        self.assertIn("naming is fixed", out)  # file named in the title
        self.assertIn("intended", out)  # rejected under this task
        self.assertEqual(cli.block_for(es, ["nothing.py"], 8, self.d), "")
        with self.assertRaises(ValueError):
            cli.block_for(es, ["TASK-9999"], 8, self.d)


if __name__ == "__main__":
    unittest.main()
