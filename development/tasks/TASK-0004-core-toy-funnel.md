---
id: TASK-0004
title: "Core: модель ToyFunnel (A, B, C) на Kernel"
status: PROPOSED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: core
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0001, TASK-0003]
related: [HYP-0001, EXP-0001]
approved_by:             # owner | orchestrator (orchestrator — только level: light)
approved_at:
approval_ref:
created: 2026-11-02
---

# TASK-0004 — Core: модель ToyFunnel (A, B, C) на Kernel

## Цель
Реализация замороженной модели из `docs/simulation/toy-science-industry-model.md` в `Singularity.Core.Science.ToyFunnel` на таблицах и раннере Kernel.

## Описание
Утверждается владельцем после TASK-0003 (заморозка модели).

## Критерии приёмки
- [ ] Инварианты модели выполняются на 200 seeds.
- [ ] L1 и L2 (1/4/8 потоков) на 6 и 64 странах.
- [ ] Результат совпадает с прототипом статистически (квантили итоговой мощи в пределах допуска, указанного в TASK-0003).

## REVIEW
- [ ] Code review (reviewer)
- [ ] Тесты (test-engineer)
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
