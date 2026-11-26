"""research (#577): fake provider, fake pages, stubbed gitleaks. No network, no real gitleaks."""

import json
import sys
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

    def test_fabricated_quote_becomes_not_found_and_is_dropped(self):
        _, (path, rec, md) = self.run_it([answer(quote="invented in 1962 at the Battelle lab", value="1962")])
        it = rec["items"][0]
        self.assertEqual(it["verdict"], "not_found")
        self.assertFalse(it["quote_verified"])
        self.assertIn("quote_not_in_page", it["note"])
        blob = path.read_text(encoding="utf-8") + md
        self.assertNotIn("1962", blob)
        self.assertEqual((it["quote"], it["value"], it["source"]), ("", "", None))

    def test_contradiction_with_the_submitted_year_is_rejected(self):
        # the quote and the value both say 1955, the claim says 1955: that is no contradiction
        _, (_, rec, _) = self.run_it([answer("contradicted")])
        self.assertEqual(rec["items"][0]["verdict"], "not_found")
        self.assertIn("contradiction_gives_the_submitted_value", rec["items"][0]["note"])

    def test_confirmation_must_quote_the_submitted_year(self):
        _, (_, rec, _) = self.run_it([answer(quote="It densifies castings", value="")])
        self.assertEqual(rec["items"][0]["verdict"], "not_found")  # quote exists but lacks 1955
        _, (_, rec, _) = self.run_it([answer("contradicted", quote="invented in 1955 at the Battelle Memorial Institute", value="1955")])
        self.assertIn("contradiction_gives", rec["items"][0]["note"])

    def test_not_found_publishes_no_evidence_and_review_flag(self):
        _, (path, rec, md) = self.run_it([answer("not_found", quote="invented in 1955 at the Battelle", value="1955")])
        it = rec["items"][0]
        self.assertEqual((it["quote"], it["value"], it["source"], it["source_url"]), ("", "", None, None))
        self.assertTrue(rec["review_required"] and it["review_required"])
        self.assertIn("| проверить |", md)
        self.assertTrue(md.splitlines()[2].endswith("| да |"))

    def test_short_quote_allowed_on_short_page_with_value(self):
        text = "Maraging steel appeared in 1959."
        self.assertTrue(research.quote_in_text("appeared in 1959", text, "1959"))
        self.assertFalse(research.quote_in_text("appeared in 1959", text, ""))
        self.assertFalse(research.quote_in_text("appeared in 1959", text + " " + "x" * 2000, "1959"))
        self.assertFalse(research.quote_in_text("in 1959", text, "1959"))  # 2 words

    def test_page_failure_keeps_earlier_results(self):
        self.inp.write_text(YAML.replace("https://example.org/cc", "https://example.org/hip"), encoding="utf-8")
        runs = self.tmp / "runs"
        calls = []

        def boom(text):
            calls.append(1)
            if len(calls) > 1:
                raise gitleaks.GitleaksUnavailable("gone")  # becomes a ReviewError in load_page

        self.scan.side_effect = boom
        with self.assertRaises(research.ReviewError):
            research.run_research(self.inp, runs_dir=runs, make_provider=lambda: FakeProvider([answer()]),
                                  fetch=self.fetch, cache_dir=self.tmp / "cache")
        rec = json.loads(next(runs.glob("research-*.json")).read_text(encoding="utf-8"))
        self.assertEqual(([i["id"] for i in rec["items"]], rec["complete"]), (["hip"], False))

    def test_year_must_match_whole_token(self):
        text = "Hot isostatic pressing was invented in 1955 at the Battelle Memorial Institute."
        self.assertFalse(research.quote_in_text("pressing was invented in 1955 at the Battelle", text, "955"))

    def test_question_needs_a_grounded_value(self):
        self.inp.write_text("items:\n  - id: q\n    question: When?\n    field: year\n    urls: [https://example.org/hip]\n",
                            encoding="utf-8")
        _, (_, rec, _) = self.run_it([answer(value="")])
        self.assertEqual(rec["items"][0]["verdict"], "not_found")
        _, (_, rec, _) = self.run_it([answer(value="955")])
        self.assertEqual(rec["items"][0]["verdict"], "not_found")
        _, (_, rec, _) = self.run_it([answer(value="1955")])
        self.assertEqual((rec["items"][0]["verdict"], rec["items"][0]["value"]), ("confirmed", "1955"))

    def test_later_failure_keeps_earlier_paid_results(self):
        self.inp.write_text(YAML.replace("https://example.org/cc", "https://example.org/hip"), encoding="utf-8")
        runs = self.tmp / "runs"
        with self.assertRaises(RuntimeError):
            research.run_research(self.inp, runs_dir=runs, make_provider=lambda: FakeProvider([answer(), RuntimeError("outage")]),
                                  fetch=self.fetch, cache_dir=self.tmp / "cache")
        rec = json.loads(next(runs.glob("research-*.json")).read_text(encoding="utf-8"))
        self.assertEqual([i["id"] for i in rec["items"]], ["hip"])
        self.assertFalse(rec["complete"])
        self.assertIn("outage", rec["error"])

    def test_long_quote_refused(self):
        words = " ".join(["Hot"] * 31)
        self.assertFalse(research.quote_in_text(words, words))
        self.assertTrue(research.quote_in_text("HOT  isostatic\npressing was invented in", "Hot isostatic pressing was invented in 1955"))

    def test_short_quote_and_value_rules(self):
        text = "Hot isostatic pressing was invented in 1955 at the Battelle Memorial Institute."
        self.assertFalse(research.quote_in_text("1955", text))  # too short to prove anything
        self.assertFalse(research.quote_in_text("was invented in 1955", text))  # 4 words
        self.assertTrue(research.quote_in_text("pressing was invented in 1955 at the Battelle", text, "1955"))
        self.assertFalse(research.quote_in_text("pressing was invented in 1955 at the Battelle", text, "1962"))

    def test_typographic_marks_normalised(self):
        text = "A well\N{HYPHEN}known \N{LEFT DOUBLE QUOTATION MARK}first\N{RIGHT DOUBLE QUOTATION MARK} design from the co\N{SOFT HYPHEN}mpany was made in 1955 and later"
        self.assertTrue(research.quote_in_text('a well-known "first" design from the company was made', text))

    def test_contradicted_keeps_corrected_value(self):
        self.inp.write_text(YAML.replace("year: 1955", "year: 1962").replace("in 1955.", "in 1962."), encoding="utf-8")
        _, (_, rec, _) = self.run_it([answer("contradicted", "invented in 1955 at the Battelle", "1955")])
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

    def curl_out(self, body=b"body", code=b"200", redirect=b"", remote=b"93.184.216.34"):
        out = b"\n".join([body, code, redirect, remote])
        return subprocess.CompletedProcess([], 0, stdout=out, stderr=b"")

    def pinned(self, url):
        return ("example.org", 443, "93.184.216.34")

    def test_curl_command_limits(self):
        with mock.patch.object(research, "check_url", self.pinned), \
                mock.patch.object(research, "_run_capped", return_value=self.curl_out()) as run:
            self.assertEqual(research.curl_fetch("https://example.org/"), b"body")
        cmd = run.call_args.args[0]
        for flag in ("-q", "--max-filesize", "--proto", "=http,https", "--max-time", "--noproxy", "example.org:443:93.184.216.34"):
            self.assertIn(flag, cmd)
        self.assertNotIn("-L", cmd)  # redirects are followed by us, hop by hop
        self.assertNotIn("-b", cmd)  # no cookie jar
        self.assertEqual(cmd[-2:], ["--", "https://example.org/"])

    def test_curl_refuses_unchecked_or_missing_remote_address(self):
        for remote in (b"10.0.0.5", b"", b"garbage"):
            with mock.patch.object(research, "check_url", self.pinned), \
                    mock.patch.object(research, "_run_capped", return_value=self.curl_out(remote=remote)):
                with self.assertRaises(research.FetchError, msg=remote):
                    research.curl_fetch("https://example.org/")

    def test_redirect_hops_are_each_checked(self):
        seen = []

        def check(url):
            seen.append(url)
            if "internal" in url:
                raise research.FetchError("refused non-public address")
            return self.pinned(url)

        hop = self.curl_out(code=b"302", redirect=b"https://internal.example/x")
        with mock.patch.object(research, "check_url", check), mock.patch.object(research, "_run_capped", return_value=hop):
            with self.assertRaises(research.FetchError):
                research.curl_fetch("https://example.org/")
        self.assertEqual(seen, ["https://example.org/", "https://internal.example/x"])

    def test_redirect_followed_and_limited(self):
        runs = [self.curl_out(code=b"301", redirect=b"https://example.org/b"), self.curl_out(body=b"final")]
        with mock.patch.object(research, "check_url", self.pinned), mock.patch.object(research, "_run_capped", side_effect=runs):
            self.assertEqual(research.curl_fetch("https://example.org/a"), b"final")
        loop = self.curl_out(code=b"302", redirect=b"https://example.org/a")
        with mock.patch.object(research, "check_url", self.pinned), mock.patch.object(research, "_run_capped", return_value=loop):
            with self.assertRaises(research.FetchError):
                research.curl_fetch("https://example.org/a")

    def test_output_cap_kills_unannounced_huge_body(self):
        cmd = [sys.executable, "-c", "import sys; sys.stdout.buffer.write(b'x' * 5_000_000)"]
        with self.assertRaises(research.FetchError):
            research._run_capped(cmd, 20, 100_000)
        done = research._run_capped([sys.executable, "-c", "print('ok')"], 20, 100_000)
        self.assertEqual((done.returncode, done.stdout), (0, b"ok\n"))

    def test_http_error_status(self):
        with mock.patch.object(research, "check_url", self.pinned), \
                mock.patch.object(research, "_run_capped", return_value=self.curl_out(code=b"404")):
            with self.assertRaises(research.FetchError):
                research.curl_fetch("https://example.org/")


class InputFile(Base):
    def test_every_example_loads(self):
        examples = sorted((Path(research.__file__).parent / "examples").glob("*.yaml"))
        self.assertTrue(examples)
        for f in examples:
            for it in research.load_input(f)["items"]:
                self.assertTrue(all(u.startswith("http") and " " not in u for u in it["urls"]), (f, it["id"]))


    def test_bad_input(self):
        for text in ("items: []", "items:\n - id: a\n   urls: [x]", "items:\n - {id: a, claim: c}"):
            self.inp.write_text(text, encoding="utf-8")
            with self.assertRaises(research.ReviewError):
                research.load_input(self.inp)


if __name__ == "__main__":
    unittest.main()
