---
id: TASK-0153
title: "Player levers core: AI adviser for the player country production (no AI opening or closing of player lines)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: gate
milestone: MS-M05
agent: developer
category: core
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0148, TASK-0156]
approved_by: owner
approved_at: 2026-11-24
approval_ref: "#482 (designs approved), #470-#481"
created: 2026-11-24
---

# TASK-0153 — Player levers core: AI adviser for the player country production (no AI opening or closing of player lines)

## Цель
ИИ страны игрока в производстве только советует (#479).

## Описание
docs/game-design/economy/production-lines-and-licence-market.md разд. 1: ИИ не открывает и не закрывает линии страны игрока; выдаёт предложения в View; принять/отказать одной командой. Предел линий игрока и правило пары линий (IsWorkingOfPair) — по документу.

## Критерии приёмки
- [ ] Тест: ИИ не меняет линии актора игрока
- [ ] Предложения видны во View
- [ ] reviewer APPROVE, Codex

## Связанные документы
`docs/game-design/PLAYER_LEVERS_WAVE1.md`, `docs/game-design/economy/production-lines-and-licence-market.md`, `docs/game-design/playtests/2026-11-24-owner-m-play.md`

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
