---
id: TASK-0189
title: "Data: node and institute directions into mods (12 directions, O-091)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: developer
category: data
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: orchestrator
approved_at: 2026-11-25
approval_ref: "O-091, #571"
created: 2026-11-25
---

# TASK-0189 — Data: node and institute directions into mods (12 directions, O-091)

## Цель
Направления 2–4 у каждого узла и института в данных модов по `docs/research/notes/directions-nodes-institutes.md` (O-091), CEA — кандидат на стройку.

## Описание
Схема: nodes.yaml поле directions (главное первым), institutes.yaml directions с уровнями; загрузчик и валидатор (2–4, известные id); тесты. Без изменения поведения ядра до TASK слотов (поле читается, но не используется) — эталоны не меняются, кроме DataHash если он покрывает поле (объяснить).

## Критерии приёмки
- [ ] Валидатор и тесты; reviewer APPROVE

## Тесты
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
