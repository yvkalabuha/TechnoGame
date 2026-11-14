"""Spend ledger for paid providers (ADR-0015 §6). Append-only JSON lines outside the repository, every
read-modify-write under flock. Anything doubtful refuses the call: a missing ledger is not "spent 0"."""

import fcntl
import hashlib
import json
import os
import shutil
import uuid
from contextlib import contextmanager
from datetime import datetime, timezone
from decimal import ROUND_CEILING, Decimal

from . import config

MARKER = "ledger.init"
LEDGER = "ledger.jsonl"
HEAD = "ledger.head"  # record count + hash chain head, rewritten under the same flock on every append
QUANT = Decimal("0.00000001")
MILLION = Decimal(1000000)


class SpendRefused(Exception):
    """The call must not be made. `code` is one of: ledger_missing, ledger_corrupt, cap_exceeded,
    task_cap_exceeded, provider_not_ready, unknown_model_price, no_task, bad_prices, bad_reconcile."""

    def __init__(self, code, detail=""):
        super().__init__(f"{code}: {detail}" if detail else code)
        self.code = code


def _usd(x):
    return Decimal(x).quantize(QUANT, rounding=ROUND_CEILING)


def load_prices(path=config.PRICES_FILE):
    try:
        with open(path, encoding="utf-8") as f:
            raw = json.load(f)
        return {"ready": raw["paid_providers_ready"] is True,
                "cap": Decimal(raw["max_usd_total"]), "task_cap": Decimal(raw["max_usd_task"]),
                # #226: gate tasks (ledger key "gate:<ID>") have their own cap
                "gate_task_cap": Decimal(raw.get("max_usd_task_gate", raw["max_usd_task"])),
                "models": {m: {k: Decimal(v[k]) for k in ("input_per_mtok", "output_per_mtok")}
                           for m, v in raw["models"].items()}}
    except (OSError, ValueError, KeyError, TypeError, ArithmeticError) as e:
        raise SpendRefused("bad_prices", type(e).__name__) from None


