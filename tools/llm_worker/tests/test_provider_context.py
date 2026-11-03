import io
import json
import socket
import tempfile
import unittest
import urllib.error
from pathlib import Path

from helpers import NODES_CSV, TempRepo, a2_contract

from tools.llm_worker.context import ContextError, build_package, select_lessons
from tools.llm_worker.context import excluded
from tools.llm_worker.provider import (AuthError, OpenAICompatProvider, ProviderError, ProviderUnavailable,
                                       load_api_key)

OK_BODY = {"choices": [{"message": {"content": "{\"x\": 1}"}}],
           "usage": {"prompt_tokens": 5, "completion_tokens": 2}, "timings": {"predicted_per_second": 8}}


class _Resp(io.BytesIO):
    def __enter__(self):
        return self

    def __exit__(self, *a):
        return False


class FakeOpener:
    def __init__(self, script):
        self.script = list(script)
        self.requests = []

    def __call__(self, req, timeout):
        self.requests.append((req, timeout))
        item = self.script.pop(0)
        if isinstance(item, BaseException):
            raise item
        return _Resp(json.dumps(item).encode())


def provider(script, **kw):
    opener = FakeOpener(script)
    p = OpenAICompatProvider(url="http://fake/v1/chat/completions", api_key="SECRET-KEY", opener=opener,
                             sleep=lambda s: None, retries=2, **kw)
    return p, opener


class ProviderTest(unittest.TestCase):
    def test_request_body_and_headers(self):
        p, op = provider([OK_BODY])
        c = p.complete([{"role": "user", "content": "hi"}], {"type": "object"}, "result", 100, seed=2)
        self.assertEqual(c.content, "{\"x\": 1}")
        self.assertEqual(c.usage["prompt_tokens"], 5)
        req, timeout = op.requests[0]
        self.assertGreaterEqual(timeout, 3600)
        body = json.loads(req.data)
        self.assertEqual(body["chat_template_kwargs"], {"enable_thinking": False})
        self.assertEqual(body["response_format"]["type"], "json_schema")
        self.assertEqual(body["response_format"]["json_schema"]["name"], "result")
        self.assertEqual(body["seed"], 2)
        self.assertEqual(req.get_header("Authorization"), "Bearer SECRET-KEY")
        self.assertNotIn("SECRET", repr(p))

    def test_timeout_and_disconnect_are_retried(self):
        p, op = provider([socket.timeout("t"), ConnectionResetError("reset"), OK_BODY])
        self.assertEqual(p.complete([], {}, "r", 10).content, "{\"x\": 1}")
        self.assertEqual(len(op.requests), 3)

    def test_outage_raises_unavailable(self):
        err = urllib.error.HTTPError("u", 503, "busy", {}, None)
        p, _ = provider([urllib.error.URLError("down"), err, socket.timeout("t")])
        with self.assertRaises(ProviderUnavailable) as cm:
            p.complete([], {}, "r", 10)
        self.assertNotIn("SECRET", str(cm.exception))

    def test_client_error_not_retried(self):
        p, op = provider([urllib.error.HTTPError("u", 400, "no", {}, None)])
        with self.assertRaises(ProviderError):
            p.complete([], {}, "r", 10)
        self.assertEqual(len(op.requests), 1)

    def test_auth_error_not_retried(self):
        for code in (401, 403):
            p, op = provider([urllib.error.HTTPError("u", code, "no", {}, None)])
            with self.assertRaises(AuthError) as cm:
                p.complete([], {}, "r", 10)
            self.assertEqual(len(op.requests), 1)
            self.assertNotIn("SECRET", str(cm.exception))

    def test_api_key_sources(self):
        with tempfile.TemporaryDirectory() as d:
            f = Path(d) / "key"
            f.write_text("  file-key\n")
            self.assertEqual(load_api_key({"LLM_WORKER_API_KEY": "env-key", "LLM_WORKER_API_KEY_FILE": str(f)}), "env-key")
            self.assertEqual(load_api_key({"LLM_WORKER_API_KEY_FILE": str(f)}), "file-key")
            self.assertEqual(load_api_key({"LLM_WORKER_API_KEY_FILE": str(Path(d) / "none")}), "")


class ContextTest(unittest.TestCase):
    def setUp(self):
        self.repo = TempRepo({"data/nodes.csv": NODES_CSV, ".env": "KEY=1\n"})
        self.lessons = self.repo.tmp / "lessons"
        self.lessons.mkdir()
        (self.lessons / "a2-false.md").write_text("applies_to: A2\nDo not report clean rows.\n")
        (self.lessons / "b1-only.md").write_text("applies_to: B1\nUse raw values.\n")
        (self.lessons / "README.md").write_text("# no header\n")

    def tearDown(self):
        self.repo.cleanup()

    def test_package_contains_only_context_files_and_matching_lessons(self):
        text, manifest = build_package(a2_contract(repo=self.repo), self.repo.root, self.lessons)
        self.assertIn("=== FILE data/nodes.csv", text)
        self.assertIn("Do not report clean rows.", text)
        self.assertNotIn("Use raw values.", text)
        self.assertNotIn("KEY=1", text)
        self.assertEqual(manifest[-1], {"lesson": "a2-false.md"})
        self.assertEqual([n for n, _ in select_lessons("B1", self.lessons)], ["b1-only.md"])

    def test_secret_like_paths_excluded(self):
        for path in (".ssh/id_rsa", "home/id_ed25519.pub", "certs/a.p12", "b.pfx", "config/db_secret.json",
                     "credentials.json", "x/credentials", "tools/llm_worker/reports/2026-11-03.md",
                     "tools/llm_worker/worktrees/T-c1/src/a.cs", ".env", "a/b.key"):
            self.assertTrue(excluded(path), path)
        self.assertFalse(excluded("src/Singularity.Kernel/Fx6.cs"))

    def test_rejections(self):
        c = a2_contract(repo=self.repo)
        c["context_files"] = [{"path": ".env", "sha256": "0" * 64}]
        with self.assertRaises(ContextError):
            build_package(c, self.repo.root, self.lessons)
        c = a2_contract(repo=self.repo)
        c["budget"] = dict(c["budget"], max_context_tokens=10)
        with self.assertRaises(ContextError):
            build_package(c, self.repo.root, self.lessons)
        c = a2_contract(repo=self.repo)
        (self.repo.root / "data/nodes.csv").write_text("changed\n")
        with self.assertRaises(ContextError):
            build_package(c, self.repo.root, self.lessons)


if __name__ == "__main__":
    unittest.main()
