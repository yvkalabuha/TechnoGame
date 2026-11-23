---
id: TASK-0133
title: "G5/G6 data in mods on the train-2 branch and calibration probe (O-038)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M05
agent: developer
category: data
priority: high
needs_owner: false
depends_on: [TASK-0128, TASK-0129, TASK-0131]
related: [TASK-0128, TASK-0129, TASK-0131]
approved_by: orchestrator
approved_at: 2026-11-23
approval_ref: "O-038 (data on a branch, probe before owner approval)"
created: 2026-11-23
---

# TASK-0133 — Данные G5/G6 в mods на ветке поезда 2 и пробный прогон

## Цель
Порядок O-038: данные G5/G6 пишутся на ветке, пробный прогон с заранее записанными порогами показывает, убирает ли контент простой 1978–2030, и только потом владелец утверждает контент (TASK-0128). На `main` данные не попадают до утверждения.

## Описание
1. developer: ветка от `main` с влитой веткой TASK-0129 (старение парка); узлы 62–81, альтернативы, `af_class` 5–6, `pu_ranks`, якоря — строго по `docs/research/slice-g5-g6-content.md` ред. 2; дисциплины существующие; `max_designs` 12. Отклонения — в «Разногласия».
2. simulation-engineer: пре-регистрация (`lab/` манифест) и пробный прогон 1945–2030, seeds 1–10 (сначала пилот на 1 seed по правилу 15 минут). Пороги O-038: последний новый узел лидера ≥ 2005; G5 в строю у ≥ 2 стран к 2030; узкая метрика простоя 1978–2030 < 0,8; годы с G = 0 по странам; доля провалов проверки «в строю» 1990–2030; L1/L2 на 1020 мес. (DEBT-0079). Калибровка `COST_MULT_G5/G6` под целевые годы открытия ред. 2 — не более 3 точек на множитель.
3. Итог — раздел в `docs/research/slice-g5-g6-content.md` или отдельная записка `docs/simulation/g5-g6-probe.md` с цифрами по порогам.

## Критерии приёмки
- [ ] Данные загружаются, `verify.py` зелёный на ветке (golden перезаписаны один раз с объяснением).
- [ ] Пробный прогон: таблица по каждому порогу O-038, `VALIDITY: OK`.
- [ ] Ревьюер APPROVE на данные; сверка данных с документом ред. 2 (правило §4 оркестрации).

## Связанные документы
`docs/research/slice-g5-g6-content.md`, `docs/reviews/slice-g5-g6-content-*.md`, `docs/game-design/notes/fleet-without-replacement.md`.

## Результат
