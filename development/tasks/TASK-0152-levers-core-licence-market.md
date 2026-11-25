---
id: TASK-0152
title: "Player levers core: licence market (multi-licence state, seller catalogue, BuyLicence, AdoptLicensedDesign, AI price rule, selling own projects)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: gate
milestone: MS-M05
agent: developer
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0148, TASK-0154, TASK-0150]
related: [TASK-0148, TASK-0156]
approved_by: owner
approved_at: 2026-11-24
approval_ref: "#482 (designs approved), #470-#481"
created: 2026-11-24
---

# TASK-0152 — Player levers core: licence market (multi-licence state, seller catalogue, BuyLicence, AdoptLicensedDesign, AI price rule, selling own projects)

## Цель
Рынок лицензий полного объёма (#471, #477, #478).

## Описание
docs/game-design/economy/production-lines-and-licence-market.md разд. 2: несколько лицензий на страну (ключ seller, gen), постоянный каталог LP-1 (#158), цена = m × справочная, BuyLicence через очередь сделок и ApplyDeals, AdoptLicensedDesign, правило покупки ИИ (цена ≤ k × справочная и не дороже своего исследования), продажа только своих проектов, круг покупателей по данным союзов (TASK-0154). Взнос из резерва (O-055). Может быть разделена на подзадачи developer'ом с согласия Orchestrator.

## Критерии приёмки
- [ ] Тесты на каждый отказ (Self, Empty, Gen, Money, круг)
- [ ] Игрок не может опустошить ИИ ценой (тест)
- [ ] reviewer APPROVE, Codex

## Связанные документы
`docs/game-design/PLAYER_LEVERS_WAVE1.md`, `docs/game-design/economy/production-lines-and-licence-market.md`, `docs/game-design/playtests/2026-11-24-owner-m-play.md`

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
