---
id: TASK-0132
title: "Keyboard selection of technology tree nodes and keyboard tooltip (A6-A8)"
status: DEFERRED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M05
agent: developer
category: client
priority: medium
needs_owner: false
depends_on: [TASK-0126]
related: [TASK-0126, TASK-0117]
approved_by: orchestrator
approved_at: 2026-11-24
approval_ref: "PLAN Фокус п.5; prerequisite of design pass 2 (#453); light task approved by Orchestrator"
created: 2026-11-23
tags: [task, ui, deferred]
---

# TASK-0132 — Выбор узлов дерева знаний с клавиатуры (A6–A8)

## Цель
Узлы дерева знаний выбираются и подсказка открывается только мышью. Арт-директор (TASK-0126, круг 2) принял подпись «№N» без названия только при условии, что название доступно с клавиатуры; пока это не так, дерево в целом не получает PASS по #417 (A6–A8).

## Описание
Перебор узлов стрелками и Tab, кольцо фокуса не тоньше 2 px, панель узла по Enter, подсказка с клавиатуры. Входит в UX фазу 1 (П1.6, фокус-рамка), `docs/ui/UX_IMPROVEMENT_PLAN.md`.

## Добавлено 2026-11-24 (остаток TASK-0136, арт-директор раунд 2 + Gemini)
- Легенда: «Цель (синяя рамка)» → «Цель (двойная рамка)» — признак не только цветом.
- Центральный циферблат дерева: подписать или добавить строку в легенду.
- «1/3» вплотную к «№6» на кадре SUN — развести.
- «(число, не интервал)» в «ПОЧЕМУ НЕ» — служебный текст; заменить на «срок: ≈ N мес.».

## Критерии приёмки
- [ ] Узел выбирается и открывается без мыши; фокус виден на кадре 1920x1080 при 100 %.
- [ ] Кадр с меткой «№N» и подсказкой, открытой с клавиатуры.
- [ ] Ревьюер APPROVE; ui-art-director PASS.

## Связанные документы
`docs/ui/reviews/task-0126-visual.md`, `docs/ui/UI_DESIGN_SYSTEM.md` §12.

## Результат
