"""Processes queued tasks: context package, candidates, verdict, transitions with reasons."""

import time

from . import config, worktree
from .context import build_package
from .loop import PASSED, REJECTED, CandidateLoop
from .provider import AuthError, ProviderUnavailable
from .taskqueue import ACCEPTED, FAILED, QUEUED, RUNNING, TaskQueue, write_json_atomic
from .taskqueue import ESCALATED as S_ESCALATED, REJECTED as S_REJECTED
from .verifiers.a1_inventory import A1Verifier
from .verifiers.a2_data import A2Verifier
from .verifiers.b1_tests import B1Verifier


class Env:
    """Paths and factories; tests replace them with temporary dirs and fakes."""

    def __init__(self, repo_root=config.REPO_ROOT, runs_dir=config.RUNS_DIR,
                 worktrees_dir=config.WORKTREES_DIR, lessons_dir=config.LESSONS_DIR,
                 b1_runner=None, clock=time.monotonic):
        self.repo_root = repo_root
        self.runs_dir = runs_dir
        self.worktrees_dir = worktrees_dir
        self.lessons_dir = lessons_dir
        self.b1_runner = b1_runner
        self.clock = clock

    def make_verifier(self, contract, candidate):
        t = contract["type"]
        if t == "A1":
            return A1Verifier(contract, self.repo_root)
        if t == "A2":
            return A2Verifier(contract, self.repo_root)
        meta = self.runs_dir / contract["task_id"] / f"c{candidate}" / "worktree.json"
        path, base, branch = worktree.ensure(self.repo_root, self.worktrees_dir, contract["task_id"], candidate, meta)
        return B1Verifier(contract, path, base, branch, runner=self.b1_runner)


def _rank(st):
    """Best candidate: passed first, then quality, then fewer tokens."""
    best = st["rounds"][st["best"]]["report"]["quality"] if st["best"] is not None else -1
    return (st["outcome"] == PASSED, best, -(st["tokens"]["prompt"] + st["tokens"]["completion"]))


def process_task(queue, task_id, provider, env):
    """Runs one QUEUED task to a final state; it is never left RUNNING.
    ProviderUnavailable and AuthError propagate after requeueing."""
    queue.transition(task_id, RUNNING, "picked by worker")
    try:
        return _run_task(queue, task_id, provider, env)
    except ProviderUnavailable as e:
        queue.transition(task_id, QUEUED, f"provider unavailable, will resume: {e}")
        raise
    except AuthError as e:
        queue.transition(task_id, QUEUED, f"authentication failed, run stopped: {e}")
        raise
    except Exception as e:  # noqa: BLE001 - any other error is a technical failure of this task
        if queue.state(task_id)["state"] == RUNNING:
            queue.transition(task_id, FAILED, f"{type(e).__name__}: {e}", unverified=True)
        return FAILED


def _run_task(queue, task_id, provider, env):
    contract = queue.contract(task_id)
    context_text, manifest = build_package(contract, env.repo_root, env.lessons_dir)
    task_dir = env.runs_dir / task_id
    task_dir.mkdir(parents=True, exist_ok=True)
    write_json_atomic(task_dir / "context_manifest.json", manifest)
    results = []
    for k in range(1, contract["max_candidates"] + 1):
        verifier = env.make_verifier(contract, k)
        loop = CandidateLoop(contract, k, provider, verifier, task_dir / f"c{k}", context_text, env.clock)
        st = loop.run()
        results.append((st, loop, verifier))

    st, loop, verifier = max(results, key=lambda r: _rank(r[0]))
    changed = verifier.finalize(loop.best_artifact()) if st["outcome"] == PASSED else []
    summary = {
        "chosen_candidate": st["candidate"],
        "candidates": [{"candidate": r[0]["candidate"], "outcome": r[0]["outcome"], "stop": r[0]["stop"]}
                       for r in results],
        "changed_files": changed,
    }
    write_json_atomic(task_dir / "result.json", {**summary, "artifact": loop.best_artifact()})
    if st["outcome"] == PASSED:
        reason = "all hard checks passed" + ("; Claude review required" if contract["requires_review"] else "")
        queue.transition(task_id, ACCEPTED, reason, **summary)
        return ACCEPTED
    if all(r[0]["outcome"] == REJECTED for r in results):
        queue.transition(task_id, S_REJECTED, st["stop"], **summary)
        return S_REJECTED
    queue.transition(task_id, S_ESCALATED, f"{st['stop']}; to Pilot: {contract['escalation']}", **summary)
    return S_ESCALATED


def run_queue(queue_dir, provider, env, max_outages=12, outage_wait_s=300, sleep=time.sleep, log=print):
    """Processes all QUEUED tasks. On repeated outage stops; queued tasks stay for the next run."""
    queue = TaskQueue(queue_dir)
    queue.recover()
    outages = 0
    while True:
        pending = queue.with_state(QUEUED)
        if not pending:
            return True
        task_id = pending[0]
        try:
            state = process_task(queue, task_id, provider, env)
            outages = 0
            log(f"{task_id}: {state}")
        except AuthError as e:
            log(f"{task_id}: authentication failed, stopping the run: {e}")
            return False
        except ProviderUnavailable as e:
            outages += 1
            log(f"{task_id}: provider unavailable ({outages}/{max_outages}): {e}")
            if outages >= max_outages:
                return False
            sleep(outage_wait_s)
