---
id: TASK-0149
title: "Player levers: client legibility pass and pace (plain labels instead of numbers and G-codes, speeds 0/1/3/7/30, default 3 days/s)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: gate
milestone: MS-M05
agent: developer
category: client
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0148, TASK-0156]
approved_by: owner
approved_at: 2026-11-24
approval_ref: "#482 (designs approved), #470-#481"
created: 2026-11-24
---

# TASK-0149 — Player levers: client legibility pass and pace (plain labels instead of numbers and G-codes, speeds 0/1/3/7/30, default 3 days/s)

## Цель
Убрать непонятные подписи и сделать темп партии 10–20 часов (#472, #481, #483).

## Описание
docs/game-design/PLAYER_LEVERS_WAVE1.md §A3 (темп: скорости 0/1/3/7/30 тиков в секунду, по умолчанию 3, автопауза по #196) и §A4 (список подписей: №XX, G1, сплав, колбы, «почему не», «черновик пуст», статьи бюджета). Тексты дерева — по docs/ui/specs/design-pass-2-tree-science.md §2б (без смены раскладки). Только клиент, ядро не трогать.

## Критерии приёмки
- [ ] Ни одной подписи «№N»/«G1» без слова на кадрах 1920x1080 100 %
- [ ] Скорости и умолчание по #481
- [ ] ui-art-director PASS, reviewer APPROVE

## Связанные документы
`docs/game-design/PLAYER_LEVERS_WAVE1.md`, `docs/game-design/economy/production-lines-and-licence-market.md`, `docs/game-design/playtests/2026-11-24-owner-m-play.md`

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