class Ledger:
    def __init__(self, directory=config.LEDGER_DIR):
        self.dir = directory
        self.marker = directory / MARKER
        self.path = directory / LEDGER
        self.head = directory / HEAD

    def init(self):
        """Creates marker, ledger and head. Returns False (no-op) when all exist. A marker without the ledger
        means history was lost: refuses instead of starting from zero."""
        have = (self.marker.is_file(), self.path.is_file(), self.head.is_file())
        if all(have):
            return False
        if any(have):
            raise SpendRefused("ledger_missing", "partial ledger: restore it or reconcile by hand; "
                                                 "init never recreates history")
        self.dir.mkdir(parents=True, exist_ok=True)
        self.path.touch()
        self._write_head(0, "")
        fd = os.open(self.marker, os.O_CREAT | os.O_WRONLY, 0o600)
        os.close(fd)
        return True

    def _write_head(self, count, head):
        tmp = self.head.with_suffix(".tmp")
        tmp.write_text(json.dumps({"count": count, "head": head}), encoding="utf-8")
        os.replace(tmp, self.head)

    @contextmanager
    def _locked(self):
        if not (self.marker.is_file() and self.path.is_file() and self.head.is_file()):
            raise SpendRefused("ledger_missing", str(self.dir))
        with open(self.marker, "rb") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            try:
                yield
            finally:
                fcntl.flock(lock, fcntl.LOCK_UN)

    @staticmethod
    def _chain(head, line):
        return hashlib.sha256(head.encode() + line).hexdigest()

    @staticmethod
    def _apply(out, line):
        """Parses one ledger line into `out`; ValueError/KeyError/TypeError/ArithmeticError when it is not valid."""
        r = json.loads(line)
        usd = Decimal(r["usd"])
        if not usd.is_finite() or usd < 0:
            raise ValueError("amount")
        if r["t"] == "reserve":
            out[r["id"]] = {"task": r["task"], "model": r["model"], "usd": usd, "settled": False}
        elif r["t"] == "settle":
            out[r["id"]].update(usd=usd, settled=True)
        else:
            raise ValueError(r["t"])

    def _entries(self):
        """id -> {task, model, usd, settled}. The file must match the recorded count and hash chain, so a
        truncated, edited or garbled ledger refuses everything instead of reading as 'spent less'."""
        out = {}
        try:
            want = json.loads(self.head.read_text(encoding="utf-8"))
            count, head = 0, ""
            with open(self.path, "rb") as f:
                for line in f:
                    count += 1
                    head = self._chain(head, line)
                    self._apply(out, line)
            if (count, head) != (want["count"], want["head"]):
                raise ValueError("count or hash chain mismatch")
        except (OSError, ValueError, KeyError, TypeError, ArithmeticError):
            raise SpendRefused("ledger_corrupt", str(self.path)) from None
        return out

    def repair(self):
        """Owner tool (explicit command only): recomputes `ledger.head` after a crash between the line write and
        the head write. Under flock. It repairs only when the old head equals the chain at some prefix of the file
        and every later line is a complete valid record; anything else is reported and nothing is changed
        (a damaged tail is never deleted). Returns {"status": "ok"|"repaired"|"refused", "records", "added",
        "detail"}."""
        with self._locked():
            try:
                want = json.loads(self.head.read_text(encoding="utf-8"))
                want_pair = (want["count"], want["head"])
            except (OSError, ValueError, KeyError, TypeError):
                return self._refused(0, "ledger.head is unreadable: cannot tell which records were verified")
            lines = self.path.read_bytes().splitlines(keepends=True)
            head, out, matched = "", {}, (0, "") == want_pair
            for i, line in enumerate(lines, 1):
                try:
                    if not line.endswith(b"\n"):
                        raise ValueError("no newline")
                    self._apply(out, line)
                except (ValueError, KeyError, TypeError, ArithmeticError):
                    return self._refused(i - 1, f"line {i} is not a complete valid record (tail left in place)")
                head = self._chain(head, line)
                if (i, head) == want_pair:
                    matched = True
            if not matched:
                return self._refused(len(lines), "ledger.head matches no prefix of the file: edited or truncated")
            if want_pair == (len(lines), head):
                return {"status": "ok", "records": len(lines), "added": 0, "detail": "head already matches"}
            stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
            baks = []
            for src in (self.path, self.head):  # copies first: a bad repair can be undone by hand
                bak = src.with_name(f"{src.name}.bak-{stamp}")
                shutil.copy2(src, bak)
                baks.append(str(bak))
            self._write_head(len(lines), head)
            return {"status": "repaired", "records": len(lines), "added": len(lines) - want_pair[0],
                    "detail": "head recomputed; backups: " + ", ".join(baks)}

    @staticmethod
    def _refused(records, detail):
        return {"status": "refused", "records": records, "added": 0, "detail": detail}

    def _append(self, record):
        record["ts"] = datetime.now(timezone.utc).isoformat(timespec="seconds")
        line = (json.dumps(record, sort_keys=True) + "\n").encode()
        want = json.loads(self.head.read_text(encoding="utf-8"))  # verified by _entries in this lock
        with open(self.path, "ab") as f:
            f.write(line)
            f.flush()
            os.fsync(f.fileno())
        # A crash between the two writes leaves a mismatch: every later call refuses (never under-counts).
        self._write_head(want["count"] + 1, self._chain(want["head"], line))

    def reserve(self, task, model, usd, cap, task_cap):
        """Books `usd` as spent before the request. Refuses when the total or the task total would pass a cap."""
        with self._locked():
            entries = self._entries().values()
            total = sum((e["usd"] for e in entries), Decimal(0))
            task_total = sum((e["usd"] for e in entries if e["task"] == task), Decimal(0))
            if total + usd > cap:
                raise SpendRefused("cap_exceeded", f"spent {total} + worst case {usd} > {cap}")
            if task_total + usd > task_cap:
                raise SpendRefused("task_cap_exceeded", f"{task}: {task_total} + {usd} > {task_cap}")
            rid = uuid.uuid4().hex
            self._append({"t": "reserve", "id": rid, "task": task, "model": model, "usd": str(usd)})
            return rid

    def settle(self, rid, usd):
        """Replaces the reservation by the actual cost (may be higher: the overrun is one call's estimate error)."""
        with self._locked():
            if rid not in self._entries():
                raise SpendRefused("ledger_corrupt", "settle of unknown reservation")
            self._append({"t": "settle", "id": rid, "usd": str(usd)})

    def reconcile(self, rid, usd, source):
        """Owner tool: settle an unsettled reservation by the amount from the provider's billing."""
        try:
            usd = _usd(usd)
        except ArithmeticError:
            raise SpendRefused("bad_reconcile", "usd is not a number") from None
        if usd < 0 or not source.strip():
            raise SpendRefused("bad_reconcile", "need usd >= 0 and a source reference")
        with self._locked():
            e = self._entries().get(rid)
            if e is None or e["settled"]:
                raise SpendRefused("bad_reconcile", "unknown or already settled reservation")
            self._append({"t": "settle", "id": rid, "usd": str(usd), "source": source})

    def summary(self):
        """None when the ledger is not initialised; otherwise totals for the report."""
        try:
            with self._locked():
                entries = self._entries()
        except SpendRefused as e:
            if e.code == "ledger_missing":
                return None
            raise
        by_task, by_model = {}, {}
        for e in entries.values():
            by_task[e["task"]] = by_task.get(e["task"], Decimal(0)) + e["usd"]
            by_model[e["model"]] = by_model.get(e["model"], Decimal(0)) + e["usd"]
        return {"total": sum((e["usd"] for e in entries.values()), Decimal(0)), "by_task": by_task,
                "by_model": by_model, "calls": len(entries),
                "unsettled": sum(1 for e in entries.values() if not e["settled"]),
                "unsettled_ids": [i for i, e in entries.items() if not e["settled"]]}


