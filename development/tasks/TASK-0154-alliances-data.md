---
id: TASK-0154
title: "Data: alliances and blocs by year 1945-2030 for the licence buyer circle"
status: REVIEW         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: gate
milestone: MS-M05
agent: research-scientist
category: data
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0148, TASK-0156]
approved_by: owner
approved_at: 2026-11-24
approval_ref: "#482 (designs approved), #470-#481"
created: 2026-11-24
---

# TASK-0154 — Data: alliances and blocs by year 1945-2030 for the licence buyer circle

## Цель
Данные союзов и блоков по годам для круга покупателей лицензий (#478).

## Описание
Источник с лицензией PD/CC0/CC BY (ADR-0009), например Correlates of War Formal Alliances (проверить лицензию). Формат mods/core/scenarios/1945/alliances.yaml по годам для 5 стран среза и их окружения; сверка с фактами.

## Критерии приёмки
- [ ] Источник и лицензия записаны
- [ ] Сверка с историей для 5 стран среза

## Связанные документы
`docs/game-design/PLAYER_LEVERS_WAVE1.md`, `docs/game-design/economy/production-lines-and-licence-market.md`, `docs/game-design/playtests/2026-11-24-owner-m-play.md`

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
