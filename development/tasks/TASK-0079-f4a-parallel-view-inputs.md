---
id: TASK-0079
title: "F4-A: parallel ViewBuilder.Inputs per actor, TrainingBudget plan once per actor"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0078]
related: [ADR-0016, ADR-0017, EXP-0007]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "#317 (next: ADR-0017 and F4); light, ADR-0017 «Вне ADR»"
created: 2026-11-18
---

# TASK-0079 — F4-A: parallel ViewBuilder.Inputs per actor, TrainingBudget plan once per actor

## Цель
F4-A из `docs/simulation/tick-profile-m0.md` §4: убрать повторный `Allocate` в Session без общего кэша.

## Описание
1. `ViewBuilder.Inputs` строится параллельно по акторам; результаты пишутся в слоты `rows[a]` по индексу актора, порядок вывода не зависит от потоков.
2. `ProductsInputsAdapter.TrainingBudget`: `Plan`/`RestActor` считаются один раз на актора внутри `Purchases` (локальная переменная цикла по линиям).
3. Условие ADR-0017 «Вне ADR»: кэши F2 не заполняются лениво при чтении из параллельных потоков; если заполняются — заполнить заранее или доказать потокобезопасность и детерминизм.

Вариант B (общий мемо `Allocate`) вне задачи: он задевает правило записи в compute и требует записи в ADR.

## Критерии приёмки
- [ ] `StateHash` и снимок view побайтово равны прежним (golden, `lab selftest`).
- [ ] L2: threads 1/4/8/16 × ChunkSize 1/8/32 — одинаковый `StateHash` и одинаковый view.
- [ ] Замер до/после `ViewBuilder.Inputs` на (200, 2000) в «Результате» (справочно; официальный — EXP-0007).
- [ ] `dotnet test Singularity.sln` зелёный.

## Тесты
Свойство «view параллельный == view последовательный» на (24, 180) с командами в нескольких месяцах; L2.

## Связанные документы
`docs/simulation/tick-profile-m0.md` §4 (F2, F4), ADR-0017 «Вне ADR», ADR-0016.

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят
- [ ] Детерминизм L1/L2

## Результат
