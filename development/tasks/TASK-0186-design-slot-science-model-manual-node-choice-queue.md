---
id: TASK-0186
title: "Design: slot science model (manual node choice, queue, focus, research_speed, AI) for gate (#561-#568)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: gate
milestone: MS-M0
agent: game-designer
category: design
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: owner
approved_at: 2026-11-25
approval_ref: "#561-#568"
created: 2026-11-25
---

# TASK-0186 — Design: slot science model (manual node choice, queue, focus, research_speed, AI) for gate (#561-#568)

## Цель
Один проект модели «по слотам» для gate и для developer.

## Описание
Свести `docs/game-design/notes/pivot-561-game-design.md`, `docs/simulation/notes/pivot-561-model-impact.md`, `docs/ui/specs/pivot-561-tech-map.md` и решения #561–#570 в `docs/game-design/SCIENCE_SLOTS.md`: узел (цена, T_n, направление, военная ценность), слот = институт, поток денег, нехватка, излишек в резерв, очередь активная + 5 (#567), прогресс снятого узла, фокус (уровни, цена), research_speed (сумма, потолок ×2,5, #568), стройка институтов (время, цена, содержание, потолок #564), ИИ (PATH + запасные ступени, фокус, стройка), судьба старых рычагов (κ, FundProgramme, доля бюджета), команды и поля Session для клиента, инварианты #566. Числа — из Lab-пилота (TASK-0185), до него — помеченные заглушки.

## Критерии приёмки
- [ ] Документ с разделом «Разногласия»
- [ ] Gate: critic, advocate, DeepSeek; утверждение владельцем

## Тесты
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
