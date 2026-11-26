"""CLI: python3 -m tools.llm_worker {fill-hashes,submit,run,status,report,ledger,review,review-confirm,gate,visual,sketch,research} ..."""

import argparse
import json
import sys
from pathlib import Path

from . import codexsketch, config, gitleaks, rejections, report, research as research_mod, review as review_mod, sketch as sketch_mod, visual as visual_mod
from .contract import ContractError, fill_hashes, validate_contract
from .ledger import Guard, Ledger, SpendRefused, load_prices
from .provider import AuthError, KeyFileError, OpenAICompatProvider, ProviderError, ProviderUnavailable
from .runner import Env, run_queue
from .taskqueue import TaskQueue


def _thinking_flags(p, default):
    p.add_argument("--thinking", choices=["on", "off"], default=default, help="DeepSeek thinking mode")
    p.add_argument("--effort", choices=config.REASONING_EFFORTS, default=config.DEFAULT_REASONING_EFFORT,
                   help="reasoning_effort when thinking is on")


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
    p.add_argument("--profile", choices=sorted(n for n, pr in config.PROFILES.items() if not pr.free_public),
                   default="llamacpp",
                   help="provider profile; deepseek-* is paid and needs the ledger and paid_providers_ready")
    p.add_argument("--url", default=None)
    p.add_argument("--model", default=None)
    _thinking_flags(p, default="off")
    p.add_argument("--max-outages", type=int, default=12)
    p.add_argument("--outage-wait", type=int, default=300, help="seconds between outage retries")
    p = sub.add_parser("ledger", help="spend ledger: init creates it, show prints totals, "
                                       "reconcile settles one reservation by the provider's bill, "
                                       "repair recomputes ledger.head after a crash (owner only)")
    p.add_argument("action", choices=["init", "show", "reconcile", "repair"])
    p.add_argument("reservation_id", nargs="?")
    p.add_argument("usd", nargs="?")
    p.add_argument("--source", default="", help="reconcile: where the amount comes from (billing page, date)")
    p = sub.add_parser("review", help="second review of base...head (auto: Codex in bwrap, Sonnet 5.5 via agy when the Codex limit is out, DeepSeek last)")
    p.add_argument("--task", required=True)
    p.add_argument("--base", required=True)
    p.add_argument("--head", required=True)
    p.add_argument("--provider", choices=["auto", "codex", "agy-sonnet", "deepseek"], default="auto",
                   help="auto remembers the Codex limit reset time; codex keeps Codex then DeepSeek")
    _thinking_flags(p, default="on")
    p = sub.add_parser("review-confirm", help="mark one finding of a review record as confirmed or rejected")
    p.add_argument("record", type=Path)
    p.add_argument("--finding", type=int, required=True, help="1-based number as printed by `review`")
    g = p.add_mutually_exclusive_group(required=True)
    g.add_argument("--confirmed", action="store_true")
    g.add_argument("--rejected", action="store_true")
    p.add_argument("--reason", help="why the finding is rejected; later reviews see it (also in other tasks, via the journal)")
    p.add_argument("--ref", default="", help="decision that settles it: ADR-NNNN, #NNN, TASK-NNNN (goes to the journal)")
    p.add_argument("--journal", type=Path, default=config.REPO_ROOT / rejections.JOURNAL_REL,
                   help="rejection journal (default development/review-rejections.yaml)")
    p = sub.add_parser("gate", help="DeepSeek as the third voice of a gate")
    p.add_argument("--item", required=True, help="repository-relative path of the document")
    p.add_argument("--stance", choices=["third"], required=True)
    p.add_argument("--head", default="HEAD")
    p.add_argument("--profile", choices=["deepseek-flash", "deepseek-v4-pro"], default=review_mod.DEFAULT_PROFILE)
    p.add_argument("--dry-run", action="store_true", help="build and scan the input, print its size, send nothing")
    _thinking_flags(p, default="on")
    p = sub.add_parser("visual", help="Gemini as a second opinion on frames from shots-out/ (agy subscription by default)")
    p.add_argument("--provider", choices=visual_mod.PROVIDERS, default="agy",
                   help="agy: Antigravity subscription (default); key: AI Studio free-tier key (fallback)")
    p.add_argument("--task", required=True)
    p.add_argument("--frames", type=Path, nargs="+", required=True)
    p.add_argument("--criteria", default=visual_mod.DEFAULT_CRITERIA)
    p.add_argument("--model", default=None, help="model id (default: agy gemini-4-neon-high; key gemini-flash-latest)")
    p.add_argument("--mode", choices=visual_mod.MODES, default="criteria",
                   help="criteria: findings against the criteria; design: also Gemini's own proposals")
    p.add_argument("--allow-small", action="store_true", help="send frames smaller than 1920x1080")
    p.add_argument("--dry-run", action="store_true", help="load frames, scan the text, send nothing")
    p = sub.add_parser("sketch", help="Gemini design sketches (images) through the Antigravity CLI agy")
    p.add_argument("--task", required=True)
    p.add_argument("--prompt-file", type=Path, required=True, help="Markdown brief inside the repository")
    p.add_argument("--ref", type=Path, action="append", default=[], help="reference frame from shots-out/ (repeatable)")
    p.add_argument("--count", type=int, default=1)
    p.add_argument("--engine", choices=codexsketch.ENGINES, default="auto",
                   help="auto: Gemini (agy), Codex only when Gemini has no quota; codex makes one image per call")
    p.add_argument("--codex-daily-cap", type=int, default=codexsketch.DEFAULT_DAILY_CAP, help="Codex sketch attempts per day")
    p.add_argument("--timeout", type=int, default=sketch_mod.DEFAULT_TIMEOUT_S, help="seconds for agy")
    p.add_argument("--parallel", action="store_true", help="run the image sub-agents at once (default: one after another)")
    p.add_argument("--with-visual", action="store_true", help="also run 'visual --mode design' on the --ref frames")
    p.add_argument("--visual-provider", choices=visual_mod.PROVIDERS, default="agy",
                   help="provider of the --with-visual run: agy (subscription, default) or key (AI Studio key)")
    p.add_argument("--allow-small", action="store_true", help="with --with-visual: send frames below 1920x1080")
    p.add_argument("--dry-run", action="store_true", help="check inputs, run nothing")
    p = sub.add_parser("research", help="DeepSeek checks claims against pages the tool fetched (#577)")
    p.add_argument("--input", type=Path, required=True, help="YAML with items: id, claim or question, urls")
    p.add_argument("--profile", choices=["deepseek-flash", "deepseek-v4-pro"], default="deepseek-flash")
    p.add_argument("--dry-run", action="store_true", help="fetch and scan pages, send nothing")
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
    if a.cmd == "sketch":
        return _sketch(a)
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
            elif a.action == "repair":
                res = ledger.repair()
                print(f"ledger repair: {res['status']}: records {res['records']}, added {res['added']}: {res['detail']}")
                return 0 if res["status"] != "refused" else 4
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
    if a.cmd == "review-confirm":
        try:
            n = review_mod.mark_finding(a.record, a.finding, a.confirmed, a.reason, journal=a.journal, ref=a.ref)
        except review_mod.ReviewError as e:
            print(f"error: {e}")
            return 5
        print(f"finding {a.finding}/{n}: {'confirmed' if a.confirmed else 'rejected'}")
        if a.rejected and not a.reason:
            print("warning: no --reason: the journal keeps 'no reason recorded', which does not stop a repeat")
        return 0
    if a.cmd == "visual":
        return _visual(a)
    if a.cmd == "research":
        try:
            path, rec, md = research_mod.run_research(a.input, runs_dir=a.runs, profile_name=a.profile, dry_run=a.dry_run)
        except (SpendRefused, KeyFileError) as e:
            print(f"refused: {e}")
            return 4
        except review_mod.ReviewError as e:
            print(f"error: {e}")
            return 5
        print(md, end="")
        print(f"record: {path}" if path else "dry-run: nothing sent")
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
            provider = OpenAICompatProvider(url=a.url, model=a.model, profile=profile, guard=guard,
                                        thinking=a.thinking == "on", effort=a.effort)
            if profile.name == "deepseek-flash" and not a.model:  # chain flash -> pro (#175 d)
                pro = config.PROFILES["deepseek-v4-pro"]
                provider.fallbacks = [OpenAICompatProvider(profile=pro, guard=Guard(ledger, prices, pro.model),
                                                                thinking=a.thinking == "on", effort=a.effort)]
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


