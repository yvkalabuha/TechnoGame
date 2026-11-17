---
id: TASK-0063
title: "Session DailyTickProbe flag for direct daily-mode benchmark (ADR-0016 T1a)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: core
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0016, EXP-0007]
approved_by: orchestrator
approved_at: 2026-11-17
approval_ref: "ADR-0016 ACCEPTED (#276), light task inside approved plan"
created: 2026-11-17
---


## Goal (T1a)
Execution-only flag `DailyTickProbe` in `SliceRunConfig`: an empty daily system so that every day is a non-empty tick (table copy, hash, view snapshot), per ADR-0016 §5 and note §2. It adds no columns. A test proves that slice golden hashes are unchanged with the flag off, and that the flag is execution-only (no StateHash effect beyond tick scheduling, as specified in the note).

## Inputs
- docs/decisions/ADR-0016-tick-budget.md (ACCEPTED, rev 5)
- docs/architecture/notes/tick-benchmark-m0.md §2, §5
- DECISIONS_LOG #269–#272, #276

## Acceptance
- Matches the ADR-0016 rules; the full `dotnet test` is green; zone_check is clean.
