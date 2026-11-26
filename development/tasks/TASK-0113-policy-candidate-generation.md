---
id: TASK-0113
title: "Policy line candidate by generation and DesignView SlotGeneration rename (DEBT-0053, DEBT-0055)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
review_ref: "reviewer: APPROVE (original); Codex findings fixed inside TASK-0115 (SliceObserver.Newer, one Candidate ordering, LiveParent), reviewer APPROVE 7d8d0b01; landed c00fbb3"
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

## Codex second review (2026-11-19, record review-TASK-0113-797780a8-*-codex.json): changes_requested
Run after landing (catch-up, DEBT-0063). Findings to check and fix in the horizon/handle schema train:
1. [high] PolicySystem.Products.cs:420: equal-platform candidates are ordered by prototype entry (`StageOMonth`), which can select the older production line after slot reuse.
2. [high] SliceObserver.cs:468: Lab still assigns candidate and working roles by slot index and can reject valid F17b close decisions.
3. [medium] PolicyInputsAdapter.cs:21: training can still target the working line when fleet and closure policy select the reused lower slot.
4. [medium] ProductAiTests.cs:495: the regression test never reuses a slot or exercises the policy decision.
