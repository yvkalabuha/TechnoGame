"""llmlimits client, consumer states, review chain skip and the capacity.py line (TASK-0211). The source is always pinned."""
import json
import subprocess
import sys
import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path

from tools.development import llm_limits as ll
from tools.llm_worker import limitstate, review

ROOT = Path(__file__).resolve().parents[3]
NOW = datetime(2026, 11, 26, 19, 40, tzinfo=timezone.utc)
CMAP = ll.load_map()


def win(name, head, reset=None, enough=True, used=None, value=None):
    return {"window": name, "ok": True, "used_pct": used, "value": value, "resets_at": reset, "sampled_at": "2026-11-26T19:30:00Z",
            "forecast": {"headroom": head, "enough_until_reset": enough}}


def payload(**provs):
    return {"providers": [{"provider": p, "ok": True, "stale": False, "windows": w} for p, w in provs.items()]}


def state(p, consumer):
    return ll.consumer_state(p, consumer, CMAP, NOW)


class StatesTest(unittest.TestCase):
    def test_map_covers_consumers_and_splits_gemini_groups(self):
        self.assertEqual(list(CMAP), ["claude", "codex", "agy-sonnet", "gemini-ui", "deepseek"])
        self.assertEqual(CMAP["agy-sonnet"]["windows"][0], "claude-and-gpt-models:5h")
        self.assertEqual(CMAP["gemini-ui"]["windows"][0], "gemini-models:5h")
        self.assertEqual(CMAP["deepseek"]["usd_low"], 1.0)

    def test_out_takes_latest_reset_of_exhausted_windows(self):
        p = payload(codex=[win("5h", 0, "2026-11-27T01:00:00Z"), win("7d", 0, "2026-11-28T08:04:03Z")])
        s = state(p, "codex")
        self.assertEqual((s["state"], s["resets_at"]), ("out", "2026-11-28T08:04:03+00:00"))

    def test_window_already_reset_since_sample_is_ignored(self):
        p = payload(codex=[win("5h", 0, "2026-11-26T19:21:48Z"), win("7d", 50, "2026-11-28T08:04:03Z")])
        self.assertEqual(state(p, "codex")["state"], "ok")

    def test_short_low_ok_by_forecast_not_advice(self):
        p = payload(claude=[win("5h", 95, "2026-11-26T23:30:00Z"), win("7d", 16, "2026-11-29T04:00:00Z", enough=False)])
        p["advice"] = {"recommendation": "proceed"}
        self.assertEqual(state(p, "claude")["state"], "short")
        self.assertEqual(state(payload(claude=[win("7d", 9, "2026-11-29T04:00:00Z")]), "claude")["state"], "low")
        self.assertEqual(state(payload(claude=[win("7d", 60, "2026-11-29T04:00:00Z")]), "claude")["state"], "ok")

    def test_gemini_groups_are_independent(self):
        p = payload(gemini=[win("claude-and-gpt-models:7d", 0, "2026-12-02T02:00:00Z"), win("gemini-models:7d", 98, "2026-12-03T13:00:00Z")])
        self.assertEqual(state(p, "agy-sonnet")["state"], "out")
        self.assertEqual(state(p, "gemini-ui")["state"], "ok")

    def test_deepseek_uses_balance_not_ledger_cap(self):
        cap = win("ledger_cap", 0, used=100)
        self.assertEqual(state(payload(deepseek=[win("balance", None, value=4.31), cap]), "deepseek")["state"], "ok")
        self.assertEqual(state(payload(deepseek=[win("balance", None, value=0.5)]), "deepseek")["state"], "low")
        self.assertEqual(state(payload(deepseek=[win("balance", None, value=0.0)]), "deepseek")["state"], "out")

    def test_stale_missing_or_old_sample_is_unknown(self):
        p = payload(codex=[win("7d", 0, "2026-11-28T08:04:03Z")])
        p["providers"][0]["stale"] = True
        self.assertEqual(state(p, "codex")["state"], "unknown")
        self.assertEqual(state(None, "codex")["state"], "unknown")
        self.assertEqual(state(payload(), "codex")["state"], "unknown")
        old = win("7d", 0, "2026-11-28T08:04:03Z")
        old["sampled_at"] = "2026-11-26T10:00:00Z"
        self.assertEqual(state(payload(codex=[old]), "codex")["state"], "unknown")


