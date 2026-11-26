---
name: recheck
description: Send fixed work back to the same reviewer with what changed since the last verdict. Use after a developer fixes reviewer or Codex findings, before landing again.
---

# /recheck - re-review after fixes

1. Find the last verdict: reviewer agent id and the commit it reviewed (task file "Результат" or the report).
2. List what changed: `git log --oneline <old>..<new>` on the branch and, per finding, "fixed in <sha>" or "moved to DEBT-NNNN".
3. Add the rejection memory block when it exists (`python3 tools/development/rejections.py for <paths>`, TASK-0204).
4. SendMessage to the same reviewer: branch, old and new commit, the list, the test command by absolute path, "verdict ≤ 8 lines". If the reviewer is gone, start a new reviewer with the same list.
5. Tools (tools/development, .claude/hooks, skills): after 3 Codex rounds land with reviewer APPROVE (#592) unless a finding is destructive or irreversible. Core and game: #432.
