---
id: TASK-0195
title: "Era price of research nodes: node cost grows with era so leaders spend science money after 1990 (#579)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: simulation-engineer         # ответственный агент
category: lab                # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: orchestrator
approved_at: 2026-11-26
approval_ref: "DECISIONS_LOG #579"
created: 2026-11-26
tags: [task, economy, science, active]
---

# TASK-0195 — Era price of research nodes: node cost grows with era so leaders spend science money after 1990 (#579)

## Цель

## Описание

## Критерии приёмки
- [ ]

## Тесты
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->

## Цель
Решение владельца #579: после 1990 у лидеров ~90 % денег науки не тратится (TASK-0193, DEBT-0115). Цена тем растёт с эпохой, чтобы деньги шли на науку.

## Объём
1. Lab-пилот (simulation-engineer): множитель цены по году узла (кривая от 1990, параметр в данных), замер на сетке 250: возврат денег науки после 1990 < 20 %, GAP36 ≤ 36 мес., G2/G3/G4 в полосах #566, простой 0 % до 2030.
2. По итогам — правка данных/ядра одним пакетом, меняющим эталоны (отдельная задача developer, если нужна правка Core).

## Критерии приёмки
- Записка docs/simulation/era-node-price.md: кривая, 3 варианта, замеры USA/SUN/GBR/FRA/SWE, рекомендация.
- Lab тесты проходят; эталоны меняются только с объяснением.
