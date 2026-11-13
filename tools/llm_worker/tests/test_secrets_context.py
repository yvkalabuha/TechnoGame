"""Data-leak channels (ADR-0015 §8, DEBT-0023): gitleaks gate, git-object context, strict key file."""

import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path

from helpers import NODES_CSV, TempRepo, a2_contract

from tools.llm_worker import config, gitleaks
from tools.llm_worker.context import ContextError, build_package, excluded
from tools.llm_worker.provider import KeyFileError, OpenAICompatProvider, load_strict_key
from tools.llm_worker.verifiers.b1_tests import error_lines

from test_ledger import FakeOpener

HAVE_GITLEAKS = gitleaks.find_gitleaks() is not None
# Built at run time so this file itself is not flagged by a scanner.
FAKE_TOKEN = "gh" + "p_" + "aB3dE5gH7jK9mN1pQ3sT5vX7zA9cD1fG3hJ5"


@unittest.skipUnless(HAVE_GITLEAKS, "gitleaks not installed")
class GitleaksTest(unittest.TestCase):
    def test_clean_text_passes(self):
        gitleaks.scan_text("int Add(int a, int b) => a + b;\n")

    def test_secret_is_found_and_not_echoed(self):
        with self.assertRaises(gitleaks.SecretDetected) as cm:
            gitleaks.scan_text(f"error: auth failed with token {FAKE_TOKEN}\n")
        self.assertNotIn(FAKE_TOKEN, str(cm.exception))
        self.assertNotIn("aB3dE5gH7j", str(cm.exception.findings))

    def test_scan_dir_finds_secret(self):
        with tempfile.TemporaryDirectory() as d:
            (Path(d) / "a.txt").write_text(f"x = '{FAKE_TOKEN}'\n")
            with self.assertRaises(gitleaks.SecretDetected):
                gitleaks.scan_dir(d)

    def _fingerprints(self, args, stdin=None):
        """Fingerprints of the fake secret, taken from a raw gitleaks run (to build a .gitleaksignore)."""
        with tempfile.TemporaryDirectory() as d:
            rep = Path(d) / "r.json"
            subprocess.run([gitleaks.find_gitleaks(), *args, "--no-banner", "--report-format", "json",
                            "--report-path", str(rep), "--exit-code", "0"], input=stdin, text=True,
                           capture_output=True, cwd=d)
            return [f["Fingerprint"] for f in json.loads(rep.read_text())]

    def test_gitleaks_allow_comment_does_not_hide_secrets(self):
        with self.assertRaises(gitleaks.SecretDetected):
            gitleaks.scan_text(f"# gitleaks:allow\nx = '{FAKE_TOKEN}'\n")
        with tempfile.TemporaryDirectory() as d:
            (Path(d) / "a.txt").write_text(f"x = '{FAKE_TOKEN}'  # gitleaks:allow\n")
            with self.assertRaises(gitleaks.SecretDetected):
                gitleaks.scan_dir(d)

    def test_ignore_file_in_scanned_dir_does_not_suppress(self):
        with tempfile.TemporaryDirectory() as d:
            (Path(d) / "a.txt").write_text(f"x = '{FAKE_TOKEN}'\n")
            fps = self._fingerprints(["dir", d])
            self.assertTrue(fps)
            (Path(d) / ".gitleaksignore").write_text("\n".join(fps) + "\n")
            with self.assertRaises(gitleaks.SecretDetected):
                gitleaks.scan_dir(d)

    def test_ignore_file_in_cwd_does_not_suppress(self):
        text = f"x = '{FAKE_TOKEN}'\n"
        with tempfile.TemporaryDirectory() as d:
            fps = self._fingerprints(["stdin"], stdin=text)
            self.assertTrue(fps)
            (Path(d) / ".gitleaksignore").write_text("\n".join(fps) + "\n")
            old = os.getcwd()
            os.chdir(d)
            try:
                with self.assertRaises(gitleaks.SecretDetected):
                    gitleaks.scan_text(text)
            finally:
                os.chdir(old)

    def test_tree_config_cannot_allow_secrets(self):
        with tempfile.TemporaryDirectory() as d:
            (Path(d) / "a.txt").write_text(f"x = '{FAKE_TOKEN}'\n")
            (Path(d) / ".gitleaks.toml").write_text('[allowlist]\npaths = [".*"]\n')
            with self.assertRaises(gitleaks.SecretDetected):
                gitleaks.scan_dir(d)


