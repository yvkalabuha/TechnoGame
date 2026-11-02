---
id: TASK-0001
title: "Kernel: fixed-point, детерминированная математика, RNG, таблицы, хеш состояния"
status: REVIEW         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: core
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [HYP-0001, EXP-0001]
approved_by: owner
approved_at: 2026-11-02
approval_ref: DECISIONS_LOG #12–#15; EXP-0001 «Ревью»
created: 2026-11-02
---

# TASK-0001 — Kernel: fixed-point, детерминированная математика, RNG, таблицы, хеш состояния

## Цель
Основа `Singularity.Kernel`, не зависящая от модели: всё, что нужно для детерминированной симуляции (ADR-0002…0007).

## Описание
- Решение `Singularity.sln`; проекты `src/Singularity.Kernel`, `tests/Singularity.Kernel.Tests`, `tests/Singularity.Architecture.Tests`. .NET 10, nullable, warnings as errors. Без NuGet, кроме тестового фреймворка (xUnit) — указать версию.
- `Fx6`: fixed-point на `long`, масштаб 10^6, checked-арифметика, `FromDouble` (единственный путь, `IsFinite`-проверка, floor).
- `DetMath`: `Sqrt` (через `Math.Sqrt`), `PowDyadic(x, k/8)` через повторный sqrt.
- `CounterRng`: SplitMix64, ключ (seed, tick, system, entity, n), цепочка `mix(mix(mix(seed ^ c1) ^ tick) ^ …)` с константой на поле; `NextUnit` = `>>11 · 2^-53`.
- `LongColumn`/`IntColumn`/`Table`: SoA-колонки с регистрацией по имени; двойной буфер (current/next, swap).
- `TickRunner`: фазы compute (параллельно по чанкам, чтение current) → merge (детерминированный порядок) → post.
- `StateHash`: SHA-256; заголовок (schema_version, tick, n_rows) + колонки в порядке регистрации, little-endian.

## Критерии приёмки
- [ ] `dotnet build` и `dotnet test` проходят.
- [ ] Эталонные векторы `CounterRng` (≥10 ключей) и `StateHash` (фиксированная таблица) зафиксированы в тестах.
- [ ] Перестановка полей ключа RNG даёт другое значение (тест на коллизии вида (tick, entity) ↔ (entity, tick)).
- [ ] `TickRunner` на тестовой системе даёт одинаковый хеш при 1, 4, 8 потоках (L2) и при повторе (L1).
- [ ] Архитектурный тест: в Kernel из `System.Math` используются только Sqrt, Floor, Abs, Min, Max, BigMul; нет `float`, `System.Random`, `DateTime.Now`.
- [ ] Kernel не ссылается ни на один другой проект решения.

## Тесты
Unit-тесты Kernel; архитектурный тест (рефлексия по IL или анализ исходников — выбрать проще и надёжнее, обосновать).

## REVIEW
- [ ] Code review (reviewer)
- [ ] Тесты (test-engineer)
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
Kernel реализован developer 2026-11-02: `src/Singularity.Kernel` (Fx6, DetMath, CounterRng, Column/Table, TickRunner, StateHash), `tests/Singularity.Kernel.Tests` (69), `tests/Singularity.Architecture.Tests` (14, IL-сканер запретов), эталон `tools/reference/kernel_vectors.py`. `dotnet build` без предупреждений; `dotnet test` 83/83 (проверено Orchestrator).
Отклонения: `InternalsVisibleTo` для тестов; хеш вызывается снаружи post (`StateHash.Compute`); эталонный Python-скрипт в tools/. Частоты систем и мир из нескольких таблиц — следующие задачи.
