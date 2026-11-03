---
id: TASK-0010
title: "Kernel: DetMath.PowFrac, PowFracFx и целочисленные выборки; архитектурный тест ADR-0003 R1–R3"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: developer
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0003, ADR-0012, HYP-0002]
approved_by: orchestrator
approved_at: 2026-11-03
approval_ref: DECISIONS_LOG #48, #49
created: 2026-11-03
---

# TASK-0010 — Kernel: DetMath.PowFrac, PowFracFx и целочисленные выборки; архитектурный тест ADR-0003 R1–R3

## Цель
Дать срезу детерминированную арифметику без `double` в Core (решение #49) — задача T1 спецификации среза.

## Описание
Источники: `docs/simulation/slice-jet-aviation.md` §9.2, §11 T1; `docs/decisions/ADR-0012-*.md`; `docs/decisions/ADR-0003-numeric-policy.md` (раздел правила R1–R4); `.claude/rules/csharp-core.md`.
1. `DetMath.PowFrac(double, q10)` по ADR-0012 и `DetMath.PowFracFx(Fx6, q10)` (R2) с golden-векторами.
2. Целочисленная проверка вероятности через `NextU64` (R2); выборки вида `⌊k·U⌋` — в целых.
3. Перевести вызовы `PowDyadic` по ADR-0012, если этого требует ADR; ToyFunnel не ломать (исключение R4).
4. Режим `forbidDouble` в `tests/Singularity.Architecture.Tests/DeterminismRules.cs`: в Core нет `double` в сигнатурах, локальных переменных и опкодах `*.r8`, кроме исключений R4; в Kernel `double` только в методах из списка R3.

## Критерии приёмки
- golden-векторы `PowFrac`/`PowFracFx` проходят; результаты бит в бит стабильны (L1).
- Архитектурный тест R1/R3 зелёный и имеет отрицательные фикстуры (тест ловит нарушение).
- Существующие тесты (включая паритет ToyFunnel) зелёные.
- Ревью reviewer; test-engineer проверяет детерминизм.
