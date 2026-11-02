---
name: handoff
description: End-of-session procedure. Updates item statuses, regenerates indexes, updates CURRENT_STATE.md, writes a handoff file and commits.
disable-model-invocation: true
argument-hint: "[short note about the session]"
---

# /handoff — end of session

Run these steps in order. Keep everything short.

1. **Statuses.** For every item touched this session (tasks, hypotheses, experiments, ADRs, milestones) set the correct `status:`. Approved items need `approved_by`, `approved_at`, `approval_ref`.
2. **Logs.** New owner decisions → append rows to `docs/decisions/DECISIONS_LOG.md`. New risks / tech debt → rows in `development/RISKS.md` / `development/TECH_DEBT.md`.
3. **Index.** Run `python3 tools/development/registry.py index`, then `python3 tools/development/registry.py check`. Fix every error before continuing.
4. **CURRENT_STATE.md.** Update the "Сводка", "Блокеры" and "Точка продолжения" sections (max 60 lines in the file). Do not edit the generated active-tasks block by hand. Point "Последний handoff" to the new file.
5. **Handoff file.** Create `development/handoff/YYYY-MM-DD-N.md` from `development/templates/handoff.md` (N = next number for today). Max 40 lines. Sections: Сделано, Решения, Открыто, Точка продолжения, Расход сессии (ask the owner whether they ran `/usage`; if not, write "не измерялось").
6. **Commit.** `git add` the changed project files and commit with a Conventional Commits message (`docs:`, `feat:`, `chore:` ...) ending with the attribution trailer. Push only if this ends a phase or milestone (owner policy), never with force.
7. Report to the owner in Russian: 3–5 lines with the continuation point.

Session note from the owner (may be empty): $ARGUMENTS