class SourceTest(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.cache = self.tmp / "cache.json"

    def test_off_and_unreachable_give_no_data_and_unknown(self):
        self.assertIsNone(ll.status(NOW, "off", self.cache))
        self.assertIsNone(ll.status(NOW, "http://127.0.0.1:9/x", self.cache))
        self.assertIsNone(ll.status(NOW, "http://example.com/status", self.cache))  # not loopback: no request
        self.assertEqual(ll.check("codex", NOW, "off", self.cache)["state"], "unknown")

    def test_file_source_and_line(self):
        f = self.tmp / "s.json"
        f.write_text(json.dumps(payload(codex=[win("7d", 0, "2026-11-28T08:04:03Z")])))
        st = ll.summary(NOW, "file:" + str(f), self.cache)
        self.assertEqual(st["codex"]["state"], "out")
        self.assertIn("codex out until 11-28 08:04Z", ll.line(st))
        self.assertIn("claude unknown", ll.line(st))

    def test_cache_ttl(self):
        calls = []
        orig = ll._fetch
        ll._fetch = lambda s: calls.append(s) or {"providers": []}
        try:
            ll.status(NOW, "http://127.0.0.1:8790/x", self.cache)
            ll.status(NOW + timedelta(seconds=30), "http://127.0.0.1:8790/x", self.cache)
            ll.status(NOW + timedelta(seconds=90), "http://127.0.0.1:8790/x", self.cache)
        finally:
            ll._fetch = orig
        self.assertEqual(len(calls), 2)


def out_codex(consumer):
    if consumer == "codex":
        return {"state": "out", "resets_at": "2026-11-28T08:04:03+00:00"}
    return {"state": "ok", "resets_at": None}


class ChainTest(unittest.TestCase):
    def setUp(self):
        self.sd = Path(tempfile.mkdtemp())

    def test_codex_out_skips_to_agy_with_reason_and_time_and_remembers_it(self):
        fb = []
        chain, reset = review._service_skip(["codex", "deepseek"], out_codex, NOW, fb, self.sd, True)
        self.assertEqual(chain, ["agy-sonnet", "deepseek"])
        self.assertEqual(fb, [{"from": "codex", "reason": "codex_limit_service", "codex_reset_at": "2026-11-28T08:04:03+00:00"}])
        self.assertIsNotNone(limitstate.limited_until(NOW, self.sd))
        self.assertEqual(reset, datetime.fromisoformat("2026-11-28T08:04:03+00:00"))

    def test_explicit_provider_does_not_write_shared_state(self):
        chain, _ = review._service_skip(["codex", "deepseek"], out_codex, NOW, [], self.sd, False)
        self.assertEqual(chain, ["deepseek"])
        self.assertIsNone(limitstate.limited_until(NOW, self.sd))

    def test_last_provider_is_never_dropped_and_no_limits_is_noop(self):
        everything_out = lambda c: {"state": "out", "resets_at": None}
        fb = []
        chain, _ = review._service_skip(["agy-sonnet", "deepseek"], everything_out, NOW, fb, self.sd, False)
        self.assertEqual(chain, ["deepseek"])
        self.assertEqual(review._service_skip(["codex", "deepseek"], None, NOW, [], self.sd, True), (["codex", "deepseek"], None))

    def test_run_review_auto_goes_to_agy_when_service_says_codex_out(self):
        fb = []
        chain, _ = review._chain("auto", self.sd, NOW, fb)
        chain, svc = review._service_skip(chain, out_codex, NOW, fb, self.sd, True)
        self.assertEqual(chain, ["agy-sonnet", "deepseek"])
        self.assertTrue(svc)


class CapacityTest(unittest.TestCase):
    def run_cap(self, source, *args):
        env = {"SINGULARITY_LLM_LIMITS": source, "PATH": "/usr/bin:/bin", "HOME": tempfile.mkdtemp()}
        return subprocess.run([sys.executable, str(ROOT / "tools/development/capacity.py"), *args], capture_output=True, text=True,
                              env=env, timeout=20, cwd=ROOT)

    def test_off_prints_unknown_line_and_json_field(self):
        r = self.run_cap("off")
        self.assertIn("limits: claude unknown", r.stdout)
        data = json.loads(self.run_cap("off", "--json").stdout)
        self.assertEqual(data["limits"]["codex"]["state"], "unknown")
        self.assertIn("agent_slots_free", data)

    def test_file_source_shows_out(self):
        f = Path(tempfile.mkdtemp()) / "s.json"
        f.write_text(json.dumps(payload(codex=[win("7d", 0, "2099-01-01T00:00:00Z")])))
        self.assertIn("codex out until", self.run_cap("file:" + str(f)).stdout)


if __name__ == "__main__":
    unittest.main()