class GitleaksMissingTest(unittest.TestCase):
    def test_missing_binary_refuses(self):
        with self.assertRaises(gitleaks.GitleaksUnavailable):
            gitleaks.scan_text("x", binary="/nonexistent/gitleaks")

    def test_crash_is_not_a_clean_scan(self):
        with self.assertRaises(gitleaks.GitleaksUnavailable):
            gitleaks.scan_text("x", binary="/bin/false")


def paid_provider(opener):
    from tools.llm_worker import config as c
    p = OpenAICompatProvider(profile=c.PROFILES["deepseek-flash"], api_key="k", opener=opener, guard=_NoGuard())
    return p


class _NoGuard:
    def before_attempt(self, data, max_tokens):
        return None


@unittest.skipUnless(HAVE_GITLEAKS, "gitleaks not installed")
class ProviderScanTest(unittest.TestCase):
    def test_secret_in_context_stops_the_request(self):
        op = FakeOpener([])
        p = paid_provider(op)
        msgs = [{"role": "user", "content": f"=== FILE a.cs ===\nvar t = \"{FAKE_TOKEN}\";"}]
        with self.assertRaises(gitleaks.SecretDetected):
            p.complete(msgs, {"type": "object"}, "r", 10)
        self.assertEqual(len(op.requests), 0)

    def test_secret_in_error_lines_of_retry_prompt_stops_the_request(self):
        op = FakeOpener([])
        p = paid_provider(op)
        out = f"CS0103 error: name 'x' ... Authorization: token {FAKE_TOKEN}\nBuild FAILED"
        msgs = [{"role": "user", "content": "fix it"},
                {"role": "user", "content": "Check output:\n" + error_lines(out, "/w")}]
        with self.assertRaises(gitleaks.SecretDetected):
            p.complete(msgs, {"type": "object"}, "r", 10)
        self.assertEqual(len(op.requests), 0)

    def test_allow_comment_and_json_escaping_do_not_hide_a_secret_in_a_prompt(self):
        op = FakeOpener([])
        p = paid_provider(op)
        msgs = [{"role": "user", "content": "note # gitleaks:allow\n"},
                {"role": "user", "content": f"token = \"{FAKE_TOKEN}\"\n"}]
        with self.assertRaises(gitleaks.SecretDetected):
            p.complete(msgs, {"type": "object"}, "r", 10)
        self.assertEqual(len(op.requests), 0)

    def test_missing_gitleaks_stops_a_paid_request(self):
        from unittest import mock
        op = FakeOpener([])
        with mock.patch.object(gitleaks, "find_gitleaks", return_value=None):
            with self.assertRaises(gitleaks.GitleaksUnavailable):
                paid_provider(op).complete([{"role": "user", "content": "hi"}], {"type": "object"}, "r", 10)
        self.assertEqual(len(op.requests), 0)


