---
id: TASK-0094
title: "Client v0.5d: tree legend, glyph shapes, alert callout to country, foreign-power tag tint"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M1
agent: developer         # ответственный агент
category: tooling
priority: low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0088, TASK-0081]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "DEBT-0047, v0.5b/v0.5c visual reviews (item-9 tails)"
created: 2026-11-18
---

# TASK-0094 — Client v0.5d: tree legend, glyph shapes, alert callout to country, foreign-power tag tint

## Цель
Хвосты визуальных ревью v0.5b/v0.5c (DEBT-0047): `docs/ui/reviews/task-0077-visual.md`, `task-0081-visual.md`, `task-0088-visual.md`.

## Описание
1. Легенда дерева знаний не перекрывает диск планисферы.
2. Глифы типов узлов (FND/PRN/ENG/CMP/PRC/APP) — разные формы, не одинаковые круги.
3. Плашка тревоги на глобусе — выноска к стране.
4. Метка выбранной чужой державы — цветная кромка в тинт державы.
5. Рамки подписей стран не закрывают контуры.
6. При 150 % на 1280×720 кнопка «Отдать приказ» видна без прокрутки (прокручивается только блок лицензии).
Не менять Core/Session/Kernel. Снимки — только существующий набор (Q49 не решён).

## Критерии приёмки
- [ ] Пункты 1–6; валидатор T1–T6 PASS; build без предупреждений; тесты клиента зелёные.

## REVIEW
- [ ] Визуальное ревью (ui-art-director)

## Результат
