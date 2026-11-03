---
id: TASK-0009
title: "Kernel: контрольный тест отсутствия FMA-сжатия на всех платформах CI"
status: APPROVED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0003, ADR-0012]
approved_by: orchestrator
approved_at: 2026-11-03
approval_ref: light; OPEN_QUESTIONS #1
created: 2026-11-03
---

# TASK-0009 — Kernel: контрольный тест отсутствия FMA-сжатия на всех платформах CI

## Цель
Доказать на каждой платформе CI (ubuntu x64, windows x64, macos ARM64), что JIT не сжимает `a*b+c` в FMA, — вход для ADR-0003.

## Описание
Тест в `tests/Singularity.Kernel.Tests`: на фиксированном наборе (seed) значений, где fused и unfused результаты различаются, метод `a*b+c` с `NoInlining` и в цикле (горячий путь, tiering) даёт побитно тот же результат, что раздельные `m = a*b; m + c`, и отличается от `Math.FusedMultiplyAdd`. Набор и ожидаемые биты фиксированы (детерминированы, без `System.Random` в утверждениях — значения сгенерировать заранее).
Локальная проверка Orchestrator (x64): 2 млн случаев, 0 расхождений с раздельными операциями.

## Критерии приёмки
- [ ] Тест зелёный локально и в CI на трёх ОС (результат CI — при push фазы).
- [ ] Если на ARM64 тест упадёт — это блокер ADR-0003, а не повод ослабить тест.

## Результат
- Добавлен `tests/Singularity.Kernel.Tests/FmaContractionCanaryTests.cs` (6 тестов):
  - таблица из 8 случаев (биты a, b, c, ожидаемые unfused и fused), посчитана заранее точной рациональной арифметикой (Python `fractions`); тест проверяет, что unfused ≠ fused и что `Math.FusedMultiplyAdd` даёт ожидаемые fused-биты;
  - `a*b+c` в методе с `NoInlining` и раздельные `m = a*b; m + c` дают ожидаемые unfused-биты;
  - то же выражение в горячем цикле (200 000 итераций, OSR/tier-1) и повторные вызовы `NoInlining`-метода (rejit tier-0 → tier-1);
  - вариант со `static readonly` полями (tier-1 подставляет их как константы — проверяет и свёртку констант);
  - 4096 случаев из `CounterRng` с c = −round(a·b): unfused = +0 точно, FMA ≠ 0 (требуется > 90% различающих случаев).
- Проверка: `dotnet test Singularity.sln` локально (linux x64) — 47 + 157 + 73 = 277 тестов, 0 падений. [Факт] CI на windows x64 и macos ARM64 — при push фазы.
