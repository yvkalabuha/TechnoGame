---
id: TASK-0150
title: "Player levers core: budget reserve account with 10 %/year leak, shares at most 1, FundProgramme"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
review_ref: "reviewer: APPROVE (a0ece5a re-review); codex: waived after 3 returns (#432); landed 9d7d3a3; DEBT-0099"
created: 2026-11-24
---

# TASK-0150 — Player levers core: budget reserve account with 10 %/year leak, shares at most 1, FundProgramme

## Цель
Резерв бюджета как настоящий счёт (#475) и «ускоренная программа» (#471 «проекты»).

## Описание
docs/game-design/PLAYER_LEVERS_WAVE1.md §A2: доли ≤ 1, остаток в резерв Fx6, утечка 10 %/год, команда FundProgramme через CEff с множителем опережения и убывающей отдачей; скрыть статьи без читателя (Quality). Пакет изменений, меняющих эталоны: при нулевом резерве — побитно как до изменения (отдельный тест).

## Критерии приёмки
- [ ] Тест нейтральности: доли = 1, без программ — колонки совпадают с main
- [ ] Инварианты денег I1–I4 из части Б
- [ ] reviewer APPROVE, Codex

## Связанные документы
`docs/game-design/PLAYER_LEVERS_WAVE1.md`, `docs/game-design/economy/production-lines-and-licence-market.md`, `docs/game-design/playtests/2026-11-24-owner-m-play.md`

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
