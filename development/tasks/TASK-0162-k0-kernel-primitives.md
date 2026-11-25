---
id: TASK-0162
title: "K0: Kernel primitives for the daily tick (DaySlice, Apportion, DayRate) per ADR-0027 §5"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: gate
milestone: MS-M1
agent: developer         # ответственный агент
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: owner
approved_at: 2026-11-25
approval_ref: "#516 (ADR-0027), #520"
created: 2026-11-25
---

# TASK-0162 — K0: Kernel primitives for the daily tick (DaySlice, Apportion, DayRate) per ADR-0027 §5

## Цель
Примитивы Kernel для дневного тика (ADR-0027 §5): `DaySlice`, `Apportion`, `DayRate` на типе `Rate` ADR-0003 ред. 3. Вливаются до веток рычагов и М1.

## Описание
Только Kernel и его тесты; эталоны не меняются (новые функции без вызовов из систем).

## Критерии приёмки
- [ ] Тесты: сумма долей дней = месячной величине побитно (Apportion), ошибка DayRate в пределах ADR-0027
- [ ] Эталоны без изменений
- [ ] reviewer APPROVE, Codex

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
