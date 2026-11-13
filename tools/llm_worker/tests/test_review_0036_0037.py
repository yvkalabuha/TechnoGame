"""Test review of TASK-0036/0037: classifier properties and model switching with a real ledger.
Seeded stdlib random, no network."""

import random
import tempfile
import unittest
from decimal import Decimal
from pathlib import Path

from helpers import FakeProvider, TempRepo
from test_ledger import entries
from test_loop import EXPLAIN, FakeVerifier, art, contract

from tools.llm_worker import paths, risk
from tools.llm_worker.ledger import Guard, Ledger
from tools.llm_worker.loop import ESCALATED, PASSED, CandidateLoop

IDX = risk.CLASSES.index


def rank(path):
    return IDX(risk.classify_path(path))


class ClassifierPropertyTest(unittest.TestCase):
    def setUp(self):
        self.classes = risk.load_classes()
        self.rng = random.Random(20260101)

    def concrete(self, pattern):
        """A concrete path matching a glob pattern."""
        r = self.rng
        name = lambda: r.choice(["a", "Fx6", "x_golden_y", "b.cs", "n.md"])
        out = pattern.replace("**/", r.choice(["", "d1/", "d1/d2/"])).replace("**", name()).replace("*", name())
        return out

    def test_generated_paths_never_below_their_pattern_class(self):
        for cls, pats in ((c, self.classes[c]) for c in risk.CLASSES):
            for pat in pats:
                for _ in range(20):
                    p = self.concrete(pat)
                    if not paths.matches(p.lower(), [pat]):
                        continue
                    self.assertGreaterEqual(rank(p), IDX(cls), f"{p} (from {pat}) got below {cls}")

    def test_decorated_paths_never_lower_than_plain(self):
        """./, //, upper case, trailing dots segments must not lower the class of the plain path."""
        base = ["docs/decisions/ADR-1.md", "development/tasks/T.md", "src/Singularity.Kernel/Fx6.cs",
                "tests/Singularity.Kernel.Tests/Llm/X.cs", ".github/workflows/ci.yml", "tools/llm_worker/risk.py",
                "lab/results/r.json", "a/b.csproj", "lab/golden/x.json", "CURRENT_STATE.md", ".git/config"]
        for p in base:
            plain = rank(p)
            for v in ("./" + p, p.upper(), p.swapcase(), p.replace("/", "//"), "./" + p.replace("/", "/./")):
                self.assertGreaterEqual(rank(v), plain, f"{v!r} < {p!r}")
            for v in ("../" + p, "/" + p, "x/../" + p, p + "/../../x", "~/" + p, p.replace("/", "\\")):
                self.assertEqual(rank(v), IDX("R3"), f"{v!r} must be R3")

    def test_random_sets_are_monotone(self):
        pool = ["docs/a.md", "tools/x.py", "src/Singularity.Core/a.cs", "docs/decisions/d.md", "README.md",
                "tests/t/a.cs", "mods/m.json", "../x", "a.csproj"]
        for _ in range(200):
            files = self.rng.sample(pool, self.rng.randint(0, 5))
            extra = self.rng.choice(pool)
            c1, c2 = IDX(risk.classify_paths(files)), IDX(risk.classify_paths(files + [extra]))
            self.assertGreaterEqual(c2, c1)
            self.assertGreaterEqual(c2, rank(extra))
            self.assertEqual(c1, max([0] + [rank(f) for f in files]))

    def test_unicode_and_nul_paths_do_not_crash_or_downgrade(self):
        for p in ("docs/decisions/а.md", "docs/decisions/x\x00.md", "", " ", "docs/decisions/ ", "docs/decisions/x.md "):
            self.assertGreaterEqual(rank(p), IDX("R2"), repr(p))  # unknown R2 at worst, R3 when under decisions


class RandomRenameRangeTest(unittest.TestCase):
    def test_random_renames_take_the_higher_class_of_both_sides(self):
        rng = random.Random(7)
        files = ["docs/n1.md", "tools/t1.py", "src/Singularity.Core/c.cs", "docs/decisions/d.md",
                 "tests/o/a.cs", "lab/golden/g.json", "x.csproj", "tests/Singularity.Kernel.Tests/k.cs"]
        for trial in range(6):
            repo = TempRepo({f: f"content of {f} {'z' * 40}\n" for f in files})
            try:
                repo.git("checkout", "-q", "-b", "llm/t")
                src, dst = rng.sample(files, 2)
                dst = dst.rsplit("/", 1)[0] + f"/moved{trial}." + dst.rsplit(".", 1)[1]
                dstp = repo.root / dst
                dstp.parent.mkdir(parents=True, exist_ok=True)
                repo.git("mv", src, dst)
                repo.git("commit", "-q", "-m", "mv")
                overall, rows = risk.classify_range("main...llm/t", repo.root)
                self.assertGreaterEqual(IDX(overall), max(rank(src), rank(dst)), (src, dst, rows))
                self.assertEqual({r[0] for r in rows}, {src, dst})
            finally:
                repo.cleanup()


