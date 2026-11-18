---
id: TASK-0102
title: "Re-registered tick benchmark series (frequency rule revision, #405)"
status: PROPOSED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: gate
milestone: MS-M0
agent: simulation-engineer
category: lab
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0103]
related: [EXP-0007, EXP-0008, TASK-0103]
approved_by:             # owner | orchestrator (orchestrator — только level: light)
approved_at:
approval_ref:
created: 2026-11-18
---

# TASK-0102 — Re-registered tick benchmark series (frequency rule revision, #405)

## Goal
Re-register the M0 tick benchmark: EXP-0007 is INCONCLUSIVE because of the frequency rule P5 and noise A 11.8 %. Owner decision #405: average hardware, default CPU governor, no `performance`.

## Description
1. A new EXP item (re-registration). The frozen EXP-0007 section is not edited. Base it on EXP-0007 "Предложение":
   - no frequency gate for repeat or INCONCLUSIVE; busy-core frequency only as a flag (top-k, k = threads, f_ref under the same load, 0.85);
   - repeat on a foreign-load gate: foreign CPU > 5 % of the window, or the process median > 1.10 × the point median;
   - noise ≤ 10 % as the main criterion;
   - loadavg and the top-5 foreign processes recorded.
2. Gate review (critic + advocate + DeepSeek), then owner approval.
3. Run the full series. It includes what EXP-0007 did not run: P6 profile (top-10 methods, system shares, P, rule 19·P), P9 p.2 (hc, noop, noophc), ADR-0017 R1, R2(а)–(д) with mandatory R2(г) (DEBT-0045), and R3.
4. Run it after TASK-0103, so that the series measures the code without the GC hot spots, with no other agents running (#355).

## Acceptance criteria
- [ ] The re-registration is approved by the owner.
- [ ] The series is complete; the outcome is recorded by the ADR-0016 §4 table.
- [ ] R2(г) is evaluated; DEBT-0045 is closed or a task is created.

## Related
EXP-0007, ADR-0016, ADR-0017, #355, #376, #405. EXP-0007 notes that S_p95 + K_d,p95 = 580 > 500: a partition revision ADR is required before E1.
