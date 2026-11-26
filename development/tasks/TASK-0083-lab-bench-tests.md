---
id: TASK-0083
title: "Tests for lab bench: statistics, manifest, plan, CLI"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
Тесты: `tests/Singularity.Lab.Tests/BenchTests.cs` (35 тестов; весь проект: 54 зелёных, Debug). Все 7 групп покрыты.
- 1–2: Percentile (n=1, p=0/1, [1..10] p50=5,5 и p95=9,55, неотсортированный вход, пустой/NaN/p вне [0,1]), Summarize == Percentile.
- 3: Parse (неизвестный ключ в корне и блоке, kind≠bench, пустые/нулевые threads и seeds, повтор id, grid=24 точки), EXP-0007-bench = 114 (A15 B15 C1 6 C2 3 C3 3 D72), хеш не зависит от форматирования.
- 4: Plan детерминирован и перестановка таблицы, другой shuffle_seed даёт другой порядок, DailyFirst == чётная реплика.
- 5: Pick = ⌊C·pct/100⌋, 0 → пусто, 100 → все.
- 6: Clone даёт тот же StateHash, R2a на клоне не трогает источник; R2(в) (R2c) не меняет состояние (state_unchanged, хеш).
- 7: CLI через публичный `Program.Main` (BenchCommand internal, InternalsVisibleTo в Lab нет, src/ не менялся): неизвестный режим/ключ/дубль ключа → 2; `--percents 0,101` → 2 без файла; `--warmup/--measure` с манифестом печатают `[smoke]`, совпадающие с манифестом значения не ставят smoke.
Дефектов не найдено. Заметки: CLI-тесты перехватывают Console, поэтому класс в коллекции LabConsole без параллелизма; smoke проверяется по строке вывода «[smoke]», а не по полю в JSON (запуск процесса реальный, 2 месяца). Изменений в src/ не требуется.
