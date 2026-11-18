---
id: TASK-0064
title: "lab bench harness (ADR-0016 T1)"
status: REVIEW         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: simulation-engineer
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

## Результат (2026-11-18, simulation-engineer; перенесено Orchestrator)
- [Факт] Команда `lab bench --mode plan|process|validity|r1|r2|kcopy|r3|fref` (`docs/simulation/lab-cli.md`, «lab bench»). Манифест `lab/manifests/EXP-0007-bench.json`: блоки A–D по П2, 114 процессов, Fisher–Yates на `CounterRng(20261117)`, прогрев 24 / замер 120 мес., ChunkSize 8, `hash_checkpoints: false`.
- [Факт] Процесс: проверка StateHash (t1/4/8/16 при c8, t8 при c1/32), месячный и дневной прогоны с новой сессией; JSON — M/T по месяцам, k, K_d, S, аллокации, GC, peak WS, частота, троттлинг. Переключатели `--hash-checkpoints`, `--hash-every-tick`, `--daily-noop`, `--full-copy`, `--background N`.
- [Факт] R2: (а)/k_copy — полная копия + swap на клоне; (б) CoW колонок месячного шага; (в) Lab-only `WriteShareSystem`; (г) две сессии попарно; (д) один `Span.CopyTo`. R3 — System.IO.Hashing (только Lab).
- Смоук (машина занята, loadavg ≈ 19, не замер): R1 p95 16,1 мс; R3 XxHash3 2,2 мс против SHA-256 15,4 мс; R2(б) CoW 1,0 мс против 1,9; R2(в) 100 % — CoW 6,1 мс против 4,7 (может не пройти порог); R2(г) t8 345/344 мс. Process (200, 2000, t8): M p50 523 мс, T p50 6358 мс — **дневной тик с зондом ≈ 200 мс и ≈ 120 МБ аллокаций**, причина [Предп.] ежедневный `ViewBuilder.Snapshot` (F7a не сделан) → TASK-0082 до TASK-0066.
- Тесты статистики, манифеста и CLI — TASK-0083 (test-engineer). `dotnet test` зелёный, selftest 48/48.
