---
id: TASK-0094
title: "Client v0.5d: tree legend, glyph shapes, alert callout to country, foreign-power tag tint"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
(developer, 2026-11-18; перенесено Orchestrator) 1. Легенда: диск планисферы сдвигается и уменьшается правее открытой легенды (`DiscArea`). 2. Глифы: FND треугольник, PRN круг, ENG шестиугольник, CMP пятиугольник, PRC квадрат, APP ромб; одна функция `TechTree.Outline` для узлов и легенды; тест «6 форм различны». 3. Плашка тревоги на выноске с точкой к стране, плашки одной страны стопкой. 4. Метка чужой державы — полоса 6 px в тинте державы. 5. Подписи стран: жирный шрифт, ореол бумаги, подчёркивание вместо рамки. 6. «Финансы» при 150 %: блок лицензии в собственной прокрутке, `finance at 150%: order button in view without scroll True`. Проверка: build 0 предупреждений, тесты клиента 151, `--shots` rc=0, палитра C 107/107, текстуры 118/118, T1–T6 PASS. Открыто: при 150 % на 1280×720 блок лицензии сжат до заголовка (лицензий в M1 нет); нет снимка чужой державы (Q49).
