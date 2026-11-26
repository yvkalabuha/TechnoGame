---
id: TASK-0075
title: "Map builder T2: tools/map on NTS, Europe first, 1945 cuts, coverage simplification check"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: tooling
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0011, ADR-0009]
approved_by: owner
approved_at: 2026-11-18
approval_ref: "DECISIONS_LOG #315 (ADR-0011 ACCEPTED)"
created: 2026-11-18
---


## Goal
The first map builder step from ADR-0011 (ACCEPTED, revision 3): `tools/map/` §2. Run on Europe first. The main risk is the NTS coverage operations for shared-boundary simplification; verify them first and report early if they are missing.

## Scope
1. Inputs:
   - pin the Natural Earth 10m admin-1 and admin-0 map_units GeoJSON by URL and SHA-256 in `sources.lock.yaml`, with licence and date (ADR-0009);
   - download into a cache; do not commit raw NE files.
2. A C# console tool under tools/map on NetTopologySuite (BSD-3), with the LGPL modules excluded:
   - CI check of the package graph;
   - pinned SDK and NuGet.
3. Spec files per ADR §2 for Europe: ids with `bootstrap`, merges, cuts (Oder–Neisse, the German and Austrian zones as of 1945-10-01, Karelia, the Baltics in the USSR), subdivide by Voronoi.
4. Outputs per ADR §2.5:
   - provinces, regions, adjacency, straits, the owners/controllers table for Europe as of 1945-10-01, polities (#311–#313);
   - generated geo/ with a manifest binding hash (§4).
5. The validator per ADR «Проверка» for Europe; a determinism check (two runs give byte-identical output).
6. A debug render (PNG) of Europe with owner fills and controller hatching, into docs/ui/prototypes/map-1945/ for review.

## Out of scope
The rest of the world; the simulation ownership layer; client integration (later tasks).

## Acceptance
- Byte-identical output on two runs.
- The validator passes for Europe.
- The coverage simplification works or the gap is reported.
- The main solution's `dotnet test` is unaffected and green.

## Результат
**Сделано** (разработчик, 2026-11-18). Сборщик `tools/map` работает на Европе: 603 провинции, 76 регионов, 1484 пары соседства, валидатор без ошибок.

**Главный риск закрыт.** [Факт] В закреплённом NetTopologySuite 2.6.0 (лицензия пакета BSD-3-Clause, проверено по nuspec) есть `CoverageValidator`, `CoverageSimplifier` (TPVW), `CoverageGapFinder`, `CoverageUnion`. На 1629 объектах Европы валидатор даёт 0 ошибок, упрощение с допуском 0,01° уменьшает число вершин с 324 714 до 152 637, результат остаётся валидным покрытием и повторяется побайтово. Резервный путь mapshaper не нужен.

**Что внутри**
- `tools/map/sources.lock.yaml`: NE 10m admin-1 и admin-0 map units (тег v5.1.2, public domain) и 6 снимков OHM (CC0 по умолчанию OHM). SHA-256 проверены повторной загрузкой с нуля. Запрос Overpass закреплён `[date:...]`: основной API OHM закрыт Cloudflare (403), поэтому используется Overpass.
- `tools/map/MapBuilder` (C#, NTS 2.6.0 и YamlDotNet 18.1.0): `fetch`, `bootstrap`, `build`, `verify` и вспомогательные `find/at/overlap/dump`. `global.json`, `packages.lock.json` (locked restore), `check_packages.sh` (граф без LGPL), `check_determinism.sh`.
- `tools/map/spec/`: `05-keep` (Берлин, Вена, Бремен), `10-merge` (жадное слияние малых единиц), `15-subdivide` (Вороной, 5 крупных областей РФ), `20-cuts` (7 разрезов класса A), `ids/` (якоря), `regions/`, `straits.yaml`, `area.yaml`.
- Разрезы по полигонам OHM: граница Германии 1937 по воеводствам Польши (Одер–Нейсе), зона США в Баден-Вюртемберге и Баварии (Линдау во французской зоне), зоны Австрии (Верхняя Австрия, Восточный Тироль), Карелия и Петсамо по границе Финляндии до 1940, Порккала; Додеканес по меридиану 26,1° в. д. Прибалтика, Бессарабия, Закарпатье — только в `owners.yaml`.
- Выход: `mods/core/map/{provinces,regions,adjacency,straits}.yaml`, `geo/manifest.json` с `data_binding`, `geo/arcs.bin` (738 КБ, в git не идёт: `geo/.gitignore`). Черновик владения: `mods/core/scenarios/1945/{polities,owners}.yaml`.
- Рендер: `docs/ui/prototypes/map-1945/europe-1945-10-01.png` и `README.md` (заливка owner, штриховка controller).

**Проверка**
- `check_determinism.sh`: две сборки и два PNG побайтово равны, таблицы равны закоммиченным.
- `dotnet test tests/Singularity.MapBuilder.Tests`: 25 тестов зелёные (площадь, Хэлтон, Вороной, дуги и кольца, разрез и лечение покрытия, якоря, замок, граф пакетов, `data_binding` и ошибка при правке таблицы без пересборки).
- `dotnet test Singularity.sln`: зелёный, основное решение не затронуто (проекты tools/map и тестов в `.sln` не добавлены).
- Валидатор: покрытие, полосы площади с исключениями (а)(б)(в), якоря, `cut` (source, as_of, precision, ref), лицензии OHM, `owners.yaml`, связность графа регионов, коллизии `polity_id` с ADM0_A3. Диапазоны 2000–5000 и 150–250 для части мира не применяются, только печатаются.
- Найдена и исправлена ошибка ключа вершин в дугах (на отрицательных долготах); её поймала сверка «кольца из дуг = полигон».

**Что не сделано или отклоняется от ADR**
- Секторы Берлина и Вены не разрезаны: PD/CC0 источника линий не найдено. Оба города целиком, `controller` = совместная полития `ALLIED_BERLIN`/`ALLIED_VIENNA`.
- Не сделано: Триест, Истрия, Рийека, Задар (линия Моргана), участок обмена Польша — СССР 1951, Рюкю и Азия (вне Европы). Регионы Европы разбиты автоматически (k-means по странам), это черновик для T3.
- `owners.yaml` и `polities.yaml` — черновик разработчика, не сверен по источникам; ссылки на договоры не проверены (T4, затем critic). Спорные пункты для владельца: де-юре владелец земель к востоку от Одера–Нейсе и Кёнигсбергской области (здесь owner DEU, controller POL/SUN).
- Две щели NE (Эстония–Латвия, Италия–Словения, шире 0 и уже 0,01°) выводятся предупреждением, не заполняются.
- Манифест не хранит SHA-256 растра R16 (его печёт клиент, T6). Расстояние Хаусдорфа для линий класса A не считается: линии взяты из OHM или заданы параметром.
- Файлы вне моей зоны не менялись: `mod.yaml` не включает `map/*` (загрузчик ModLoader их пока не знает), CI-шаги не добавлены.

**Правки после ревью (2026-11-18)**
- Культура: все числа в таблицах, манифесте и спецификациях пишутся через `FormattableString.Invariant`; `InvariantGlobalization` и InvariantCulture в CLI. Тест гоняет writer под sv-SE и сверяет байты.
- Лицензии: явный список CC0-1.0, CC-BY-2.0/3.0/4.0, PD; написания OHM («CC BY 4.0», «Creative Commons BY 4.0 International») нормализуются, SA/NC отклоняются. Лицензия читается и с member ways (запрос закреплён `way(r.r);out tags`, хеши обновлены). [Факт] CC BY 4.0 объекты в снимках есть (Kartverket, геодезическая служба Словении, участники OHM): они перечислены в `mods/core/map/ATTRIBUTION.md`, который строит `build`.
- Сценарий перенесён в `mods/core/scenarios/1945/`. Класс A ставится только на куски, названные в `a_pieces` разреза (иначе на оба); при слиянии и клонировании флаг не наследуется. Пины 2852444 и 2746444 удалены, id `@vN` сверяется с версией снимка. Heal падает, если площадь группы меняется больше 1e-6 или брошена грань больше 5 км² без владельца. Центроид (плоский, в градусах) описан в `tools/map/README.md`. Тест «сдвиг линии разреза в пределах якорей не меняет набор id» добавлен (39 тестов).
- Для T3/T4: Данциг и земли к востоку от Одера–Нейсе, не входившие в границы 1937 (Гданьск попал в `former_german` с owner DEU); привязка Калининграда к региону `russia_vologda` (кластер k-means); id регионов по стране и крупнейшей провинции вместо географии; границы зон Берлина и Вены.

### Правки после второго ревью
- F1: в `sources.lock.yaml` и `manifest.json` снимки 2692205, 2746443, 2959045, 2855285 теперь с лицензией `CC-BY-4.0+CC0-1.0` (набор лицензий снимка), строки источников в `20-cuts-europe.yaml` исправлены; Validator сверяет лицензию лока с набором `OhmShape.Licenses` и проверяет каждую часть по списку разрешённых.
- F2: README обновлён (устаревшее «сейчас таких нет» убрано).
- F3: `DoCut` выдаёт ошибку, если `a_pieces` не входит в `pieces` (или в `a`/`b` при отсутствии `pieces`); тест добавлен.
- F4: тесты на обе защиты Heal (потеря крупной грани; изменение площади > 1e-6 при перекрытии кусков).
- Проверка: `dotnet test tests/Singularity.MapBuilder.Tests` — 42 пройдено, 0 провалено; `fetch` в `.cache` (не коммитится) и `check_determinism.sh` — «determinism OK» (две сборки побайтово равны и совпадают с таблицами).
