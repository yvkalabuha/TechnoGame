---
name: watch
description: Check for stale tasks, unlanded or orphan branches and silent agents, and act on each. Use at session start when the hook reports stale items, before a handoff, and whenever the owner asks what is running.
---

# /watch — control of tasks, branches and agents

1. Run `python3 tools/development/watch.py`.
2. For every line act, do not just report:
   - STALE task IN_PROGRESS with no branch: no agent is working. Start the agent, or set the task back to APPROVED with a note.
   - STALE task in REVIEW: the review or the landing was dropped. Get the verdict, then `/land`.
   - UNLANDED branch of an open task: land it or send it back; never leave finished work on a branch.
   - ORPHAN branch: read its last commit. Land what is needed; list the rest for deletion (deleting a branch with unmerged commits needs the owner's yes).
   - DONE? task with an unlanded branch: the task was closed without landing. Reopen it or land the branch.
   - SILENT agent: ask it for its report with SendMessage; if it does not answer, check its branch with `git log main..<branch>` and take over or restart.
3. Tell the owner in plain words what was stuck and what you did.
