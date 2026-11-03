"""File queue: queue/<task_id>/{contract.json,state.json}. Every transition is logged with a reason."""

import json
import os
from datetime import datetime, timezone

from .contract import ContractError, contract_hash, validate_contract

CREATED, QUEUED, RUNNING = "CREATED", "QUEUED", "RUNNING"
ACCEPTED, REJECTED, ESCALATED, FAILED = "ACCEPTED", "REJECTED", "ESCALATED", "FAILED"
FINAL = {ACCEPTED, REJECTED, ESCALATED, FAILED}

TRANSITIONS = {
    CREATED: {QUEUED},
    QUEUED: {RUNNING},
    # RUNNING -> QUEUED: provider outage or restart; finished rounds stay on disk.
    RUNNING: {QUEUED} | FINAL,
}


def _now():
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def write_json_atomic(path, data):
    tmp = path.with_suffix(path.suffix + ".tmp")
    tmp.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    os.replace(tmp, path)


class TaskQueue:
    def __init__(self, root):
        self.root = root

    def _dir(self, task_id):
        return self.root / task_id

    def submit(self, contract, repo_root=None):
        """Validates the contract and enqueues it. Invalid contracts never reach the queue."""
        kwargs = {} if repo_root is None else {"repo_root": repo_root}
        errors = validate_contract(contract, **kwargs)
        if errors:
            raise ContractError("; ".join(errors))
        d = self._dir(contract["task_id"])
        if d.exists():
            raise ContractError(f"task {contract['task_id']} already in queue (contracts are immutable)")
        d.mkdir(parents=True)
        write_json_atomic(d / "contract.json", contract)
        write_json_atomic(d / "state.json", {
            "task_id": contract["task_id"],
            "contract_sha256": contract_hash(contract),
            "state": CREATED,
            "history": [{"to": CREATED, "reason": "submitted", "at": _now()}],
        })
        self.transition(contract["task_id"], QUEUED, "contract valid")

    def contract(self, task_id):
        c = json.loads((self._dir(task_id) / "contract.json").read_text(encoding="utf-8"))
        if contract_hash(c) != self.state(task_id)["contract_sha256"]:
            raise ContractError(f"contract of {task_id} was modified after submit")
        return c

    def state(self, task_id):
        return json.loads((self._dir(task_id) / "state.json").read_text(encoding="utf-8"))

    def transition(self, task_id, to, reason, **extra):
        st = self.state(task_id)
        if to not in TRANSITIONS.get(st["state"], set()):
            raise ValueError(f"{task_id}: illegal transition {st['state']} -> {to}")
        st["history"].append({"from": st["state"], "to": to, "reason": reason, "at": _now()})
        st["state"] = to
        st.update(extra)
        write_json_atomic(self._dir(task_id) / "state.json", st)

    def task_ids(self):
        if not self.root.is_dir():
            return []
        return sorted(p.name for p in self.root.iterdir() if (p / "state.json").is_file())

    def with_state(self, state):
        return [t for t in self.task_ids() if self.state(t)["state"] == state]

    def recover(self):
        """After a crash or restart: RUNNING tasks go back to QUEUED and resume from saved rounds."""
        for t in self.with_state(RUNNING):
            self.transition(t, QUEUED, "resumed after restart")
