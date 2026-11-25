---
id: TASK-0161
title: "Design pass 2 B: knowledge tree by year rings and direction sectors, 6 zoom steps, icons, node menu, prominent close (#473)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M05
agent: developer         # ответственный агент
category: client
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0132, TASK-0156, TASK-0157]
approved_by: orchestrator
approved_at: 2026-11-25
approval_ref: "owner #473 (layout), #453, #466; owner request 2026-11-25 to start; light client task inside approved design"
created: 2026-11-25
---

# TASK-0161 — Design pass 2 B: knowledge tree by year rings and direction sectors, 6 zoom steps, icons, node menu, prominent close (#473)

## Цель
Дерево знаний: радиус = год (кольца по десятилетиям 1950–2030), секторы = направления, 6 шагов зума, значки на крупном зуме, эффекты узла (из `NodeEffects`, TASK-0157), меню узла (ПКМ и удержание ЛКМ), заметная кнопка «Закрыть» и Esc (#473).

## Описание
Спека: `docs/ui/specs/design-pass-2-tree-science.md` §2б и задачи A/B, раздел «мнение Gemini». Только клиент. Выбор узлов с клавиатуры из ветки TASK-0132 (worktree-agent-a1bcca52483f7ba2b) перенести, если совместим с новой раскладкой. Кадры — одной пачкой с `--only`.

## Критерии приёмки
- [ ] Кадры 1920x1080 100 % и серый; ui-art-director PASS с мнением Gemini (#450)
- [ ] C1–C8, A1–A12 (§12); фокус ≥ 2 px
- [ ] reviewer APPROVE

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
