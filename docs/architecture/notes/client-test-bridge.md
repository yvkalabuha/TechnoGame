---
tags: [note, testing, ui, draft]
---
# Агентное тестирование клиента: debug bridge, MCP, GdUnit4Net, агент ui-tester

> Автор: systems-architect, 2026-11-17. Основа: DECISIONS_LOG #293, ADR-0014 (ACCEPTED, §7–§9), `docs/architecture/TESTING_ARCHITECTURE_PROPOSAL.md` §9–§11, `docs/architecture/spikes/godot-compat.md` §7, `src/Singularity.Client.Spike/` (v0.2, TASK-0069; код не менялся).
> Статус: записка с предложениями. Решений не принимает. Источники — в §9, доступ ко всем 2026-11-17.

## Краткий вывод
1. [Предл.] **Свой мост, а не чужой MCP.** Шесть живых проектов Godot MCP (MIT, один AGPL) рассчитаны на GDScript и на Node/TypeScript. Почти все дают выполнение произвольного GDScript (`run_script`, `godot_exec`, `runtime_call_method`). Ни один не знает `SimHost`/Session и не соблюдает транспорт ADR-0014 §7 (токен, IPv4 loopback, длина-префикс). Erodenn на время запуска пишет autoload в `project.godot`. Форк обошёлся бы дороже своего моста на ~600–900 строк C#. Из чужих проектов берём только идеи (MIT): выбор узла по пути и метке, ожидание кадра перед снимком, остановку процесса вместе с потомками.
2. [Предл.] **MCP на C#.** Это `tools/GameTest.Mcp` поверх библиотеки `tools/GameTest.Client` на `ModelContextProtocol` 2.4.0 (Apache-2.0), STDIO. Так требует ADR-0014 §8: `GameTest.Mcp` вызывает `GameTest.Client`. MCP на Python (§4.3) возможен, но он дублирует клиент протокола на втором языке и обходит §8. Выбор между ними — вопрос владельцу (§8, В1).
3. [Факт] Транспорт уже решён ADR-0014 §7: TCP только на `127.0.0.1`, IPv4, порт 0, токен 256 бит через переменную окружения, `FixedTimeEquals`, один клиент, JSON с префиксом длины ≤ 1 MiB, рукопожатие с `protocolVersion` за 5 с. Новый ADR не нужен, пока мост этому соответствует. Схема исключения из release проверена спайком: `Compile Remove` + `#if SINGULARITY_TEST` + регистрация во время выполнения [godot-compat §7].
4. [Факт] GdUnit4Net поддерживает Godot 4.7–4.7.2 и .NET 8/10 только в **prerelease** `gdUnit4.api 5.1.0-rc7` (07.11.2026). Stable 5.0.0 рассчитан на Godot 4.2–4.4 [S7, S8]. Scene runner умеет мышь, клавиши и actions [S7, S9]. [Факт] Клик мышью по `Button` в чистом `--headless` не срабатывает (GDScript GdUnit4 6.2.3, Godot 4.7.2); под `xvfb-run` срабатывает [S10]. [Предл.] Начать с спайка test-engineer на 1 день, потом решать.
5. [Предл.] Снимки и клики идут через виртуальный дисплей (Xvfb). На рабочем столе владельца окно не открывается, ввод вводится внутрь движка, а не через ОС.
6. [Факт] Почти весь интерфейс v0.2 рисуется кодом: узлы созданы без `Name`, а `TechTree`, `Overlay` и глобус рисуют себя в `_Draw` и сами разбирают ввод. Поэтому команды «клик по имени» мало. [Предл.] Нужны метки `test_id` (meta) и «виртуальные элементы» для отрисованных виджетов (§3.3). Это правка кода клиента после TASK-0069.

