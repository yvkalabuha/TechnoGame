---
id: TASK-0132
title: "Keyboard selection of technology tree nodes and keyboard tooltip (A6-A8)"
status: PROPOSED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M05
agent: developer
category: client
priority: medium
needs_owner: false
depends_on: [TASK-0126]
related: [TASK-0126, TASK-0117]
approved_by:
approved_at:
approval_ref:
created: 2026-11-23
---

# TASK-0132 — Выбор узлов дерева знаний с клавиатуры (A6–A8)

## Цель
Узлы дерева знаний выбираются и подсказка открывается только мышью. Арт-директор (TASK-0126, круг 2) принял подпись «№N» без названия только при условии, что название доступно с клавиатуры; пока это не так, дерево в целом не получает PASS по #417 (A6–A8).

## Описание
Перебор узлов стрелками и Tab, кольцо фокуса не тоньше 2 px, панель узла по Enter, подсказка с клавиатуры. Входит в UX фазу 1 (П1.6, фокус-рамка), `docs/ui/UX_IMPROVEMENT_PLAN.md`.

## Критерии приёмки
- [ ] Узел выбирается и открывается без мыши; фокус виден на кадре 1920x1080 при 100 %.
- [ ] Кадр с меткой «№N» и подсказкой, открытой с клавиатуры.
- [ ] Ревьюер APPROVE; ui-art-director PASS.

## Связанные документы
`docs/ui/reviews/task-0126-visual.md`, `docs/ui/UI_DESIGN_SYSTEM.md` §12.

## Результат