class ContextGitTest(unittest.TestCase):
    def setUp(self):
        self.repo = TempRepo({"data/nodes.csv": NODES_CSV, "lab/results/r.csv": "SECRET\n", ".mcp.json": "{}\n"})
        self.lessons = self.repo.tmp / "lessons"
        self.lessons.mkdir()

    def tearDown(self):
        self.repo.cleanup()

    def test_untracked_file_is_not_packaged(self):
        (self.repo.root / "data/extra.csv").write_text("a,b\n1,2\n")
        c = a2_contract(repo=self.repo)
        c["context_files"] = [{"path": "data/extra.csv", "sha256": "0" * 64}]
        with self.assertRaises(ContextError):
            build_package(c, self.repo.root, self.lessons)

    def test_working_tree_edit_is_not_packaged(self):
        c = a2_contract(repo=self.repo)
        (self.repo.root / "data/nodes.csv").write_text("edited,locally\n")
        text, _ = build_package(c, self.repo.root, self.lessons)  # the committed bytes match the hash
        self.assertNotIn("edited,locally", text)
        self.assertIn(NODES_CSV.splitlines()[0], text)

    def test_commit_change_is_detected_by_hash(self):
        c = a2_contract(repo=self.repo)
        (self.repo.root / "data/nodes.csv").write_text("changed\n")
        self.repo.git("commit", "-qam", "change")
        with self.assertRaises(ContextError):
            build_package(c, self.repo.root, self.lessons)

    def test_symlink_is_refused(self):
        (self.repo.root / "link.csv").symlink_to("data/nodes.csv")
        self.repo.git("add", "link.csv")
        self.repo.git("commit", "-qm", "link")
        c = a2_contract(repo=self.repo)
        c["context_files"] = [{"path": "link.csv", "sha256": "0" * 64}]
        with self.assertRaises(ContextError):
            build_package(c, self.repo.root, self.lessons)

    def test_new_excludes(self):
        for path in (".mcp.json", "sub/.mcp.json", ".mcp.json.bak", "a/.git/config", ".git/config",
                     "lab/results/r.csv"):
            self.assertTrue(excluded(path), path)

    def test_excluded_tracked_file_is_refused(self):
        c = a2_contract(repo=self.repo)
        c["context_files"] = [{"path": ".mcp.json", "sha256": "0" * 64}]
        with self.assertRaises(ContextError):
            build_package(c, self.repo.root, self.lessons)


class StrictKeyTest(unittest.TestCase):
    def setUp(self):
        self._d = tempfile.TemporaryDirectory()
        self.f = Path(self._d.name) / "key"

    def tearDown(self):
        self._d.cleanup()

    def write(self, text, mode):
        if self.f.exists():
            self.f.chmod(0o600)
        self.f.write_text(text)
        self.f.chmod(mode)

    def test_good_key(self):
        self.write("  sk-123\n", 0o600)
        self.assertEqual(load_strict_key(str(self.f)), "sk-123")

    def test_bad_modes_empty_missing_symlink(self):
        for mode in (0o644, 0o640, 0o400, 0o660):
            self.write("sk-123\n", mode)
            with self.assertRaises(KeyFileError):
                load_strict_key(str(self.f))
        self.write("\n", 0o600)
        with self.assertRaises(KeyFileError):
            load_strict_key(str(self.f))
        with self.assertRaises(KeyFileError):
            load_strict_key(str(self.f) + ".none")
        self.write("sk-123\n", 0o600)
        link = Path(self._d.name) / "link"
        link.symlink_to(self.f)
        with self.assertRaises(KeyFileError):
            load_strict_key(str(link))

    def test_error_has_no_key(self):
        self.write("sk-SECRETVALUE\n", 0o644)
        with self.assertRaises(KeyFileError) as cm:
            load_strict_key(str(self.f))
        self.assertNotIn("SECRETVALUE", str(cm.exception))

    def test_deepseek_profiles_ignore_env_and_use_own_file(self):
        for name in ("deepseek-flash", "deepseek-v4-pro"):
            p = config.PROFILES[name]
            self.assertTrue(p.strict_key)
            self.assertTrue(p.key_file.endswith(".config/singularity/deepseek-api-key"))
        saved = dict(os.environ)
        os.environ.update(DEEPSEEK_API_KEY="from-env", DEEPSEEK_API_KEY_FILE=str(self.f),
                          LLM_WORKER_API_KEY="from-env")
        try:
            self.write("sk-123\n", 0o644)
            prof = config.Profile(**{**config.PROFILES["deepseek-flash"].__dict__, "key_file": str(self.f)})
            with self.assertRaises(KeyFileError):
                OpenAICompatProvider(profile=prof, guard=_NoGuard())
        finally:
            os.environ.clear()
            os.environ.update(saved)


if __name__ == "__main__":
    unittest.main()