## 1. Обзор готовых Godot MCP
| Проект | Лицензия | Активность | Godot / C# | Что даёт во время игры | Безопасность | Оценка |
|---|---|---|---|---|---|---|
| HaD0Yun/godot-mcp (GoPeak) [S1] | MIT | v2.4.1, 13.11.2026 (исправлена shell-инъекция CWE-78) | 4.x; про C# ничего | addon, сокет :7777: снимок, ввод, дерево, вызов методов, метрики | мост редактора на 127.0.0.1; адрес runtime-сокета не указан | 95+ инструментов, тяжёлый; Bun/TS |
| Erodenn/godot-mcp-runtime [S2] | MIT | v4.0.1, 13.11.2026 | 4.5+; про C# ничего | `McpBridge` autoload **вписывается в `project.godot`** при `run_project`; TCP на localhost, порт выбирается сам; снимки, ввод, поиск UI, профиль, `get_debug_output` | `run_script` — произвольный GDScript; «сервер ничего не изолирует» | лучшие идеи по жизненному циклу; правит проект |
| satelliteoflove/godot-mcp [S3] | MIT | 16.11.2026 | 4.5+; про C# ничего | через addon редактора и протокол отладчика Godot: снимок игры, ввод, дерево, профайлер, пошаговое время | `godot_exec` — произвольный GDScript; WebSocket :6550 на 127.0.0.1, без аутентификации | нужен открытый редактор |
| yanhuifair/Godot-MCP [S4] | **AGPL-3.0** | v1.12.16 | 4.x; 5 инструментов для C#/csproj | autoload :9877: дерево, ввод, заморозка и шаг кадров, снимки | токен для HTTP; в экспорте выключен без `GODOT_MCP_RUNTIME=1`; `editor_run_gdscript` | лицензия несовместима с нашей политикой кода |
| fernforestgames/mcp-server-godot-editor, slangwald/godot-mcp, beckettlab [S5] | разные | не проверялось подробно | — | снимок, ввод, дерево | — | beckettlab: автономный плейтест только в платной версии |

Общее для всех: [Факт] сервер на Node/Bun, мост на GDScript, есть eval или вызов любого метода [S1–S4]. В mono-сборке GDScript работает, поэтому чужой мост технически поднимется. Но:
- шаг симуляции и состояние `SimHost` всё равно пришлось бы писать нам;
- eval противоречит TEST_INFRA_BRIEF §20 («no remote code execution»);
- транспорт не совпадает с ADR-0014 §7;
- добавится зависимость на Node.

**Рекомендация: строить своё.** [Предл.] Одноразовое ручное исследование с Erodenn (ничего не коммитим, `.mcp/` в gitignore) допустимо как разведка, но не как инфраструктура.

## 2. Godot API для моста (Godot 4.7, C#)
- **Снимок.** `await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw)`, затем `GetViewport().GetTexture().GetImage().SavePng(path)`. [Предп.] В `--headless` рендер пустой (dummy), снимок невозможен, поэтому нужен Xvfb или реальный дисплей. Проверить в спайке Т1.
- **Метрики.** [Факт] `Performance.GetMonitor`: `TIME_FPS`, `TIME_PROCESS`, `TIME_PHYSICS_PROCESS`, `MEMORY_STATIC` (только debug), `OBJECT_COUNT`, `OBJECT_NODE_COUNT`, `RENDER_TOTAL_OBJECTS_IN_FRAME`, `RENDER_TOTAL_DRAW_CALLS_IN_FRAME`, `RENDER_VIDEO_MEM_USED` [S6]. Добавить `GC.GetTotalMemory(false)` и `GC.GetGCMemoryInfo()`: управляемая куча .NET в `MEMORY_STATIC` не входит [Предп.].
- **Ввод.** Клавиши и actions — `Input.ParseInputEvent`. Мышь — `InputEventMouseMotion`, затем `InputEventMouseButton` (нажатие и отпускание) через `GetViewport().PushInput(e)` в координатах viewport. Клик идёт настоящим путём GUI (hit-test, `_GuiInput`), а не через `EmitSignal("pressed")`: так тест проверяет то, что видит игрок.
- **Поиск узлов.** `GetNode(NodePath)`; `%Unique` в пределах owner; `FindChild(pattern, recursive: true, owned: false)`. [Предп.: по документации Node] По умолчанию `owned = true`, и такой поиск не находит узлы, созданные кодом без owner, а в v0.2 так созданы все. Отсюда селектор по meta `test_id`: он не зависит от русского текста и от иерархии.
- **Проверка перекрытия.** После `MouseMotion` сравнить `GetViewport().GuiGetHoveredControl()` с целью или её потомком. Не совпало — ошибка `occluded`, клик не выполняется.
- **Глобус (3D).** Виртуальный элемент страны — проекция центроида через `Camera3D.UnprojectPosition`, если точка на видимой стороне (`IsPositionBehind == false`).
- **Время кадра.** `--fixed-fps 60` отключает синхронизацию с реальным временем [S11]. Анимации на `delta` дают одинаковые кадры, и снимки становятся сравнимыми.
- **Аргументы.** Всё после `--` читается через `OS.GetCmdlineUserArgs()` [S11]; v0.2 так уже делает (`Main.cs`).
- **Отключение в сборке.** [Факт] Godot.NET.Sdk 4.7.2 знает конфигурации `Debug`, `ExportDebug`, `ExportRelease`; в `ExportRelease` задаёт `EXPORTRELEASE` и `Optimize` [godot-compat §7]. [Факт] Символ `DEBUG` в экспорте зависит от галочки debug в диалоге экспорта, а не только от конфигурации [S12]. Поэтому `#if DEBUG` барьером не считаем. Барьер — условие `$(Configuration) != ExportRelease`, как в спайке. `OS.IsDebugBuild()` — третья проверка во время выполнения.

