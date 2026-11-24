#!/usr/bin/env python3
"""Land a branch on main only after the merged result passes verify.py.

  python3 tools/development/land.py BRANCH -m "merge: TASK-0001 ..." [--full] [--map]

Steps:
  1. a temporary worktree at main;
  2. `git merge --no-ff BRANCH` there (a conflict stops the landing; main is untouched);
  3. tools/development/verify.py on the merged tree;
  4. `git merge --ff-only` of that merge commit into main.

So main never holds a commit that was not built and tested as merged.
  0. second review (ADR-0015): a diff that touches R2 paths (Kernel, Core, Session and their
     tests, golden files, project files) or tools/** needs a Codex review record for the branch
     head (`python3 -m tools.llm_worker review`). land.py runs the review if the record is
     missing. A `changes_requested` verdict or an open high finding stops the landing; a finding
     is closed with `python3 -m tools.llm_worker review-confirm <record> --finding N --rejected`
     after the Orchestrator checked it, or by a new commit and a new review.

Exit codes: 0 landed, 1 verify failed, 3 merge conflict, 4 main moved or is dirty,
5 the branch changes registry status fields, 6 second review missing or not passed.
The commit message is used as given: put the attribution trailer into -m.
"""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
import time
from pathlib import Path


# Paths whose content verify.py builds or tests.
CODE_PATHS = ("src/", "tests/", "mods/", "tools/", "lab/", ".github/")


def git(*args: str, cwd: Path, check: bool = True) -> subprocess.CompletedProcess:
    return subprocess.run(["git", *args], cwd=cwd, text=True,
                          capture_output=True, check=check)


def needs_second_review(files: list[str], root: Path) -> list[str]:
    """Files of the diff that ADR-0015 puts under a second review: R2 class or tools/**."""
    sys.path.insert(0, str(root))
    from tools.llm_worker import risk  # one source of the risk classes
    classes = risk.load_classes()
    return [f for f in files
            if f.startswith("tools/") or risk.classify_path(f, classes) == "R2"]


def second_review(root: Path, task: str, base: str, head: str, run: bool) -> tuple[bool, str]:
    """(passed, line for the merge message). Runs the review when no record exists for this head."""
    runs = root / "tools" / "llm_worker" / "runs"

    def record():
        best = None
        for f in sorted(runs.glob(f"review-{task}-{head[:8]}-*.json")):
            if f.name.endswith(".confirm.json"):
                continue
            try:
                rec = json.loads(f.read_text(encoding="utf-8"))
            except (OSError, ValueError):
                continue
            if rec.get("head") == head and rec.get("kind") == "review":
                best = (f, rec)
        return best

    found = record()
    if not found and run:
        print(f"land: second review of {task} {base[:8]}..{head[:8]} (ADR-0015), this takes minutes")
        r = subprocess.run([sys.executable, "-m", "tools.llm_worker", "review", "--task", task,
                            "--base", base, "--head", head], cwd=root, text=True,
                           capture_output=True, timeout=1800)
        print("\n".join((r.stdout + r.stderr).strip().splitlines()[-6:]))
        found = record()
    if not found:
        return False, "no review record for this head"
    path, rec = found
    res = rec["result"]
    marks = {}
    side = path.with_name(path.name + ".confirm.json")  # same name as tools/llm_worker/review.py confirm_path
    if side.exists():
        try:
            marks = json.loads(side.read_text(encoding="utf-8")).get("confirmed", {})
        except (OSError, ValueError):
            marks = {}
    open_high = []
    for n, f in enumerate(res.get("findings", []), 1):
        rejected = marks.get(str(n)) is False
        tag = "rejected" if rejected else "open"
        print(f"  [{f['severity']}] #{n} {tag}: {f['path']}:{f['line']} {f['claim'][:160]}")
        if f["severity"] == "high" and not rejected:
            open_high.append(n)
    provider = rec.get("provider", "?")
    model = f" ({rec['model']})" if rec.get("model") else ""
    replaced = rec.get("fallbacks") or []
    why = f", replacing codex: {replaced[0]['reason']}" if replaced and replaced[0].get("from") == "codex" else ""
    print(f"land: second review by {provider}{model}{why}")
    line = (f"Second-Review: {provider}{model} {res['verdict']}, {len(res.get('findings', []))} findings, "
            f"record {path.name}")
    if open_high:
        return False, f"open high findings {open_high} in {path.name}"
    if res["verdict"] != "accept" and any(marks.get(str(n)) is not False
                                           for n in range(1, len(res.get("findings", [])) + 1)):
        return False, f"verdict {res['verdict']} with findings not rejected in {path.name}"
    return True, line


def main_worktree() -> Path:
    out = subprocess.run(["git", "worktree", "list", "--porcelain"], text=True,
                         capture_output=True, check=True).stdout
    return Path(out.splitlines()[0].split(" ", 1)[1])


