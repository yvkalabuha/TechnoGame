"""Second review and the third gate voice (ADR-0015 §4-§5). No network: fake providers, temporary repository."""

import hashlib
import json
import os
import shutil
import subprocess
import tempfile
import unittest
from decimal import Decimal
from pathlib import Path
from unittest import mock

from helpers import FakeProvider, TempRepo
from test_ledger import MODEL, prices
from test_provider_context import FakeOpener

from tools.llm_worker import codex, config, gitleaks, review
from tools.llm_worker.ledger import Guard, Ledger
from tools.llm_worker.provider import OpenAICompatProvider
from tools.llm_worker.review import ReviewError

TASK = ("---\nid: TASK-0001\n---\n# T\n## Цель\nMake X work.\n## Описание\nsecret plan\n"
        "## Критерии приёмки\n- [ ] X works\n\n## Результат\n")
GOOD = {"verdict": "changes_requested", "summary": "s",
        "findings": [{"severity": "high", "path": "a.py", "line": 3, "claim": "c", "evidence": "e"},
                     {"severity": "low", "path": "a.py", "line": 4, "claim": "c2", "evidence": ""}]}


def git(root, *args):
    subprocess.run(["git", *args], cwd=root, check=True, capture_output=True)
    return subprocess.run(["git", "rev-parse", "HEAD"], cwd=root, capture_output=True, text=True).stdout.strip()


def make_repo(extra=None, changed=None):
    repo = TempRepo({"development/tasks/TASK-0001-x.md": TASK, "tools/a.py": "x = 1\n",
                     ".claude/rules/csharp-core.md": "RULE-CORE\n",
                     "docs/decisions/ADR-0002-determinism-levels.md": "ADR2-TEXT\n", **(extra or {})})
    base = git(repo.root, "rev-parse", "HEAD")
    for rel, text in (changed or {"tools/a.py": "x = 2\n"}).items():
        p = repo.root / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(text, encoding="utf-8")
    git(repo.root, "add", "-A")
    head = git(repo.root, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "change")
    return repo, base, head


class InputTest(unittest.TestCase):
    def test_input_from_git_objects(self):
        repo, base, head = make_repo()
        (repo.root / "tools/a.py").write_text("LOCAL-EDIT\n")  # working tree edit must not leak
        (repo.root / "untracked.txt").write_text("UNTRACKED\n")
        text, meta = review.build_review_input("TASK-0001", base, head, repo.root)
        self.assertIn("Make X work.", text)
        self.assertIn("X works", text)
        self.assertNotIn("secret plan", text)  # only goal and criteria
        self.assertIn("+x = 2", text)
        self.assertIn("=== FILE tools/a.py ===\nx = 2\n", text)
        self.assertNotIn("LOCAL-EDIT", text)
        self.assertNotIn("UNTRACKED", text)
        self.assertNotIn("RULE-CORE", text)  # tools/a.py is R1
        self.assertEqual(meta["risk"], "R1")

    def test_r2_adds_rules(self):
        repo, base, head = make_repo(changed={"src/Singularity.Core/X.cs": "class X {}\n"})
        text, meta = review.build_review_input("TASK-0001", base, head, repo.root)
        self.assertEqual(meta["risk"], "R2")
        self.assertIn("RULE-CORE", text)
        self.assertIn("ADR2-TEXT", text)

    def test_excluded_paths_withheld(self):
        repo, base, head = make_repo(changed={"tools/a.py": "x = 3\n", "lab/results/r.txt": "RESULT-DATA\n",
                                              "keys/prod.key": "KEYDATA\n"})
        text, meta = review.build_review_input("TASK-0001", base, head, repo.root)
        self.assertNotIn("RESULT-DATA", text)
        self.assertNotIn("KEYDATA", text)
        self.assertEqual(sorted(meta["withheld"]), ["keys/prod.key", "lab/results/r.txt"])

    def test_codex_mode_lists_files_instead_of_contents(self):
        repo, base, head = make_repo()
        text, _ = review.build_review_input("TASK-0001", base, head, repo.root, codex_mode=True)
        self.assertNotIn("=== FILE tools/a.py", text)
        self.assertIn("- tools/a.py", text)

    def test_input_too_large(self):
        repo, base, head = make_repo(changed={"tools/a.py": "y" * 5000})
        with self.assertRaises(ReviewError) as cm:
            review.build_review_input("TASK-0001", base, head, repo.root, max_bytes=1000)
        self.assertEqual(cm.exception.code, "input_too_large")

    def test_unknown_task(self):
        repo, base, head = make_repo()
        with self.assertRaises(ReviewError) as cm:
            review.build_review_input("TASK-0002", base, head, repo.root)
        self.assertEqual(cm.exception.code, "task_not_found")


