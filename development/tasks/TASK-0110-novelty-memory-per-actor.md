---
id: TASK-0110
title: "Novelty memory per actor: tried alternatives survive slot reuse (DEBT-0054)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M1
agent: developer         # ответственный агент
category: core
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0099, ADR-0005]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "DEBT-0054, spec C10 (light)"
created: 2026-11-18
---

# TASK-0110 — Novelty memory per actor: tried alternatives survive slot reuse (DEBT-0054)

## Goal
Fix DEBT-0054. After slot reuse (TASK-0099), `ProductsSystem.Create` builds `used` by walking the slots, so the alternatives of a project withdrawn in stage О become "new" again. Spec C10 defines novelty through the actor's knowledge, not the slot. Design note: `docs/game-design/notes/slot-reuse-novelty-memory.md`.

## Description
- A monotone per-actor column "alternative a reached О". It is set when a project enters О and for the starting fleet.
- `Create` takes `used` from the column.
- SchemaVersion +1. The golden hashes change: explain them; the trajectory must stay identical until the first slot reuse (~month 265).

## Acceptance criteria
- [ ] Tests:
  - (a) withdrawn in О, then reuse: novelty unchanged;
  - (b) withdrawn in П: the alternatives stay new;
  - (c) the starting fleet;
  - (d) a different slot-freeing order gives the same novelty;
  - (e) L1/L2 and column save/load.
- [ ] `dotnet test`, `lab selftest` pass; reviewer APPROVE.