class PaidChain(FakeProvider):
    """A scripted provider that books a real reservation per call, like OpenAICompatProvider (one per attempt)."""
    paid = True

    def __init__(self, model, script, guard, crash_after_reserve=False):
        super().__init__(script)
        self.model, self.guard, self.crash = model, guard, crash_after_reserve

    def complete(self, messages, schema, schema_name, max_tokens, seed=None, on_raw=None):
        rid = self.guard.before_attempt(b"x" * 400, max_tokens)
        if self.crash:
            raise KeyboardInterrupt()  # killed after the reservation, before settle
        c = super().complete(messages, schema, schema_name, max_tokens, seed, on_raw)
        self.guard.settle(rid, c.usage)
        return c


def prices2():
    p = lambda i, o: {"input_per_mtok": Decimal(i), "output_per_mtok": Decimal(o)}
    return {"ready": True, "cap": Decimal("5"), "task_cap": Decimal("5"), "models": {"flash": p("0.3", "1.2"), "pro": p("1", "4")}}


class SwitchLedgerTest(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.tmp = Path(self._tmp.name)
        self.ledger = Ledger(self.tmp / "ledger")
        self.ledger.init()
        self.dir = self.tmp / "c1"

    def tearDown(self):
        self._tmp.cleanup()

    def provs(self, flash_script, pro_script, **kw):
        g = lambda m: Guard(self.ledger, prices2(), m, task="T1")
        return PaidChain("flash", flash_script, g("flash")), PaidChain("pro", pro_script, g("pro"), **kw)

    def loop(self, flash, pro):
        return CandidateLoop(contract(), 1, flash, FakeVerifier(), self.dir, "CTX", lambda: 0.0, fallbacks=[pro])

    def test_one_reservation_per_call_and_all_settled(self):
        flash, pro = self.provs([art("x_only"), EXPLAIN, art("x_only")], [EXPLAIN, art("good")])
        self.assertEqual(self.loop(flash, pro).run()["outcome"], PASSED)
        ent = entries(self.ledger)
        reserves = [e for e in ent if e["t"] == "reserve"]
        self.assertEqual(len(reserves), len(flash.calls) + len(pro.calls))
        self.assertEqual([e["model"] for e in reserves], ["flash"] * 3 + ["pro"] * 2)
        self.assertEqual(len({e["id"] for e in reserves}), len(reserves))
        s = self.ledger.summary()
        self.assertEqual(s["unsettled"], 0)
        self.assertEqual(s["calls"], 5)

    def test_restart_after_switch_keeps_position_and_adds_no_reservation(self):
        # real scenario: stop right after the switch
        flash, pro = self.provs([art("x_only"), EXPLAIN, art("x_only")], [], crash_after_reserve=True)
        with self.assertRaises(KeyboardInterrupt):
            self.loop(flash, pro).run()
        before = self.ledger.summary()
        self.assertEqual(before["calls"], 4)       # 3 flash + 1 pro (killed before settle)
        self.assertEqual(before["unsettled"], 1)
        spent = before["total"]
        # restart with a fresh Ledger object and fresh providers
        ledger2 = Ledger(self.tmp / "ledger")
        flash2 = PaidChain("flash", [], Guard(ledger2, prices2(), "flash", task="T1"))
        pro2 = PaidChain("pro", [EXPLAIN, art("good")], Guard(ledger2, prices2(), "pro", task="T1"))
        st = CandidateLoop(contract(), 1, flash2, FakeVerifier(), self.dir, "CTX", lambda: 0.0, fallbacks=[pro2]).run()
        self.assertEqual(st["outcome"], PASSED)
        self.assertEqual(flash2.calls, [])  # never goes back to flash
        after = ledger2.summary()
        self.assertEqual(after["by_model"]["flash"], before["by_model"]["flash"])  # flash not re-reserved
        self.assertGreaterEqual(after["total"], spent)  # the killed reservation still counts
        self.assertEqual(after["unsettled"], 1)

    def test_escalation_after_last_model_reserves_nothing_more(self):
        flash, pro = self.provs([art("x_only"), EXPLAIN, art("x_only")], [EXPLAIN, art("x_only"), EXPLAIN, art("x_only")])
        self.assertEqual(self.loop(flash, pro).run()["outcome"], ESCALATED)
        n = self.ledger.summary()["calls"]
        self.assertEqual(n, 7)
        self.assertEqual(self.loop(flash, pro).run()["outcome"], ESCALATED)  # rerun of a finished loop
        self.assertEqual(self.ledger.summary()["calls"], n)


if __name__ == "__main__":
    unittest.main()