class ParseTest(unittest.TestCase):
    def test_valid(self):
        self.assertEqual(review.parse_answer(json.dumps(GOOD), review.REVIEW_SCHEMA, "review")["verdict"],
                         "changes_requested")

    def test_fenced_json_ok(self):
        self.assertTrue(review.parse_answer("```json\n" + json.dumps(GOOD) + "\n```", review.REVIEW_SCHEMA, "review"))

    def test_accept_with_high_is_invalid(self):
        for schema, kind, bad in ((review.REVIEW_SCHEMA, "review", {**GOOD, "verdict": "accept"}),
                                  (review.GATE_SCHEMA, "gate", {"STATUS": "approve", "SUMMARY": "s", "OPEN": [],
                                   "FINDINGS": [{"severity": "high", "item": "i", "reason": "r", "proposal": ""}]})):
            with self.assertRaises(ReviewError) as cm:
                review.parse_answer(json.dumps(bad), schema, kind)
            self.assertEqual(cm.exception.code, "schema_invalid")

    def test_accept_without_high_ok(self):
        ok = {**GOOD, "verdict": "accept", "findings": [GOOD["findings"][1]]}
        self.assertEqual(review.parse_answer(json.dumps(ok), review.REVIEW_SCHEMA, "review")["verdict"], "accept")

    def test_schema_violations(self):
        for bad in ("not json", "[]", json.dumps({"verdict": "maybe", "summary": "s", "findings": []}),
                    json.dumps({**GOOD, "extra": 1}),
                    json.dumps({**GOOD, "findings": [{"severity": "high", "path": "p", "line": -1, "claim": "c",
                                                     "evidence": ""}]})):
            with self.assertRaises(ReviewError) as cm:
                review.parse_answer(bad, review.REVIEW_SCHEMA, "review")
            self.assertEqual(cm.exception.code, "schema_invalid")

    def test_review_line_format(self):
        d = "a" * 64
        self.assertEqual(review.review_line("codex", "a1b2c3d4e5f6", "changes_requested", GOOD["findings"], d),
                         f"- [x] Второе ревью (codex, head a1b2c3d4) — changes_requested: 1 high, 1 low; report sha256 {d}")
        self.assertIn("accept: no findings", review.review_line("deepseek", "a1b2c3d4", "accept", [], d))


