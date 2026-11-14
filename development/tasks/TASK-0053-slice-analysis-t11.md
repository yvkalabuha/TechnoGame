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
(simulation-engineer, 2026-11-14)

**Анализ `lab/analysis/exp0002.py`** (спец. v0.25 r2, формат v0.5 r7):
- Критерии §7 (P1–P5, исход, S1–S3, крайние seeds) и диагностики §5.8, §5.9, §6.3, §6.5–6.7 были в скрипте с T10; T11 добавил чтение и печати таблиц v0.5: `a40.csv`, `a40_c_months.csv` (`left`, `training`, `w_after_n`), `tooling_ledger.csv`, `lines.csv`, `fleet_cmd.csv`, `licences.csv` — флаги P0 L-O0 (`DEFECTS` в main = 0), L-C0 (`OTHER` в main = 0), сверка класса `COEXIST_MONEY` с `left = 0 ∧ training = 0`, инварианты леджера §4.11, Σ `left_share` ≤ 10⁶, R-gen, `licences.csv` = `LIC_BUY`; печати A40 по сериям, `pool_exh`, F17b (сосуществования, длительность с цензурой 384, `HORIZON`, причины конца, `left_share` N, `w_after_n`, запуски `g_p(X) ≤ g_p(W)`), лицензии §6.11 (пары, срок, цена, отказы по деньгам, `LIC_RESUME`, первая лицензия FRA по профилю), парк §6.10 (E-gen, `ALLOY`, #146 [Предп.: `G` = `gen_line`]); холостые серии (golden = main). P5 подписан `k_diff` и `r`; `volume_lead` — строка t = 348.
- `--calib`: таблицы §6.1 по erratum v0.24 r2 — стартовое знание −384 в `m`, `s`, `δ`, участок Б `Y_k`, `U_k` (768 узлов), проверка T-SK; C2 — цензура +∞ (нижняя медиана), исключение освоенных на старте, «tier-5 длиннее» при полной цензуре не выполнено; читаемая печать таблиц; P0 калибровочной сборки (A40, леджер). Исправлено: прежний C2 давал −0,08 года (пары с `t_07 = −1`) и конечные значения вместо +∞; прежние `m`/`δ`/`Y_k` игнорировали стартовое знание.
- `--c1 c1_stand.csv`: C1a, C1b, C1d, C1e (`d_C1e`), Wright P0 — [Предп.] знак `dlog_*` (падение > 0) уточнить, когда T12 напишет стенд.
- Тесты: `lab/analysis/test_exp0002.py` — все 40 проверок `--selftest` (P1–P5, исходы A–J, S1–S3, D1, D2, η², источники, детерминизм анализа, таблицы контролей) + 11 случаев на синтетике для печатей v0.5, C1, T-SK/C2/−384, подписей P5.

**Прогоны** (`lab/results/t11-*`, не коммитятся; дерево 276f2ea + правки, dirty): smoke 88/88, det 60/60 (L2 1/4/8 OK, `thread_mismatch` 0), calib 100/100; анализ читает все три без ошибок. [Факт] smoke: L-O0, L-C0, леджер, Σ `left_share`, R-gen, `licences` = `LIC_BUY` — OK; холостая только `lite` (ожидаемо). calib (до T12): C2 tier-4 цензура 92% → `d` = 1, C4 G2/G3 0%; T-SK OK; `Y_3` стартового знания падает до 0,23 на τ = 444 (t = 60) — печать для T12.

**DEBT-0032:** закрыто — повторная активация (`LIC_RESUME`, `SliceObserver.Track`, проверка `slice: licence track`); `ForcedStarts` обрабатывает только новые записи лога; повторное использование слота — Core не переиспользует слоты (`ProductsLayout`), наблюдатель падает, если слот из С вернулся в П/О; тест переключателей (`slice: switches`, 14 значений FORCE/START/CHANNELS/GRID/FLEET_ARM/SALE_ARM, seed 1, полный горизонт — все меняют golden); L2 на полном горизонте (`slice: L2`, было 150 мес.; плюс серия `det`); «три совпадающие серии smoke» — на r6/r7 совпадает с main только `lite` (холостое плечо по определению); ревизия формата в манифестах и lab-cli — r7; предупреждение о коммите во время матрицы — `lab-cli.md`. Открыто: [Fact]-строки для двух новых проверок в `tests/Singularity.Lab.Tests` (вне зоны simulation-engineer).
**DEBT-0033:** закрыто — ревизия формата (`slice-csv-format.md` §8 и манифесты — v0.5 r7); запись в спеке §6.11 (v0.25 r2): эквивалентность «`T6D_OFF` = сборка до T6d» [Факт] перепроверена 2026-11-14 тестом на дереве 1cf93d8 (`git archive`, тест пройден). Перенесено (вне T11, тесты Core/Session): ToyFunnel parity, отказ `BuildLk` при узле > max, L_K на 2 актора с узлом 1000, `TablesDigest` без ActorCount/MaxNodeNumber — developer/test-engineer.

**Для T12:** блокеров со стороны анализа нет. Нужны: формат файла `CONTROL_TABLES` (JSON `--calib` — кандидат), стенд `c1_stand.csv`, ключи `LAYER = LC/LP`.
