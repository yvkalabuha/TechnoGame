---
id: TASK-0053
title: "T11: анализ exp0002.py — критерии §7, диагностики, таблицы контролей"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: simulation-engineer
category: lab
priority: high
needs_owner: false
depends_on: [TASK-0044, TASK-0050, TASK-0051]
related: [HYP-0002, EXP-0002]
approved_by: orchestrator
approved_at: 2026-11-14
approval_ref: "спека среза docs/simulation/slice-jet-aviation.md, таблица задач, строка T11 (план M0, ROADMAP шаг 5); #220"
created: 2026-11-14
---

# TASK-0053 — T11: анализ exp0002.py

## Цель
Довести `lab/analysis/exp0002.py` до полного разбора выходов `JetSlice`/`JetSliceCalib` (формат `slice-csv-format.md` v0.5 r6) по спеке среза: критерии §7 и диагностики, нужные для T12 и EXP-0002.

## Описание
Объём — строка T11 таблицы задач спеки (`slice-jet-aviation.md`, около строки 1272):
- критерии §7: P1 по #45; P3 по §6.1 с `TVD_bc` и перестановками; парный bootstrap; Wilson; S1–S3; крайние seeds;
- диагностики: `dominant_doctrine` (§5.8), D1, D2 (§5.9), источники (§6.3), старт (§6.5), факторный план с `η²` (§6.6), `lic_knows` (§6.7);
- вывод таблиц контролей §6.1 из калибровочных прогонов;
- чтение таблиц и колонок v0.5 по `slice-csv-format.md` §10: `a40.csv`, `a40_c_months.csv` (`training`), `tooling_ledger.csv`, `lines.csv`, `fleet_cmd.csv`, `licences.csv` (`t_end` по `Active`); класс С `COEXIST_MONEY`/`OTHER`; L-C0 по `OTHER`; `eng_short_months` (по одному бюро); C1 по `c1_stand.csv`; печати §7.
Долги, закрываемые здесь:
- DEBT-0032 — повторная активация лицензии; три совпадающие серии smoke (подтвердить холостые плечи); тест, что FORCE PERT/SYM, CHANNELS, GRID, FLEET_ARM, SALE_ARM меняют выход; `ForcedStarts` без квадратичного перечитывания; повторное использование слота `_tLaunch`/`_forcedSent`; L2 на полном горизонте в CI-ночном прогоне или как вариант L1.
- DEBT-0033 — ревизия формата в `slice-csv-format.md:283`; запись в спеке, что эквивалентность «T6D_OFF = сборка до T6d» проверена 2026-11-14 на 1cf93d8.
Подготовка T12 (ROADMAP шаг 6) — отдельная задача после T11.

## Правила
- `src/Singularity.{Kernel,Core,Session}` не меняются; если нужно — остановиться и описать.
- Код Lab (C#) — только для пунктов DEBT-0032; тесты — в `tests/Singularity.Lab.Tests`.
- Результаты прогонов — в `lab/results/` (не коммитятся).

## Критерии приёмки
- [ ] `exp0002.py` читает smoke, det и calib-манифесты v0.5 r6 без ошибок и печатает все критерии §7 и диагностики; тесты анализа (`lab/analysis/tests` или аналог) на синтетических данных для каждого критерия.
- [ ] Таблицы контролей §6.1 выводятся из калибровочных прогонов.
- [ ] Пункты DEBT-0032 и DEBT-0033 закрыты или явно перенесены с причиной.
- [ ] Полный `dotnet test` зелёный, golden без изменений.

## REVIEW
- [ ] Code review (reviewer)
- [ ] Второе ревью (codex)
- [ ] Тесты (test-engineer)

## Результат
