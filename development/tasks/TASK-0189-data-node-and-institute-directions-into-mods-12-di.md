---
id: TASK-0189
title: "Data: node and institute directions into mods (12 directions, O-091)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
review_ref: "reviewer: APPROVE; codex findings rejected per O-093/O-094; merge a082491"
created: 2026-11-25
tags: [task, data, science, done]
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
Added 12-direction field (directions) to all 99 nodes and 27 institutes of mods/core; loader and validator in Core (Directions.cs, NodeTable, InstituteTable); node.schema.json; tests in tests/Singularity.Core.Tests/Data/DirectionsTests.cs. Behaviour unchanged: the field is read, not used. Not in DataHash nor InstituteTable.Hash, so goldens did not move (a test pins the institute hash). Field optional per entry (other mods, fixtures), validated when present: 2-4 distinct known ids, nodes main first, institute levels 1-5. Verified: Core.Tests 1227 pass, Architecture 147 pass, Session InstituteTests+ViewSubscription 56 pass. Negative check: with nodes.yaml reverted, DirectionsTests fail (2).
