---
id: TASK-0066
title: "Tick benchmark run and outcome (ADR-0016 T3)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: simulation-engineer
category: lab
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0064, TASK-0065]
related: [ADR-0016, EXP-0007]
approved_by: orchestrator
approved_at: 2026-11-17
approval_ref: "ADR-0016 ACCEPTED (#276), light task inside approved plan"
created: 2026-11-17
---


## Goal (T3)
Run the pre-registered benchmark on the owner's i7-11800H:
- `dotnet-trace` diagnostics and unit costs;
- the outcome per ADR-0016 §4;
- EXP-0007 completed with provenance.

Any valid outcome closes the MS-M0 benchmark criterion (#272).

## Inputs
- docs/decisions/ADR-0016-tick-budget.md (ACCEPTED, rev 5)
- docs/architecture/notes/tick-benchmark-m0.md §2, §5
- DECISIONS_LOG #269–#272, #276

## Acceptance
- Matches the ADR-0016 rules; the full `dotnet test` is green; zone_check is clean.
