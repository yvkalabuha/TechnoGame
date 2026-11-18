---
id: TASK-0107
title: "Tick path allocations to <= 1 MiB/month (ADR-0023 p.2.1)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0023, TASK-0103, TASK-0106, EXP-0008]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "ADR-0023 p.2.1, 2.5 (light)"
created: 2026-11-18
---

# TASK-0107 — Tick path allocations to <= 1 MiB/month (ADR-0023 p.2.1)

## Goal
The split `--mode alloc` probe (TASK-0106) measures the tick path at point A at about 2.26 MiB/month, estimated by subtraction, because the snapshot is built inside Step. The ADR-0023 p.2.1 limit is 1 MiB/month. The fresh snapshot is 6.86 MiB (X = 8 MiB, #412), the snapshot without nodes 0.73 MiB, LOH 0, gen2 1 in 3 months. Bring the tick path within the limit before the EXP-0008 series.

## Description
1. Make the tick path measurable directly: separate the snapshot build from Step in the probe, or add a probe that skips the views.
2. Remove the remaining tick-path allocations. Candidates from TASK-0103: AiFleetCommands, ViewBuilder.Decisions and Inputs, DesignView, small lists. Arithmetic and order stay unchanged; golden hashes must not change.
3. If gen2 is still > 0, find its source.

## Acceptance criteria
- [ ] Tick path ≤ 1 MiB/month at point A (direct probe), LOH 0, gen2 0 over 12 months; numbers in this task.
- [ ] Golden hashes unchanged; `dotnet test`, `lab selftest` pass; reviewer APPROVE.
