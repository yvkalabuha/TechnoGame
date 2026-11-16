# Simulation Lab CLI (`src/Singularity.Lab`, TASK-0005)

> Связано: ADR-0008 (PROPOSED), EXP-0001 «Ревью» п. 11 (JSON вместо TOML для пилота), `exp0001-csv-format.md`.

## Краткий вывод
- [Факт] (код в `src/Singularity.Lab`) Команды: `run`, `matrix`, `hash`, `models`, `selftest`. Один прогон = один процесс; `matrix` запускает `run` дочерними процессами, до `--jobs` одновременно.
- [Факт] `manifest_hash` = SHA-256 канонического JSON (ключи по ordinal, без пробелов, числа как нормализованные decimal: `1`, `1.0`, `1e0` → `1`). Форматирование и порядок ключей на хеш не влияют; дубликаты ключей, комментарии, висящие запятые — ошибка. Число, которое decimal представил бы с округлением (> 28 значащих цифр, |x| < 1e-28, например `1e-40`), — ошибка (TASK-0008).
- [Факт] Строки: перекодируются стандартным энкодером System.Text.Json (`JavaScriptEncoder.Default`: не-ASCII, `<>&'+` и управляющие символы → `\uXXXX`), поэтому `"é"` и `"é"` дают один хеш. Нормализации Unicode (NFC/NFD) нет: `é` (U+00E9) и `e`+U+0301 — разные строки и разные хеши.
- [Факт] `golden_hash` = `StateHash(tables, SchemaVersion)` из Kernel; tick в хеш не входит (TASK-0050, 2026-11-14; хеши более ранних прогонов с новыми несопоставимы). `final_tick` остаётся отдельным полем `run.json`. `matrix` сам сверяет golden_hash одного (точка, seed) при разных `threads` (бесплатная L2-проверка).
- [Факт] (TASK-0008 этап 2) Интерфейс модели (`ILabModelFactory`, `ILabModel`, `ParamSet`, `ParamValue`, `ParamException`, `MetricFormat`) — в `Singularity.Core.Simulation`; Lab ссылается на Core и только строит `ParamValue` из JSON (`ParamValues.FromJson`). Зарегистрированы `toy-funnel` (Core, EXP-0001) и **тестовая** `test.random-walk` (`model_is_test: true`, без научного смысла).
- [Факт] (TASK-0044, T10) Модели Session `JetSlice` и `JetSliceCalib` (`ISessionModelFactory`, `src/Singularity.Lab/SessionModel.cs`): мир ведёт `SliceSession` (ADR-0014), Lab шагает его и читает views; `golden_hash` = `SliceSession.LastHash` после последнего непустого тика. Данные — `mods/core` репозитория манифеста (или бинарника Lab). Формат CSV — `slice-csv-format.md` v0.5 r6.

## Запуск
```
dotnet src/Singularity.Lab/bin/Release/net10.0/Singularity.Lab.dll <command> ...
run    --manifest M --seed N --threads T --out DIR [--point P] [--force] [--quiet]
matrix --manifest M --out DIR [--jobs J] [--timeout SECONDS] [--force] [--point P] [--seeds N]
hash   --manifest M [--canonical]
```
Коды выхода: 0 — успех; 1 — прогон/матрица/selftest провалены или непредвиденная ошибка (I/O, баг; сообщение и стек в stderr); 2 — ошибка аргументов, манифеста или отсутствующий входной файл. `--timeout` — предел одного дочернего прогона (по умолчанию 3600 с); по истечении процесс убивается, статус `timeout`. С `--force` каталог прогона очищается перед запуском. Выходные каталоги — в `lab/results/` (в `.gitignore`).

