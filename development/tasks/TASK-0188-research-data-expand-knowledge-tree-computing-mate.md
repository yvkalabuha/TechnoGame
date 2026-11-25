---
id: TASK-0188
title: "Research + data: expand knowledge tree (computing, materials, rocketry, space, electronics, nuclear) with directions (#575)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: research-scientist
category: data
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: owner
approved_at: 2026-11-25
approval_ref: "#575"
created: 2026-11-25
---

# TASK-0188 — Research + data: expand knowledge tree (computing, materials, rocketry, space, electronics, nuclear) with directions (#575)

## Цель
Расширить дерево знаний до партии (#575): вычислительная техника (узлы-ускорители науки research_speed), материалы, ракетная техника, космические исследования отдельной областью (спутники, космонавтика, наука), электроника, ядерные исследования.

## Описание
Шаг 1 (research-scientist): записка с узлами 1945–2030 по каждой области — id, название, год-якорь, цена-тип, предпосылки из существующих и новых узлов, 2–4 направления из 12 (O-091), эффекты (research_speed с долей, военная ценность), источник на узел. Объём ≈ 40–60 узлов. Шаг 2 (developer): данные в mods/core/nodes.yaml, локализация, тесты графа (ацикличность, якоря), pace_check справкой. Новые изделия (ракеты, спутники) — не в этой задаче, только знания.

## Критерии приёмки
- [ ] Записка с источниками (URL + дата), ADR-0009 для данных
- [ ] Данные в модах, граф ацикличен, тесты зелёные; reviewer APPROVE

## Тесты
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