def _visual(a):
    try:
        path, md, rec = visual_mod.run_visual(a.task, a.frames, a.criteria, runs_dir=a.runs, model=a.model,
                                              dry_run=a.dry_run, mode=a.mode, allow_small=a.allow_small,
                                              provider=getattr(a, "provider", "agy"))
    except KeyFileError:
        print(f"refused: {config.GEMINI_KEY_HINT}")
        return 4
    except (gitleaks.SecretDetected, gitleaks.GitleaksUnavailable) as e:
        print(f"refused: {e}")
        return 4
    except visual_mod.VisualError as e:
        refused = e.code in ("rate_limit", "secret_detected", "sandbox_unavailable", "agy_quota", "agy_capacity",
                             "agent_tool_violation")
        print(f"refused: {e}" if refused else f"error: {e}")
        return 4 if refused else 5
    except (ProviderUnavailable, ProviderError, AuthError) as e:
        print(f"error: {e}")
        return 5
    if a.dry_run:
        print(f"dry-run: {len(rec['frames'])} frames, model {rec['model']}, hash {rec['input_hash'][:8]}")
        return 0
    res = rec["result"]
    tk = rec["tokens"]
    print(f"record: {path}\nsummary: {md}\nopinion {res['opinion']}: {review_mod.count_line(res['findings'])}")
    print(f"tokens: prompt {tk['prompt']}, completion {tk['completion']}, thinking {tk['thinking']}")
    return 0


