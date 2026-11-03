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
