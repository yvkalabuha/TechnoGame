---
id: TASK-0148
title: "Player levers: Lab probes P1-P3 (budget items matter, reserve dominance, goal strategies)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: gate
milestone: MS-M05
agent: simulation-engineer
category: lab
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0148, TASK-0156]
approved_by: owner
approved_at: 2026-11-24
approval_ref: "#482 (designs approved), #470-#481"
review_ref: "reviewer: APPROVE (af1971604f9e650eb, Lab only); landed 2e65cce with --full verify OK"
created: 2026-11-24
tags: [task, simulation-lab, gameplay, done]
---

# TASK-0148 — Player levers: Lab probes P1-P3 (budget items matter, reserve dominance, goal strategies)

## Цель
Измерить до кода рычагов, что статьи бюджета, резерв и цели исследований дают настоящий выбор без доминирующей стратегии.

## Описание
Пробы P1–P3 из docs/game-design/PLAYER_LEVERS_WAVE1.md: P1 — меняет ли перенос 10 % между статьями итог; P2 — доля в резерве больше 20 % не должна выигрывать по ключевым метрикам; P3 — четыре стратегии целей (рывок двигателя / материалы / ровно / без целей) × 10 seeds, ни одна не выигрывает по всем метрикам; проверка допуска прогноза срока цели. Плюс из docs/game-design/economy/production-lines-and-licence-market.md: тормозит ли вторая линия первую (кадры, сплав). Предрегистрация до прогона: какое решение меняет исход. Прогоны с nice, не больше одного полного прогона за раз.

## Критерии приёмки
- [ ] Предрегистрация записана до прогона
- [ ] Отчёт `docs/simulation/levers-probes.md` с исходом P1–P3 и линий
- [ ] Если доминирующая стратегия найдена — предложение правки дизайна

## Связанные документы
`docs/game-design/PLAYER_LEVERS_WAVE1.md`, `docs/game-design/economy/production-lines-and-licence-market.md`, `docs/game-design/playtests/2026-11-24-owner-m-play.md`

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
