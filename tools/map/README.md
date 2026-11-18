# tools/map: сборщик карты (ADR-0011 §2)

Консольный инструмент на C# и NetTopologySuite 2.6.0 (BSD-3-Clause). LGPL-модули (`IO.ShapeFile`, `CoordinateSystems`, `ProjNet`) запрещены: `check_packages.sh`. SDK закреплён `global.json`, пакеты — `packages.lock.json` (locked restore). Инструмент не входит в `Singularity.sln`; Core, Session и клиент на него не ссылаются.

## Команды
`dotnet run -c Release --project tools/map/MapBuilder -- <команда> [--cache dir] [--out dir] [--render file.png]`

| Команда | Что делает |
|---|---|
| `fetch [--pin]` | скачивает источники из `sources.lock.yaml` в кеш (`tools/map/.cache` или `SINGULARITY_MAP_CACHE`), сверяет SHA-256 |
| `bootstrap` | один раз пишет `spec/10-merge`, `15-subdivide`, `ids`, `regions`; повторный запуск дописывает id только для кусков без якоря |
| `build` | применяет `spec/`, проверяет (валидатор ADR §2.10), пишет `mods/core/map/{provinces,regions,adjacency,straits}.yaml` и `geo/{arcs.bin,manifest.json}` |
| `verify` | сверяет `geo/manifest.json` с таблицами (`data_binding`) |
| `find`, `at`, `overlap`, `dump` | вспомогательные запросы для ручной правки спецификации |

## Данные
- `sources.lock.yaml`: NE 10m admin-1 и admin-0 map units (тег v5.1.2, public domain) и снимки OHM (CC0 по умолчанию OHM; запрос Overpass закреплён `[date:...]`). Сырые файлы в git не лежат.
- `spec/`: только геометрия. Файлы применяются по имени: `05-keep`, `10-merge`, `15-subdivide`, `20-cuts`; `ids/` (якоря), `regions/`, `straits.yaml`, `area.yaml`.
- Владение и политии (ручная таблица, черновик T4): `mods/core/scenarios/1945/{polities,owners}.yaml` (вне `map/`: файлы `map/*` запечатаны для модов, владение остаётся модифицируемым).

## Проверки
- `check_determinism.sh`: две сборки побайтово равны и совпадают с закоммиченными таблицами.
- `check_packages.sh`: граф пакетов без LGPL.
- Тесты: `dotnet test tests/Singularity.MapBuilder.Tests` (вне основного решения).

## Формулы
- `area_km2`: сферический избыток кольца по Chamberlain & Duquette (2007) на аутентической сфере R = 6 371 007,181 м, дыры вычитаются, `long`, округление к чётному.
- `centroid`: **плоский** центроид NTS в градусах (долгота как x, широта как y, без поправки на широту), затем в микроградусы; для мультиполигона он может лежать вне провинции. `label` — внутренняя точка NTS (`InteriorPoint`), всегда внутри.
- Числа в таблицах пишутся в инвариантной культуре; `ATTRIBUTION.md` в `mods/core/map/` перечисляет объекты OHM под CC BY (сейчас таких нет). Разрешены лицензии CC0-1.0, CC-BY-2.0/3.0/4.0, PD.
