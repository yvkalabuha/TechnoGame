---
id: TASK-0123
title: "Lab measurement of budget rule variants: workforce, alloy and limiter columns, manifest, analysis"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
review_ref: "reviewer: APPROVE 2939b80 (step 1) and fdea2b7 (series, report); codex accept c5604833 (step 1, with TASK-0122); land.py ran no second review for the step 2 diff; landed f570000 and ffe8d0f"
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

- [ ] Замечания Codex после слияния (`review-TASK-0122-c5604833-codex.json`): самотест Lab проверяет, что выбранное правило действительно меняет бюджет (`SliceChecks.cs:93`); разбор не падает, когда у seed нет успешного прогона B1 или B2 (`tm_budget_rules.py:504`).

## Тесты
Тесты Lab на новые колонки; `verify.py --full` при правках `lab/analysis`.

## Связанные документы
`docs/game-design/economy/slice-budget-convergence.md` §7, §8; `lab/manifests/TM-horizon-2030.json`; `lab/analysis/tm_horizon_2030.py`.

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
**Шаг 1 и пилот (simulation-engineer, 2026-11-19); серия не запускалась.**
- Lab: ключ `BUDGET_RULE` (`SliceParameters`), `SurfaceVersion` 14, таблица `supply_month.csv` (формат `slice-csv-format.md` v0.5 r14 §4.17; `SupplyProbe` читает пулы до тика, раздачу кадров и ограничитель линии после). Тесты — `SliceChecks` «slice: BUDGET_RULE», «slice: supply_month» (+ `SliceModelTests`), `lab/analysis/test_tm_budget_rules.py`.
- [Факт] При `NOW` прежние 15 файлов прогона побайтно равны сборке `a4e385a` (seed 1, 150 мес.), golden = `a0b7ab02…`; эталоны Lab не перезаписаны.
- Манифест `lab/manifests/TM-budget-rules.json`, разбор `lab/analysis/tm_budget_rules.py` (`--pilot` — для частичной матрицы).
- Пилот [Факт: `matrix --seeds 1 --jobs 4`, 8 прогонов, все `ok`, 1024 мес.]: 4,2–5,3 с на прогон, вся матрица 10,3 с; оценка серии 80 прогонов ≈ 2 мин при `--jobs 4`. Потоки 1 = 2 у всех 4 серий; `NOW` seed 1 = golden TM-horizon-2030 (`63a31fe3…`). Бюджет всех серий совпадает с точной копией `BudgetMath` в целых без единого расхождения. B1 = `NOW` во всех помесячных таблицах до t = 395 (первое отличие бюджета t = 396, FRA). Потолок SWE: первый урезанный месяц 418 (B1) и 298 (B2) против 414/294 по формуле без округления и 413/293 по таблице записки — задержка 4–5 мес. из-за floor при малом бюджете подтверждена.
- [Факт, пилот, 1 seed — не вывод] Выпуск линий упирается в деньги (`Bind = Money`) в 99 % месяцев с линией (29 574 из 29 914 по всем сериям); раздача кадров ни разу не была меньше запроса (0 из 40 960 актор-месяцев).
- Команда серии: `dotnet src/Singularity.Lab/bin/Release/net10.0/Singularity.Lab.dll matrix --manifest lab/manifests/TM-budget-rules.json --out lab/results/tm-budget-rules --jobs 4`, затем `python3 lab/analysis/tm_budget_rules.py lab/results/tm-budget-rules`. Не коммитить во время матрицы (`lab-cli.md`).
