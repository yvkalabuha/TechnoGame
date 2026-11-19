---
id: TASK-0123
title: "Lab measurement of budget rule variants: workforce, alloy and limiter columns, manifest, analysis"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M05
agent: simulation-engineer  # ответственный агент
category: lab            # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: high           # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0122]
related: [TASK-0122]
approved_by: orchestrator  # owner | orchestrator (orchestrator — только level: light)
approved_at: 2026-11-19
approval_ref: "#430, O-025"
created: 2026-11-19
---

# TASK-0123 — Lab measurement of budget rule variants: workforce, alloy and limiter columns, manifest, analysis

## Цель
Замер четырёх правил бюджета (`NOW`, `A`, `B1`, `B2`) на партии до 2030-12 по плану §7 записки `docs/game-design/economy/slice-budget-convergence.md`. Исход меняет решение владельца: выбор А, Б или отложить (В).

## Описание
- Шаг 1 (не зависит от TASK-0122): Lab печатает по стране и году кадры (`WorkforceAvailable`), потолок сплавов (`AlloyCap`), наибольший месячный выпуск линии и долю месяцев, когда выпуск ограничивали кадры, сплавы или деньги. Формат CSV — новая редакция `docs/simulation/slice-csv-format.md`.
- Шаг 2 (после слияния TASK-0122): манифест с сериями `NOW`, `A`, `B1`, `B2`, seeds 1–10, потоки 1 и 2; разбор в `lab/analysis/`; отчёт `docs/simulation/budget-rule-measurement.md`.
- Перед серией — пилот на реальной машине (1 seed, все серии) с проверкой правил годности; серия дольше 15 минут без пилота не запускается.

## Критерии приёмки
- [ ] Величины 1–6 из §7 записки по годам 1960, 1977, 1990, 2010, 2030.
- [ ] Допуски §8 проверены для A и B2 относительно `NOW`; B1 до 1978 совпадает с `NOW`.
- [ ] Критерий «деньги ограничивают» (§7) применён; указано, что не измерено.
- [ ] L2: потоки 1 и 2 дают одинаковые хеши.

## Тесты
Тесты Lab на новые колонки; `verify.py --full` при правках `lab/analysis`.

## Связанные документы
`docs/game-design/economy/slice-budget-convergence.md` §7, §8; `lab/manifests/TM-horizon-2030.json`; `lab/analysis/tm_horizon_2030.py`.

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
