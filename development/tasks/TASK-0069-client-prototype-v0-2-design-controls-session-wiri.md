---
id: TASK-0069
title: "Client prototype v0.2: design controls, Session wiring, tech tree A/B"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: tooling
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0067, ADR-0014]
approved_by: owner
approved_at: 2026-11-17
approval_ref: "DECISIONS_LOG #292"
created: 2026-11-17
tags: [task, ui, gameplay, done]
---


## Goal
Continue the Godot globe spike (`src/Singularity.Client.Spike/`, TASK-0067) into a playable client mock-up, following the design documents. Wherever the Session API exists, wire it for real; elsewhere use visibly marked mock data. Owner decision #292.

## Design inputs
- docs/ui/UI_ART_DIRECTION.md, UI_DESIGN_SYSTEM.md (controls: segments, signal field, panels, typography), GLOBE_DIRECTION.md (approved, #288).
- docs/ui/TECH_TREE_SCREEN.md and prototypes/tech-tree.html + tech-tree-scale.html: A «Планисфера» main view, B «Ведомость цели» second view (#286).
- Tempo #269: pause; slow 1 day/s, medium ~1 week/s, max 1 month/s.

## Scope, in order
1. **Session wiring.** Reference Singularity.Session/Core/Kernel from the Godot project. Godot 4.7.2 targets net8.0 and the core targets net10.0: find a working setup (Godot project on net10.0 with rollForward, or another way). Record it. If impossible, stop and report.
   - Load the slice scenario (`SliceSession.Create` with mods/core; see how Lab or tests build `GameData` and `SliceRunConfig`).
   - Run the simulation on a background thread per ADR-0014 item 4. The client reads `View` only.
2. **Top signal field** per the design system: date (`Date`), speed segment (pause / 3 speeds), power selector (the 6 slice actors), signal slots.
3. **Globe ↔ simulation.** Map the 6 slice actors to their countries on the globe. Colour fills per the direction. Click on a slice country opens the actor panel.
4. **Actor panel (inset).** Real data from `ActorView`:
   - budget shares with sliders → `Submit(SetBudgetShares)`, showing the verdict;
   - designs and lines (`DesignView`);
   - licences.
   - «Почему не» via `WhyNot`.
5. **Tech tree screen** as a full-screen inset:
   - A «Планисфера» from real `NodeView` (61 nodes) with states known / frontier / unavailable, the goal route, and the why_not root;
   - B «Ведомость цели» as the second view (switch).
   - Priorities (`SetEffortFocus` if it maps; otherwise mock with a label).
6. Everything without an API is mock, labelled «макет» in the UI.

## Rules
- The prototype stays outside Singularity.sln and must not change Core, Session or Kernel. If an API is missing, list it in «Результат» as a proposal. Do not add it.
- Licences per ADR-0009 (CC BY allowed with the registry, #288).
- Keep `./run-globe-spike.sh` working.

## Acceptance
- Runs on the owner's machine.
- Speed controls drive real ticks; the date advances; a budget slider change is accepted by Session and visible in the next view.
- Screenshots of: globe with the panel, tech tree A, tech tree B, signal field.
- The main solution's full `dotnet test` is green.
- «Результат» lists real vs mock per control and the missing APIs.

## Результат
**Сделано.** Клиент `src/Singularity.Client.Spike/` (вне `Singularity.sln`, Core/Kernel/Session не тронуты) подключён к реальному `SliceSession`; запуск `./run-globe-spike.sh` (интерактив) или `--shots` (скриптовый прогон + снимки).

**net8/net10.** [Факт] Хост Godot 4.7.2 запускается на .NET 8, но его `GodotPlugins.runtimeconfig.json` содержит `rollForward: LatestMajor`; при установленном рантайме .NET 10 хост загружает сборку игры `net10.0` и `ProjectReference` на Session/Core/Kernel без изменений. Решение: `TargetFramework` = `net10.0` в csproj (Godot.NET.Sdk 4.7.2 собирает его без ошибок). Требует .NET 10 рантайм рядом с Godot (на машине владельца он есть: SDK 10.0.403).

**Потоки.** `SimHost` держит `SliceSession` в фоновом потоке (ADR-0014 п. 4): UI читает только неизменяемый `HostSnapshot` (View + дата + вердикты + why_not), команды и запросы идут через очередь. Тики = дни; темп 1 день/с, 7 дней/с, 30 дней/с (#269). Реальные тики: в прогоне 1945-11-27 → 1946-07-13 (315 тиков).

**Реально / макет по контролам**
- Сигнальное поле: дата, темп (пауза+3), выбор державы (6 по данным; в сценарии 5 актёров: USA, GBR, SUN, FRA, SWE) — реально. Места СПЛАВ (`AlloyDeficit`), ЛИЦЕНЗИЯ (`Refusals`), ОТКАЗ (вердикт Submit) — реально; ЦЕЛЬ, РАЗРЫВ, ВОЙНА — макет (◦).
- Глобус: заливка/подсветка 5 держав среза, клик открывает панель — реально; остальные страны приглушены. Метки «Узкое место/Дефицит/Срыв стройки», заводик, самолёт из спайка — макет (подписаны).
- Панель державы: 5 долей бюджета с ползунками → `Submit(SetBudgetShares)` и вердикт; в снимке-прогоне доли R 0,28→0,38, закупки 0,42→0,32 приняты, через ≤ 1 мес видны в `Inputs.Shares` (`run-log.txt`) — реально. Проекты/линии (`DesignView`), кнопки `CloseLine`/`CancelDesign`, лицензия, «Почему не» (`CurrentRequirement`+`WhyNot`) — реально. `ComposeDesign` и `StartSeries` НЕ подключены (нужны список альтернатив слотов и единицы темпа).
- Дерево знаний: вид A (планисфера из 61 реального узла, состояния известно/фронтир/недоступно из `NodeView` + `requires`, маршрут, корень why_not) и вид B (ведомость) — состояния реальные; ступени зависимости — вычислены клиентом; цель = APP требования + замыкание звеньев KNOWLEDGE. Сроки, интервалы P10–P90, доли прогресса, «отставание от чужого знания» — нет данных (в UI помечено). Приоритеты НИОКР по фронтиру — макет (рычага нет). `SetEffortFocus` подключён реально, но он про фокус освоения ≤ 3 уже ИЗВЕСТНЫХ процессов, не про приоритет исследования фронтира.
- Шрифты: встроенный шрифт Godot (OpenGost/Golos не подключены); локализация — черновые строки, ключей нет.

**Недостающие API (предложения, не добавлялись)**
1. Текущие доли бюджета и фокус в силе: в `ActorView` их нет, только `Inputs.Shares` ненулевых месяцев (клиент кэширует; на старте берёт доли профиля).
2. Список альтернатив слотов/опций для `ComposeDesign`; единицы и допустимый диапазон `rate` для `StartSeries`.
3. Прогресс узла (`prog`), `c_eff`, вес `w_n`; интервал срока (P10–P90) и срок недоступного узла; «самая долгая цепочка»; `known_elsewhere` по державам.
4. Рычаг `research_priority` как команда.
5. Потокобезопасный доступ к `WhyNot`/`Outcomes` (сейчас вызывается только из потока симуляции; клиент это соблюдает).
6. Атрибуты узла для UI: ключи `data.node.<id>.name/.short`; сведения о «тоне» сигналов (категории сигнального поля).

**Проверка.** `dotnet build` клиента — 0 ошибок; `./run-globe-spike.sh --shots` — прогон до конца, снимки в `docs/ui/prototypes/client-v02/` (globe_panel, signal_field, tech_tree_a, tech_tree_b, run-log.txt); полный `dotnet test` основного решения — зелёный (Architecture 98, Kernel 322, Core 1060, Lab 20, Session 353). Не проверено: ручное управление мышью (клики вручную не гонялись; скриптовый прогон идёт через те же `ChoosePower`/`Submit`), снимок B без цепочки (в данных SUN маршрут из одного узла).
