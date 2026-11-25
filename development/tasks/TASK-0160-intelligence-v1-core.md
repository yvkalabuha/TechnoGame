---
id: TASK-0160
title: "Intelligence v1 core: security budget share, targets and modes, theft channel in CEff, counterintelligence, disinformation, confidence of estimates (#515)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: gate
milestone: MS-M05
agent: developer         # ответственный агент
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0150]
related: []
approved_by: owner
approved_at: 2026-11-25
approval_ref: "#515, #259-#261, #364-#367, O-051"
created: 2026-11-25
---

# TASK-0160 — Intelligence v1 core: security budget share, targets and modes, theft channel in CEff, counterintelligence, disinformation, confidence of estimates (#515)

## Цель
Разведка v1 в ядре по `docs/game-design/INTELLIGENCE.md` ред. 3 (#515): седьмая доля бюджета «безопасность», ≤ 3 цели с режимом (наблюдать / красть / ложные следы), канал кражи только в `CEff` (O-051), рост сети и раскрытие (#260), контрразведка, проценты достоверности оценок, скрытые события через разведку (#508).

## Описание
За выключателем, по умолчанию выкл.; при выкл. — побитно как main. Седьмая доля меняет состав долей и формат — входит в пакет изменений, меняющих эталоны. ИИ по тем же правилам. Правила черновиков ADR М1: годовые нормы, без новых очередей `cmd.*`.

## Критерии приёмки
- [ ] Нейтральность при выключенном флаге (хеши main)
- [ ] Тесты: кража ≤ s_max, раскрытие детерминировано (RNG по ключу сети), L1/L2
- [ ] reviewer APPROVE, Codex

## Связанные документы
`docs/game-design/INTELLIGENCE.md`, `docs/reviews/intelligence-gate.md`, RR-0017

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