## 3. Протокол моста v1
### 3.1 Транспорт и envelope (ADR-0014 §7, TEST_INFRA_BRIEF §5.2)
- Кадр — `uint32` little-endian (длина), затем UTF-8 JSON; ≤ 1 MiB. Больше — ошибка `frame_too_large` и разрыв.
- Запрос: `{protocolVersion:1, requestId, command, parameters}`. Ответ: `{protocolVersion, requestId, success, result | error:{code, message, details}}`.
- Первым кадром идёт `hello {token}`. Без него за 5 с или с неверным токеном — разрыв. Второе подключение отклоняется.
- Сокет принимает в своём потоке. Запросы уходят в `ConcurrentQueue`, главный поток Godot разбирает их в `_Process`: API сцены не потокобезопасен. Ответ отправляется, когда команда завершилась (снимок — после `FramePostDraw`, шаг — после тика).
- Готовность: строка stdout `GAMETEST_READY {"port":N,"protocolVersion":1,"build":"debug"}` (TESTING_ARCHITECTURE_PROPOSAL §9).

### 3.2 Команды
| command | parameters | result |
|---|---|---|
| `hello` | `token` | `godot`, `build`, `pid`, `scene`, `viewport:[w,h]`, `commands[]` |
| `tree.get` | `root?` (селектор), `depth?` (4), `visibleOnly?` (true), `props?[]` | `nodes[]`: `path, name, type, testId, visible, rect:[x,y,w,h], text?, disabled?, items?, children[]` |
| `node.get` | `selector`, `props[]` (белый список: `text, visible, disabled, button_pressed, value, rect, modulate, tooltip_text`) | `{prop: value}` |
| `ui.items` | `selector` (виджет с `ITestItems`) | `items[]`: `id, label, rect, enabled` |
| `ui.click` | `selector` или `selector + item`, `button?` (left), `double?` | `path, pos:[x,y]`; ошибки `not_found`, `not_visible`, `occluded`, `ambiguous` |
| `ui.hover` | как `ui.click` | `path, pos` |
| `input.key` | `key` (имя `Key`) или `action`, `pressed?` (нажать и отпустить), `mods?[]` | `{}` |
| `input.mouse` | `x, y, kind` (`move`/`down`/`up`/`wheel`), `button?`, `delta?` | `{}` |
| `input.text` | `text` | `{}` (по символу на `InputEventKey.Unicode`) |
| `wait.frames` | `n` (≤ 600) | `frame` |
| `wait.until` | `selector`, `prop`, `equals`, `timeoutFrames` (≤ 600) | `frames` или ошибка `timeout` |
| `screen.capture` | `name` (`[a-z0-9_-]{1,64}`), `region?` | `path, w, h, sha256` |
| `perf.get` | — | мониторы §2, `gcHeapBytes`, `frame` |
| `sim.state` | — | `tick, date, paused, speed, stateHash?, lastVerdict` (из `HostSnapshot`) |
| `sim.pause` / `sim.resume` | — | `sim.state` |
| `sim.step` | `ticks` (1..3650) | `tick, date, stateHash` после последнего post (ADR-0014 §4) |
| `log.tail` | `sinceSeq?`, `max?` (200) | `lines[]: {seq, level, text}` — кольцевой буфер логов моста и `GD.Print`/ошибок [Предп.: перехват через `OS.AddLogger`, Logger есть с 4.5 [S3]] |
| `app.quit` | `code?` | ответ, потом `GetTree().Quit(code)` |

