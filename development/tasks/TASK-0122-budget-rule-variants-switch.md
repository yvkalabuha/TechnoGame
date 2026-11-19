---
id: TASK-0122
title: "Budget rule variants behind a run switch for the Lab measurement (NOW, A, B1, B2)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M05
agent: developer         # ответственный агент
category: core           # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: high           # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0123]
approved_by: orchestrator  # owner | orchestrator (orchestrator — только level: light)
approved_at: 2026-11-19
approval_ref: "#430, O-025"
created: 2026-11-19
---

# TASK-0122 — Budget rule variants behind a run switch for the Lab measurement (NOW, A, B1, B2)

## Цель
Lab может посчитать партию до 2030 года с четырьмя правилами бюджета: `NOW` (как в `main`), `A`, `B1`, `B2` из `docs/game-design/economy/slice-budget-convergence.md` ред. 2 (§3, §4). Нужно для замера до решения владельца по #430 (PLAN «Фокус», п. 2).

## Описание
- Переключатель запуска `BUDGET_RULE` по образцу `LINE_CLOSE` / `TOOLING_TARGET` (`SliceRunConfig`, `SliceParameters`, `PolicyProfile`). По умолчанию `NOW`.
- Параметры вариантов (`s`, `λ` для A; потолок `c` для B1, B2) — набор констант замера в коде, не колонки таблиц и не данные сценария: `SchemaVersion` не меняется.
- Это переключатель для замера, а не принятое правило: правило выбирает владелец, в данные и схему оно попадёт вторым «поездом схемы».
- Вне объёма: `PolicyLiveness` для новых правил (записать, что не сделано), стартовые варианты (`growth_pa`), клиент.

## Критерии приёмки
- [ ] При `NOW` все эталонные хеши и тесты не меняются.
- [ ] Тесты T1–T7 из §9 записки (T4: чтение `B_USA` из `Current`; T5: USA бит-в-бит как `BudgetMath.Next`).
- [ ] Нет аллокаций на пути тика; целочисленная арифметика `Fx6` с `checked`.
- [ ] `python3 tools/development/verify.py` проходит.

## Тесты
T1–T7 записки §9; существующие эталоны при `NOW`.

## Связанные документы
`docs/game-design/economy/slice-budget-convergence.md`, `docs/reviews/slice-budget-convergence-{critic,advocate,deepseek}.md`, `docs/simulation/m-play-horizon-2030.md`.

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
