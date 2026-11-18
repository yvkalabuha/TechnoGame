---
id: TASK-0103
title: "Tick allocations and GC: 1.1 GB per month, gen2 collections (EXP-0007 P8)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: simulation-engineer
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [EXP-0007, TASK-0102]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "EXP-0007 P8 (light)"
created: 2026-11-18
---

# TASK-0103 — Tick allocations and GC: 1.1 GB per month, gen2 collections (EXP-0007 P8)

## Goal
EXP-0007 P8 (high): the tick allocates 1.1 GB per month against a 1 MB limit. There are 243–288 gen2 collections per window against a limit of 0. GC pauses take about 25 % of the time. Peak memory is 1.96 GB. Find and remove the sources before the re-registered series (TASK-0102).

## Description
1. simulation-engineer profiles allocations by system and method on point A (t8, 1800 months). Use dotnet-counters/dotnet-trace or the existing Lab sampler. Write the report into this task.
2. developer fixes the hot spots inside approved architecture (ADR-0017 columns, ADR-0003 rules). Reviewer checks every diff.

## Acceptance criteria
- [ ] The allocation report by source is in this task.
- [ ] Steady-state allocations per month are at or below the P8 limit, or the gap is explained with a follow-up item.
- [ ] Golden hashes are unchanged; `dotnet test`, `lab selftest` pass.

## Related
EXP-0007 (Results, P8), ADR-0016, ADR-0017, DEBT-0045.
