"""Test helpers: fake provider, temporary repository, contract builders. No network."""

import copy
import hashlib
import json
import subprocess
import tempfile
from pathlib import Path

from tools.llm_worker.config import PACKAGE_DIR
from tools.llm_worker.provider import Completion

CONTRACTS = PACKAGE_DIR / "contracts"


class FakeProvider:
    """Returns scripted items in order: a dict (sent as JSON), a str (raw content) or an exception."""

    def __init__(self, script):
        self.script = list(script)
        self.calls = []

    def complete(self, messages, schema, schema_name, max_tokens, seed=None):
        self.calls.append({"messages": copy.deepcopy(messages), "schema_name": schema_name, "seed": seed})
        if not self.script:
            raise AssertionError("fake provider: script exhausted")
        item = self.script.pop(0)
        if isinstance(item, BaseException):
            raise item
        content = item if isinstance(item, str) else json.dumps(item)
        return Completion(content=content, usage={"prompt_tokens": 100, "completion_tokens": 10},
                          timings={}, elapsed_s=1.0)


def load_contract(name):
    return json.loads((CONTRACTS / name).read_text(encoding="utf-8"))


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


class TempRepo:
    """A throwaway git repository with given files committed on main."""

    def __init__(self, files):
        self._tmp = tempfile.TemporaryDirectory()
        self.root = Path(self._tmp.name) / "repo"
        self.root.mkdir()
        for rel, text in files.items():
            p = self.root / rel
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_text(text, encoding="utf-8")
        for args in (["init", "-q", "-b", "main"], ["add", "-A"],
                     ["-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "init"]):
            subprocess.run(["git", *args], cwd=self.root, check=True, capture_output=True)
        subprocess.run(["git", "config", "user.name", "t"], cwd=self.root, check=True)
        subprocess.run(["git", "config", "user.email", "t@t"], cwd=self.root, check=True)
        self.tmp = Path(self._tmp.name)

    def git(self, *args):
        return subprocess.run(["git", *args], cwd=self.root, check=True, capture_output=True, text=True).stdout

    def cleanup(self):
        self._tmp.cleanup()


def a2_contract(data_rel="data/nodes.csv", repo=None, **over):
    c = load_contract("A2-jet-nodes-clean.json")
    c["context_files"] = [{"path": data_rel, "sha256": sha(repo.root / data_rel)}]
    c["verification"] = {"data_file": data_rel}
    c.update(over)
    return c


NODES_CSV = (
    "idx,id,type,gen,requires,known_at_start\n"
    "1,a,FND,G0,,ALL\n"
    "2,b,ENG,G1,a,USA\n"
    "3,c,ENG,G1,zz,\n"
)
