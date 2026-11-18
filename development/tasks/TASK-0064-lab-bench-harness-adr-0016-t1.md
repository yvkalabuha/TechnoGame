---
id: TASK-0064
title: "lab bench harness (ADR-0016 T1)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: lab
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0063, TASK-0065]
related: [ADR-0016, EXP-0007]
approved_by: orchestrator
approved_at: 2026-11-17
approval_ref: "ADR-0016 ACCEPTED (#276), light task inside approved plan"
created: 2026-11-17
---


## Goal (T1)
`lab bench` command per note §5 T1:
- threads 1/4/8/16 and ChunkSize 1/8/32;
- monthly and daily modes;
- synthetic background load;
- the fallback `k_copy` path;
- allocations, GC and CPU frequency sampling;
- JSON output with provenance and conditions;
- a StateHash validity check across threads and ChunkSize.

Uses the TASK-0065 world generator.

## Inputs
- docs/decisions/ADR-0016-tick-budget.md (ACCEPTED, rev 5)
- docs/architecture/notes/tick-benchmark-m0.md §2, §5
- DECISIONS_LOG #269–#272, #276

## Acceptance
- Matches the ADR-0016 rules; the full `dotnet test` is green; zone_check is clean.

## Дополнение 2026-11-18 (Orchestrator): замеры ADR-0017
ADR-0017 (ACCEPTED, #317) реализован в TASK-0078; его R1–R4 замеряются в EXP-0007. Харнесс поддерживает:
- флаги Session `HashCheckpoints` / `HashEveryTick` и вариант «no-op-команда каждый день» (R4 б);
- переключатель Kernel `Table.FullCopyMode` (полная копия против CoW);
- R1: `StateHash` на (200, 2000) и (200, 500), прогрев 100, 1000 вызовов, медиана и p95;
- R2: (а) полная копия, (б) CoW на месячном шаге среза (открытые колонки, цена копии, swap), (в) синтетическая дневная система, пишущая 0/10/50/100 % колонок, (г) месяц `M` с CoW против полной копии при t1 и t8, (д) slab — одно memcpy той же таблицы; 1000 повторов;
- R3 (справка): `XxHash3`/`XxHash128` той же таблицы, мс на вызов (только Lab, не ядро).
Пороги и правила — ADR-0017 «Research»; R2(г) обязателен (DEBT-0045).
