---
id: TASK-0182
title: "Fix: AI fleet sale booked at a different G_front than the decision priced (FL7 seed 10)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: developer
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0180]
approved_by: orchestrator
approved_at: 2026-11-25
approval_ref: "TASK-0180 FL7 diagnosis"
review_ref: "reviewer: APPROVE after comment fixes (Core 1212, Session 651); codex accept e3b86996, leftovers DEBT-0110; merge f45c3d4"
created: 2026-11-25
---

# TASK-0182 — Fix: AI fleet sale booked at a different G_front than the decision priced (FL7 seed 10)

## Цель
Продажа парка ИИ проводится по той же цене (G_front, φ), которую ИИ учёл при решении; инвариант FL7 держится точно.

## Описание
Диагноз (simulation-engineer, ветка worktree-agent-ad185b07967aabcff): SUN в мес. 369 решает продать 24 машины G1, ожидаемый доход inc_exp 0,010546 по G_front = 2 (PolicySystem.Fleet.cs:180, состояние после мес. 368); в том же месяце США выставляют первую G3; продажа в мес. 370 (ProductsSystem.Fleet.cs:380, `GFront(t - 1)`) пересчитывает G_front по состоянию после мес. 369 = 3, φ 0,6, проводит 0,006314. `GFront` читает FieldedL из текущих когорт, поэтому зависит от того, чьё состояние читается. Исправление: хранить G_front решения в колонке политики рядом с `_incExp` (PolicySystem.Products.cs:749) и использовать её в `ProductsSystem.ApplyFleetCommands` для приказов ИИ; приказы игрока без изменений. Меняет эталоны — пакет изменений, меняющих эталоны.

## Критерии приёмки
- [ ] Тест: другой актор выставляет новое поколение в месяц решения ИИ о продаже; FL7 точно
- [ ] FleetRunTests seeds 1–20 проходят на данных TASK-0180
- [ ] Объяснён остаток 0,2 % (0,5987 против 0,6) или записан в долг
- [ ] Эталоны перезаписаны с объяснением; reviewer APPROVE
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
