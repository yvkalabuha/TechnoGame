---
id: TASK-0004
title: "Core: модель ToyFunnel (A, B, C) на Kernel"
status: IN_PROGRESS 
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: core
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0001, TASK-0003]
related: [HYP-0001, EXP-0001]
approved_by: owner
approved_at: 2026-11-02
approval_ref: DECISIONS_LOG #16–#17
created: 2026-11-02
---

# TASK-0004 — Core: модель ToyFunnel (A, B, C) на Kernel

## Цель
Реализация замороженной модели из `docs/simulation/toy-science-industry-model.md` в `Singularity.Core.Science.ToyFunnel` на таблицах и раннере Kernel.

## Описание
Модель заморожена владельцем (#16). Спецификация — `docs/simulation/toy-science-industry-model.md`; эталон — `lab/proto/model.py` (бит-в-бит для Fx6/RNG).
- Создать `src/Singularity.Core` (ссылается только на Kernel) и `tests/Singularity.Core.Tests`.
- Перенести интерфейс модели Lab (`ILabModelFactory`/`ILabModel`/`IMetricSink`/`MetricFormat`) в `Singularity.Core.Simulation` — Core не может ссылаться на Lab. Lab переключает simulation-engineer отдельно.
- `Singularity.Core.Science.ToyFunnel`: модели A, B, C на таблицах и `TickRunner` Kernel.
- До модели закрыть DEBT-0002 (детерминированный номер чанка в TickComputeException) и DEBT-0003 (IL-правила: интерфейсы хеш-коллекций, Frozen/Immutable/Hashtable/ConcurrentBag, generic math) и распространить архитектурные правила детерминизма на сборку Core.

## Критерии приёмки
- [ ] Инварианты модели выполняются на 200 seeds.
- [ ] L1 и L2 (1/4/8 потоков) на 6 и 64 странах.
- [ ] Паритет с прототипом: для ≥20 пар (модель, seed) state_hash/итоговые raw-значения совпадают с `lab/proto/model.py` бит-в-бит; если нет — объяснение и статистический допуск.
- [ ] DEBT-0002 и DEBT-0003 закрыты с тестами; архитектурный тест применяется к Core.

## REVIEW
- [ ] Code review (reviewer)
- [ ] Тесты (test-engineer)
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