def insert_trailer(message: str, line: str) -> str:
    """Put the line before the attribution trailer block, or at the end."""
    marker = "\n\nCo-Authored-By:"
    if marker in message:
        head, tail = message.split(marker, 1)
        return f"{head}\n\n{line}{marker}{tail}"
    return f"{message}\n\n{line}"


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("branch")
    ap.add_argument("-m", "--message", required=True)
    ap.add_argument("--full", action="store_true")
    ap.add_argument("--map", action="store_true")
    ap.add_argument("--target", default="main")
    ap.add_argument("--task", help="TASK-NNNN of the branch: needed when the diff is R2 or tools/**")
    ap.add_argument("--no-second-review", metavar="REASON",
                    help="waive the ADR-0015 second review; the reason goes into the merge message")
    ap.add_argument("--allow-status", action="store_true",
                    help="accept status changes made on the branch (new items only)")
    args = ap.parse_args()

    root = main_worktree()
    head = git("rev-parse", "--abbrev-ref", "HEAD", cwd=root).stdout.strip()
    if head != args.target:
        print(f"land: the main worktree is on '{head}', not '{args.target}'")
        return 4
    base = git("rev-parse", args.target, cwd=root).stdout.strip()
    if git("rev-parse", "--verify", "--quiet", args.branch, cwd=root, check=False).returncode != 0:
        print(f"land: unknown branch {args.branch}")
        return 4

    head_sha = git("rev-parse", args.branch, cwd=root).stdout.strip()
    mb = git("merge-base", args.target, args.branch, cwd=root).stdout.strip()
    files = git("diff", "--name-only", mb, head_sha, cwd=root).stdout.split()
    risky = needs_second_review(files, root)
    if risky:
        if args.no_second_review:
            note = f"Second-Review: waived ({args.no_second_review})"
        elif not args.task:
            print(f"land: {len(risky)} file(s) are R2 or tools/** (ADR-0015); pass --task TASK-NNNN "
                  "or --no-second-review REASON")
            return 6
        else:
            ok, note = second_review(root, args.task, mb, head_sha, run=True)
            if not ok:
                print(f"land: second review not passed: {note}; main untouched")
                return 6
        print("land:", note)
        args.message = insert_trailer(args.message, note)

    tmp = root / ".claude" / "worktrees" / f"_land-{int(time.time())}"
    git("worktree", "add", "--detach", str(tmp), base, cwd=root)
    try:
        m = git("merge", "--no-ff", "-m", args.message, args.branch, cwd=tmp, check=False)
        if m.returncode != 0:
            files = git("diff", "--name-only", "--diff-filter=U", cwd=tmp, check=False).stdout.split()
            print("land: merge conflict, main untouched. Conflicting files:")
            for f in files:
                print("  -", f)
            if not files:
                print(m.stdout + m.stderr)
            return 3
        # Only the Orchestrator changes status fields (set-status on main), never a branch.
        d = git("diff", "-U0", base, "HEAD", "--", "development", "docs/decisions",
                "docs/research", cwd=tmp, check=False).stdout
        changed = [l for l in d.splitlines()
                   if l.startswith(("+status:", "-status:")) and not l.startswith(("+++", "---"))]
        if changed and not args.allow_status:
            print("land: the branch changes registry status fields; main untouched.")
            for l in changed:
                print("  ", l)
            return 5
        cmd = [sys.executable, "tools/development/verify.py"]
        if args.full:
            cmd.append("--full")
        if args.map:
            cmd.append("--map")
        if subprocess.run(cmd, cwd=tmp).returncode != 0:
            print("land: verify failed on the merged tree, main untouched")
            return 1
        sha = git("rev-parse", "HEAD", cwd=tmp).stdout.strip()
        now = git("rev-parse", args.target, cwd=root).stdout.strip()
        if now != base:
            # main moved while verify ran. If only non-code files moved, merge again
            # on the new main and accept it when the code tree equals the verified one.
            moved = git("diff", "--name-only", base, now, cwd=root).stdout.split()
            code = [f for f in moved if f.startswith(CODE_PATHS) or f == "Singularity.sln"]
            if code:
                print("land: main moved in code paths while verify ran; nothing landed. Run again.")
                for f in code[:10]:
                    print("  -", f)
                return 4
            git("reset", "--hard", now, cwd=tmp)
            m = git("merge", "--no-ff", "-m", args.message, args.branch, cwd=tmp, check=False)
            same = m.returncode == 0 and git("diff", "--quiet", sha, "HEAD", "--", *CODE_PATHS,
                                             "Singularity.sln", cwd=tmp, check=False).returncode == 0
            if not same:
                print("land: could not re-merge on the moved main; nothing landed. Run again.")
                return 4
            sha = git("rev-parse", "HEAD", cwd=tmp).stdout.strip()
        ff = git("merge", "--ff-only", sha, cwd=root, check=False)
        if ff.returncode != 0:
            print("land: main has conflicting local changes; nothing landed.")
            print(ff.stdout + ff.stderr)
            return 4
        print(f"land: {args.branch} landed on {args.target} as {sha[:7]}")
        return 0
    finally:
        git("worktree", "remove", "--force", str(tmp), cwd=root, check=False)


if __name__ == "__main__":
    sys.exit(main())