class Guard:
    """What the provider calls around every HTTP attempt, retries included. `task` is set by the runner."""

    def __init__(self, ledger, prices, model, task=None):
        self.ledger = ledger
        self.prices = prices
        self.model = model
        self.task = task

    def before_attempt(self, request_bytes, max_tokens):
        p = self.prices
        if not p["ready"]:
            raise SpendRefused("provider_not_ready")
        price = p["models"].get(self.model)
        if price is None:
            raise SpendRefused("unknown_model_price", self.model)
        if not self.task:
            raise SpendRefused("no_task")
        in_tokens = (len(request_bytes) + 1) // 2  # 1 token per 2 bytes: stricter than any tokenizer
        worst = _usd((in_tokens * price["input_per_mtok"] + max_tokens * price["output_per_mtok"]) / MILLION)
        task_cap = p["gate_task_cap"] if self.task.startswith("gate:") else p["task_cap"]
        return self.ledger.reserve(self.task, self.model, worst, p["cap"], task_cap)

    def settle(self, rid, usage):
        """Actual cost from `usage` (all input at the cache-miss price). No usage: the reservation stays."""
        pt, ct = usage.get("prompt_tokens"), usage.get("completion_tokens")
        # Strict ints (bool is not trusted); a request always has input, so zero input means broken usage.
        if type(pt) is not int or type(ct) is not int or pt <= 0 or ct < 0:
            return
        price = self.prices["models"][self.model]
        self.ledger.settle(rid, _usd((pt * price["input_per_mtok"] + ct * price["output_per_mtok"]) / MILLION))

    def release(self, rid):
        """The request never left or the provider answered with an error that is never billed (4xx, 429)."""
        self.ledger.settle(rid, Decimal(0))
