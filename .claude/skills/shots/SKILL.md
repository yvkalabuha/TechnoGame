---
name: shots
description: Run one shared shot batch for every UI task waiting for frames - collect, check branches, ask the owner once, shoot, revoke, Gemini verdict, Claude review, show. Use whenever UI tasks wait for frames (owner #583, #591).
---

# /shots - one shared batch of client frames

1. **Collect.** `python3 tools/development/shot_batch.py`. Every client task in REVIEW/IN_PROGRESS must have a `shots:` list (`{branch, only, flags}`) in its frontmatter; fix any `SKIP ... missing shots field` line first.
2. **Check every branch** (ERR-0001, ERR-0005): `git merge-base --is-ancestor f12684f <branch>` and no client commits on main missing from it (shot_batch prints both). A branch that fails is left out and named in the question.
3. **Ask once** with AskUserQuestion (#587): which screens, how many frames, about how long, which branch, "if the first frame is wrong I stop at once".
4. **After the owner says yes:** `python3 tools/development/client_permit.py grant 30`, run the printed commands one by one, read the run log and the first frame yourself before the next command, then `python3 tools/development/client_permit.py revoke` (also on any failure).
5. **Verdict.** `python3 -m tools.llm_worker visual` (agy, Gemini lead #578) on the frames of each task; then the ui-art-director agent reviews the verdict against §12 and writes docs/ui/reviews/task-NNNN-visual.md.
6. **Show** the owner with /show (frames at 100 %).
7. A wrong-version or failed batch is an error: `/error`.
