---
name: idea
description: Register a new idea and route it through the idea protocol at the right level (Light or Gate), choosing the minimal set of agents.
disable-model-invocation: true
argument-hint: "<idea in a few words>"
---

# /idea — idea protocol

Protocol levels are defined in `docs/architecture/DEVELOPMENT_FRAMEWORK.md` §6. Summary:

| Level | When | Agents | Approval |
|---|---|---|---|
| light | task inside approved architecture and mechanics; bugfix; refactoring | developer, test-engineer, reviewer | orchestrator |
| gate | new or changed mechanic; new subsystem; hypothesis; ADR | domain agent + critic + advocate | owner |
| gate+research | fundamental concept, simulation principles, ADR-0002..0007, save or data format, anything irreversible | + research-scientist | owner |

Steps:

1. **Check duplicates.** `python3 tools/development/registry.py list` and search `docs/research/` for the topic. If the idea already exists, report it and stop.
2. **Classify.** Choose the level using the table. Hard rule: ADRs and changes to determinism rules, save format or data format are at least `gate`.
3. **Register.** Game or model idea → HYP via `/new HYP`; technical work → TASK via `/new TASK`; architectural decision → ADR via `/new ADR`. Fill CONTEXT (problem) and alternatives.
4. **Confirm with the owner** (AskUserQuestion) when the level is gate or higher: show the level and the planned agents. Do not start agents before the answer.
5. **Run.**
   - light: create the task as PROPOSED, approve it yourself (`approved_by: orchestrator`, `approval_ref`: the approved milestone), then developer → test-engineer → reviewer.
   - gate: domain agent writes ANALYSIS/DESIGN in the item; then `/review-gate <item path>`; then ask the owner for APPROVAL.
   - gate+research: research-scientist writes an RR first, then as gate.
6. Record disagreements in the item's "Разногласия" section only if there are real disagreements.

Idea: $ARGUMENTS
