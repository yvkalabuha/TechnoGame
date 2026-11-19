---
id: TASK-0115
title: "End-to-end project handle through Session, AI, UI and command log v2 (DEBT-0052)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M1
agent: developer         # ответственный агент
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0113]
related: [TASK-0099, TASK-0113, ADR-0005, ADR-0014]
approved_by: orchestrator
approved_at: 2026-11-19
approval_ref: "DEBT-0052, ADR-0005 p.3 (light)"
created: 2026-11-19
---

# TASK-0115 — End-to-end project handle through Session, AI, UI and command log v2 (DEBT-0052)

## Goal
Close DEBT-0052. The project handle `DesignHandle(Slot, Generation)` becomes mandatory through Session, AI, UI and the command log. Design: `docs/architecture/notes/end-to-end-project-handle.md` (tasks H1–H5, §2–§6).

## Description
- Real bug on current main, per the note: an AI cancel decided in month t applies after an external compose has retaken the slot, and cancels the new project. The same holds for a stale client view, ForceProxy keyed by (actor, design), and WhyNotTarget / CancelPending using bare indices.
- `commands.log` v2 with a header line; v1 logs are rejected loudly, with no migration: no saved logs exist and replay is a build artefact (ADR-0013 p.9). This is an Orchestrator decision.
- SeriesStarts/LineCloses take up to two candidates; Core applies the first one that resolves.
- SchemaVersion and TestApiVersion are bumped. Trajectories stay unchanged: check by projections.
- Starts after TASK-0113 is merged: it touches the same Policy and DesignView code.

## Acceptance criteria
- [ ] Tests H1–H5 from the note: a stale handle from UI, from AI (test H3 fails on old main) and from replay; L2; a property test.
- [ ] SLICE_COMPOSITION.md §7.5 and LAYER_CONTRACT.md updated.
- [ ] `dotnet test`, `lab selftest` pass; reviewer APPROVE.
