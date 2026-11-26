---
id: TASK-0213
title: "CI fix: kb_structure and localization tests after the UI_DESIGN_SYSTEM split (#607)"
tags: [task, platform, testing, active]
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: tooling          # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: high         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: orchestrator
approved_at: 2026-11-26
approval_ref: "red CI on main (run 45902170825), orchestration.md §2"
created: 2026-11-26
---

# TASK-0213 — CI fix: kb_structure and localization tests after the UI_DESIGN_SYSTEM split (#607)

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
После разделения UI_DESIGN_SYSTEM.md (#607, 3226f16b) CI красный. (1) CoreLocaleTests.BudgetTable_HasEveryRowOfSection13_2 читал §13.2 из UI_DESIGN_SYSTEM.md — §13 возвращён в основной файл (коммит Orchestrator вместе с этой задачей), тест проходит. (2) test_kb_structure.CatalogueDatabaseTest.test_chain_pack_and_over_limit ожидает, что настоящий UI_DESIGN_SYSTEM.md больше предела — теперь это не так. Тест должен создавать свой документ больше предела во временной базе/дереве, а не зависеть от размера настоящих файлов.