Селектор — строка одного из видов: `#test_id`, `/root/Main/...` (NodePath), `%Unique`, `name:Foo` (ошибка `ambiguous`, если совпадений больше одного).

Чего в протоколе **нет** намеренно: eval, вызова произвольного метода, записи произвольного свойства, доступа к файлам, команд Session помимо step/pause. Действия игрока (Submit) идут только через UI, то есть через `ui.click`/`input.*`: так проверяется настоящий путь «интерфейс → команда» (ADR-0006, ADR-0014 §2).

### 3.3 Что нужно от кода клиента (после TASK-0069)
- `SetMeta("test_id", "...")` на значимых Control: кнопки темпа, вкладки, панель актора, кнопки команд, закрытие дерева.
- Интерфейс `ITestItems { IReadOnlyList<TestItem> GetTestItems(); }` (id, label, rect в координатах viewport, enabled) у `TechTree` (узлы), `SignalField` (места), `Overlay`/глобуса (страны по ISO_A3).
- `SimHost`: `StepTicks` синхронный для моста (возвращает `Task<StepResult>` с тиком, датой и хешем), `Pause/Resume`. Сейчас `StepTicks` только ставит задачу в очередь, и узнать о её завершении нельзя.
- Флаг `-- --bridge`: мост включается только с ним; `-- --sim-paused`: старт без авто-тиков, чтобы прогон был воспроизводим.

## 4. Запуск, остановка, MCP
### 4.1 Жизненный цикл (в `GameTest.Client`)
1. `client.build`: `godot --headless --path src/Singularity.Client.Spike --build-solutions --quit` (или `dotnet build` с SDK 10, как `run-globe-spike.sh`). Код выхода не доверяем: проверяем, что DLL появилась (ADR-0014 §10).
2. `client.start {display: xvfb|desktop, fixedFps?: 60, simPaused?: true}`:
   - запуск `xvfb-run -a -s "-screen 0 1600x900x24" <godot> --path … --fixed-fps 60 -- --bridge --sim-paused`;
   - токен — в env `SINGULARITY_BRIDGE_TOKEN`; порт берётся из `GAMETEST_READY`, ждём не больше 30 с;
   - `desktop` — окно на `:0`, только по явному запросу.
3. `client.stop`: `app.quit` → ожидание 5 с → `SIGTERM` группе процессов → 3 с → `SIGKILL`. Процесс стартует в своей группе (`setsid`): иначе Xvfb от `xvfb-run` остаётся висеть. При выходе MCP-сервера клиент убивается всегда.
4. Одновременно работает один клиент. Снимки и логи пишутся в `artifacts/ui-runs/<run_id>/` (gitignore).

### 4.2 MCP (рекомендуемый вариант, C#)
`tools/GameTest.Mcp` (net10.0, `ModelContextProtocol` 2.4.0, STDIO). Инструменты 1:1 с §3.2 плюс `client_build`, `client_start`, `client_stop`, `client_status`. Снимок возвращается путём к PNG, агент открывает его через `Read`. Подключение через `.mcp.json` проекта меняет настройки Claude Code, это делает Orchestrator с одобрения владельца.

