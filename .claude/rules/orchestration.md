# Orchestration rules (in force from 2026-11-19)

Source: `docs/reviews/project-review-2026-11-19.md`. Current plan: `development/PLAN.md` (its "Фокус" section is printed at session start). These rules bind the Orchestrator in every session.

## 1. Focus and work in progress
- Lane A is the critical path named in `development/PLAN.md` "Фокус". It gets agents first.
- Lane B is everything else (polish, side research, debt). At most 2 Lane B agents run at once. Lane B never touches Kernel, Core or Session while a schema train or a benchmark freeze is open.
- Do not create tasks to keep agents busy. Before a task starts, name the Focus item it moves; if there is none, it goes to the backlog.
- A proposal that adds scope names the milestone it belongs to and what it displaces. Do not recommend a "full" or "wide" option that no play test supports.

## 2. Landing and CI
- A branch lands only through `python3 tools/development/land.py BRANCH -m "..."`: the merged tree is built and tested before `main` moves. Use `--map` for map changes and `--full` for `lab/analysis` or `tools/llm_worker` changes.
- Push after landing, then read CI: `gh run list --branch main --limit 1`. A red CI on `main` is the first task of the session; SessionStart prints it.
- `python3 tools/development/verify.py` is the local CI. Do not run the suite by hand in ad-hoc worktrees.
- Prune merged worktrees at the end of a phase: `python3 tools/development/worktrees.py --prune`.
- Map sources cache: `~/.cache/singularity/map` (`SINGULARITY_MAP_CACHE`); `verify.py` sets it. Give this path to agents that build the map.

## 3. Status discipline
- A code or data task becomes DONE only after reviewer APPROVE: `registry.py set-status TASK-X DONE --review "<who: verdict, commit>"`. The tool refuses otherwise. A UI task also needs ui-art-director PASS on 100 % frames (`docs/ui/UI_DESIGN_SYSTEM.md` §12, #417).
- Never tell the owner or an agent that a check passed before its output is read.
- `set-status` regenerates `BACKLOG.md`; commit them together.

## 4. Agents
- Routing: `src/Singularity.Lab/**` and `lab/**` go to simulation-engineer; client code goes to developer, then to ui-art-director; a data edit in `mods/` or `tools/map/spec` needs a check against the actual data (for example the composition of a region), not only against sources.
- The first prompt of a code task carries the review checklist: tests fail without the fix; no `[ThreadStatic]` or static buffer keeps world-bound state; entities are addressed by handle, not by bare index; no allocations on the tick path; what was not verified.
- After two CHANGES rounds on one task, stop: read the diff yourself, then split the task or change the approach.
- Every agent that changes code or data runs in an isolated worktree and commits on its branch only; no agent commits on `main` (a commit on `main` during a landing makes `land.py` refuse it). `land.py` also refuses a branch that changes `status:` fields.

## 5. Schema train
- Changes of `SchemaVersion`, table columns or golden hashes land as one train per day at most: collect the tasks, land them together, re-record the goldens once, explain each re-record.

## 6. Experiments and benchmarks
- No series longer than 15 minutes before a pilot of its validity rules on the real machine (a smoke with the real gate).
- A benchmark series is run by the owner from a terminal, with no agent sessions (#355). Do not start agents during a series window.
- Measure at the scale the decision is about. State in the pre-registration which decision the outcome changes.

## 7. Decisions
- D1 goes to the owner (AskUserQuestion): product direction, scope, milestone content, anything irreversible, a change of an ACCEPTED ADR or of an owner decision, work costing more than about one day.
- D2 is decided by the Orchestrator: a reversible detail inside an approved design. Record it in `docs/decisions/ORCH_DECISIONS.md` (O-NNN) and list new rows in the next report; the owner may veto any row.
- Ask the owner at most 4 questions per call, the critical ones first. Every option states its cost in scope or time.
- Write every owner question in plain Russian for a reader outside the project (owner, 2026-11-19: he picks "recommended" either from trust or because the wording is unclear). Explain the situation in 2–4 sentences inside the question; no bare ids (#NNN, ADR, TASK) and no internal terms without a few words of explanation; each option says what will happen in practice and what the owner will see. If a question cannot be made clear in the dialog, explain it in chat first and then ask.

## 7a. Showing results
- The owner judges by looking (owner, 2026-11-19). After every task that changes what the player sees, show it: frames at 100 % in the report (SendUserFile or a gallery page), and keep `./run-client.sh` (or the current run command) working so that the owner can start the client himself.
- The client runs fullscreen or as a borderless full window (#424); windowed sizes are for tests only.

## 8. State hygiene
- At each phase end and before a handoff: update `CURRENT_STATE.md` "Сводка", `development/PLAN.md` "Фокус" and, when a milestone changes, `development/ROADMAP.md`.
- `docs/architecture/ARCHITECTURE.md` lists ADR statuses: update it when an ADR changes status.

## 9. Autonomy and capacity (owner, 2026-11-25)
- Work at full capacity without asking: skill `/autopilot`. Agents start on their own while `tools/development/capacity.py` shows free slots; docs-only agents do not count.
- The game client starts only with the owner's permission: hook `.claude/hooks/guard_shots.py` needs a live permit from `tools/development/client_permit.py grant`, granted only after the owner allows a shot batch; one `--only` batch per task.
- Tests run in batches: module tests once per change set; the hook `.claude/hooks/guard_load.py` refuses test and build runs while the CPU is full.
- Questions to the owner are collected in `development/OPEN_QUESTIONS.md` and asked all at once when nothing can move without them (stop condition of `/autopilot`).