### Точки калибровки T12 (TASK-0055) [Предл.]
```
c1-stand --out DIR [--mods <dir>] [--manifest M [--point P]]          # этап C1: DIR/pNN/c1_stand.csv по каждой точке
c3-stand --out DIR --manifest M [--point P] [--seeds N] [--mods <dir>] # статистика C3 точки этапа C3+C4: DIR/pNN/c3_summary.csv
calib-print --out DIR --manifest M [--point P] [--seeds N] [--jobs J]  # печати C2/#247 точки JetSliceCalib (TASK-0056)
c5-stand --out DIR [--mods <dir>] [--manifest M [--point P]] [--horizon H] [--threshold Y]  # C5 (#248): c5_stand.csv и печати; H < 383 или Y ≠ 0,7 — смоук
```
- Параметры точки разбираются тем же `SliceParameters.Parse`, что и в `JetSliceCalib` (ключи «К» — `slice-csv-format.md` §3); стенды берут только свои: C1 — `ALPHA`, `X_SCALE`, `DELTA_H`, `C_M`; C3 — `K_DIV`, `THETA_START`, `H_MAX`. Без `--manifest` `c1-stand` считает значения §8.1.
- **Этап C1** — `lab/manifests/EXP-0002-c1.json` (25 точек, стенд детерминирован, `seeds` — формальность схемы): `c1-stand --manifest lab/manifests/EXP-0002-c1.json --out lab/results/c1` (все точки по порядку файла) или `--point P`; затем для каждой `exp0002.py --c1 lab/results/c1/pNN/c1_stand.csv --json …` (`loss_C1`). Остановка — по правилу спец. §8.2 п.4 (первая точка с `L` = 0 в порядке файла).
- **Этапы C3+C4 и C2** — свои манифесты `lab/manifests/EXP-0002-c3c4.json` (фаза А, точки 0…44) и `EXP-0002-c2.json` (точки 0…8); `EXP-0002-calib.json` — базовая точка §8.1 и объявление калибровки (`meta`). Одна точка на её 100 калибровочных seeds: `matrix --manifest lab/manifests/EXP-0002-c2.json --point P --out lab/results/c2-pP` (`--point`/`--seeds N` — выбор точки и первых N seeds; индексы и run id — как у полного манифеста, `matrix.json.selection`), затем `exp0002.py --calib lab/results/c2-pP --point P` (C2; для C3+C4 — C4) и для C3+C4 ещё `c3-stand --manifest lab/manifests/EXP-0002-c3c4.json --point P --out …` (C3). `--calib --point P` берёт только прогоны точки P (`runs.csv` `point`), поэтому годится и общий каталог `matrix` без `--point`.
- ~~**Эскиз HYP-0004 (TASK-0057)**: параметр `CULTURE_VARIANT` и `c1-stand --culture-variant` (хук `IntroCultureRule`)~~ — **удалены в TASK-0058 шаг 5 (коммит 840dc6b)** вместе с `INTRO_CHANNEL` и `PILOT_CHOICE`: I-OC и A41 SLOT для P2+P3 — поведение сборки (контракт r12/r13a, #245, #247). Манифесты и скрипты с этими ключами — исторические, требуют сборки ≤ 84d833d (`lab/manifests/README.md`). [Факт: код] Остались выключатели модели `A41_FORM` ∈ {`SLOT`, `WHOLE`} и `PILOT_PROFILES` ∈ {`P2P3`, `P3`} (`slice-csv-format.md` §3; не по умолчанию — колонка `run.a41`). Стенд C1 (`C1Stand`) считает линию стенда рядом C (`Planned`, `GrowthNominal = V_C`).
- **`calib-print` (TASK-0056)** — прогоняет точку манифеста `JetSliceCalib` так же, как `matrix` (финальный хеш = `golden_hash`, файл `fielded_runs.csv`), и пишет: `fielded_actor.csv` (по актору: месяцы, где `G` по `fielded_L` > `G` по правилу r6 и наоборот, первый месяц `G ≥ g` по обоим правилам), `fielded_spells.csv` (эпизоды `G_L > G_r6`: начало, конец или пусто — до горизонта), `designs.csv` (проекты: поколение, `prc_set`, первые месяцы стадий). `G` по r6 пересобирается из представлений того же снимка (стадия, ⌊в строю⌋ ≥ `n_service_min`, все процессы `Mastered`) — только для печати #247/RISK-0022, решения его не читают. Разбор — `lab/analysis/t12_c2_print.py CALIB_DIR PRINT_DIR` (КМ и моды C2, «процесс брошен», K-классы, A40, #247, инварианты §9.1 r13a).
  - r12 (ревью 321d8c9): точка с `FORCE` или `CONTROL_TABLES` — ошибка использования (код 2): печать строит сессию из конфигурации и не повторила бы мир matrix (`ForceProxy`, таблицы). Колонки поколений `first_l_g{g}`, `first_r6_g{g}` — по `Generations` раскладки; `t12_c2_print.py` берёт поколения из заголовка и `LAST` = `months − 1` из `fielded_actor.csv`. `n_service_min` правила r6 — значение `ProductsParams` по умолчанию: ни один ключ манифеста его не меняет (`SliceWorld` пишет другие поля). Проверка — selftest Lab «calib-print» (1 seed, 60 мес.: хеш = `golden_hash`, `G_r6 ≤ G_L`, первый вход совпадает, отказ при `FORCE`/`CONTROL_TABLES`).
- **`c5-stand` (TASK-0056, #248)** — стенд C5 спец. v0.33 §8.2 п.1, формат `slice-csv-format.md` §6.1. Считается один раз на замороженной точке (`--manifest lab/manifests/EXP-0002-calib.json --point 0`) после короткого gate определения. Selftest — смоук (горизонт 6, порог 0,25): фазы простоя и перезапуска, побайтное равенство двух прогонов.
- **Заморозка между этапами:** выбранные значения этапа пишутся в общий `params` **более поздних** манифестов этапов (C1 → `c3c4`, `c2`; C3+C4 → `c2`) и `EXP-0002-calib.json` (прогон таблиц контролей §8.2 п.8), ревизия с новым `manifest_hash` в `meta.journal`. Манифест этапа перебирает (`sweep`/`params` серий) только свои ключи, поэтому замороженный ключ никогда не становится одновременно параметром и осью (`Manifest.ReadPoints` это запрещает). Фаза Б `κ` — серии `C3C4-B/rank=r` в `EXP-0002-c3c4.json` по `meta.phase_B`.

## Манифест (schema 1)
| Ключ | Обяз. | Смысл |
|---|---|---|
| `schema` | да | `1` |
| `model` | да | id модели из `ModelRegistry` |
| `ticks` | да | число тиков |
| `seeds` | да | массив целых `[0, 2^64)` или `{"start": a, "count": n}` |
| `threads` | нет | целое или массив (по умолчанию `[1]`) |
| `chunk_size` | нет | размер чанка TickRunner (64) |
| `params` | нет | параметры модели: число, строка, bool, массив/объект |
| `series` | нет | `[{"name", "params"?, "sweep"?, "seeds"?}]`; без него — одна серия `default`. `seeds` серии (тот же формат) заменяет общий `seeds` для её точек (TASK-0008: у EXP-0001 main 200 seeds, grid 50) |
| `data` | нет | пути к файлам данных относительно манифеста → `data_hash` |
| `experiment`, `description`, `meta` | нет | хешируются, Lab их не интерпретирует |

Неизвестный ключ верхнего уровня — ошибка. Точки матрицы: серии по порядку в файле; внутри — декартово произведение осей `sweep` (оси по ordinal, последняя меняется быстрее). Параметр серии перекрывает общий; ключ и в `params`, и в `sweep` — ошибка. Порядок прогонов: точка → seed → threads; run id `p{point}-s{seed}-t{threads}`.

Числа передаются модели как текст decimal: `GetFx6Raw` даёт точный raw ×10^6 (больше 6 знаков после точки — ошибка, без округления); `GetDouble` — ближайший double к тексту, то же значение, что `float()` в Python (для коэффициентов, которые спецификация модели задаёт как double, например `dt` = 1/12 и калибровка B). Ошибка параметра модели (`ParamException`) вне прогона — код 2; внутри `run` — `status: failed` в run.json.

## Выход
- `run`: `DIR/run.json`, `DIR/runs.csv` (1 строка), `DIR/<metric>.csv` (колонка `run` + колонки модели).
- `run.json`: `status`, `error`, `run`, `experiment`, `model`, `model_is_test`, `series`, `point`, `seed`, `threads`, `chunk_size`, `ticks`, `final_tick`, `golden_hash`, `core_commit`, `dirty`, `dirty_untracked_src`, `build_commit`, `manifest_hash`, `manifest_path`, `data_hash`, `data_files`, `params`, `unused_params`, `started_utc`, `elapsed_ms`, `runtime`, `os`; `model_info` — поля модели Session (у табличных моделей пуст; состав — `slice-csv-format.md` §9.1).
- `core_commit` = `git rev-parse HEAD`; `dirty` = есть изменения в отслеживаемых файлах (неотслеживаемые не учитываются); `dirty_untracked_src` = есть неотслеживаемые файлы в `src/` (SDK компилирует все `*.cs`, так что они меняют бинарник); вне git — `"unknown"`/`null`. `build_commit` — коммит, из которого собран бинарник (SDK дописывает `+<SourceRevisionId>` к `AssemblyInformationalVersion`); если он ≠ `core_commit`, бинарник собран не из текущего HEAD. [Предп.] Незакоммиченные правки на момент сборки `build_commit` не отражает.
- [Факт] (TASK-0053, DEBT-0032) **Не коммитить во время `matrix`.** Каждый дочерний `run` заново читает `core_commit`/`dirty`; прогоны, начатые после коммита, расходятся с `matrix.json` по provenance, и матрица завершается с `problems` («provenance differs from matrix»). Длинные матрицы — на зафиксированном дереве или в отдельной копии (`git archive`).
- `data_hash` = SHA-256 по файлам в ordinal-порядке путей: UTF-8 путь, байт 0, SHA-256 содержимого. Нет файлов → SHA-256 пустого входа (`e3b0…b855`).
- Файлы `data_hash`/`data_files` — `data` манифеста плюс файлы данных, которые загружает модель Session (`ISessionModelFactory.DataFiles`; у `JetSlice`/`JetSliceCalib` — `mod.yaml` и перечисленные в нём файлы загруженных модов, из `GameData.Mods`), пути относительно манифеста, без повторов (TASK-0044, ревью Codex). `model_info.data_hash` — канонический хеш таблиц `GameData.DataHash`, это другой хеш.
- `matrix`: `DIR/runs/<run>/…` (+ `log.txt`), объединённые `DIR/runs.csv` и `DIR/<metric>.csv` в порядке прогонов (не зависит от порядка завершения), `DIR/matrix.json` с `golden` по прогонам, `problems`, `thread_mismatch` и `golden_set_hash` = SHA-256 строк `"<run> <golden_hash>\n"` в порядке прогонов (только если все прогоны ok и нет `thread_mismatch`). Нечитаемый `run.json` ребёнка — статус прогона `bad-run-json`, `matrix.json` всё равно пишется. `runs.csv`: `run, series, point, seed, threads, status, golden_hash` + объединение ключей параметров.
- [Факт] `toy-funnel` пишет `yearly`, `countries`, `milestones`; в `runs.csv` EXP-0001 — все параметры точки (в т.ч. `model`, `nu`, `beta`, `gamma`, `t_ref`). Текст чисел отличается от прототипа (`25.32777` против `25.327770`), значения те же.

## Проверка (TASK-0005, 2026-11-02)
- `selftest`: 13/13 PASS (канонизация, Fx6, раскладка матрицы, golden при повторе, 1 vs 4 потока, чувствительность к seed, путь ошибки, data_hash).
- `hash` на `examples/test-random-walk.json` и на его переформатированной копии (обратный порядок ключей, отступ 7, `492.0`) — одинаковый `313ab3e2…f4ac8`.
- `run` p1/seed 5 дважды (threads 1, повтор с `--force`) и с threads 4 — один `golden_hash` `1f84b2f8…9498`.
- `matrix` 100 прогонов (`--jobs 16` и `--jobs 3`): ok=100, thread_mismatch=0, ~2,4 с; `golden_set_hash` одинаков, объединённые CSV побайтно равны.
- Матрица с `fail_at_tick`: exit 1, ошибки перечислены, `golden_set_hash: null`.

## Проверка (TASK-0008 этап 2, 2026-11-02)
- `selftest`: 17/17 PASS (+ длинные десятичные → точный double; `toy-funnel` из `Singularity.Core`).
- EXP-0001, seed 1, ν=0,25: точки p0 (A), p2 (B), p4 (C) — `yearly`/`countries`/`milestones` по значениям равны свежему прогону прототипа (Fx6 — точный decimal, double — побитно); `golden_hash` A и B = `state_hash` прототипа; у C хеш другой по построению (schema 2 включает историю K). Дополнительно: p3 (B, ν=1) seed 7 — хеш равен; p0 threads 4 = threads 1. `unused_params` пуст. Скрипт: `lab/results/t8-stage2/compare.py` (не коммитится).

## Открытые вопросы
- [Факт] (TASK-0008) `lab/manifests/EXP-0001.json` переведён в schema 1: модель `toy-funnel`, вариант — параметр `model` (A/B/C), серии `main/A`, `main/B/nu0.25`, `main/B/nu1.0` (калибровка B своя для каждого ν), `main/C`, `grid` (свои 50 seeds); `archetypes`/`levels` — в `params`, остальное — в `meta`. Анализ берёт группу серии до `/`. Этап 2: `n_ticks` убран из `params` (длина прогона — `ticks`); `B_c_tau0`/`B_delta_tau` в общих `params` со значениями по умолчанию прототипа (1,0 / 0,25), потому что Core читает их для всех вариантов; A и C их не используют, серии `main/B/nu*` перекрывают калиброванными.
- [Предл.] Core мог бы читать `B_*` только для варианта B и `C_*` только для C — тогда в `runs.csv` A не было бы посторонних значений. Это меняет манифест и `manifest_hash`; решать до официального прогона.
- Тестовый проект `tests/Singularity.Lab.Tests` (вне зоны simulation-engineer) должен заменить `selftest`; архитектурный тест — правила детерминизма для `Singularity.Lab.Models.*`.