### 4.3 Альтернатива: Python
`mcp` SDK (MIT, Python ≥ 3.11 [S13]) по STDIO, `asyncio.open_connection("127.0.0.1", port)`, кадр `struct.pack("<I", len)`, запуск через `subprocess.Popen(..., start_new_session=True)`. Плюсы: ~300 строк, без сборки. Минусы: второй клиент протокола (C# понадобится всё равно для `gametest` и xUnit), расхождение с ADR-0014 §8, Python-зависимость вне `tools/llm_worker`.

## 5. Правила безопасности
1. Код моста лежит в `src/Singularity.Client.Spike/TestBridge/**`. В `ExportRelease`: `Compile Remove` + `#if SINGULARITY_TEST`, `DefineConstants` только при `$(Configuration) != ExportRelease`. Узел регистрируется во время выполнения (`Root.CallDeferred(AddChild)`), в `project.godot` его нет.
2. Включается тремя условиями сразу: сборка с `SINGULARITY_TEST`, `OS.IsDebugBuild()`, флаг `--bridge`. Без токена в env мост не стартует (пишет причину в лог).
3. Только `IPAddress.Loopback` (IPv4), порт 0, один клиент, токен 256 бит из `RandomNumberGenerator`, сравнение `FixedTimeEquals`, рукопожатие 5 с, кадр ≤ 1 MiB, лимиты параметров (§3.2).
4. Никакого eval, рефлексии или вызова методов по имени; записи только через ввод. Белый список свойств для чтения.
5. Файлы пишутся только в каталог артефактов, заданный при старте; имя снимка проверяется regex, `..` и абсолютные пути отклоняются.
6. Пока мост активен, в окне есть плашка «TEST BRIDGE» (TESTING_ARCHITECTURE_PROPOSAL §10); на снимках она маскируется.
7. Проверка в CI (Linux): release-экспорт → в DLL нет типов `*.TestBridge.*` (инспекция метаданных, как в спайке); release-экспорт с `--bridge` не открывает порт. Тест на xUnit/скрипт в `tests/`.

## 6. Задачи
| # | Исполнитель | Задача | Критерий приёмки |
|---|---|---|---|
| Т1 | test-engineer | Спайк GdUnit4Net 5.1.0-rc7 + Godot 4.7.2 + net10.0: отдельный тестовый csproj или тесты внутри проекта клиента; `dotnet test` c `GODOT_BIN`; клик по `Button` в `--headless` и под `xvfb-run`; снимок под Xvfb (Vulkan/lavapipe или `gl_compatibility`) | отчёт с фактами; 1 зелёный тест сцены; решение: GdUnit4Net или только мост |
| Т2 | developer | `TestBridge` в клиенте: транспорт §3.1, команды §3.2 без `sim.*`, барьеры §5 | xUnit-тест протокола (кадры, токен, лимиты); ручной прогон `tree.get`/`ui.click`/`screen.capture` под Xvfb |
| Т3 | developer | Хуки клиента §3.3: `test_id`, `ITestItems`, синхронный `SimHost.Step`, `--sim-paused`; `sim.*` в мосте | `sim.step 30` дважды с одного старта даёт одинаковый `stateHash` (L1); клик по узлу дерева технологий открывает его карточку |
| Т4 | developer | `tools/GameTest.Client` + `tools/GameTest.Mcp` (§4) | интеграционный тест: start → hello → click → capture → stop без висящих процессов (`pgrep` пусто) |
| Т5 | test-engineer | CI-проверка §5 п. 7 и задание Linux с Xvfb: тесты сцен (GdUnit4Net по итогам Т1) | release без типов моста; CI зелёный |
| Т6 | orchestrator | агент `ui-tester` (§7), `.mcp.json` с одобрения владельца | файл агента в конце сессии |

Порядок: Т1 ‖ Т2 → Т3 → Т4 → Т5, Т6. Т2 и Т3 — после закрытия TASK-0069 (её код не трогаем).

## 7. Агент `ui-tester` (предложение)
- **Роль:** проводит сценарии в живом клиенте через MCP, сверяет экран и состояние с документами UI (`UI_DESIGN_SYSTEM`, `TECH_TREE_SCREEN`, `GLOBE_DIRECTION`), находит дефекты и регрессии производительности. Сам код не чинит. Визуальное суждение остаётся за ui-art-director.
- **Инструменты:** `Read, Grep, Glob, Write` + MCP `GameTest.Mcp` (все `client_*`, `tree/node/ui/input/wait/screen/perf/sim/log`). Без `Bash` и `Edit` в коде.
- **Зона записи:** `artifacts/ui-runs/**` (снимки, логи) и `docs/testing/ui-runs/**` (отчёты прогонов, на русском). [Предл.] Сценарии прогонов в `tests/ui-scenarios/**` пишет test-engineer, ui-tester их только читает.
- **Выход:** ≤ 40 строк; дефект = шаги, ожидание, факт, путь к снимку, `stateHash`/тик.
- Отличие от предложенного ранее `qa-automation` (TESTING_ARCHITECTURE_PROPOSAL §11): только имя; #293 называет агента `ui-tester`.

## 8. Открытые вопросы
- **В1 (владелец).** MCP на C# (`GameTest.Mcp`, соответствует ADR-0014 §8) или на Python (быстрее, но второй клиент протокола)? Рекомендация — C#.
- **В2 (владелец).** Принять prerelease `gdUnit4.api 5.1.0-rc7` (stable под 4.7 нет), или UI-тесты в CI идут только через мост, пока нет stable?
- **В3.** Локальные прогоны: Xvfb (окно не мешает, рендер программный и, возможно, отличается от GPU-картинки) или окно на `:0` с RTX 3050 Ti? Предложение: Xvfb по умолчанию, `desktop` по запросу. Установлен ли `xvfb-run` на машине — проверить (Т1).
- **В4.** Отображается ли шейдер глобуса (`globe.gdshader`, Forward+) под программным Vulkan (lavapipe) или под `gl_compatibility` в CI? Т1.
- **В5.** Где жить тестам GdUnit4Net: `tests/**` (зона test-engineer) возможно только при отдельном тестовом csproj; если GdUnit4Net требует тестов внутри проекта Godot, нужна зона в `src/Singularity.Client.Spike/Tests/**`. Т1.
- **В6.** ADR клиента (ADR-0014 §9) ещё не написан. Мост ему не мешает, но барьер §5 п. 1 должен войти в этот ADR.

## 9. Источники (доступ 2026-11-17)
- S1 https://github.com/HaD0Yun/godot-mcp (+ /commits/main)
- S2 https://github.com/Erodenn/godot-mcp-runtime (+ /commits/main)
- S3 https://github.com/satelliteoflove/godot-mcp (+ /commits/main)
- S4 https://github.com/yanhuifair/Godot-MCP
- S5 https://github.com/fernforestgames/mcp-server-godot-editor, https://github.com/slangwald/godot-mcp, https://github.com/beckettlab/beckett-godot-mcp (только описание из поиска)
- S6 https://docs.godotengine.org/en/stable/classes/class_performance.html (4.7)
- S7 https://github.com/godot-gdunit-labs/gdUnit4Net (README: Godot 4.7–4.7.2, .NET 8/10, MIT)
- S8 https://www.nuget.org/packages/gdUnit4.api (5.0.0 stable 21.06.2025; 5.1.0-rc7 07.11.2026)
- S9 https://github.com/godot-gdunit-labs/gdUnit4Net/releases
- S10 https://ziva.sh/blogs/godot-gut-gdunit4-simulate-input (07.11.2026; Godot 4.7.2, GDScript)
- S11 https://docs.godotengine.org/en/stable/tutorials/editor/command_line_tutorial.html
- S12 https://forum.godotengine.org/t/why-if-debug-is-still-true-in-exported-program/127904
- S13 https://github.com/modelcontextprotocol/python-sdk (MIT); https://www.nuget.org/packages/ModelContextProtocol (2.4.0, 07.11.2026, Apache-2.0)
- Альтернатива GdUnit4Net: https://github.com/chickensoft-games/GoDotTest (MIT, C#, тесты внутри игры; ввод — через GodotTestDriver)