def _sketch(a):
    rc = 0
    if a.with_visual:
        if not a.ref:
            print("error: --with-visual needs --ref frames")
            return 5
        a_vis = argparse.Namespace(task=a.task, frames=a.ref, criteria=visual_mod.DEFAULT_CRITERIA, runs=a.runs, model=None,
                                   dry_run=a.dry_run, mode="design", allow_small=a.allow_small,
                                   provider=a.visual_provider)
        rc = _visual(a_vis)
    try:
        path, md, rec = codexsketch.run_engine(a.engine, a.task, a.prompt_file, a.ref, a.count, runs_dir=a.runs,
                                                 timeout_s=a.timeout, dry_run=a.dry_run, parallel=a.parallel,
                                                 codex_kw={"daily_cap": a.codex_daily_cap, "timeout_s": max(a.timeout, 600)})
    except visual_mod.VisualError as e:
        refused = e.code in ("rate_limit", "secret_detected", "sandbox_unavailable", "agy_missing", "agy_not_logged_in",
                                    "agy_quota", "agy_capacity", "agent_tool_violation", "codex_quota",
                                    "codex_daily_cap", "land_in_progress")
        print(f"refused: {e}" if refused else f"error: {e}")
        return 4 if refused else 5
    if a.dry_run:
        print(f"dry-run: {rec['count']} sketch(es), refs {len(rec['refs'])}, hash {rec['input_hash'][:8]}")
        return rc
    print(f"record: {path}\nsummary: {md}\nconversation: {rec['conversation_id']}")
    for f in rec["files"]:
        print(f"  {f['path']}")
    if rec["partial"]:
        print(f"warning: partial result, {len(rec['files'])} of {rec['requested']} images")
        rc = rc or 6
    if rec.get("quota"):  # quota outranks the generic partial code
        print(f"quota: {sketch_mod.quota_text(rec['quota'])}")
        rc = 4 if rc in (0, 6) else rc  # keep a --with-visual failure code
    print("attribution (add to mods/core/ATTRIBUTION.md after ui-art-director approves):")
    for line in rec["attribution"]:
        print(line)
    return rc


def _review_gate(a):
    try:
        if a.cmd == "review":
            path, line, rec = review_mod.run_review(a.task, a.base, a.head, a.provider, runs_dir=a.runs,
                                                    log=lambda m: print(m, flush=True),
                                                    thinking=a.thinking == "on", effort=a.effort)
            res = rec["result"]
            print(f"record: {path}")
            print(f"provider: {rec['provider']}" + (f" ({rec['model']})" if rec.get("model") else "")
                  + (f", replaced codex: {rec['fallbacks'][0]['reason']}" if rec.get("fallbacks") else "")
                  + (f", codex reset at {rec['codex_reset_at']}" if rec.get("codex_reset_at") else ""))
            for i, f in enumerate(res["findings"], 1):
                print(f"  #{i} [{f['severity']}] {f['path']}:{f['line']} {f['claim']}")
            print(line)
        else:
            path, rec = review_mod.run_gate(a.item, a.head, a.stance, runs_dir=a.runs, profile_name=a.profile,
                                            dry_run=a.dry_run, thinking=a.thinking == "on", effort=a.effort)
            if a.dry_run:
                print(f"dry-run: {rec['input_bytes']} bytes, files {len(rec['files'])}, omitted {len(rec['omitted'])}, thinking {rec['thinking']}")
                return 0
            res = rec["result"]
            t = rec["thinking"]
            print(f"reasoning_tokens {t['reasoning_tokens']}, reasoning_chars {t['reasoning_chars']}, attempts {t['attempts']}")
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
