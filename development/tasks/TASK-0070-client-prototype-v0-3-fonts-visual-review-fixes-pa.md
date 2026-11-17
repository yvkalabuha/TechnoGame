---
id: TASK-0070
title: "Client prototype v0.3: fonts, visual review fixes, pause pending, robustness"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: tooling
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0069]
approved_by: owner
approved_at: 2026-11-17
approval_ref: "DECISIONS_LOG #292, #294"
created: 2026-11-17
---


## Goal
Bring the client prototype (src/Singularity.Client.Spike, v0.2 from TASK-0069) to the design system. Fix the code-review findings. Core, Session and Kernel stay unchanged.

## Inputs
- Visual review: docs/ui/prototypes/client-v02-review.md (top-10 fixes; 1, 2, 4, 5 and 6 are blocking).
- Font page: docs/ui/prototypes/client-fonts.html.
- Design documents: docs/ui/UI_DESIGN_SYSTEM.md, UI_ART_DIRECTION.md, GLOBE_DIRECTION.md, TECH_TREE_SCREEN.md, prototypes/tech-tree.html.

## Scope
1. **Fonts (#294).**
   - Embed the TTF files of Golos Text, PT Mono, Jura and Literata, with their OFL.txt, in the client. Source: the official google/fonts GitHub repository, OFL directories.
   - Add LICENSES.md entries.
   - Apply the roles from the font page.
   - Minimum interface size 14 px.
2. **Visual review fixes 1–10, in priority order.**
   - Russian node names, with ids only in tooltips; debug text and API names behind F3.
   - Segment controls with no fill.
   - Colour rules.
   - Distinct country tints and Russian map labels.
   - Planisphere per the sketch:
     - channel edges;
     - the double circle;
     - dashed frontier nodes;
     - a continuous route with a callout;
     - the clock with its hand.
   - Labels without overlap.
   - The panel as an inset sized by content.
   - Tree B: a legend.
   - Controls separated from signals; dates in sync.
3. **Code review fixes:**
   - A command on pause is shown as «ожидает» and applies at the first tick after unpause, with no extra tick. Keep the slider values while the command is pending.
   - Catch errors per job and show a sim-thread failure permanently in the signal field.
   - `_ExitTree` disposes SimHost.
   - Rebuild panel and tree controls only when their data changed.
   - Map actors by `.Actor`.
   - A `LastRejected` flag instead of matching on text.
   - Use `ReferenceEquals` for views.
   - Fix the TechTree indentation and record the stubs.
   - README: .NET 10 runtime next to Godot.
4. **Screenshots** into docs/ui/prototypes/client-v03/, including an active-signal state, for the second visual review.

## Acceptance
- `./run-globe-spike.sh` and `--shots` work.
- The main solution's full `dotnet test` is green.
- «Результат» maps each review item to done or not done.

## Результат

Клиент v0.3 в `src/Singularity.Client.Spike/`; Core, Session, Kernel не менялись. Скриншоты RU и EN: `docs/ui/prototypes/client-v03/` (`run-log.txt` рядом). Объём расширен решением #83 (EN + RU).

**Шрифты (#294).** Done. Golos Text, PT Mono, Jura, Literata + OFL.txt в `assets/fonts/` (скачаны из google/fonts, `ofl/<family>/`, отказов не было), записи в `LICENSES.md`, роли как на странице шрифтов, минимум 14 px (`Ui.MinSize`, зажим в `Ui.Lbl`/`DrawString`). Числа и даты: PT Mono, столбцы долей выровнены вправо.

**Локализация (#83).** Done. `locale/ui.csv` и `locale/data.csv` (`keys,en,ru`) читаются в `Translation` Godot (`Loc.cs`); тексты Label/Button/подсказок — ключи, динамические строки через `Loc.T`. Переключатель RU/EN в сигнальном поле, живое обновление, RU по умолчанию. Названия узлов (61), стран (~90 подписей карты), держав — таблица `data.csv`, читает только `Names.cs` (ключи `node.<id без префикса сценария>`, `country.<имя Natural Earth>`, `actor.<id>`). Сегменты и кнопки подгоняются по ширине под самую длинную подпись текущего языка (проверено по снимкам EN). Не проверено на других языках; часть enum-причин из Session (`лимит Money`, `Alloy`) показана сырым именем.

**Ревью, топ-10.**
1. Русские названия узлов, id только в подсказке: done (панель, дерево, ведомость, приоритеты; hover показывает `id · №`).
2. API-имена и отладка скрыты: done. FPS/draw calls/регион/текст Session/«тик · мс» только по F3 или `--debug`; подсказка клавиш под «?» (F1).
3. Сегменты без заливки: done (рамка `ink` 2px + нижняя засечка, остальные `rule`; одна ширина ячейки). Угловые засечки не делал, только нижняя.
4. Цвета: done. Ультрамарин только маршрут и приказ; красный только ■ корня и плашка ОТКАЗ/СБОЙ; макет нейтральной плашкой `ink-3`. Оранжевый только у плашек сигнального поля.
5. Тинты и подписи: done. Палитра только тёплая, контур `ink` у стран среза (шейдер), подписи на русском (Literata), метка среза рамкой без заливки, мок-метки глобуса убраны. Контраст USA с океаном вырос, но выбранная страна даёт сероватый тон (осталось на усмотрение ревью).
6. Планисфера: done, кроме двух пунктов. Рёбра по каналам, двойная окружность между PRC и APP, пунктир у фронтира, сплошной маршрут корень→цель (если в данных нет цепочки, прямое звено), выноска с полкой, часы со стрелкой по дате (1910–2100). Not done: дуги G1–G4 и дуга интервала (данных нет); номера колец на оси заменены легендой внизу.
7. Подписи без наложений: done (подписывается маршрут, фронтир по №, выбранный; первое свободное место из четырёх). Подписи слоёв в легенде, не на радиальной оси (not done как на эскизе). №n есть в строках приоритетов и в ведомости.
8. Панель державы вкладкой: done. Один столбец, высота по содержимому, кнопка «Отдать приказ» нейтральная, при готовности плашка `command`. Сдвиг камеры не делал: `ChoosePower` центрирует страну.
9. Типографика: done (см. выше).
10. Ведомость B и полоса сигналов: done. Повторы «фронтир» убраны (заголовки групп), шкала ступеней, легенда G0–G4 и знаков, штамп листа внизу справа (и на A). Управление и сигналы разделены вертикалью; часы дерева берут дату из того же снимка (на снимках совпадают). Снимки активных сигналов: `signal_active_new.png`, `signal_active_acked.png` (ОТКАЗ новый с двойной рамкой и счётчиком, квитированный). Состояние «снят» без снимка. Правая вклейка B: секция «Узел» скрыта без выбора.

**Код-ревью.**
- Команда на паузе «ожидает», применяется на первом тике после снятия паузы, без лишнего тика; значения слайдеров держатся: done. Проверено в `run-log.txt` (`paused: tick 88 (was 87)` — тик шёл от штатной обработки в момент паузы, `pending 1`, `Pending`; после снятия `Applied`, доли в силе 0.45) и `globe_panel_pending.png`.
- Ошибка отдельной команды ловится (`CmdStatus.Error`), сбой потока симуляции постоянная плашка СБОЙ с текстом в подсказке: done (сбой потока вживую не вызывал).
- `_ExitTree` вызывает `SimHost.Dispose`: done.
- Пересборка панели и дерева только при изменении данных (по ссылке на view, сигнатуры секций, ключ языка): done.
- Актёры по `.Actor`: done (уже так в панели/сигналах; сопоставление по индексу `Inputs.Actors[i].Actor`).
- Флаг `HostSnapshot.LastRejected` и `CmdResult` вместо сопоставления по тексту: done.
- `ReferenceEquals` для view: done (SimHost, панель, дерево).
- Отступы TechTree и заглушки: done. `HitRow` теперь работает (строки ведомости выбираются). Остаются заглушки без API Session: создание изделий и запуск серии, приоритеты НИОКР, доли прогресса узла, интервалы сроков, дуги G1–G4, сигналы ЦЕЛЬ/РАЗРЫВ/ВОЙНА (все помечены «макет»).
- README: .NET 10 runtime рядом с Godot: done. Также лаунчер всегда импортирует ассеты; поправлен NaN-вывод `DrawArc` в оверлее до первого кадра.

**Проверка.** `dotnet build` клиента без ошибок. `./run-globe-spike.sh` и `--shots` отрабатывают (13 снимков, в логе нет ошибок). Основной `dotnet test`: все зелёные (Architecture 98, Kernel 322, Core 1060, Lab 20, Session 353). Тестов клиента нет (клиент вне sln, ADR-0014); экран проверен снимками, взаимодействие пользователя мышью вживую не прогонялось.

**OPEN.** (1) Названия узлов и стран живут в `locale/data.csv` до решения systems-architect о mods-формате. (2) Нужна ли вторая колонка «в силе» рядом с долей (решение game-designer). (3) `.gitignore` клиента игнорирует `*.import`: шрифты и CSV переимпортируются при запуске, `*.translation` добавлен в игнор.
