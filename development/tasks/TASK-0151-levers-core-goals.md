---
id: TASK-0151
title: "Player levers core: research priorities through institutes (slots, funding first x1.5, institute bonus, capacity, upkeep, building institutes)"
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
approval_ref: "#482; scope changed by #484-#496"
created: 2026-11-24
---

# TASK-0151 — Player levers core: research goals with a science share slider (SetResearchGoals)

## Цель
Приоритеты исследований через институты (#484–#496): слоты = институты, деньги приоритету первыми (×1,5, общий множитель с долей остальным ≥ 25 %), бонус института +5 % за уровень, мощность и содержание, стройка институтов как проект (игрок и ИИ).

## Описание
`docs/game-design/civilization/research-institutes-priorities.md` ред. 3 (v0: без забывания и «углубления»). Без приоритетов — старый путь кода, хеши как на main. Перекалибровка лет открытий (#493, #496) — вместе с simulation-engineer в том же пакете изменений, меняющих эталоны. Оценка ≈ 26–30 дней.

## Критерии приёмки
- [ ] Без приоритетов — хеши совпадают с main
- [ ] P-8: приоритетный узел никогда не медленнее неприоритетного
- [ ] P-7: «построить всё в 1946» не доминирует
- [ ] reviewer APPROVE, Codex

## Связанные документы
`docs/game-design/PLAYER_LEVERS_WAVE1.md`, `docs/game-design/economy/production-lines-and-licence-market.md`, `docs/game-design/playtests/2026-11-24-owner-m-play.md`

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
