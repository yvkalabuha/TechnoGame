"""land.py lock (TASK-0145): flock-based; a second landing waits and then refuses, a dead holder leaves no stale lock."""

import fcntl
import importlib.util
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from tools.llm_worker import config

spec = importlib.util.spec_from_file_location("land_mod", Path(config.REPO_ROOT) / "tools" / "development" / "land.py")
land = importlib.util.module_from_spec(spec)
_path_before = list(sys.path)
spec.loader.exec_module(land)
_path_after = list(sys.path)


class LandImportPathTest(unittest.TestCase):
    def test_import_leaves_sys_path_unchanged(self):
        # tools/development has test_*.py names that would shadow llm_worker tests
        self.assertEqual(_path_before, _path_after)


class LandSysPathTest(unittest.TestCase):
    def test_needs_second_review_leaves_sys_path_unchanged(self):
        root = Path(config.REPO_ROOT)
        before = list(sys.path)
        for _ in range(2):
            self.assertEqual(land.needs_second_review(["tools/x.py", "docs/a.md"], root), ["tools/x.py"])
        self.assertEqual(sys.path, before)
        sys.path.insert(0, str(root))  # already present: left in place
        try:
            land.needs_second_review(["docs/a.md"], root)
            self.assertEqual(sys.path[0], str(root))
        finally:
            sys.path.remove(str(root))


class LandFactsGateTest(unittest.TestCase):
    """#597: a branch adding a decision row needs its related facts changed or 'facts-ok: #N'."""

    def git(self, *a):
        return subprocess.run(["git", "-C", str(self.repo), *a], check=True, capture_output=True, text=True,
                              env=dict(os.environ, GIT_AUTHOR_NAME="t", GIT_AUTHOR_EMAIL="t@t", GIT_COMMITTER_NAME="t",
                                       GIT_COMMITTER_EMAIL="t@t")).stdout.strip()

    def commit(self, name, text, msg="c"):
        p = self.repo / name
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(text, encoding="utf-8")
        self.git("add", "-A")
        self.git("commit", "-q", "-m", msg)

    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.repo = Path(tmp.name)
        self.git("init", "-q", "-b", "main")
        dev = Path(config.REPO_ROOT) / "tools" / "development"
        for name in ("kb_check.py", "registry.py"):
            self.commit(f"tools/development/{name}", (dev / name).read_text(encoding="utf-8"))
        self.commit("development/FACTS.yaml", "facts:\n  - {id: codex, statement: s, keywords: [Codex]}\n")
        self.commit("docs/decisions/DECISIONS_LOG.md", "| # | Д | Р | С |\n|---|---|---|---|\n| 1 | d | a | r |\n")
        self.base = self.git("rev-parse", "HEAD")
        self.commit("docs/decisions/DECISIONS_LOG.md",
                    "| # | Д | Р | С |\n|---|---|---|---|\n| 1 | d | a | r |\n| 2 | d | about Codex | r |\n")
        self.head = self.git("rev-parse", "HEAD")

    def test_new_decision_without_facts_is_refused(self):
        ok, out = land.facts_gate(self.repo, self.base, self.head, "merge: x")
        self.assertFalse(ok)
        self.assertIn("decision #2", out)

    def test_facts_ok_in_message_passes(self):
        ok, out = land.facts_gate(self.repo, self.base, self.head, "merge: x\n\nfacts-ok: #2")
        self.assertTrue(ok, out)

    def test_old_kb_check_skips_the_gate(self):
        self.commit("tools/development/kb_check.py", "print('old')\n")
        ok, out = land.facts_gate(self.repo, self.base, self.head, "merge: x")
        self.assertTrue(ok)
        self.assertIn("skipped", out)


class LandLockTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.lock = Path(self.tmp.name) / "land.lock"
        for p in (mock.patch.object(land, "LAND_LOCK", self.lock), mock.patch.object(land, "LAND_WAIT_S", 0)):
            p.start()
            self.addCleanup(p.stop)

    def test_held_lock_refuses_second_landing(self):
        fd = os.open(self.lock, os.O_CREAT | os.O_RDWR)
        fcntl.flock(fd, fcntl.LOCK_EX)
        self.addCleanup(os.close, fd)
        with mock.patch.object(land, "run", side_effect=AssertionError("ran")):
            self.assertEqual(land.main(), 4)

    def test_running_codex_sketch_blocks_landing(self):  # a running Codex sketch
        fd = os.open(self.lock, os.O_CREAT | os.O_RDWR)
        fcntl.flock(fd, fcntl.LOCK_SH)
        self.addCleanup(os.close, fd)
        with mock.patch.object(land, "run", side_effect=AssertionError("ran")):
            self.assertEqual(land.main(), 4)

    def test_leftover_file_is_not_a_lock_and_run_holds_the_lock(self):
        self.lock.write_text("999999999")
        seen = []

        def run(*_args, **_kwargs):
            fd = os.open(self.lock, os.O_RDWR)
            try:
                fcntl.flock(fd, fcntl.LOCK_SH | fcntl.LOCK_NB)
                seen.append("free")
            except OSError:
                seen.append("held")
            finally:
                os.close(fd)
            return 0
        with mock.patch.object(land, "run", run):
            self.assertEqual(land.main(), 0)
        self.assertEqual(seen, ["held"])
        fd = os.open(self.lock, os.O_RDWR)  # released at exit
        fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        os.close(fd)


if __name__ == "__main__":
    unittest.main()
