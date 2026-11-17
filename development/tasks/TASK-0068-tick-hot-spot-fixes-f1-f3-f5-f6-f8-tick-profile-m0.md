---
id: TASK-0068
title: "Tick hot-spot fixes F1-F3, F5, F6, F8 (tick profile M0)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0016, EXP-0007]
approved_by: orchestrator
approved_at: 2026-11-17
approval_ref: "DECISIONS_LOG #283 (owner: fix hot spots before the benchmark); light, no behaviour change"
created: 2026-11-17
---


## Goal
Implement fixes F1, F2, F3, F5, F6 and F8 from docs/simulation/tick-profile-m0.md §4. They are pure performance changes: no simulation behaviour changes, StateHash identical, golden hashes unchanged.

F4 (parallel ViewBuilder.Inputs) and F7 (reuse the view on days without a monthly step) are out of scope: they go to systems-architect first. The daily-mode hash algorithm is out of scope too: it needs gate+research.

## Acceptance
- Every fix has an equivalence test against the old path (same outputs), as §4 specifies.
- All golden tests are unchanged; the full `dotnet test` is green; determinism L1/L2 holds.
- Lab `selftest` is green (the `synth:` checks).
- Measured before and after on the synthetic points (6, 2000) and (200, 500) for 1–2 months, using the profile's method. Record the numbers in «Результат».
