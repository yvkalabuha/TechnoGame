"""land.py lock (TASK-0145): flock-based; a second landing waits and then refuses, a dead holder leaves no stale lock."""

import fcntl
import importlib.util
import os
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
