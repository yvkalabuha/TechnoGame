---
id: TASK-0003
title: "Прототип модели на Python и заморозка модели перед C#"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: simulation-engineer
category: lab
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0002]
related: [HYP-0001, EXP-0001]
approved_by: owner
approved_at: 2026-11-02
approval_ref: DECISIONS_LOG #12–#15; EXP-0001 «Ревью»
created: 2026-11-02
---

# TASK-0003 — Прототип модели на Python и заморозка модели перед C#

## Цель
Отладить toy-модель на float-прототипе до утверждения и заморозить её спецификацию для C#. Решение владельца DECISIONS_LOG #12.

## Описание
- Прототип в `lab/proto/` (Python): модели A, B (калиброванный под среднюю траекторию A), C (фиксированный лаг); выход — CSV в формате TASK-0002.
- Архетипы — по обновлённой таблице HYP-0001 (A4 выровнен, A6 — отдельный критерий).
- Прогон: сетка β×γ 3×3 × 50 seeds × 2 уровня шума для A; B и C — в точке по умолчанию × 200 seeds.
- Внести правки critic в `docs/simulation/toy-science-industry-model.md` (EXP-0001 «Ревью» п. 4–7, 10) и в тело EXP-0001 (матрица, контроли, правило решения).
- Результаты прототипа — **предварительные**, не официальный итог EXP-0001.

## Критерии приёмки
- [ ] Прототип запускается одной командой, выход проходит через `exp0001.py`.
- [ ] Модель-документ обновлён, готов как спецификация для C# (все формулы, порядок фаз, какие значения из t и t+1).
- [ ] В EXP-0001 записан замороженный манифест (параметры, сетка, seeds, пороги, правило решения).
- [ ] Отчёт: какие критерии проходят на прототипе и почему — для решения владельца перед C#.

## REVIEW
- [ ] Code review (reviewer)
- [ ] Тесты (test-engineer)
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
