# Simulation Lab CLI (`src/Singularity.Lab`, TASK-0005)

> Связано: ADR-0008 (PROPOSED), EXP-0001 «Ревью» п. 11 (JSON вместо TOML для пилота), `exp0001-csv-format.md`.

## Краткий вывод
- [Факт] (код в `src/Singularity.Lab`) Команды: `run`, `matrix`, `hash`, `models`, `selftest`. Один прогон = один процесс; `matrix` запускает `run` дочерними процессами, до `--jobs` одновременно.
- [Факт] `manifest_hash` = SHA-256 канонического JSON (ключи по ordinal, без пробелов, числа как нормализованные decimal: `1`, `1.0`, `1e0` → `1`). Форматирование и порядок ключей на хеш не влияют; дубликаты ключей, комментарии, висящие запятые — ошибка. Число, которое decimal представил бы с округлением (> 28 значащих цифр, |x| < 1e-28, например `1e-40`), — ошибка (TASK-0008).
- [Факт] Строки: перекодируются стандартным энкодером System.Text.Json (`JavaScriptEncoder.Default`: не-ASCII, `<>&'+` и управляющие символы → `\uXXXX`), поэтому `"é"` и `"é"` дают один хеш. Нормализации Unicode (NFC/NFD) нет: `é` (U+00E9) и `e`+U+0301 — разные строки и разные хеши.
- [Факт] `golden_hash` = `StateHash(Table, SchemaVersion, final_tick)` из Kernel. `matrix` сам сверяет golden_hash одного (точка, seed) при разных `threads` (бесплатная L2-проверка).
- [Факт] Пока зарегистрирована только **тестовая** модель `test.random-walk` (`model_is_test: true` в run.json). Научного смысла её результаты не имеют.
- [Предл.] Интерфейс модели (`ILabModelFactory`/`ILabModel`) сейчас в Lab; по правилу «Core не ссылается на Lab» его нужно перенести в Core (или Kernel), когда появится Core (см. «Открытые вопросы»).

## Запуск
```
dotnet src/Singularity.Lab/bin/Release/net10.0/Singularity.Lab.dll <command> ...
run    --manifest M --seed N --threads T --out DIR [--point P] [--force] [--quiet]
matrix --manifest M --out DIR [--jobs J] [--timeout SECONDS] [--force]
hash   --manifest M [--canonical]
```
Коды выхода: 0 — успех; 1 — прогон/матрица/selftest провалены или непредвиденная ошибка (I/O, баг; сообщение и стек в stderr); 2 — ошибка аргументов, манифеста или отсутствующий входной файл. `--timeout` — предел одного дочернего прогона (по умолчанию 3600 с); по истечении процесс убивается, статус `timeout`. С `--force` каталог прогона очищается перед запуском. Выходные каталоги — в `lab/results/` (в `.gitignore`).

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

Числа передаются модели как текст decimal: `GetFx6Raw` даёт точный raw ×10^6; больше 6 знаков после точки — ошибка, без округления.

## Выход
- `run`: `DIR/run.json`, `DIR/runs.csv` (1 строка), `DIR/<metric>.csv` (колонка `run` + колонки модели).
- `run.json`: `status`, `error`, `run`, `experiment`, `model`, `model_is_test`, `series`, `point`, `seed`, `threads`, `chunk_size`, `ticks`, `final_tick`, `golden_hash`, `core_commit`, `dirty`, `dirty_untracked_src`, `build_commit`, `manifest_hash`, `manifest_path`, `data_hash`, `data_files`, `params`, `unused_params`, `started_utc`, `elapsed_ms`, `runtime`, `os`.
- `core_commit` = `git rev-parse HEAD`; `dirty` = есть изменения в отслеживаемых файлах (неотслеживаемые не учитываются); `dirty_untracked_src` = есть неотслеживаемые файлы в `src/` (SDK компилирует все `*.cs`, так что они меняют бинарник); вне git — `"unknown"`/`null`. `build_commit` — коммит, из которого собран бинарник (SDK дописывает `+<SourceRevisionId>` к `AssemblyInformationalVersion`); если он ≠ `core_commit`, бинарник собран не из текущего HEAD. [Предп.] Незакоммиченные правки на момент сборки `build_commit` не отражает.
- `data_hash` = SHA-256 по файлам в ordinal-порядке путей: UTF-8 путь, байт 0, SHA-256 содержимого. Нет файлов → SHA-256 пустого входа (`e3b0…b855`).
- `matrix`: `DIR/runs/<run>/…` (+ `log.txt`), объединённые `DIR/runs.csv` и `DIR/<metric>.csv` в порядке прогонов (не зависит от порядка завершения), `DIR/matrix.json` с `golden` по прогонам, `problems`, `thread_mismatch` и `golden_set_hash` = SHA-256 строк `"<run> <golden_hash>\n"` в порядке прогонов (только если все прогоны ok и нет `thread_mismatch`). Нечитаемый `run.json` ребёнка — статус прогона `bad-run-json`, `matrix.json` всё равно пишется. `runs.csv`: `run, series, point, seed, threads, status, golden_hash` + объединение ключей параметров.
- [Предп.] Для EXP-0001 модель TASK-0004 объявит метрики `yearly`, `countries`, `milestones`; `runs.csv` с колонками `series/model/nu/beta/gamma/t_ref` получится, если эти величины заданы параметрами манифеста.

## Проверка (TASK-0005, 2026-11-02)
- `selftest`: 13/13 PASS (канонизация, Fx6, раскладка матрицы, golden при повторе, 1 vs 4 потока, чувствительность к seed, путь ошибки, data_hash).
- `hash` на `examples/test-random-walk.json` и на его переформатированной копии (обратный порядок ключей, отступ 7, `492.0`) — одинаковый `313ab3e2…f4ac8`.
- `run` p1/seed 5 дважды (threads 1, повтор с `--force`) и с threads 4 — один `golden_hash` `1f84b2f8…9498`.
- `matrix` 100 прогонов (`--jobs 16` и `--jobs 3`): ok=100, thread_mismatch=0, ~2,4 с; `golden_set_hash` одинаков, объединённые CSV побайтно равны.
- Матрица с `fail_at_tick`: exit 1, ошибки перечислены, `golden_set_hash: null`.

## Открытые вопросы
- [Предл.] Перенести `ILabModelFactory`/`ILabModel`/`IMetricSink`/`MetricFormat` в Core (например, `Singularity.Core.Simulation`), Lab оставить хостом. Сейчас в Lab, потому что Core нет.
- [Факт] (TASK-0008) `lab/manifests/EXP-0001.json` переведён в schema 1: модель `toy-funnel`, вариант — параметр `model` (A/B/C), серии `main/A`, `main/B/nu0.25`, `main/B/nu1.0` (калибровка B своя для каждого ν), `main/C`, `grid` (свои 50 seeds); `archetypes`/`levels` — в `params`, остальное — в `meta`. Анализ берёт группу серии до `/`.
- Тестовый проект `tests/Singularity.Lab.Tests` (вне зоны simulation-engineer) должен заменить `selftest`; архитектурный тест — правила детерминизма для `Singularity.Lab.Models.*`.
