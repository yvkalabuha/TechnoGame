---
id: TASK-0011
title: "Данные среза «реактивная авиация»: 61 узел, класс изделия, сценарий 1945, валидатор"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: research-scientist
category: data
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [RR-0005, RR-0008, HYP-0002]
approved_by: orchestrator
approved_at: 2026-11-03
approval_ref: DECISIONS_LOG #48, #49
created: 2026-11-03
---

# TASK-0011 — Данные среза «реактивная авиация»: 61 узел, класс изделия, сценарий 1945, валидатор

## Цель
Содержание данных среза — задача T2 спецификации, часть «содержание». Перевод в формат мода и валидатор — отдельная задача developer после решения по ADR-0010 (сейчас PROPOSED).

## Описание
Источники: `docs/simulation/slice-jet-aviation.md` §1 (состав, 61 узел, кластеры, дисциплины, hazard-узлы §1.3, сценарий §1.1), §8.1; `docs/architecture/LAYER_CONTRACT.md` v2.2; RR-0005, RR-0008 §12; решения #23, #40, #41.
Результат — `docs/research/data/jet-slice/` (таблицы в Markdown или CSV):
1. 61 узел: id, слой (FND/PRN/ENG/CMP/PRC/APP), `cluster`, `discipline`, `requires`, tier для PRC, поколение G1–G4, исторический ориентир даты с источником.
2. Класс `jet_fighter`: слоты, `prc_set` двух вариантов.
3. Сценарий 1945 для USA, GBR, SUN, FRA, SWE: каждое поле — значение с источником или правило оценки с пометкой `estimated: true` (§1.1); правило вывода для `culture_H`, `Hcap`, `w_a`.

## Критерии приёмки
- Каждое число имеет источник (URL + дата обращения) или правило оценки; лицензии данных по ADR-0009.
- Узлы удовлетворяют правилам §1 спецификации (ровно 61, кластер и дисциплина у каждого).
- Нет подгонки под историческую траекторию лидерства (RISK-0011): исторические даты — только ориентиры C4.
