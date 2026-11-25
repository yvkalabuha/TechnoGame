"""land.py lock (TASK-0145): a second landing is refused, a stale lock is replaced, a foreign lock is never deleted."""

import importlib.util
import os
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from tools.llm_worker import config

spec = importlib.util.spec_from_file_location("land_mod", Path(config.REPO_ROOT) / "tools" / "development" / "land.py")
land = importlib.util.module_from_spec(spec)
spec.loader.exec_module(land)


class LandLockTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.lock = Path(self.tmp.name) / "land.lock"
        p = mock.patch.object(land, "LAND_LOCK", self.lock)
        p.start()
        self.addCleanup(p.stop)

    def test_live_foreign_lock_refuses_and_stays(self):
        self.lock.write_text(str(os.getppid()))  # a live pid that is not ours
        with mock.patch.object(land, "run", side_effect=AssertionError("ran")):
            self.assertEqual(land.main(), 4)
        self.assertEqual(self.lock.read_text(), str(os.getppid()))

    def test_stale_lock_is_replaced_and_removed_at_exit(self):
        self.lock.write_text("999999999")
        seen = []
        with mock.patch.object(land, "run", lambda: seen.append(self.lock.read_text()) or 0):
            self.assertEqual(land.main(), 0)
        self.assertEqual(seen, [str(os.getpid())])
        self.assertFalse(self.lock.exists())


if __name__ == "__main__":
    unittest.main()
