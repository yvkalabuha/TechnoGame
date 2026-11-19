---
id: TASK-0113
title: "Policy line candidate by generation and DesignView SlotGeneration rename (DEBT-0053, DEBT-0055)"
status: REVIEW         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M1
agent: developer         # ответственный агент
category: core
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0099]
approved_by: orchestrator
approved_at: 2026-11-19
approval_ref: "DEBT-0053, DEBT-0055 (light)"
created: 2026-11-19
---

# TASK-0113 — Policy line candidate by generation and DesignView SlotGeneration rename (DEBT-0053, DEBT-0055)

## Goal
Close DEBT-0053 and DEBT-0055.

## Description
1. `PolicySystem.Fleet.Candidate` picks the candidate line by launch month or generation, not by slot index; after slot reuse a higher index is not a newer project.
2. `DesignView.Generation` becomes `SlotGeneration` (API surface v12), with an XML doc; `Gen` stays the technology generation.

Golden hashes may change only after the first slot reuse (~month 265); explain each re-record.

## Acceptance criteria
- [ ] A test with ≥ 2 lines in С after slot reuse: the candidate is the newest project.
- [ ] `dotnet test`, `lab selftest` pass; TickPathAllocationTests pass; reviewer APPROVE.
