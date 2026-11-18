---
id: TASK-0083
title: "Tests for lab bench: statistics, manifest, plan, CLI"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: test-engineer
category: lab
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0064, EXP-0007]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "TASK-0064 handoff, light"
created: 2026-11-18
---

# TASK-0083 — Tests for lab bench: statistics, manifest, plan, CLI

## Цель
Тесты для `lab bench` (TASK-0064) в `tests/Singularity.Lab.Tests`.

## Описание
По списку simulation-engineer в «Результате» TASK-0064:
1. `BenchStats.Percentile` (тип 7): n = 1; p = 0 и 1; [1..10] p50 = 5,5, p95 = 9,55; неотсортированный вход; пустой вход, NaN, p вне [0, 1] — исключение.
2. `Summarize` согласован с `Percentile`.
3. `BenchManifest.Parse`: отказ на неизвестный ключ, `kind` ≠ bench, пустые threads/seeds, повтор id блока; `grid: benchmark` = 24 точки; EXP-0007-bench = 114 процессов (A 15, B 15, C1 6, C2 3, C3 3, D 72); хеш не зависит от форматирования.
4. `Plan()`: детерминированная перестановка; другой `shuffle_seed` — другой порядок; `DailyFirst` == чётная реплика.
5. `CopyBench.Pick(C, pct)`: ⌊C·pct/100⌋, 0 → пусто, 100 → все.
6. `CopyBench.Clone`: тот же `StateHash`; R2(в) не меняет состояние.
7. CLI: неизвестный режим/ключ → код 2; `--percents 101` отклоняется; `--warmup/--measure` с манифестом ставят `smoke: true`.

## Критерии приёмки
- [ ] Все тесты зелёные; найденные дефекты — списком в «Результате», src/ не менять.

## Результат
