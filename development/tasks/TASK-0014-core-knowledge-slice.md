---
id: TASK-0014
title: "Core.Knowledge для среза: фронтир, c_eff, hazard, K_d, K_ind, расписания абляций"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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

# TASK-0014 — Core.Knowledge для среза: фронтир, c_eff, hazard, K_d, K_ind, расписания абляций

## Цель
Слой знания среза — задача T3 спецификации.

## Описание
Источники: спека `docs/simulation/slice-jet-aviation.md` §1.3, §3.1, §3.5, §6.1 (`L_K`, `L_K0`), §6.7 (`lic_knows`), §9; контракт v2.3 §4.0, §4.3; эталон `lab/reference/jet_slice/` (формулы и траектория).
Фронтир, `c_eff`, прогресс, hazard-открытие по области §1.3 (поток KNOW, `CounterRng.Chance`), `K_d`, `K_ind` в compute по снимку t−1, переключатели расписаний `L_K`/`L_K0`, `lic_knows`.

## Критерии приёмки
- Значения формул и траектория совпадают с эталоном бит в бит (знаниевая часть).
- Нет `double` в Core; L1/L2 (параллельный compute совпадает с последовательным).
- Ревью reviewer, проверка test-engineer.
