# Спайк совместимости Godot .NET (TASK-0016)

> Копия отчёта спайка TASK-0016 из ветки `spike/godot-compat` (коммит b95efb2); код спайка в `main` не вливается (#57).

Ветка `spike/godot-compat`, в `main` не вливается (DECISIONS_LOG #57). Дата прогона: 2026-11-03.
Машина: Intel i7-11800H (16 потоков), 31 ГБ ОЗУ, Linux Mint 21.3, .NET SDK 10.0.403 (runtime 10.0.14).

## Краткий вывод
- [Факт] Godot **4.7.2-stable mono** с проектом `net10.0` (выставлен вручную) собирает и запускает `Singularity.Kernel` и `Singularity.Core` через `ProjectReference`: в редакторе (Debug) и в release-экспорте. Хэш состояния после 1000 тиков совпадает побитно с чистым .NET-хостом (L1 между хостами).
- [Факт] Симуляция в отдельном потоке + публикация неизменяемого снимка одной ссылкой (`Volatile.Write`) работает: главный поток читает снимки без блокировок, тики монотонны, Table трогает только поток симуляции.
- [Факт] Тестовый код полностью исключается из release-экспорта (`Compile Remove` + `#if SINGULARITY_TEST` + условный `ProjectReference`); `--test-mode` в release ничего не делает.
- [Факт] Проблема P2: Godot собирает ссылочные проекты в конфигурации `ExportRelease`, которую обычный SDK-проект не знает, поэтому ядро собирается **без оптимизации** (тик в 2,5–3 раза медленнее). Лечится `AdditionalProperties="Configuration=Release"`.
- [Факт] Проблема P3: экспорт без `.sln` завершается кодом 0, но без сборок .NET. CI должен проверять результат, а не код возврата.
- Воспроизведение одной командой: `tools/spikes/godot-compat/fetch-godot.sh && tools/spikes/godot-compat/run.sh` (11 проверок PASS/FAIL).

## 1. Версия и источник
- [Факт] Последний stable — 4.7.2 (опубликован 18.08.2026); следующий по API GitHub — только 3.6.3; 4.8 существует лишь как beta (источник: https://api.github.com/repos/godotengine/godot/releases, доступ 2026-11-03). Stable-версии с официальной поддержкой .NET 10 нет, поэтому по #57 взят запасной вариант: 4.7.2 + `net10.0`.
- Загрузка: https://github.com/godotengine/godot/releases/tag/4.7.2-stable (доступ 2026-11-03), файлы `Godot_v4.7.2-stable_mono_linux_x86_64.zip` (108 МБ) и `Godot_v4.7.2-stable_mono_export_templates.tpz` (1203 МБ).
- Контрольные суммы сверены с `SHA512-SUMS.txt` того же релиза (`sha512sum -c` — OK):
  - editor zip: `1855960b27ee3ef5e66e5e228cced69d55637b24334a7411162687dcd077d8f9f645348cdb8eae984bec8135d49ed855a1e3a16476786b8bce60774fd8402d13`
  - templates: `bb5c41d72370ed743660361f6228006f808ab04ca33abdc545d740b044f3fe057f32ae8cb7873a1bc86ddcd82ae683b9f6dfdfe4179852f2c0f1acde2ff6bd5a`
- `godot --version`: `4.7.2.stable.mono.official.ed1daf0bf`. Godot.NET.Sdk 4.7.2 берётся с nuget.org (там же есть только 4.8.0-dev.*).
- Установка вне репозитория: редактор в `~/.local/godot/`, шаблоны (только Linux x86_64) в `~/.local/share/godot/export_templates/4.7.2.stable.mono/`.
- Внутри Godot: `RuntimeInformation.FrameworkDescription` = `.NET 10.0.14`; в release-экспорте runtime 10.0.14 упакован в `data_GodotCompat_linuxbsd_x86_64/` (self-contained, 193 файла, 85 МБ; весь экспорт 156 МБ).

## 2. Требование к окружению (обязательное)
- [Факт] .NET установлен в `$HOME/.dotnet`, `DOTNET_ROOT` не задан, `/usr/share/dotnet` пуст. Редактор без переменных падает: `Failed to load hostfxr`, затем SIGSEGV (код 134). Запуск только так: `DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH godot --headless ...`. `run.sh` выставляет это сам.
- [Факт] Экспортированная игра от системного .NET не зависит: запуск с `PATH=/usr/bin:/bin` и без `DOTNET_ROOT` работает.
- Редактор запускать только с `--headless` (окно на рабочем столе владельца не открывать).

## 3. Что построено (`tools/spikes/godot-compat/`)
| Файл | Назначение |
|---|---|
| `shared/SpikeWorld.cs` | Мир спайка: Table на N строк, одна система (целочисленный рост + `CounterRng`), `SimLoop` (поток `sim`, снимок `WorldSnapshot`). Один и тот же файл компилируется в Godot и в чистый .NET |
| `game/` | Godot-проект: `project.godot`, `Main.tscn`, `Main.cs`, `GodotCompat.csproj` (`Godot.NET.Sdk/4.7.2`, `net10.0`), `GodotCompat.sln`, `export_presets.cfg` (Linux) |
| `game/TestHooks/TestAutoload.cs` | Тестовый узел, регистрируется во время выполнения под `/root` только при `-- --test-mode` |
| `testing/Spike.Testing.csproj` | Аналог `Singularity.Testing`, строка-маркер `SPIKE_TEST_API_PRESENT` |
| `hostcheck/` | Чистый .NET-хост (эталонный хэш) и инспектор типов сборки (`System.Reflection.Metadata`) |
| `fetch-godot.sh` | Загрузка + проверка SHA512 + установка Godot вне репозитория |
| `run.sh` | Все проверки одной командой; код возврата = число FAIL |
| `bench.sh` | Сравнение времени тика (проблема P2) |
| `ci/godot-compat.yml` | Эскиз CI-задания (в `.github/workflows` не скопирован) |

## 4. Команды (проверены на 4.7.2)
Синтаксис совпадает с документацией CLI 4.7 (https://docs.godotengine.org/en/stable/tutorials/editor/command_line_tutorial.html, доступ 2026-11-03; см. TESTING_ARCHITECTURE_PROPOSAL §2, S12).
```
godot --headless --path game --import                       # первый импорт, создаёт .godot/
godot --headless --path game --build-solutions --quit       # сборка C# (Debug) -> .godot/mono/temp/bin/Debug/
godot --headless --path game -- --rows 100000 --ticks 1000 --threads 4 [--test-mode]   # запуск проекта
godot --headless --path game --export-release Linux /abs/out/spike.x86_64              # release-экспорт
out/spike.x86_64 --headless -- --rows 100000 --ticks 1000 --threads 4 --test-mode      # запуск экспорта
```
- Пользовательские аргументы — после `--`, читаются `OS.GetCmdlineUserArgs()`.
- Относительный путь экспорта считается от каталога проекта (`--path`), а не от текущего каталога; в скриптах — только абсолютные пути.
- Время на этой машине: `--import` 2,6 с; `--build-solutions` 4,1 с; `--export-release` 4,7 с (повторные прогоны, кэш NuGet тёплый).

## 5. Замеры
Мир: 100 000 строк, 1000 тиков, 4 потока, `chunkSize` 4096, seed 20261103 (`run.sh`, `bench.sh`).

| Сборка | Среднее время тика, мс (3 прогона) |
|---|---|
| Чистый .NET-хост, Release | 0,668 / 0,579 / 0,561 |
| Godot release-экспорт, ядро собрано как Release | 0,558 / 0,657 / 0,619 |
| Godot release-экспорт, ядро собрано как ExportRelease (по умолчанию) | 1,456 / 1,520 / 1,427 |
| Godot из редактора (Debug) | 1,68 |

1 000 000 строк, 200 тиков: хост 4,80 / 4,91; Godot (ядро Release) 4,59 / 5,25; Godot (ядро ExportRelease) 13,99 / 15,15 мс.
- [Факт] С исправлением P2 Godot не добавляет накладных расходов к тику (разница в пределах шума).
- [Факт] Хэш после 1000 тиков одинаков во всех трёх сборках и в хосте: `049cb0413fa7f4f93fbcbcc29b7755981527162f6bdad0855194f7ac7771f46b` (200 тиков: `cb690d75...87439`).
- Главный поток во время симуляции: в headless без ограничения FPS — 90–316 кадров за прогон, `max_frame_ms` 9–17 мс, снимок менялся почти каждый кадр, обратного хода тика не было (`monotonic=True`). [Предп.] 16,67 мс — это шаг физики/синхронизации headless-цикла, а не блокировка симуляцией; на реальном окне не проверялось.

## 6. Модель потоков
- Поток `sim` (фоновый) — единственный владелец `Table` и `TickRunner`; `TickRunner` сам распараллеливает фазу Compute через `Parallel.For`, внутри Godot это работает без изменений.
- После тика поток строит неизменяемый `WorldSnapshot` и публикует его одной записью ссылки; `_Process` читает `Volatile.Read`. Блокировок нет; чтение колонок из главного потока запрещено.
- [Предл.] Для ADR-0014: представления для UI и тестов — неизменяемые снимки, собираемые потоком симуляции между тиками; команды игрока — очередь, применяемая потоком симуляции на границе тика. Цена: снимок O(строк) на тик; при 1 млн строк и полном снимке это заметно — снимок должен быть доменным и узким, а не копией колонок.

## 7. Исключение тестового кода из release
Барьер 1 (сборка), всё в `GodotCompat.csproj`, условие `$(Configuration) != ExportRelease`:
- `DefineConstants` += `SINGULARITY_TEST`; условный `ProjectReference` на `Spike.Testing`; в `ExportRelease` — `Compile Remove="TestHooks/**"`, а сам файл ещё и обёрнут в `#if SINGULARITY_TEST`.
- [Факт] Godot.NET.Sdk 4.7.2 знает конфигурации `Debug` (редактор), `ExportDebug`, `ExportRelease`; в `ExportRelease` задаёт `Optimize=true` и константу `EXPORTRELEASE` (проверено `dotnet msbuild -getProperty`). Предположение из TESTING_ARCHITECTURE_PROPOSAL §10 подтверждено.
- Регистрация тестового узла — во время выполнения (`Root.CallDeferred(AddChild)`), не через список autoload в `project.godot`: так в release нет даже ссылки на скрипт.

Проверки в `run.sh` (все PASS):
- в `data_GodotCompat_linuxbsd_x86_64/` нет `Spike.Testing.dll`;
- строки `SPIKE_TEST_API_PRESENT` нет ни в одном файле экспорта (включая `.pck`);
- в `GodotCompat.dll` нет типов `TestHooks.*` и ссылки на `Spike.Testing` (список типов: `out/release-types.txt`);
- release-экспорт с `-- --test-mode`: `test_hooks=compiled-out`, `test_node=False`; Debug с тем же флагом — `test_node=True`, без флага — `False`.
- [Факт] `ExportDebug` по этой схеме тестовый код содержит (условие только на `ExportRelease`). Отдельно не прогонялся.

## 8. Проблемы
| # | Проблема | Серьёзность | Решение в спайке |
|---|---|---|---|
| P1 | Нужен `.sln` рядом с `.csproj`: экспорт падает с `res://Main.cs is a C# file but no solution file exists` | средняя | `dotnet new sln --format sln` + `dotnet sln add` (в .sln попали и ссылочные проекты) |
| P2 | Godot передаёт `Configuration=ExportRelease` в `ProjectReference`; обычный SDK-проект не включает для неё оптимизацию (`Optimize` пуст) — ядро в release-игре в 2,5–3 раза медленнее | **высокая** | `AdditionalProperties="Configuration=Release"` для Kernel/Core вне Debug (переключатель `CoreAsRelease`). [Предл.] Альтернатива для основного проекта — в `src/Directory.Build.props` включать `Optimize` для `Export*`; решать в ADR клиента |
| P3 | Экспорт без `.sln` завершается **кодом 0** и пишет только `.pck` и бинарь, без сборок .NET | высокая для CI | `run.sh` проверяет наличие `data_*`, хэш и типы, а не код возврата |
| P4 | Без `DOTNET_ROOT` редактор падает (hostfxr, SIGSEGV) | средняя (окружение) | раздел 2 |
| P5 | `net10.0` в 4.7 — неофициально; предупреждений сборка не дала (0 warning в логах), но гарантии нет | средняя | при выходе 4.8 stable повторить `run.sh` на 4.8 |
| P6 | Шаблоны экспорта — один архив 1,2 ГБ на все платформы | низкая | ставим только Linux-шаблоны; в CI кэшировать |

## 9. CI
- [Факт] Реального прогона в GitHub Actions не было: ветка не пушится (указание Orchestrator). Вместо него `run.sh` прогнан локально с чистого состояния (`out/` и `.godot/` удаляются в начале) — 0 FAIL, 22,5 с без загрузки.
- Эскиз задания: `ci/godot-compat.yml` (ubuntu-latest, setup-dotnet 10.0.x, кэш загрузки, `fetch-godot.sh`, `run.sh`, логи как артефакт). Xvfb не нужен — всё в `--headless`.
- [Предп.] Цена Linux-задания: загрузка 1,3 ГБ при холодном кэше (кэш actions, лимит 10 ГБ на репозиторий) + ~1 мин работы.
- [Предп.] Windows/macOS: редактор 117 МБ (win64) и 201 МБ (macOS universal) по данным релиза; шаблоны — тот же архив 1,2 ГБ. Минуты Windows-раннеров дороже в 2 раза, macOS — в 10 раз (тарифы GitHub, не проверялось сегодня). Для macOS экспорт требует подписи/нотаризации только для распространения, для тестового запуска — нет [Предп.]. [Предл.] Godot-задания на Windows/macOS — только по ручному запуску или nightly; детерминизм ядра между ОС уже проверяет обычная матрица `dotnet` в `ci.yml`.

## 10. Что меняется для ADR-0014
1. Поток симуляции отдельный; Test API и UI получают только неизменяемые доменные снимки, опубликованные на границе тика; команды — через очередь, применяемую потоком симуляции. Прямой доступ к `Table` из главного потока запретить в контракте.
2. Флаги тестового режима — только после `--` (подтверждено), harness проверяет их сам (Godot молча игнорирует неизвестные флаги).
3. Исключение тестового кода: двойной барьер из §10 реализуем как есть; конфигурации Godot `ExportRelease`/`ExportDebug` подтверждены. Тест «в release нет тестовых типов» — через инспекцию метаданных сборки, как в `hostcheck types`.
4. Сборка ядра внутри Godot обязана идти как Release (P2) — это требование к структуре проекта клиента, его надо записать в ADR клиента или ADR-0014.
5. CI-проверки Godot-сборки опираются на артефакты, а не на код возврата Godot (P3).
6. Хэш состояния в Godot совпадает с хостом: эталонные хэши, снятые headless-раннером, годятся для проверки игры в Godot (L1 между хостами).
7. Версия: 4.7.2 + `net10.0` технически работает; рекомендация §12 (ждать 4.8 stable) остаётся, но блокером 4.7.2 не является.

## Не проверено
- Реальное окно (не headless), ввод, AccessKit; `ExportDebug`; Windows/macOS; GdUnit4Net; Godot 4.8 beta.
