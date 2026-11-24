---
id: TASK-0151
title: "Player levers core: research goals with a science share slider (SetResearchGoals)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: gate
milestone: MS-M05
agent: developer
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0148]
related: [TASK-0148, TASK-0156]
approved_by: owner
approved_at: 2026-11-24
approval_ref: "#482 (designs approved), #470-#481"
created: 2026-11-24
---

# TASK-0151 — Player levers core: research goals with a science share slider (SetResearchGoals)

## Цель
Цели исследований игрока с ползунком доли науки (#480).

## Описание
docs/game-design/PLAYER_LEVERS_WAVE1.md §A1: SetResearchGoals, до 3 целей, доля s 0–80 % делится 100 / 60-40 / 50-30-20 и идёт узлам фронта на пути к цели; проекция срока диапазоном. Учитывает #205, #235, #458, #465. Тот же пакет изменений, меняющих эталоны, что TASK-0150.

## Критерии приёмки
- [ ] При s = 0 и без целей — побитно как main
- [ ] Тест: цель с s = 50 % получает долю ±1 %
- [ ] reviewer APPROVE, Codex

## Связанные документы
`docs/game-design/PLAYER_LEVERS_WAVE1.md`, `docs/game-design/economy/production-lines-and-licence-market.md`, `docs/game-design/playtests/2026-11-24-owner-m-play.md`

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
