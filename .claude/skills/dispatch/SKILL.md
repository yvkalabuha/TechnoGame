---
name: dispatch
description: Brief an agent for a task so that it can finish in one run: zone check, worktree, review checklist, verification, report rules. Use before every Agent call that writes code, data or documents.
argument-hint: "<TASK-NNNN> <agent>"
---

# /dispatch — brief an agent

1. Name the lane: A (an item of `development/PLAN.md` "Фокус") or B (at most 2 agents). No Focus item, no dispatch.
2. Check the write zone before the call: `python3 tools/development/zone_check.py <agent>` shows the zone source; `src/Singularity.Lab/**` and `lab/**` go to simulation-engineer, `docs/**` to the designers and analysts, code and tests to developer and test-engineer.
3. Code and data agents run with `isolation: "worktree"` and commit on their branch only.
4. The prompt contains: the task path; the files to read; what is already decided (decision numbers with a few words each); the scope and what not to touch (other agents' areas); the verification commands; the commit trailer.
5. The prompt carries the review checklist: a new test fails without the fix (say whether it was checked); no static or `[ThreadStatic]` buffer keeps world-bound state; entities are addressed by handle; no allocations on the tick path; golden hashes change only with an explanation; UI follows `docs/ui/UI_DESIGN_SYSTEM.md` §12 and is shot at 1920x1080.
6. The prompt ends with the report rules: run everything in the foreground, one final report, a list of what was not verified, the length limit.
7. After the call: `registry.py set-status <TASK> IN_PROGRESS` only when the agent really started. A task without a running agent stays APPROVED.
