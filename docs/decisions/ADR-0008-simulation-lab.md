---
id: ADR-0008
title: "Simulation Lab: прогоны, хранение, анализ"
status: PROPOSED
date: 2026-11-02
supersedes:
superseded_by:
related: [RR-0002, ADR-0002, ADR-0014, ADR-0016, ADR-0017, ADR-0020, TASK-0087]
approved_by:
approved_at:
approval_ref:
---

# ADR-0008 — Simulation Lab: прогоны, хранение, анализ

> После ACCEPTED текст не меняется. Изменение решения — новый ADR с `supersedes`.
> Ревизия 3 (systems-architect, 2026-11-18): правки gate rev 2 (`docs/reviews/adr-0003-0005-0007-0008-critic.md`, `…-advocate.md`, `adr-0008-deepseek.md`). Решение владельца #374 (стек JSON + CSV + stdlib, пересмотр по 10 ГБ / 30 мин) внесено в п. 8; порог стал наблюдаемым; условия валидности матрицы уточнены; `golden_set_hash` — поле воспроизводимости матричных экспериментов; полный список ключей манифеста и provenance; ToyFunnel в Lab (#375). Вопросов владельцу нет.
> Ревизия 2 (2026-11-18): текст приведён к работающему Lab (TASK-0005, -0008, -0044, -0064; EXP-0001…0007). Ревизия 1 предлагала TOML, Parquet, DuckDB, polars.

## Краткий вывод
- Стек Lab закреплён #374: манифест JSON, метрики CSV, анализ — Python stdlib со скриптом на эксперимент. Parquet/DuckDB — новым ADR, когда CSV одного эксперимента > 10 ГБ или анализ > 30 мин; обе величины записываются.
- Один прогон — один процесс; `run.json` с полным provenance; `golden_hash` прогона и `golden_set_hash` матрицы.
- Матрица валидна, только если в ней ≥ 2 значения `threads`, `thread_mismatch = 0`, `problems` пуст и она не урезана `--point`/`--seeds`.

