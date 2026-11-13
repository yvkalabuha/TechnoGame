---
id: TASK-0038
title: "Спека среза §8.2: совместный этап калибровки C3+C4 (#177)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: simulation-engineer
category: docs
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0034]
related: [HYP-0002]
approved_by: owner
approved_at: 2026-11-13
approval_ref: "DECISIONS_LOG #177"
created: 2026-11-13
---

# TASK-0038 — Спека среза §8.2: совместный этап калибровки C3+C4 (#177)

## Цель
Перестроить план калибровки T12 в спеке среза по решению #177: этапы C3 и C4 — один совместный этап.

## Описание
Источник: `docs/simulation/smoke-t4s.md` §7 (TASK-0034); `docs/simulation/slice-jet-aviation.md` §8.1–§8.2.
- §8.2: порядок C1 → (C3+C4) → C2; сетка `c_n`-множитель × `θ_start` × `h_max`, потеря d_C3² + d_C4², бюджет около 60 точек; рычаг темпа знания — `c_n`.
- Решить и записать: один общий множитель `c_n` или по поколениям; как задаётся сетка P5 по `h_max` (от откалиброванного значения или в абсолютных пределах, учитывая край 0,16).
- Новая версия спеки с записью в истории изменений.

## Критерии приёмки
- [ ] §8.2 и история изменений спеки обновлены; ссылки на #177 и `smoke-t4s.md` §7.
- [ ] Решения по множителю `c_n` и по P5 записаны с обоснованием; спорное — в «Разногласия» или как вопрос Orchestrator.

## Тесты
Нет кода.

## Связанные документы
DECISIONS_LOG #177; TASK-0034.

## REVIEW
- [ ] Сверка Orchestrator

## Результат
