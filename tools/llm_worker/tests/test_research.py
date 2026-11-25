"""research (#577): fake provider, fake pages, stubbed gitleaks. No network, no real gitleaks."""

import json
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from helpers import FakeProvider

from tools.llm_worker import gitleaks, research

PAGE = ("<html><head><title>x</title><script>var secret=1;</script></head><body><p>Hot isostatic pressing was "
        "invented in 1955 at the Battelle Memorial Institute.</p><p>It densifies castings.</p></body></html>")
YAML = """items:
  - id: hip
    claim: Hot isostatic pressing was invented in 1955.
    year: 1955
    urls: [https://example.org/hip]
  - id: q1
    question: Year of the first carbon-carbon composite
    field: year
    urls: [https://example.org/cc]
"""


def answer(verdict="confirmed", quote="invented in 1955 at the Battelle Memorial Institute", value="1955", source=0):
    return {"verdict": verdict, "source": source, "quote": quote, "value": value, "note": "n"}


class Base(unittest.TestCase):
    def setUp(self):
        t = tempfile.TemporaryDirectory()
        self.addCleanup(t.cleanup)
        self.tmp = Path(t.name)
        self.inp = self.tmp / "in.yaml"
        self.inp.write_text(YAML, encoding="utf-8")
        self.fetched = []
        p = mock.patch.object(gitleaks, "scan_text")
        self.scan = p.start()
        self.addCleanup(p.stop)

    def fetch(self, url):
        self.fetched.append(url)
        if url.endswith("/hip"):
            return PAGE.encode()
        raise research.FetchError("404")

    def run_it(self, script, **kw):
        prov = FakeProvider(script)
        res = research.run_research(self.inp, runs_dir=self.tmp / "runs", make_provider=lambda: prov, fetch=self.fetch,
                                    cache_dir=self.tmp / "cache", **kw)
        return prov, res


class Verdicts(Base):
    def test_confirmed_with_real_quote(self):
        prov, (path, rec, md) = self.run_it([answer()])
        it = rec["items"][0]
        self.assertEqual((it["verdict"], it["value"], it["quote_verified"]), ("confirmed", "1955", True))
        self.assertEqual(it["source_url"], "https://example.org/hip")
        self.assertIn("| hip | confirmed | 1955 |", md)
        self.assertEqual(len(prov.calls), 1)  # q1 has no page: no model call

    def test_fabricated_quote_becomes_not_found(self):
        _, (_, rec, _) = self.run_it([answer(quote="invented in 1962 at Battelle")])
        it = rec["items"][0]
        self.assertEqual(it["verdict"], "not_found")
        self.assertFalse(it["quote_verified"])
        self.assertIn("quote_not_in_page", it["note"])

    def test_long_quote_refused(self):
        words = " ".join(["Hot"] * 31)
        self.assertFalse(research.quote_in_text(words, words))
        self.assertTrue(research.quote_in_text("HOT  isostatic\npressing", "Hot isostatic pressing was"))

    def test_contradicted_keeps_corrected_value(self):
        _, (_, rec, _) = self.run_it([answer("contradicted", "invented in 1955 at the Battelle", "1955-ish")])
        self.assertEqual(rec["items"][0]["verdict"], "contradicted")

    def test_schema_invalid_answer_is_not_found(self):
        _, (_, rec, _) = self.run_it(["not json"])
        self.assertEqual(rec["items"][0]["verdict"], "not_found")
        self.assertIn("schema_invalid", rec["items"][0]["note"])
        _, (_, rec, _) = self.run_it([answer(source=5)])
        self.assertIn("schema_invalid", rec["items"][0]["note"])

    def test_unreachable_page_skips_model(self):
        _, (_, rec, _) = self.run_it([answer()])
        q = rec["items"][1]
        self.assertEqual((q["verdict"], q["note"]), ("not_found", "no_page_available"))
        self.assertIn("404", q["pages"][0]["error"])


class Safety(Base):
    def test_secret_in_page_drops_it(self):
        self.scan.side_effect = gitleaks.SecretDetected([{"rule": "aws", "line": 3}])
        prov, (_, rec, _) = self.run_it([])
        self.assertEqual(prov.calls, [])
        self.assertIn("secret_detected", rec["items"][0]["pages"][0]["error"])

    def test_gitleaks_missing_refuses_run(self):
        self.scan.side_effect = gitleaks.GitleaksUnavailable("none")
        with self.assertRaises(research.ReviewError):
            self.run_it([])

    def test_record_has_no_page_text(self):
        _, (path, rec, _) = self.run_it([answer()])
        blob = path.read_text(encoding="utf-8") + path.with_suffix(".md").read_text(encoding="utf-8")
        self.assertNotIn("densifies castings", blob)
        self.assertNotIn("var secret", blob)
        self.assertEqual(json.loads(path.read_text(encoding="utf-8"))["kind"], "research")

    def test_script_text_not_given_to_model(self):
        prov, _ = self.run_it([answer()])
        self.assertNotIn("var secret", prov.calls[0]["messages"][1]["content"])
        self.assertIn("densifies castings", prov.calls[0]["messages"][1]["content"])

    def test_cache_prevents_second_fetch(self):
        self.run_it([answer()])
        n = len(self.fetched)
        self.run_it([answer()])
        self.assertEqual(self.fetched.count("https://example.org/hip"), 1)
        self.assertGreater(n, 0)

    def test_dry_run_sends_nothing(self):
        prov, (path, rec, _) = self.run_it([], dry_run=True)
        self.assertEqual((prov.calls, path), ([], None))

    def test_url_rules(self):
        for bad in ("file:///etc/passwd", "ftp://example.org/x", "https://user:pw@example.org/", "http://127.0.0.1/x"):
            with self.assertRaises(research.FetchError, msg=bad):
                research.check_url(bad)

    def test_curl_command_limits(self):
        done = subprocess.CompletedProcess([], 0, stdout=b"body\n93.184.216.34", stderr=b"")
        with mock.patch.object(research, "check_url"), mock.patch.object(research.subprocess, "run", return_value=done) as run:
            self.assertEqual(research.curl_fetch("https://example.org/"), b"body")
        cmd = run.call_args.args[0]
        for flag in ("-q", "--max-filesize", "--proto", "=http,https", "--max-time"):
            self.assertIn(flag, cmd)
        self.assertNotIn("-b", cmd)  # no cookie jar
        self.assertEqual(cmd[-2:], ["--", "https://example.org/"])

    def test_curl_refuses_private_final_address(self):
        done = subprocess.CompletedProcess([], 0, stdout=b"body\n10.0.0.5", stderr=b"")
        with mock.patch.object(research, "check_url"), mock.patch.object(research.subprocess, "run", return_value=done):
            with self.assertRaises(research.FetchError):
                research.curl_fetch("https://example.org/")


class InputFile(Base):
    def test_bad_input(self):
        for text in ("items: []", "items:\n - id: a\n   urls: [x]", "items:\n - {id: a, claim: c}"):
            self.inp.write_text(text, encoding="utf-8")
            with self.assertRaises(research.ReviewError):
                research.load_input(self.inp)


if __name__ == "__main__":
    unittest.main()
