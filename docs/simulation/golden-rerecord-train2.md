# Перезапись эталонов поезда 2 (TASK-0147)

> simulation-engineer, 2026-11-24. Ветка `task-0147`. Перезапись — developer `91949e6`, `63004e4` (tests/), simulation-engineer `3eaa727` (Lab). Порядок — решение Orchestrator «перезапись эталонов поезда 2» (строка `docs/decisions/ORCH_DECISIONS.md` на `main`, 2026-11-24; в ветке её пока нет, поэтому номер здесь не пишется — `kb_check` сверяет номера с таблицей ветки).

## Краткий вывод
- Все эталоны траекторий на текущих данных изменились по двум причинам этой ветки: **узлы 82–99** (промежуточные ступени G5/G6 ред. 3.1, решение владельца № 457, плюс правки `requires`/`informs` узлов 65–81) и **умолчания цены опережения** `k` 0,02, степень 2, потолок 10 (№ 465). Освоение производства (№ 452) и старение парка (#431) уже были в базе ветки и в прошлых эталонах — они причиной этой перезаписи не являются.
- Тесты идентичности («старые колонки равны сборке до изменения») не удалены и не ослаблены по смыслу: они идут **нейтральным прогоном** — данные 61 узла (`Fixtures/core-g4`) с явным `AheadCostK = 0` (`SliceTestData.CreateLegacyNeutral`).
- L1/L2 на новых эталонах: threads 1 = 4 [Факт, ниже]. `verify.py` после перезаписи: все наборы тестов и selftest зелёные (архитектурный тест исправлен в 4d37a33).

## Перезаписанные эталоны и причина
| Где | Эталон | Причина |
|---|---|---|
| `Core.Tests/Data/DataHashTests.cs` | `CoreDataHash` (`d9dce0a2…` → `95965fa6…`) | данные: узлы 82–99, ред. 3.1 `requires`/`informs` 65–81 (только данные, не код) |
| `Session.Tests/SeriesSwitchTests.cs`, `CalibrationParamsTests.cs`, `LinkedRunTests.cs`, `ViewTests.cs`, `ProductAiSessionTests.cs` (эталон месяца 149, seed 1) | `fade1e71…` → `7720220e…` | узлы 82–99 + умолчания цены (№ 465): меняется каждая траектория на текущих данных |
| `Session.Tests/F17bTests.cs` | два ряда по 5 хэшей (seeds) | то же |
| `Session.Tests/SchemaTests.cs` | `PinnedFingerprint` | то же |
| `Session.Tests/TwoWorldsOnOnePoolTests.cs` | дайджесты seeds 1–5, 7 | то же |
| `Session.Tests/WhyNotF25Tests.cs` | — (эталон не менялся) | прогон переведён на `CreateLegacyNeutral`: диагноз записан на мире 61 узла без цены |
| `Session.Tests/Fixtures/mastery-baseline.txt` | строки месяца 387, seeds 1–3 | снята заново на голове ветки: сравнить с данными 81 узла по построению нельзя (решение Orchestrator) |
| `Lab: SliceChecks.Golden150` (selftest BUDGET_RULE «NOW = main», `Lab.Tests` `Lab_path_gives_Session_golden_seed1_month149`, `Budget_rule_key…NOW_keeps_the_main_golden`) | `fade1e71…` → `7720220e…` (= Session `MainSeed1Month149`) | то же, что Session месяц 149 |
| Lab `Golden60` и др. в tests/ (developer) | см. дифф `91949e6` | то же |

## Нейтральный прогон (решение Orchestrator)
- Где тест проверяет, что новый параметр **ничего не меняет при выключении**, он идёт с явным `AheadCostK = 0` (веса дисциплин по умолчанию = 1) на фикстуре той же сборки: `HorizonTests`, `Train2IdentityTests`, `GoldenProjectionAgainstMainTests`, `FleetAblationTests`, `T6dTests`, `ToolingUnmetTests`, `MasteryIdentity`-проекции, `DailyTickProbeTests`, `ObservablesTests` (сканы, настроенные на месяцы без цены). Колонки `run.k.ahead_*`, `run.k.disc_weight.*` из сравнения исключены (`ColumnProjection`): их нет в сборке, с которой сравнивают, и они пишутся только при заданном ключе.
- Где сравнение с прежними данными невозможно по построению (81 узел → 99), фикстура снята заново на новой базе с записью причины в самой фикстуре.
- Цена этого: стражи идентичности проверяют нейтральность нового кода на старых данных, а **не** то, что новые данные дают прежнюю историю — это и не так (g5-g6-probe.md §10).

## L1/L2 на новых эталонах [Факт]
- `verify.py` (`nice -n 10`, голова `3eaa727`): зелёные Session 530/534 (4 пропущены), в том числе `LinkedRunTests.L1_TwoRuns_SameHash_AndMatchesGolden`, `ViewTests.Views_DoNotChangeTheHash_AndAreEqualAcrossThreads_L1`, `ProductAiSessionTests.WithTheAiOn_L2_ThreadsAndChunksGiveOneHashEveryMonth…`, `TwoWorldsOnOnePoolTests` (threads 1/2/4); Core 1133/1133 (в т. ч. `KnowledgeAuditTests` L2 3 и 8 потоков, 1020 мес., `k` > 0); Lab 213/213; selftest Lab OK (в т. ч. «full run to 2030-12 … threads 1 = 2»).
- Матрицы зонда на 99 узлах с ценой `k` 0,02 / степень 2 / потолок 10 (траектория умолчаний № 465, `golden_hash` другой из-за колонок `run.k.*`): threads 1 = 4 у всех 10 seeds, 1024 мес. (g5-g6-probe.md §10.7).
