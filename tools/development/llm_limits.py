#!/usr/bin/env python3
"""Model limits from the local llmlimits service (#605): advisory only, fail-open.

  llm_limits.py                 one line per consumer (claude, codex, agy-sonnet, gemini-ui, deepseek)
  llm_limits.py --json          the same as JSON
  llm_limits.py check CONSUMER  prints the state; exit 0 = go on (ok/low/short/unknown), 3 = out

States: out (headroom <= 0, or DeepSeek balance <= 0), low (headroom <= 10 %, or balance under usd_low),
short (forecast: runs out before the reset), ok, unknown (no service, stale or missing data).
Reads /api/v1/status (headroom, forecast.enough_until_reset), not advice.recommendation. Nothing here changes agent
slots or models; callers only warn or skip an exhausted provider. Source: SINGULARITY_LLM_LIMITS = off | file:PATH |
loopback URL (default http://127.0.0.1:8790/api/v1/status). Design: docs/architecture/platform-llmlimits.md.
"""
import json
import os
import re
import sys
import tempfile
import time
import urllib.request
from datetime import datetime, timedelta, timezone
from pathlib import Path
from urllib.parse import urlparse

ENV = "SINGULARITY_LLM_LIMITS"
DEFAULT_URL = "http://127.0.0.1:8790/api/v1/status"
TIMEOUT_S = 1.5
TTL_S = 60
CACHE_FILE = Path.home() / ".cache" / "singularity" / "llm-limits.json"
MAP_FILE = Path(__file__).with_name("llm_limits.yaml")
LOW_PCT = 10.0
MAX_AGE = timedelta(hours=3)  # a sample older than this is not trusted (the service polls every 5-30 minutes)
LOOPBACK = {"127.0.0.1", "localhost", "::1"}
OUT, LOW, SHORT, OK, UNKNOWN = "out", "low", "short", "ok", "unknown"


def load_map(path=MAP_FILE):
    """{consumer: {provider, windows, usd_low?}} from the flow-style lines of the map file."""
    out = {}
    for line in Path(path).read_text(encoding="utf-8").splitlines():
        m = re.match(r"^([\w-]+):\s*\{(.*)\}\s*$", line)
        if not m:
            continue
        body = m.group(2)
        prov = re.search(r"provider:\s*([\w-]+)", body)
        wins = re.search(r"windows:\s*\[([^\]]*)\]", body)
        usd = re.search(r"usd_low:\s*([\d.]+)", body)
        if prov and wins:
            out[m.group(1)] = {"provider": prov.group(1), "windows": [w.strip() for w in wins.group(1).split(",") if w.strip()]}
            if usd:
                out[m.group(1)]["usd_low"] = float(usd.group(1))
    return out


def _parse_time(text):
    try:
        at = datetime.fromisoformat(str(text).replace("Z", "+00:00"))
    except ValueError:
        return None
    return at if at.tzinfo else at.replace(tzinfo=timezone.utc)


def _fetch(source):
    """Status payload (dict) or None. Never raises: no service means no data."""
    try:
        if source.startswith("file:"):
            return json.loads(Path(source[5:]).read_text(encoding="utf-8"))
        if urlparse(source).hostname not in LOOPBACK:
            return None  # the service is local; never send a request elsewhere
        with urllib.request.urlopen(source, timeout=TIMEOUT_S) as r:  # noqa: S310 - loopback only, checked above
            return json.loads(r.read(2_000_000))
    except (OSError, ValueError):
        return None


def status(now=None, source=None, cache_file=CACHE_FILE, ttl=TTL_S):
    """Status payload or None. The cache (ttl seconds) spares the service; a file: source is never cached."""
    source = source or os.environ.get(ENV) or DEFAULT_URL
    if source == "off":
        return None
    if source.startswith("file:"):
        return _fetch(source)
    now_ts = (now or datetime.now(timezone.utc)).timestamp()
    try:
        cached = json.loads(Path(cache_file).read_text(encoding="utf-8"))
        if cached["source"] == source and 0 <= now_ts - cached["fetched_at"] < ttl:
            return cached["payload"]
    except (OSError, ValueError, KeyError, TypeError):
        pass
    payload = _fetch(source)
    if payload is not None:
        _store(cache_file, {"source": source, "fetched_at": now_ts, "payload": payload})
    return payload


def _store(path, data):
    path = Path(path)
    try:
        path.parent.mkdir(parents=True, exist_ok=True)
        fd, tmp = tempfile.mkstemp(dir=path.parent, prefix=path.name + ".")
        with os.fdopen(fd, "w", encoding="utf-8") as f:
            json.dump(data, f)
        os.replace(tmp, path)
    except OSError:
        pass  # the cache is a convenience


