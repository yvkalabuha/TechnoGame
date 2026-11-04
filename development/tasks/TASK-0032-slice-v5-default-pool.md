---
id: TASK-0032
title: "V5 по умолчанию, колонка tool_unmet и причина why_not, переключатель POOL_ORDER"
status: REVIEW         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0031]
related: [HYP-0002, EXP-0005]
approved_by: owner
approved_at: 2026-11-04
approval_ref: "DECISIONS_LOG #163, #167; спека v0.22 §11; контракт v2.4 r8 F33"
created: 2026-11-04
---

# TASK-0032 — V5 по умолчанию, колонка tool_unmet и причина why_not, переключатель POOL_ORDER

## Цель
V5 — правило A39 по умолчанию (#163); колонка `tool_unmet` и причина `why_not` `TOOLING_POOL_EXHAUSTED` (F33, #167); переключатель `POOL_ORDER` для эксперимента по распределению пула (`docs/simulation/exp-pool-order.md`).

## Описание
По спеке `docs/simulation/slice-jet-aviation.md` v0.22 §11 строки «V5 default + why_not pool» и строка developer для `POOL_ORDER` (варианты CAN, REV, PROP, SEV по `exp-pool-order.md`) и контракту v2.4 r8 (§3.1, §4.0, §4.1b, §6). Где спека расходится с контрактом r8 — следовать контракту: колонка считается в compute (не merge); загрузка старого сохранения — громкая ошибка (ADR-0013 п.7), фикстуры пересоздаются; поле `unmet` у `CultureH`/`TOOLING` — только UI. Два коммита: (1) V5 по умолчанию (сверка: main seed 41 = хеш плеча V5 EXP-0005 `a968b6ee…`), (2) колонка и причина. Ответ с Результатом — Orchestrator.

## Критерии приёмки
- [ ] Всё из §11 по обеим строкам (с поправками контракта r8).
- [ ] Общие пункты T6; golden с причиной «V5 default» и «F33».

## Тесты
По §11.

## Связанные документы
Спека v0.22; контракт r8; `docs/simulation/exp-pool-order.md`; EXP-0005; DECISIONS_LOG #163, #167.

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
**Сделано (developer; перенесено Orchestrator).** 0b95f1b — V5 по умолчанию (сверка: main seed 41, 384 мес. = `a968b6ee…`, как плечо V5 EXP-0005). a32022c — колонка `tool_unmet`, причина `TOOLING_POOL_EXHAUSTED` (ранг сортировки как `ToolingM`), переключатели `POOL_ORDER` {CAN, REV, PROP, SEV} и `k_cap`; проекция старых колонок seeds 41/384, 1/2/7/61 по 150 мес. побитно равна сборке части 1 (колонка не влияет на решения ИИ). Golden: часть 1 «A39 V5 default» (Golden60 b7107259…, Golden150 beb445d6…, F17b YPROD 1–5); часть 2 «tool_unmet column» (Golden60 ec3ce196…, Golden150 409293e4…, F17b ADMIT/YPROD 1–5), `SchemaVersion` 6. Тесты: Arch 80, Kernel 284, Core 966, Session 202.
**Печать** (seeds 1–20, 150 мес., каждый 3-й месяц): доля `TOOLING_POOL_EXHAUSTED` среди звеньев CAPABILITY — актор 0 (USA) 0,000; актор 2 (SUN) 0,312; у 1, 3, 4 звеньев CAPABILITY в окне нет.
**Отклонения.** (1) Переключатели в части 2. (2) `Unmet` у `CultureH`/TOOLING — по ячейке звена (UI). (3) Ранг сортировки как `ToolingM`. (4) `ToolingInvest(..., out unmet)` — метод интерфейса по умолчанию. (5) SEV только с V5/VINF; `PoolOrder`, `KCap` в хеше только при не-CAN/≠1. Сохранения в `src` нет — «громкая ошибка старого сохранения» применять не к чему. Lab-харнесс не читает `pool_order`/`k_cap` — для simulation-engineer.