## Контекст
- [Факт, код] `src/Singularity.Lab`: команды `run`, `matrix`, `hash`, `models`, `selftest`, стенды T12 и `bench` (ADR-0016). `MatrixRunner` запускает тот же бинарник дочерним процессом на каждую тройку (точка, seed, threads), до `--jobs` одновременно, с таймаутом. Формат — `docs/simulation/lab-cli.md`.
- [Факт, код] Манифест JSON schema 1 (`src/Singularity.Lab/Manifest.cs:25`): ключи верхнего уровня `schema` (обязателен), `experiment`, `description`, `model` (обязателен), `ticks`, `seeds`, `threads` (по умолчанию `[1]`), `chunk_size` (по умолчанию 64), `params`, `series` (у серии — `name`, `params`, `seeds`, `sweep`), `data`, `meta`; неизвестный ключ — ошибка; `manifest_hash` — SHA-256 канонического JSON, `meta` и `description` входят в хеш.
- [Факт, код] `run.json` (`lab-cli.md`): `status`, `golden_hash` (`StateHash` Kernel), `core_commit`, `dirty`, `dirty_untracked_src` (SDK компилирует все `*.cs`), `build_commit`, `manifest_hash`, `manifest_path`, `data_hash`, `data_files`, `model`, `model_is_test`, `model_info`, `params`, `unused_params`, `seed`, `threads`, `chunk_size`, `elapsed_ms`, `runtime`, `os` и др. `matrix.json`: `golden` по прогонам, `problems` (в т.ч. «provenance differs from matrix»), `thread_mismatch`, `golden_set_hash` — только если все прогоны ok и нет `thread_mismatch`. Метрики — CSV на метрику в порядке прогонов.
- [Факт, код] `thread_mismatch` сравнивает `golden_hash` одной (точки, seed) по значениям `threads` манифеста. При `threads = [1]` сравнивать нечего. `chunk_size` матрица не варьирует; L2 по `ChunkSize` проверяют `TickRunnerL2StressTests` (ADR-0016 §4, ADR-0017 §8).
- [Факт, код] `tools/development/registry.py`: `REPRO_FIELDS = core_commit, data_hash, manifest_hash, seeds, threads, golden_hash, outcome`; `golden_set_hash` в списке нет.
- [Факт] Анализ: `lab/analysis/exp0001.py`, `exp0002.py` и др. — stdlib, детерминированные bootstrap, KS, перестановки с фиксированными seed; тесты анализа в CI (job `analysis`). Пре-регистрация: хеш замороженного скрипта — в `meta.analysis.script_sha256` манифеста (`lab/manifests/EXP-0002.json`), т.е. входит в `manifest_hash`, который закоммичен до прогона (#258, #264). `exp0001.py` печатает «поправки на множественные сравнения нет».
- [Факт] #264: замороженный `exp0002.py` занял 22,5 ГБ RSS на 3,7 ГБ CSV; принят потоковый загрузчик с доказательством побайтного равенства JSON.
- [Факт] ADR-0014: мир из систем Core Lab строит только через Session; риск «прототип Lab принят за поведение Core» закрывается тестами и маркировкой (`model_is_test`).
- [Факт] Golden мира — в тестах Session и в `tests/Singularity.Lab.Tests/ToyFunnel/ToyFunnelParityTests.cs` (после TASK-0087), на трёх ОС CI. EXP-0002-det (#316) — эталон `golden_set_hash` **только на git-теге среза M0** (#346): после миграции хеша ADR-0020 п. 7 и #228 хеши несравнимы. `lab/results/` в `.gitignore`; неисполняемые манифесты — в `lab/manifests/README.md` с коммитом.

## Решение
1. **Процессы.** Один прогон = один процесс `Singularity.Lab run`. Параллелизм прогонов — процессами (`matrix --jobs`); внутри прогона — потоки `TickRunner` (`threads`). Мир из систем Core — только через Session (ADR-0014); модель без научного смысла помечается `model_is_test: true`, её результаты в выводы EXP не входят.
2. **Манифест — JSON schema 1** в `lab/manifests/`, ключи — как в «Контексте» (закрытый набор, `Manifest.cs`). Идентичность эксперимента — `manifest_hash`. Числа передаются модели десятичным текстом (`GetFx6Raw` — точно).
3. **Provenance обязателен** в каждом `run.json`: `core_commit`, `dirty`, `dirty_untracked_src`, `build_commit`, `manifest_hash`, `manifest_path`, `data_hash`, `data_files`, `golden_hash`, `model_is_test`, runtime и ОС. Прогон с `dirty = true` или `dirty_untracked_src = true` не годится для DONE-эксперимента. Коммиты во время `matrix` запрещены.
4. **Метрики — CSV** (одна таблица на метрику, колонка `run`, порядок прогонов не зависит от порядка завершения). Результаты — только в `lab/results/`, не в git. [Предл.] `matrix.json` пишет `csv_bytes` — суммарный размер CSV матрицы.
5. **Анализ — Python stdlib** со скриптом на эксперимент в `lab/analysis/`, детерминированный (фиксированные seed), с тестами в CI. Новый скрипт читает CSV потоково (не держит все строки в памяти). Пре-регистрация: `meta.analysis.script_sha256` в манифесте до прогона; правка скрипта — отклонение в журнале эксперимента. [Предл.] Скрипт печатает `analysis_seconds` в вывод; EXP-запись его хранит.
6. **Регрессия:**
   - точная — `golden_hash` прогона и `golden_set_hash` матрицы. Матрица **валидна**, если `threads` содержит ≥ 2 значения (например, `[1, 4]`), `thread_mismatch = 0`, `problems` пуст, матрица полная (без `--point`/`--seeds`) и `golden_set_hash` не `null`;
   - статистическая — сравнение распределений (квантили, KS, bootstrap) по пре-регистрации в скрипте эксперимента. При нескольких метриках — либо одна первичная метрика, названная до прогона, либо поправка Бенджамини–Хохберга (RR-0002 §7.4).
   - **Перевыпуск golden** (смена формата хеша, правка модели): отдельный коммит с причиной; EXP-записи со старым `golden_set_hash` получают пометку «действителен на коммите/теге X».
7. **В git:** манифесты, скрипты анализа, golden-хеши (в тестах и EXP-записях), выводы в EXP-файлах.
8. **Стек закреплён (#374):** JSON + CSV + Python stdlib. Переход на Parquet (+ DuckDB/polars) — новым ADR, когда `csv_bytes` одного эксперимента > 10 ГБ или `analysis_seconds` > 1800.
9. **Golden-матрица в CI не запускается.** L2 в CI дают `selftest` (1 и 4 потока) и `TickRunnerL2StressTests`; полная матрица — перед закрытием milestone.
10. **Поля воспроизводимости EXP:** `seeds` и `threads` — из манифеста (вся матрица), `golden_hash` — одного эталонного прогона; для матричных экспериментов добавляется `golden_set_hash`.

## Рассмотренные альтернативы
| # | Вариант | Плюсы | Минусы |
|---|---|---|---|
| **A** | **JSON + CSV + Python stdlib, скрипт на эксперимент (выбран, #374)** | без зависимостей; канонизация JSON уже есть; скрипт легко заморозить и проверить хешем | CSV объёмный и медленный; память при больших прогонах (#264) |
| B | Ревизия 1: TOML + Parquet + DuckDB + polars | компактно, быстрый анализ | 3 внешние зависимости; канонизация TOML; ломает «stdlib + хеш скрипта» |
| C | Прогоны потоками в одном процессе | меньше накладных на старт | утечка состояния между прогонами, падение одного убивает все |
| D | Общий статистический модуль Lab | одно место для KS/bootstrap | пре-регистрация привязана к скрипту; общий модуль меняет прошлые анализы |
| E | Малая golden-матрица шагом CI | ловит L2 на модели целиком | дублирует `selftest` и стресс-тесты L2; минуты CI |
| F | Ось `chunk_size` в `matrix` | L2 по чанкам в каждой матрице | уже покрыто `TickRunnerL2StressTests`; расширение схемы манифеста |

## Последствия
- Плюсы: результат воспроизводится по `run.json` и манифесту; валидная матрица проверяет L2 по потокам; порог пересмотра хранения наблюдаем.
- Минусы: CSV-тяжесть; дублирование разбора между скриптами; правило валидности матрицы требует ≥ 2 значений `threads` (вдвое больше прогонов на эталонном наборе).
- Обязательно: DONE-эксперимент имеет `REPRO_FIELDS` (матричный — и `golden_set_hash`); неисполняемый манифест — в `lab/manifests/README.md` с коммитом сборки.

## Проверка
- `registry.py check` — `REPRO_FIELDS`. [Предл., light] `golden_set_hash` обязателен у DONE-EXP, выполненных через `matrix` (признак в frontmatter EXP выбирает Orchestrator при правке `registry.py`).
- CI: `Lab self-test` (`selftest`: канонизация, golden при повторе, 1 vs 4 потока, чувствительность к seed, `data_hash`), job `analysis`.
- [Предл., light] `matrix` пишет `csv_bytes` и флаг `valid` по условию п. 6; тест `selftest` — матрица с `threads = [1]` даёт `valid = false`.
- Golden ToyFunnel — `tests/Singularity.Lab.Tests/ToyFunnel/ToyFunnelParityTests.cs`.

## Разногласия
| # | тема | позиции | итог |
|---|---|---|---|
| 1 | Parquet сейчас | advocate: не нужен; ревизия 1: да | Нет; порог #374 (п. 8) |
| 2 | `thread_mismatch = 0` как L2-проверка | DeepSeek: при `threads = [1]` пусто | Валидность требует ≥ 2 значений `threads` (п. 6) |
| 3 | Порог 10 ГБ при сбое на 3,7 ГБ | DeepSeek: память взорвётся раньше порога | Порог — про хранение; память решает потоковое чтение (п. 5, обязательно для новых скриптов) |
| 4 | Множественные сравнения | DeepSeek, RR-0002 §7.4: нужна поправка | Первичная метрика или BH (п. 6) |
| 5 | Golden-матрица в CI | ревизия 2: вопрос владельцу; advocate: не нужна | Не нужна (п. 9), вопрос снят |