def _headroom(w):
    fc = w.get("forecast") or {}
    if fc.get("headroom") is not None:
        return float(fc["headroom"])
    return None if w.get("used_pct") is None else 100.0 - float(w["used_pct"])


def consumer_state(payload, consumer, cmap=None, now=None):
    """{consumer, state, headroom, resets_at, reason}; resets_at is an ISO string or None."""
    now = now or datetime.now(timezone.utc)
    spec = (cmap or load_map()).get(consumer)
    res = {"consumer": consumer, "state": UNKNOWN, "headroom": None, "resets_at": None, "reason": "no data"}
    if not payload or not spec:
        return res
    prov = next((p for p in payload.get("providers", []) if p.get("provider") == spec["provider"]), None)
    if not prov or not prov.get("ok") or prov.get("stale"):
        res["reason"] = f"{spec['provider']} data missing or stale"
        return res
    wins = {w.get("window"): w for w in prov.get("windows", [])}
    seen, out_until, low, short, worst = False, None, False, False, None
    for name in spec["windows"]:
        w = wins.get(name)
        if not w or not w.get("ok", True):
            continue
        sampled = _parse_time(w.get("sampled_at"))
        reset = _parse_time(w.get("resets_at"))
        if (sampled and now - sampled > MAX_AGE) or (reset and reset <= now):
            continue  # an old sample, or a window that has already reset since the sample
        if name == "balance":
            head, floor = w.get("value"), spec.get("usd_low", 0.0)
            if head is None:
                continue
            seen = True
            worst = head if worst is None else min(worst, head)
            if head <= 0:
                out_until = out_until or "balance"
            elif head < floor:
                low = True
            continue
        head = _headroom(w)
        if head is None:
            continue
        seen = True
        worst = head if worst is None else min(worst, head)
        if head <= 0:
            if reset is None:
                out_until = out_until or "?"
            elif out_until in (None, "?") or reset > _parse_time(out_until):
                out_until = reset.isoformat(timespec="seconds")  # all exhausted windows must reset
        elif head <= LOW_PCT:
            low = True
        if (w.get("forecast") or {}).get("enough_until_reset") is False:
            short = True
    if not seen:
        res["reason"] = "no usable window"
        return res
    res["headroom"] = None if worst is None else round(worst, 2)
    unit = "USD" if "balance" in spec["windows"] else "%"
    if out_until:
        res.update(state=OUT, resets_at=None if out_until in ("?", "balance") else out_until,
                   reason=f"{consumer} exhausted (headroom {res['headroom']}{unit})")
    elif low:
        res.update(state=LOW, reason=f"headroom {res['headroom']}{unit}")
    elif short:
        res.update(state=SHORT, reason="forecast: runs out before the reset")
    else:
        res.update(state=OK, reason=f"headroom {res['headroom']}{unit}")
    nxt = [t for t in (_parse_time(w.get("resets_at")) for n in spec["windows"] for w in [wins.get(n) or {}]) if t and t > now]
    if res["resets_at"] is None and nxt:
        res["resets_at"] = min(nxt).isoformat(timespec="seconds")
    return res


def check(consumer, now=None, source=None, cache_file=CACHE_FILE):
    """State of one consumer; unknown when there is no service."""
    return consumer_state(status(now, source, cache_file), consumer, now=now)


def summary(now=None, source=None, cache_file=CACHE_FILE):
    """{consumer: state dict} in the order of the map; unknown everywhere without data."""
    payload, cmap = status(now, source, cache_file), load_map()
    return {c: consumer_state(payload, c, cmap, now) for c in cmap}


def line(states):
    """One short line for capacity.py and the session start."""
    parts = []
    for c, s in states.items():
        extra = f" until {s['resets_at'][5:16].replace('T', ' ')}Z" if s["state"] == OUT and s["resets_at"] else ""
        parts.append(f"{c} {s['state']}{extra}")
    return "limits: " + ", ".join(parts)


def main(argv):
    if argv[:1] == ["check"]:
        if len(argv) != 2 or argv[1] not in load_map():
            print(f"usage: llm_limits.py check {{{'|'.join(load_map())}}}")
            return 2
        s = check(argv[1])
        print(f"{s['consumer']}: {s['state']} ({s['reason']}){' until ' + s['resets_at'] if s['resets_at'] else ''}")
        return 3 if s["state"] == OUT else 0
    st = summary()
    if "--json" in argv:
        print(json.dumps(st))
    else:
        for c, s in st.items():
            print(f"{c:11} {s['state']:8} {s['reason']}{'; resets ' + s['resets_at'] if s['resets_at'] else ''}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
