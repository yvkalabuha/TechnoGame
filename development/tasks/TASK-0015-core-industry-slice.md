---
id: TASK-0015
title: "Core.Industry для среза: culture_H, tooling_M, production_profile, сплав, reserve_workforce"
status: REVIEW         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: developer
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0013]
related: [HYP-0002]
approved_by: orchestrator
approved_at: 2026-11-03
approval_ref: DECISIONS_LOG #48, #53
created: 2026-11-03
---

# TASK-0015 — Core.Industry для среза: culture_H, tooling_M, production_profile, сплав, reserve_workforce

## Цель
Слой способности среза — задача T4 спецификации.

## Описание
Источники: контракт v2.3 §4.0–4.2, §4.6; спека §3.2, §3.5, §6.1 (`L_C`, `L_C0`); эталон `lab/reference/jet_slice/`.
`culture_H` в compute тика t по `proc_v` за t−1, `tooling_M` и инвестиции (`cost_M`), `production_profile`, `q`, сплав, `reserve_workforce`, `report_output` в merge; заглушки `L_C`/`L_C0`. В способности нет RNG (#28).

## Критерии приёмки
- Значения формул и траектория совпадают с эталоном бит в бит (часть способности).
- Тест «нет RNG в способности»; нет `double` в Core; L1/L2.
- Ревью reviewer, проверка test-engineer.
