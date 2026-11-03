---
id: TASK-0023
title: "T6b: ИИ способности — план оснастки A39, учебная серия, программа качества"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0022]
related: [HYP-0002]
approved_by: orchestrator
approved_at: 2026-11-03
approval_ref: "DECISIONS_LOG #125; спека v0.12 §11 T6b; контракт v2.4 r3"
created: 2026-11-03
---

# TASK-0023 — T6b: ИИ способности — план оснастки A39, учебная серия, программа качества

## Цель
Правила ИИ и системы для слоя способности среза (T6b спеки v0.12): план оснастки A39 с поддержкой A30 (закрыть DEBT-0015), деньги учебной серии через `TrainingBudget` (F15, #125), программа качества. Снять DEBT-0018: длинные прогоны должны показывать экономику.

## Описание
- `ToolingPlan(a, t, rest_a)` по A39: один пул денег (сначала `rest_a`, затем `B_cap`), проход 1 — поддержка A30, проход 2 — узкие места `M`; порядок site → MET, CHEM, MECH, AERO → precision, purity, qc; водопад в raw `Fx6`; потолок ячейки `(M_cap − M_dec)·cost_M`; невзятое из `rest_a` возвращается (#118); остаток `T_a` — в учебную серию. Подставить вместо константы `ToolingInvest=0` из T6a.
- `TrainingBudget(actor, design, t)` в `IProductsInputs` и шаг 7 A38 по контракту v2.4 r3 §4.4d (`from_left`, `tb_spent`, колонка Σ `tb_spent`); при `tb ≡ 0` поведение прежнее.
- Программа качества: состояние, `reserve_workforce`, `QualityProgramme`, триггер P2 (A41; 0,05 из закупок на 84 мес., F13). Триггеры P1/P3 по `why_not` — не здесь (T6d).
- После правил: перезаписать golden-60 и L1 384 мес. с работающей оснасткой, повторить L2 (DEBT-0018); причина в сообщении коммита.

## Критерии приёмки
- [ ] Всё из спеки v0.12 §11 «T6b» (инварианты A39, регрессия DEBT-0015 на фикстуре «2 site», план одинаков во всех вызовах месяца, `M ≤ M_cap`, A30 держит `p*` 120 мес., баланс `Σ взято + неизрасходованное = rest_a + B_cap`, программа качества).
- [ ] Инварианты контракта v2.4 r3 §4.4d для `TrainingBudget`.
- [ ] Общие пункты §11 T6a–T6d (тесты, ADR-0003 R1–R3, нет `CounterRng` в Industry/Products, L1/L2).
- [ ] На 384 мес. main выпуск не падает в 0 из-за износа оснастки (печать по акторам в тесте или отчёте задачи).

## Тесты
По §11 T6b; фикстура «2 site»; golden и L1/L2 на сборке Session.

## Связанные документы
Спека `docs/simulation/slice-jet-aviation.md` v0.12 (§3.4, §3.5 A22, A30, A37–A39, A41, §4, §11); `docs/architecture/LAYER_CONTRACT.md` v2.4 r3 (§4.1b, §4.4d, §5); `docs/architecture/SLICE_COMPOSITION.md` r1; `docs/game-design/PROCESS_DEVELOPMENT.md`; DEBT-0015, DEBT-0018; TASK-0022 «Результат».

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
