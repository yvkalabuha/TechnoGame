---
id: TASK-0032
title: "V5 по умолчанию, колонка tool_unmet и причина why_not, переключатель POOL_ORDER"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
