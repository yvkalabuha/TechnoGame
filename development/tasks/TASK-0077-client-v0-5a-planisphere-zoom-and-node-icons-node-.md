---
id: TASK-0077
title: "Client v0.5a: planisphere zoom and node icons, node card with schematic and effects, map click fix, starry sky"
status: REVIEW         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: tooling
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0071, ADR-0018]
approved_by: owner
approved_at: 2026-11-18
approval_ref: "DECISIONS_LOG #318"
created: 2026-11-18
---


## Goal
Owner feedback #318 for the client prototype (src/Singularity.Client.Spike).

## Scope
1. **Planisphere zoom and pan.**
   - Wheel zoom under the cursor, drag to pan, a reset button.
   - Level of detail per docs/ui/prototypes/tech-tree-scale.html: aggregated at overview, individual nodes when zoomed in.
   - When zoomed in, nodes become icons per node type and discipline, or at least colour-coded by discipline (palette C tokens from docs/ui/UI_DESIGN_SYSTEM.md §17).
2. **Node card on click:**
   - a blueprint-style schematic picture: SVGs from docs/ui/assets/node-schematics/, made by ui-art-director in parallel. Until they exist, use a placeholder frame with the node type label;
   - **what it affects** (layer and system);
   - **which parameter it raises**, from the Session/Core data if available; otherwise mark it «макет» and list the missing API;
   - **what it unlocks next** (its dependants);
   - each block in a distinct colour per palette C.
3. **Map bug:** clicking a country centres it and changes zoom incorrectly. Find the cause (camera target, latitude/longitude conversion, zoom clamp) and fix it. Clicking should centre smoothly and keep the zoom, or use a sensible per-country zoom.
4. **Background:** a dark starry sky around the globe. A procedural star field, subtle and deterministic, that does not compete with the globe; the paper tint stays only on panels.

## Rules
- Do not change Core, Session or Kernel. List missing APIs in «Результат».
- Build check: run `dotnet build src/Singularity.Client.Spike` and the Godot `--build-solutions` step via `./run-globe-spike.sh --shots`.
- Screenshots go to docs/ui/prototypes/client-v05a/.

## Acceptance
- Zoom and pan are smooth; the node card shows all four blocks; the country click works for at least the USA, the USSR and France, before and after rotation; the sky is visible.
- The main solution's full `dotnet test` is green.

