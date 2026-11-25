---
id: TASK-0155
title: "Data: constant 1990 US dollar display series and conversion from Core money unit"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: gate
milestone: MS-M05
agent: economy-analyst
category: data
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0148, TASK-0156]
approved_by: owner
approved_at: 2026-11-24
approval_ref: "#482 (designs approved), #470-#481"
review_ref: "reviewer: CHANGES closed by O-079 (K rounding fixed, K into data via TASK-0156)"
created: 2026-11-24
---

# TASK-0155 — Data: constant 1990 US dollar display series and conversion from Core money unit

## Цель
Показ бюджета в долларах 1990 года (#476).

## Описание
docs/game-design/PLAYER_LEVERS_WAVE1.md §A2: ряд и коэффициент перевода из денежной единицы Core (#368, $2011) в доллары 1990; источник PD/CC0/CC BY (Maddison CC BY, BEA/FRED) — проверить лицензию.

## Критерии приёмки
- [ ] Ряд и коэффициент в данных с источником
- [ ] Пример: бюджет USA 1950 в $1990 сверен

## Связанные документы
`docs/game-design/PLAYER_LEVERS_WAVE1.md`, `docs/game-design/economy/production-lines-and-licence-market.md`, `docs/game-design/playtests/2026-11-24-owner-m-play.md`

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