class RunTest(unittest.TestCase):
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

    def run_review(self, provider, **kw):
        return review.run_review("TASK-0001", self.base, self.head, provider, root=self.repo.root,
                                 runs_dir=self.tmp / "runs", attempts_dir=self.tmp / "attempts", log=lambda m: None, **kw)

    def test_deepseek_review_writes_record_and_line(self):
        fake = FakeProvider([GOOD])
        path, line, rec = self.run_review("deepseek", make_provider=lambda: fake)
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        self.assertTrue(line.endswith(f"report sha256 {digest}"))
        self.assertIn(f"(deepseek, head {self.head[:8]})", line)
        self.assertEqual(json.loads(path.read_text())["result"]["verdict"], "changes_requested")
        self.assertEqual(fake.calls[0]["schema_name"], "review")

    def test_codex_runs_on_clean_worktree_and_cleans_up(self):
        seen = {}

        def runner(prompt, wt):
            seen["files"] = sorted(p.relative_to(wt).as_posix() for p in Path(wt).rglob("*") if p.is_file())
            seen["is_dir"] = Path(wt).is_dir()
            return json.dumps(GOOD), {"input_tokens": 7, "output_tokens": 3}

        path, line, rec = self.run_review("codex", codex_runner=runner)
        self.assertIn("(codex, head", line)
        self.assertIn("tools/a.py", seen["files"])
        self.assertFalse(any("/bin/" in f or "/obj/" in f for f in seen["files"]))
        self.assertEqual(list((self.tmp / "wt").iterdir()), [])  # worktree removed
        att = [json.loads(x) for x in (self.tmp / "attempts" / codex.ATTEMPTS).read_text().splitlines()]
        self.assertEqual((att[0]["outcome"], att[0]["input_tokens"]), ("ok", 7))

    def test_codex_limit_falls_back_to_deepseek(self):
        def runner(prompt, wt):
            raise codex.CodexLimit("limit")

        fake = FakeProvider([GOOD])
        path, line, rec = self.run_review("codex", codex_runner=runner, make_provider=lambda: fake)
        self.assertEqual((rec["provider"], rec["fallback"]), ("deepseek", "codex_limit"))
        self.assertIn("(deepseek,", line)
        att = [json.loads(x) for x in (self.tmp / "attempts" / codex.ATTEMPTS).read_text().splitlines()]
        self.assertEqual(att[0]["outcome"], "codex_limit")
        # the fallback prompt carries file contents, the Codex one did not
        self.assertIn("=== FILE tools/a.py", fake.calls[0]["messages"][1]["content"])

    def test_codex_prompt_starts_with_instruction(self):
        got = []

        def runner(prompt, wt):
            got.append(prompt)
            return json.dumps(GOOD), {}

        self.run_review("codex", codex_runner=runner)
        self.assertTrue(got[0].startswith(review.REVIEW_SYSTEM))
        self.assertIn("untrusted data", got[0].split("=== DIFF")[0])
        self.assertIn("/work", got[0].split("=== DIFF")[0])

    def test_existing_directory_is_not_removed(self):
        fixed = mock.Mock(hex="a" * 32)
        victim = self.tmp / "wt" / f"review-{self.head[:8]}-{'a' * 12}"
        victim.mkdir(parents=True)
        (victim / "keep.txt").write_text("other call\n")
        with mock.patch("uuid.uuid4", return_value=fixed):
            with self.assertRaises(ReviewError) as cm:
                self.run_review("codex", codex_runner=lambda p, w: self.fail("must not run"))
        self.assertEqual(cm.exception.code, "worktree_failed")
        self.assertEqual((victim / "keep.txt").read_text(), "other call\n")

    def test_codex_mode_counts_changed_file_sizes(self):
        repo, base, head = make_repo(changed={"tools/a.py": "y" * 5000})
        text, _ = review.build_review_input("TASK-0001", base, head, repo.root, max_bytes=100000, codex_mode=True)
        with self.assertRaises(ReviewError) as cm:  # the prompt is small, the file Codex reads is not
            review.build_review_input("TASK-0001", base, head, repo.root, max_bytes=len(text.encode()) + 100,
                                      codex_mode=True)
        self.assertEqual(cm.exception.code, "input_too_large")

    def test_no_paid_fallback_on_other_failures(self):
        for exc, code in ((codex.CodexFailed("boom"), "codex_failed"),
                          (codex.SandboxUnavailable("no bwrap"), "sandbox_unavailable")):
            fake = FakeProvider([GOOD])

            def runner(prompt, wt, exc=exc):
                raise exc

            with self.assertRaises(ReviewError) as cm:
                self.run_review("codex", codex_runner=runner, make_provider=lambda: fake)
            self.assertEqual(cm.exception.code, code)
            self.assertEqual(fake.calls, [])

    def test_no_paid_fallback_on_guard_violation(self):
        fake = FakeProvider([GOOD])

        def runner(prompt, wt):
            (Path(wt) / "tools" / "a.py").write_text("tampered\n")
            return json.dumps(GOOD), {}

        with self.assertRaises(ReviewError):
            self.run_review("codex", codex_runner=runner, make_provider=lambda: fake)
        self.assertEqual(fake.calls, [])

    def test_secret_in_fallback_input_stops_the_paid_call(self):
        err = gitleaks.SecretDetected([{"rule": "r", "line": 1}])
        fake = FakeProvider([GOOD])

        def runner(prompt, wt):
            raise codex.CodexLimit("limit")

        # the Codex input scans clean; the fallback input (with file contents) does not
        with mock.patch("tools.llm_worker.gitleaks.scan_text", side_effect=[None, err]):
            with self.assertRaises(ReviewError) as cm:
                self.run_review("codex", codex_runner=runner, make_provider=lambda: fake)
        self.assertEqual(cm.exception.code, "secret_detected")
        self.assertEqual(fake.calls, [])

    def test_worktree_git_failure_is_review_error(self):
        with mock.patch.object(codex, "make_worktree", side_effect=review.GitError("boom")):
            with self.assertRaises(ReviewError) as cm:
                self.run_review("codex", codex_runner=lambda p, w: self.fail("must not run"))
        self.assertEqual(cm.exception.code, "worktree_failed")

    def test_literal_pathspecs(self):
        repo, base, head = make_repo(changed={"tools/a.py": "x = 3\n", "tools/[a].py": "BRACKET\n",
                                              "lab/results/r.txt": "RESULT-DATA\n", "lab/results/*": "STAR\n"})
        text, meta = review.build_review_input("TASK-0001", base, head, repo.root)
        self.assertNotIn("RESULT-DATA", text)
        self.assertNotIn("STAR", text)
        self.assertIn("BRACKET", text)

    def test_guard_violation_when_worktree_changes(self):
        def runner(prompt, wt):
            (Path(wt) / "tools" / "a.py").write_text("tampered\n")
            return json.dumps(GOOD), {}

        with self.assertRaises(ReviewError) as cm:
            self.run_review("codex", codex_runner=runner)
        self.assertEqual(cm.exception.code, "guard_violation")
        self.assertEqual(list((self.tmp / "wt").iterdir()), [])
        self.assertFalse((self.tmp / "runs").exists())  # no accepted review

    def test_secret_detected_stops_before_any_call(self):
        err = gitleaks.SecretDetected([{"rule": "r", "line": 1}])
        fake = FakeProvider([GOOD])
        with mock.patch("tools.llm_worker.gitleaks.scan_text", side_effect=err):
            with self.assertRaises(ReviewError) as cm:
                self.run_review("deepseek", make_provider=lambda: fake)
        self.assertEqual(cm.exception.code, "secret_detected")
        self.assertEqual(fake.calls, [])

    def test_worktree_secret_detected_skips_codex(self):
        ran = []
        err = gitleaks.SecretDetected([{"rule": "r", "line": 1}])
        with mock.patch("tools.llm_worker.gitleaks.scan_dir", side_effect=err):
            with self.assertRaises(ReviewError) as cm:
                self.run_review("codex", codex_runner=lambda p, w: ran.append(1))
        self.assertEqual((cm.exception.code, ran), ("secret_detected", []))
        self.assertEqual(list((self.tmp / "wt").iterdir()), [])

    def test_schema_invalid_keeps_spend_in_ledger(self):
        ledger = Ledger(self.tmp / "ledger")
        ledger.init()
        body = {"choices": [{"message": {"content": json.dumps({**GOOD, "verdict": "accept"})}}],
                "usage": {"prompt_tokens": 1000, "completion_tokens": 100}}
        provider = OpenAICompatProvider(profile=config.PROFILES["deepseek-flash"], api_key="k",
                                        opener=FakeOpener([body]), guard=Guard(ledger, prices(), MODEL, task="TASK-0001"))
        with self.assertRaises(ReviewError) as cm:
            self.run_review("deepseek", make_provider=lambda: provider)
        self.assertEqual(cm.exception.code, "schema_invalid")
        s = ledger.summary()
        self.assertEqual((s["calls"], s["unsettled"]), (1, 0))
        self.assertEqual(s["total"], Decimal("0.00042000"))  # actual usage, not the reservation
        invalid = list((self.tmp / "runs").glob("*-invalid.json"))
        self.assertEqual(len(invalid), 1)

    def test_deepseek_without_scan_of_sensitive_files(self):
        repo, base, head = make_repo(changed={"tools/a.py": "x = 3\n", "lab/results/r.txt": "RESULT-DATA\n"})
        fake = FakeProvider([GOOD])
        review.run_review("TASK-0001", base, head, "deepseek", root=repo.root, runs_dir=self.tmp / "runs",
                          attempts_dir=self.tmp / "a", log=lambda m: None, make_provider=lambda: fake)
        self.assertNotIn("RESULT-DATA", fake.calls[0]["messages"][1]["content"])


