---
id: TASK-0092
title: "DeterminismRules: string hashing, culture comparisons, threading APIs, Parallel.For allowlist, Lab ToyFunnel check"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M1
agent: developer
category: core
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0007, ADR-0002]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "ADR-0002 determinism; ADR-0007 rev 3 recommendations (light, stricter tests only)"
created: 2026-11-18
---

# TASK-0092 — DeterminismRules: string hashing, culture comparisons, threading APIs, Parallel.For allowlist, Lab ToyFunnel check

## Цель
Расширить `DeterminismRules` по ADR-0007 ред. 3 (п. 3–5, 8).

## Описание
1. Запретить в Kernel/Core/Session: хеш строк через `HashCode.Combine/Add`, `StringComparer.*.GetHashCode`, `EqualityComparer<string>.Default.GetHashCode`; культурно-зависимые сравнения и сортировки строк без явного ordinal-компаратора (`string.CompareTo`, `Comparer<string>.Default`, `OrderBy(string)` без компаратора, `ToLower/ToUpper` без Invariant); `Task`/`Thread`/`Parallel.Invoke`/`BlockingCollection`.
2. Allowlist мест вызова `Parallel.For` (TickRunner, ViewBuilder.Inputs).
3. `Check` (без `forbidDouble`) для `Singularity.Lab.Models.ToyFunnel`.
4. Фикстуры на каждый запрет. Найденные нарушения в коде — список в «Результат»; исправлять только тривиальные (ordinal-компаратор), остальное — не трогать.

## Критерии приёмки
- [ ] Тесты падают на фикстурах и зелёные на коде (или нарушения перечислены).
- [ ] `dotnet test Singularity.sln`, `lab selftest` (golden без изменений).

## Результат
