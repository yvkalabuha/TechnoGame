"""CLI: python3 -m tools.llm_worker {fill-hashes,submit,run,status,report} ..."""

import argparse
import json
import sys
from pathlib import Path

from . import config, report, review as review_mod
from .contract import ContractError, fill_hashes, validate_contract
from .ledger import Guard, Ledger, SpendRefused, load_prices
from .provider import KeyFileError, OpenAICompatProvider
from .runner import Env, run_queue
from .taskqueue import TaskQueue


def main(argv=None):
    ap = argparse.ArgumentParser(prog="python3 -m tools.llm_worker")
    ap.add_argument("--queue", type=Path, default=config.QUEUE_DIR)
    ap.add_argument("--runs", type=Path, default=config.RUNS_DIR)
    ap.add_argument("--reports", type=Path, default=config.REPORTS_DIR)
    sub = ap.add_subparsers(dest="cmd", required=True)

    p = sub.add_parser("fill-hashes", help="write current sha256 of context files into a contract file")
    p.add_argument("contract", type=Path)
    p = sub.add_parser("validate", help="validate contracts without queueing")
    p.add_argument("contracts", type=Path, nargs="+")
    p = sub.add_parser("submit", help="validate and enqueue contracts")
    p.add_argument("contracts", type=Path, nargs="+")
    p = sub.add_parser("run", help="process the queue, then write the report")
    p.add_argument("--profile", choices=sorted(config.PROFILES), default="llamacpp",
                   help="provider profile; deepseek-* is paid and needs the ledger and paid_providers_ready")
    p.add_argument("--url", default=None)
    p.add_argument("--model", default=None)
    p.add_argument("--max-outages", type=int, default=12)
    p.add_argument("--outage-wait", type=int, default=300, help="seconds between outage retries")
    p = sub.add_parser("ledger", help="spend ledger: init creates it, show prints totals, "
                                       "reconcile settles one reservation by the provider's bill")
    p.add_argument("action", choices=["init", "show", "reconcile"])
    p.add_argument("reservation_id", nargs="?")
    p.add_argument("usd", nargs="?")
    p.add_argument("--source", default="", help="reconcile: where the amount comes from (billing page, date)")
    p = sub.add_parser("review", help="second review of base...head (Codex in bwrap, DeepSeek as fallback)")
    p.add_argument("--task", required=True)
    p.add_argument("--base", required=True)
    p.add_argument("--head", required=True)
    p.add_argument("--provider", choices=["codex", "deepseek"], default="codex")
    p = sub.add_parser("gate", help="DeepSeek as the third voice of a gate")
    p.add_argument("--item", required=True, help="repository-relative path of the document")
    p.add_argument("--stance", choices=["third"], required=True)
    p.add_argument("--head", default="HEAD")
    p.add_argument("--profile", choices=["deepseek-flash", "deepseek-v4-pro"], default=review_mod.DEFAULT_PROFILE)
    p.add_argument("--dry-run", action="store_true", help="build and scan the input, print its size, send nothing")
    sub.add_parser("status", help="list tasks and states")
    p = sub.add_parser("report", help="write reports/<date>.md")
    p.add_argument("--date")

    a = ap.parse_args(argv)
    if a.cmd == "fill-hashes":
        c = fill_hashes(json.loads(a.contract.read_text(encoding="utf-8")))
        a.contract.write_text(json.dumps(c, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        return 0
    if a.cmd == "validate":
        rc = 0
        for f in a.contracts:
            errors = validate_contract(json.loads(f.read_text(encoding="utf-8")))
            print(f"{f}: {'ok' if not errors else '; '.join(errors)}")
            rc |= bool(errors)
        return rc
    queue = TaskQueue(a.queue)
    if a.cmd == "submit":
        rc = 0
        for f in a.contracts:
            try:
                queue.submit(json.loads(f.read_text(encoding="utf-8")))
                print(f"{f}: queued")
            except (ContractError, json.JSONDecodeError) as e:
                print(f"{f}: rejected: {e}")
                rc = 1
        return rc
    if a.cmd == "status":
        for t in queue.task_ids():
            st = queue.state(t)
            print(f"{t}\t{st['state']}\t{st['history'][-1]['reason']}")
        return 0
    if a.cmd == "ledger":
        ledger = Ledger()
        try:
            if a.action == "init":
                print(f"ledger: {ledger.dir}" if ledger.init() else f"warning: ledger already exists, nothing done: {ledger.dir}")
            elif a.action == "reconcile":
                if not (a.reservation_id and a.usd):
                    ap.error("ledger reconcile needs <reservation_id> <usd> --source <ref>")
                ledger.reconcile(a.reservation_id, a.usd, a.source)
                print("settled")
            else:
                print(ledger.summary() or "ledger is not initialised")
        except SpendRefused as e:
            print(f"refused: {e}")
            return 4
        return 0
    if a.cmd in ("review", "gate"):
        return _review_gate(a)
    if a.cmd == "run":
        profile = config.PROFILES[a.profile]
        guard = ledger = prices = None
        if profile.paid:
            try:
                prices = load_prices()
                if not prices["ready"]:
                    raise SpendRefused("provider_not_ready")
                ledger = Ledger()
                if ledger.summary() is None:  # corrupt ledger raises SpendRefused here
                    raise SpendRefused("ledger_missing", str(ledger.dir))
            except SpendRefused as e:
                print(f"refused: {e}")
                return 4
            guard = Guard(ledger, prices, a.model or profile.model)
        try:
            provider = OpenAICompatProvider(url=a.url, model=a.model, profile=profile, guard=guard)
            if profile.name == "deepseek-flash" and not a.model:  # chain flash -> pro (#175 d)
                pro = config.PROFILES["deepseek-v4-pro"]
                provider.fallbacks = [OpenAICompatProvider(profile=pro, guard=Guard(ledger, prices, pro.model))]
        except KeyFileError as e:
            print(f"refused: {e}")
            return 4
        done = run_queue(a.queue, provider, Env(runs_dir=a.runs), a.max_outages, a.outage_wait,
                         log=lambda m: print(m, flush=True))
        cap = prices["cap"] if prices else None
        print(f"report: {report.write(a.queue, a.runs, a.reports, ledger=ledger, cap=cap)}")
        return 0 if done else 3
    if a.cmd == "report":
        try:  # read-only: the report only shows spending, a missing or broken ledger is not an error here
            ledger = Ledger()
            cap = load_prices()["cap"] if ledger.summary() is not None else None
        except SpendRefused:
            ledger = cap = None
        print(report.write(a.queue, a.runs, a.reports, a.date, ledger=ledger, cap=cap))
        return 0
    return 2


def _review_gate(a):
    try:
        if a.cmd == "review":
            path, line, rec = review_mod.run_review(a.task, a.base, a.head, a.provider, runs_dir=a.runs,
                                                    log=lambda m: print(m, flush=True))
            res = rec["result"]
            print(f"record: {path}")
            for f in res["findings"]:
                print(f"  [{f['severity']}] {f['path']}:{f['line']} {f['claim']}")
            print(line)
        else:
            path, rec = review_mod.run_gate(a.item, a.head, a.stance, runs_dir=a.runs, profile_name=a.profile,
                                            dry_run=a.dry_run)
            if a.dry_run:
                print(f"dry-run: {rec['input_bytes']} bytes, files {len(rec['files'])}, omitted {len(rec['omitted'])}")
                return 0
            res = rec["result"]
            print(f"record: {path}\nsha256 {rec['sha256']}\nSTATUS {res['STATUS']}: {review_mod.count_line(res['FINDINGS'])}")
            for f in res["FINDINGS"]:
                print(f"  [{f['severity']}] {f['item']}: {f['reason']}")
        return 0
    except (SpendRefused, KeyFileError) as e:
        print(f"refused: {e}")
        return 4
    except review_mod.ReviewError as e:
        print(f"error: {e}")
        return 5


if __name__ == "__main__":
    sys.exit(main())