GATE_DOC = ("---\nid: ADR-9\nrelated: [ADR-0002, TASK-0001]\n---\n# Doc\nSee `tools/a.py` and docs/missing.md "
            "and lab/results/r.txt.\n")
GATE_OK = {"STATUS": "changes_requested", "SUMMARY": "s", "OPEN": ["q"],
           "FINDINGS": [{"severity": "medium", "item": "i", "reason": "r", "proposal": "p"}]}


class GateTest(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.tmp = Path(self._tmp.name)
        self.addCleanup(self._tmp.cleanup)
        m = mock.patch("tools.llm_worker.gitleaks.scan_text")
        m.start()
        self.addCleanup(m.stop)
        self.repo, _, self.head = make_repo(changed={"docs/decisions/ADR-9-doc.md": GATE_DOC,
                                                     "lab/results/r.txt": "RESULT-DATA\n"})
        self.item = "docs/decisions/ADR-9-doc.md"

    def test_input_has_related_and_linked_files(self):
        text, meta = review.build_gate_input(self.item, self.head, self.repo.root)
        self.assertEqual(sorted(meta["files"]), ["development/tasks/TASK-0001-x.md",
                                                 "docs/decisions/ADR-0002-determinism-levels.md", "tools/a.py"])
        self.assertIn("ADR2-TEXT", text)
        self.assertNotIn("RESULT-DATA", text)  # CONTEXT_EXCLUDE applies to links too

    def test_working_tree_is_not_read(self):
        (self.repo.root / "tools/a.py").write_text("LOCAL-EDIT\n")
        text, _ = review.build_gate_input(self.item, self.head, self.repo.root)
        self.assertIn("x = 1", text)
        self.assertNotIn("LOCAL-EDIT", text)

    def test_size_limit(self):
        text, meta = review.build_gate_input(self.item, self.head, self.repo.root, max_bytes=len(GATE_DOC) + 450)
        self.assertTrue(meta["omitted"])
        with self.assertRaises(ReviewError) as cm:
            review.build_gate_input(self.item, self.head, self.repo.root, max_bytes=50)
        self.assertEqual(cm.exception.code, "input_too_large")

    def test_gate_total_never_exceeds_limit(self):
        full, _ = review.build_gate_input(self.item, self.head, self.repo.root)
        for limit in range(len(GATE_DOC) + 60, len(full.encode()) + 5, 7):
            try:
                text, meta = review.build_gate_input(self.item, self.head, self.repo.root, max_bytes=limit)
            except ReviewError as e:
                self.assertEqual(e.code, "input_too_large")
                continue
            self.assertLessEqual(len(text.encode()), limit, limit)
        text, meta = review.build_gate_input(self.item, self.head, self.repo.root, max_bytes=len(full.encode()))
        self.assertEqual((len(text.encode()), meta["omitted"]), (len(full.encode()), []))

    def test_excluded_item_refused(self):
        with self.assertRaises(ReviewError):
            review.build_gate_input("lab/results/r.txt", self.head, self.repo.root)

    def test_gate_run_writes_record(self):
        fake = FakeProvider([GATE_OK])
        path, rec = review.run_gate(self.item, self.head, "third", root=self.repo.root, runs_dir=self.tmp / "runs",
                                    make_provider=lambda: fake)
        self.assertEqual(fake.calls[0]["schema_name"], "gate")
        self.assertEqual(rec["sha256"], hashlib.sha256(path.read_bytes()).hexdigest())
        self.assertEqual(rec["result"]["STATUS"], "changes_requested")

    def test_gate_schema_invalid(self):
        fake = FakeProvider([{**GATE_OK, "STATUS": "approve", "FINDINGS": [
            {"severity": "high", "item": "i", "reason": "r", "proposal": ""}]}])
        with self.assertRaises(ReviewError) as cm:
            review.run_gate(self.item, self.head, "third", root=self.repo.root, runs_dir=self.tmp / "runs",
                            make_provider=lambda: fake)
        self.assertEqual(cm.exception.code, "schema_invalid")

    def test_dry_run_sends_nothing(self):
        path, rec = review.run_gate(self.item, self.head, "third", root=self.repo.root, runs_dir=self.tmp / "runs",
                                    make_provider=lambda: self.fail("must not be called"), dry_run=True)
        self.assertIsNone(path)
        self.assertGreater(rec["input_bytes"], 0)


class CodexUnitTest(unittest.TestCase):
    def test_parse_events(self):
        out = "\n".join(json.dumps(e) for e in (
            {"type": "thread.started"}, {"type": "item.completed", "item": {"type": "reasoning", "text": "no"}},
            {"type": "item.completed", "item": {"type": "agent_message", "text": "ANSWER"}},
            {"type": "turn.completed", "usage": {"input_tokens": 5, "output_tokens": 2}})) + "\nnot json\n"
        self.assertEqual(codex.parse_events(out), ("ANSWER", {"input_tokens": 5, "output_tokens": 2}))

    def test_node_prefix_guard(self):
        with tempfile.TemporaryDirectory() as d:
            d = Path(d).resolve()
            (d / "lib" / "node_modules").mkdir(parents=True)
            self.assertEqual(codex.node_prefix(str(d / "bin" / "codex"), home="/nonexistent-home"), d)
            with self.assertRaises(codex.SandboxUnavailable):  # prefix is $HOME
                codex.node_prefix(str(d / "bin" / "codex"), home=d)
            with self.assertRaises(codex.SandboxUnavailable):  # prefix is an ancestor of $HOME
                codex.node_prefix(str(d / "bin" / "codex"), home=d / "a" / "b")
            (d / "x").mkdir()
            with self.assertRaises(codex.SandboxUnavailable):  # no lib/node_modules
                codex.node_prefix(str(d / "x" / "bin" / "codex"), home="/nonexistent-home")
        self.assertIsNone(codex.node_prefix("/usr/bin/codex"))

    def test_limit_text_only_from_error_events(self):
        fake = json.dumps({"type": "item.completed", "item": {"type": "agent_message", "text": "usage limit 429"}})
        proc = mock.Mock(returncode=1, stdout=fake, stderr="")
        with mock.patch.object(codex, "find_codex", return_value="/x/bin/codex"), \
                mock.patch.object(codex, "bwrap_argv", return_value=["bwrap"]), \
                mock.patch("subprocess.run", return_value=proc):
            with self.assertRaises(codex.CodexFailed):  # model text cannot fake a subscription limit
                codex.run_codex("p", "/w", "/s")
        proc = mock.Mock(returncode=1, stdout=json.dumps({"type": "turn.failed", "error": {"message": "rate limit"}}),
                         stderr="")
        with mock.patch.object(codex, "find_codex", return_value="/x/bin/codex"), \
                mock.patch.object(codex, "bwrap_argv", return_value=["bwrap"]), \
                mock.patch("subprocess.run", return_value=proc):
            with self.assertRaises(codex.CodexLimit):
                codex.run_codex("p", "/w", "/s")

    def test_limit_detected(self):
        proc = mock.Mock(returncode=1, stdout='{"type":"error","message":"You hit your usage limit"}', stderr="")
        with mock.patch.object(codex, "find_codex", return_value="/x/bin/codex"), \
                mock.patch.object(codex, "bwrap_argv", return_value=["bwrap"]), \
                mock.patch("subprocess.run", return_value=proc):
            with self.assertRaises(codex.CodexLimit):
                codex.run_codex("p", "/w", "/s")

    def test_other_failure(self):
        proc = mock.Mock(returncode=2, stdout="", stderr="boom")
        with mock.patch.object(codex, "find_codex", return_value="/x/bin/codex"), \
                mock.patch.object(codex, "bwrap_argv", return_value=["bwrap"]), \
                mock.patch("subprocess.run", return_value=proc):
            with self.assertRaises(codex.CodexFailed):
                codex.run_codex("p", "/w", "/s")

    def test_argv_hides_var_www_and_home(self):
        with tempfile.TemporaryDirectory() as d:
            (Path(d) / "lib" / "node_modules").mkdir(parents=True)
            argv = codex.bwrap_argv("/wt", "/schema.json", f"{d}/bin/codex", home="/home/u", bwrap="bwrap",
                                    codex_home="/nonexistent")
        self.assertNotIn("--unshare-net", argv)
        self.assertEqual(argv[argv.index("--tmpfs", argv.index("/proc")) + 1:][:1], ["/tmp"])
        self.assertIn(["--tmpfs", "/home/u"], [argv[i:i + 2] for i in range(len(argv) - 1)])
        self.assertNotIn("/var/www", argv)
        self.assertIn(["--ro-bind", "/wt", "/work"], [argv[i:i + 3] for i in range(len(argv) - 2)])
        cmd = codex.command("codex")
        self.assertEqual(cmd[cmd.index("--sandbox") + 1], "read-only")


@unittest.skipUnless(shutil.which("bwrap") and shutil.which("codex"), "needs bwrap and codex")
class CodexBwrapTest(unittest.TestCase):
    """Real bubblewrap with the real argv; the command is a shell probe instead of the model call."""

    def probe(self, script, wt):
        exe = codex.find_codex()
        argv = codex.bwrap_argv(wt, Path(__file__), exe) + ["/bin/sh", "-c", script]
        try:
            return subprocess.run(argv, capture_output=True, text=True, timeout=60)
        except OSError as e:
            self.skipTest(f"bwrap cannot start: {e}")

    def test_isolation(self):
        with tempfile.TemporaryDirectory() as d:
            wt = Path(d) / "wt"
            wt.mkdir()
            (wt / "f.txt").write_text("hi\n")
            before = codex.tree_digest(wt)
            home = str(Path.home())
            script = (f"test -d /var/www && echo SEES_VAR_WWW; ls -A {home} | tr '\\n' ' '; echo; "
                      "echo x > /work/new.txt 2>/dev/null && echo WROTE; cat /work/f.txt; "
                      "test -d /work/.git && echo HAS_GIT")
            p = self.probe(script, wt)
            if p.returncode != 0 and "bwrap:" in p.stderr:
                self.skipTest(f"bwrap unusable here: {p.stderr.strip()[:120]}")
            self.assertNotIn("SEES_VAR_WWW", p.stdout)
            self.assertNotIn("WROTE", p.stdout)
            self.assertIn("hi", p.stdout)
            visible = set(p.stdout.splitlines()[0].split())
            # .nvm only holds the read-only Node install that runs codex
            self.assertLessEqual(visible, {".codex", ".nvm"})
            self.assertEqual(codex.tree_digest(wt), before)


if __name__ == "__main__":
    unittest.main()
