---
id: TASK-0107
title: "Tick path allocations to <= 1 MiB/month (ADR-0023 p.2.1)"
status: REVIEW         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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

## Результат (developer, commit 1e78510, branch worktree-agent-af65edb8b9d14490a)
- Direct tick-path probe, point A, t1 (SkipViews seam): 2.17 MiB/month before, then 0.49–0.59 MiB in steady months. Steps: profile items in scratch plus lazy outcome strings 1.40; per-design memo plus pooled policy decisions 0.67; fleet book from the memo 0.52–0.60.
- Yearly month (12): 1.85 MiB, above 1 MiB. Sources: WhyNot closures (NodeClosure bool arrays) from TrainingTargetP3/Compose, annual ProductPresets.Variants/CandidateValue, WhyList per actor. 12-month mean about 0.6 MiB.
- Lab probe: p50 0.48–0.59 MiB, snapshot 6.6 MiB (X = 8), gen2 0, GC pauses 0.29 %.
- Golden hashes unchanged; full suite green; `lab selftest` 57/57.
- Open: the Lab probe should measure the tick path directly via SkipViews (AllocProfile.cs, simulation-engineer).
