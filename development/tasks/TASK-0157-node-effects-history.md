---
id: TASK-0157
title: "Node content: effects in numbers and short history with sources for all 99 knowledge nodes (#486)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: gate
milestone: MS-M05
agent: research-scientist
category: data
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0156, TASK-0151]
approved_by: owner
approved_at: 2026-11-24
approval_ref: "#486"
created: 2026-11-24
---

# TASK-0157 — Node content: effects in numbers and short history with sources for all 99 knowledge nodes (#486)

## Цель
Игрок изучает бонусы узлов и историю техники (#486): у каждого из 99 узлов — эффекты в цифрах и короткая история с источниками.

## Описание
Шаг 1 (game-designer): схема эффектов — только параметры, которые реально меняют симуляцию (найти в src/ и mods/core/), единицы и как они показываются; эффект без механики помечается как «историческая справка» (правило «нет фальшивых механик»). Шаг 2 (research-scientist): содержание для 99 узлов — эффекты по схеме и история 3–6 предложений (кто, где, когда, что дало) с источниками (URL + дата доступа). Шаг 3 (developer): перенос в mods/ и карточку узла (design-pass-2 C-3).

## Критерии приёмки
- [ ] Схема эффектов согласована с кодом (каждый эффект указывает параметр симуляции)
- [ ] 99 узлов: эффекты и история с источниками
- [ ] Пример «дельта-крыло» больше не «ничего не повышает»

## Связанные документы
`docs/ui/specs/design-pass-2-tree-science.md` (C-3), `docs/game-design/playtests/2026-11-24-owner-m-play.md` п. 9, `docs/research/slice-g5-g6-content.md`

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
