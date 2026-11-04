---
id: TASK-0028
title: "T-fleet: списание и продажа парка (A42, контракт r6)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: gate
milestone: MS-M0
agent: developer         # ответственный агент
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0027]
related: [HYP-0002]
approved_by: owner
approved_at: 2026-11-04
approval_ref: "DECISIONS_LOG #139, #141–#148; спека v0.17 A42, §11 T-fleet; контракт v2.4 r6 §4.4f"
created: 2026-11-04
---

# TASK-0028 — T-fleet: списание и продажа парка (A42, контракт r6)

## Цель
Реализовать списание и продажу парка на внешний рынок (#139, #141–#148): команды игрока и правило ИИ при перевооружении по поколению, цену, квоты, внешний приток денег в бюджет закупок, печати и абляции.

## Описание
По спеке `docs/simulation/slice-jet-aviation.md` v0.17: A42 (все пункты), §6.10, §11 строка «T-fleet» с приёмкой; контракт `docs/architecture/LAYER_CONTRACT.md` v2.4 r6 (§3.1, §4.0, §4.4d шаги 0, 2a–2g, 7, 8; §4.4e; §4.4f; §5; §6; §8; §9.1). Переключатель `FLEET_LINE_COND` (main = GEN, KAPPA и NONE — абляции); абляции NO_RETIRE, NO_SALE, PURCHASE_CAP. Не входит: T6d, F25 (why_not, следующая ревизия контракта).

## Критерии приёмки
- [ ] Всё из §11 «T-fleet» (в т.ч. 5 и 5a: в NO_RETIRE сверяются колонки T6c2).
- [ ] Инварианты FL1–FL7 и журнала ИИ (контракт §4.4f); `spent ≤ B_in`.
- [ ] Общие пункты T6 (тесты, ADR-0003, нет RNG в Industry/Products, L1/L2); golden с причиной «T-fleet».
- [ ] В «Результат» — печать на seeds 1…20 по §6.10 (E-gen, R2″, продажи gen ≥ 2, «парк на keep», Δ) как информация.

## Тесты
По §11 T-fleet.

## Связанные документы
Спека v0.17; контракт v2.4 r6; `docs/game-design/FLEET_RETIREMENT.md` v0.2; DECISIONS_LOG #139, #141–#148.

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
