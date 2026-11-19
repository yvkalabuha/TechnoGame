---
name: land
description: Land a finished agent branch on main: second review by Codex, build and tests of the merged tree, task status with the review record, push and CI check. Use for every branch that changes code or data.
argument-hint: "<branch> <TASK-NNNN>"
---

# /land — bring a branch to main

1. Check that the task has its reviews: reviewer APPROVE for code; ui-art-director PASS on real 1920x1080 frames for UI (`.claude/rules/ui.md`). If not, stop and get them.
2. Run, in the background if other work waits:
   `python3 tools/development/land.py <branch> --task <TASK-NNNN> -m "merge: <TASK-NNNN> <what>" ` plus the attribution trailer in `-m`; add `--map` for map changes and `--full` for `lab/analysis` or `tools/llm_worker`.
   - Exit 6: the Codex second review (ADR-0015) found something. Read the findings it printed. A real finding goes back to the developer; a false one is closed with `python3 -m tools.llm_worker review-confirm <record> --finding N --rejected`, and the reason goes into the task file. Then run `land.py` again.
   - Exit 1: verify failed on the merged tree; main is untouched. Send the failure to the developer.
   - Exit 3 or 4: a conflict, or main moved in code paths. Ask the developer to merge main into the branch, or run again.
3. Do not commit code paths on main while a landing runs.
4. After exit 0: `python3 tools/development/registry.py set-status <TASK-NNNN> DONE --review "<who: verdict, commit>; codex <verdict>"`, fill the task "Результат", commit.
5. `git push origin main`, then `gh run list --branch main --limit 1`. A red run is the next task.
6. If the task changed what the player sees, run `/show`.
