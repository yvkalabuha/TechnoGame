"""CLI: python3 -m tools.llm_worker {fill-hashes,submit,run,status,report} ..."""

import argparse
import json
import sys
from pathlib import Path

from . import config, report
from .contract import ContractError, fill_hashes, validate_contract
from .provider import OpenAICompatProvider
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
    p.add_argument("--url", default=config.DEFAULT_URL)
    p.add_argument("--model", default=config.DEFAULT_MODEL)
    p.add_argument("--max-outages", type=int, default=12)
    p.add_argument("--outage-wait", type=int, default=300, help="seconds between outage retries")
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
    if a.cmd == "run":
        provider = OpenAICompatProvider(url=a.url, model=a.model)
        done = run_queue(a.queue, provider, Env(runs_dir=a.runs), a.max_outages, a.outage_wait,
                         log=lambda m: print(m, flush=True))
        print(f"report: {report.write(a.queue, a.runs, a.reports)}")
        return 0 if done else 3
    if a.cmd == "report":
        print(report.write(a.queue, a.runs, a.reports, a.date))
        return 0
    return 2


if __name__ == "__main__":
    sys.exit(main())