## Результат
**Сделано** (клиент `src/Singularity.Client.Spike`; Core, Session, Kernel не менялись).
1. **Планисфера:** колесо — масштаб под курсором (плавный, 1–9×), перетаскивание — сдвиг, кнопка «Сбросить вид». Область рисунка обрезается по своему прямоугольнику. Три уровня детализации (`PlanisphereMath.LodOf`): обзор (<1,6×) — росписи клеток «слой × дисциплина» со счётом «известно/всего»; узлы (1,6–3,4×) — отдельные узлы цветом дисциплины; значки (≥3,4×) — пластина по типу узла (круг/квадрат/ромб) и глиф по типу (FND, PRN, ENG, CMP, PRC, APP), подписи всех узлов где есть место. Цвета — чернила палитры C (§17.3): Аэро — teal, Силовые — plum, Металлы — slate, Химия — olive, Механика — sand. Маршрут остаётся синим, корень «почему не» — красным.
2. **Карточка узла** (вкладка «Узел» справа): схема из `docs/ui/assets/node-schematics/` по правилу README (файл узла по id, иначе `{type}-{discipline}.svg`, `Image.LoadSvgFromString`; без файла — рамка-заглушка); проверено на снимке (токарный станок для PRC/mech), блоки «Что затрагивает» (teal; слой, кластер, дисциплина: реальные данные), «Что повышает» (violet; выход процесса и требуемая мощность — реальные, **прирост параметра — «макет»**), «Что открывает дальше» (olive; зависимые узлы по `requires`, с состоянием).
3. **Баг карты:** формулы lon/lat и камеры согласованы; причина — `ChoosePower` жёстко ставил `_dist = 2.4` (зум прыгал) и мгновенно переставлял камеру. Теперь камера плавно летит по кратчайшему пути, зум не меняется. Проверка кликом (shots, `run-log.txt`): США, СССР, Франция, до и после поворота — ошибка центрирования 0,0 px, зум 2,60 → 2,60, регион выбран.
4. **Небо:** процедурный звёздный шейдер `shaders/stars.gdshader` (детерминированный хэш по направлению, без времени); лимб глобуса — тонкая светлая линия. Бумажный фон остался только на панелях.
5. **Свет глобуса** (#288, дополнение): `globe.gdshader` — один фиксированный направленный свет (`light_dir`, тот же вектор для будущего терминатора), мягкий Ламберт с широким терминатором, `ambient 0,72` (ночная сторона читается), лёгкая холодная тень, блик только на океане, слабая дымка на лимбе. Свет применяется к заливкам; границы, штриховка, выделение, подписи не затеняются. Снимки до/после: `globe_unlit.png` / `globe_lit.png`. Внешнего ореола за лимбом нет (нужна вторая сфера; не делалось).
6. **Одна держава игрока** (дополнение): список держав убран из сигнального поля. Держава игрока — параметр запуска `--power ID` (по умолчанию USA; позже уйдёт в меню «Новая игра»). Панель держателя открывается для неё при старте; клик по другой стране открывает панель только для чтения: заголовок «ЧУЖАЯ ДЕРЖАВА», без приказов, ползунков, кнопок линий; фокус освоения в дереве тоже только для своей. Сигналы считаются только по державе игрока.

**Файлы:** `TechTree.cs`, `Main.cs`, `SignalField.cs`, `ActorPanel.cs`, `Overlay.cs`, `Ui.cs`, `PlanisphereMath.cs` (новый, без Godot), `shaders/globe.gdshader`, `shaders/stars.gdshader` (новый), `locale/ui.csv`; тесты `tests/Singularity.Client.Audio.Tests/PlanisphereMathTests.cs` (+ ссылка в csproj); снимки `docs/ui/prototypes/client-v05a/`.

**Недостающие API (Core/Session):** (а) `NodeEffects(node) -> (параметр, прирост)` — какой параметр и на сколько повышает узел; (б) прогресс и ETA по узлу (без изменений с прежнего). Выбор державы игрока в Session пока не нужен (только клиент).

**Проверка:** `dotnet build src/Singularity.Client.Spike`; `./run-globe-spike.sh --shots` (сборка Godot + снимки + проверка кликов); полный `dotnet test` главного решения.

**Правки после ревью (v0.5a).** Код-ревью: выбор схемы вынесен в `NodeSchematics.cs` (без Godot) по правилу README: сначала файл узла по точному id (таблица 2), затем `{type}-{discipline}`, иначе без картинки; `s8.prn.swept_wing` теперь берёт `prn-aero.svg`; 6 тестов в `NodeSchematicsTests.cs`. Чтение SVG в try/catch (заглушка при ошибке). `ActorPanel.Apply` и кнопка фокуса в дереве не работают для чужой державы. Неверный `--power` даёт предупреждение и явный откат на USA. Убраны `_clickActor`, `_clickShift`, `SelectedRow`; исправлены отступ `Shape` и комментарий `ShotZoom`. Визуальное ревью: карточка узла стоит первой, «Почему не» свёрнут (раскрыт, пока узел не выбран); «Зависит от» отдельным блоком; пустое «Якорь: 1945 ·» заменено на «Якорь: 1945» (подписи якорей ещё нет в локали); бумажный ореол у подписей; выноска укорочена; свои токены блоков карточки (`Aff*`, `Raise*`, `Open*`, `Req*` в `Ui.cs`), схема приглушена (альфа 0,6, ниже); звёзды без крупных дисков (убран слой 36, потолок яркости); у чужой державы скрыт пустой раздел «Изделия и линии», ползунки приглушены и не реагируют, добавлена пометка про доли. **Источник долей 28/15/10/42/5:** это не данные державы, а `PolicyParams.SharesByProfile[профиль]` через `SliceWorld.SharesOf` (доктрина ИИ-профиля; у США и СССР профили совпадают по умолчанию) — панель теперь так и подписана, колонка «в силе» у чужой державы «—». Не сделано (v0.5b): легенда, порог «узлов», затенение суши, центр камеры, тег страны, вопрос про разведку. Проверка: сборка клиента, `--shots` (снимки обновлены), `dotnet test tests/Singularity.Client.Audio.Tests`.
