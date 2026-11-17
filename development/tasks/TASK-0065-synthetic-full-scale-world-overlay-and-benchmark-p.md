---
id: TASK-0065
title: "Synthetic full-scale world overlay and benchmark pre-registration (ADR-0016 T2)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: simulation-engineer
category: lab
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0016, EXP-0007]
approved_by: orchestrator
approved_at: 2026-11-17
approval_ref: "ADR-0016 ACCEPTED (#276), light task inside approved plan"
created: 2026-11-17
---


## Goal (T2)
Synthetic world generator as a Lab-only overlay (ADR-0010):
- actors 6/24/96/200 × nodes 180/500/2000;
- clones plus a cross-edge variant.

Write the pre-registration into EXP-0007 before any timed run: protocol, statistics, outcome table and diagnostics, all per ADR-0016.

## Inputs
- docs/decisions/ADR-0016-tick-budget.md (ACCEPTED, rev 5)
- docs/architecture/notes/tick-benchmark-m0.md §2, §5
- DECISIONS_LOG #269–#272, #276

## Acceptance
- Matches the ADR-0016 rules; the full `dotnet test` is green; zone_check is clean.
