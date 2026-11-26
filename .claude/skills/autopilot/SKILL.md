---
name: autopilot
description: Run the project at full capacity without asking the owner — dispatch ready tasks while the CPU has room, land finished branches, batch tests and client shots, and stop only when nothing can move without the owner, then ask all collected questions at once. Use when the owner says to work on your own or asks what else can run in parallel.
---

# /autopilot — work at full capacity, ask in one batch

Owner rule 2026-11-25: launch the game only with permission; tests in batches; agents as many as the CPU allows,
started without asking; work without questions; when nothing can move without answers, stop and ask all of them.

## Loop (repeat until the stop condition)
1. **Capacity.** `python3 tools/development/capacity.py`. Start new heavy agents (code, Lab, client) only while
   `free agent slots` > 0; docs-only agents (designers, research, reviewers, critics) may run beyond it (they do not load the CPU).
2. **Ready work.** `python3 tools/development/registry.py list` — APPROVED tasks whose depends_on are DONE, plus review and
   gate steps of tasks in REVIEW. Prefer PLAN.md "Фокус" lane A. Dispatch with `/dispatch` rules (worktree for code).
3. **Land.** One landing at a time (`/land`). Rule #432 after three Codex returns. Never commit on main during a landing.
4. **Reviews.** Every code branch: reviewer; UI: ui-art-director + Gemini on frames.
5. **Client shots.** Agents never launch the client (hook guard_shots.py). Collect the frames each UI task needs; when one or
   more batches are ready, put "run the shot batch for TASK-…" into the question list. After the owner allows:
   `python3 tools/development/client_permit.py grant 30`, run ONE `--only` batch per task, then `client_permit.py revoke`.
6. **Tests.** Agents run only their module tests, once per change set, with `nice -n 10`; the hook guard_load.py refuses
   test runs when the CPU is full (the agent waits and retries).
7. **Questions.** Never ask mid-loop. Write each open owner question (D1) to the "Очередь вопросов" list in
   `development/OPEN_QUESTIONS.md` with what it blocks. Decide D2 yourself (`ORCH_DECISIONS.md`).
8. **Stop condition.** Stop when no agent is running, nothing is landable, and every remaining item waits on an owner answer or
   a client batch permission. Then ask ALL queued questions with AskUserQuestion (4 per call, as many calls as needed, critical
   first, plain Russian per orchestration §7), record the answers, and resume the loop.

## Agent control (owner #583)
Each loop: `python3 tools/development/watch.py` (with `--actions` once TASK-0197 lands). An agent silent > 2 h: SendMessage asking for status; silent 1 h more: TaskStop, note in the task, redispatch with its branch. An agent that reported done but whose branch is not on review: start the reviewer at once.

## Shot batch (owner #583)
Collect frames of ALL UI tasks waiting for shots into one batch (`tools/development/shot_batch.py` once it exists). Before asking, check each branch contains the new model (`git merge-base --is-ancestor f12684f <branch>`). One permission covers the batch.

## Stop (owner #583)
`/autopilot stop` or the owner saying stop: start no new agents, let running ones finish, land nothing new without the owner, write the handoff, then report. State file: `.claude/.state/autopilot.json` (`tools/development/autopilot.py` once TASK-0197 lands).

## Questions (owner #587)
Every owner question goes through AskUserQuestion, never as a chat list. If the call times out, ask again when the owner is back.

## Drift check (owner #529)
After every 5 finished items: re-read docs/vision/NORTH_STAR.md, the "Фокус" of development/PLAN.md, the latest handoff and the last 15 owner decisions; compare with what is running; write any drift (forgotten owner wish, work outside the focus) into the handoff and fix the queue.

## Report while running
Short status after each finished item (what landed, what was found). No questions in the status.
