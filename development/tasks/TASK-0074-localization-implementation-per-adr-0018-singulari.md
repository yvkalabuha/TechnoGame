---
id: TASK-0074
title: "Localization implementation per ADR-0018 (Singularity.Localization, ftl, anchor move)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0018, TASK-0072]
approved_by: owner
approved_at: 2026-11-18
approval_ref: "DECISIONS_LOG #297, #300, #304"
created: 2026-11-18
---


## Goal
Implement ADR-0018 (ACCEPTED; S1 passed, erratum #304) in the main solution. The client migration from CSV comes in a later task, so do not touch src/Singularity.Client.Spike.

## Scope (ADR-0018 §7, step 1)
- A new assembly src/Singularity.Localization: a facade over Linguini.Bundle 0.11.0 with no Godot dependency. NUMBER/DATE per the S1 erratum: a FluentNumber subclass, no FormatterFunc. Includes the DataRef case/gender functions, the fallback chain and the missing-key marker.
- Loading `mods/<mod>/locale/<lang>/*.ftl`:
  - the id mapping from data ids to Fluent ids per the ADR;
  - the mod override and conflict rules;
  - the stale-translation sidecar (`_sources.tsv`).
- tests/Singularity.Localization.Tests, all from ADR §7:
  - L1–L6;
  - id mapping in both directions, including dotted ids;
  - mod conflicts;
  - CLDR RU/EN plurals including «1,0»;
  - NUMBER and DATE;
  - staleness;
  - glyph coverage against the client fonts (#294);
  - the `xx` pseudo-locale;
  - "deleting locale/ changes neither DataHash nor StateHash".
- mods/core/locale/en and ru: .ftl for the 61 nodes (name, short, anchor), actors and countries for 1945 (SUN = СССР/USSR), enums, months. Russian names and anchors exist in the data and CSVs today. English: use the client's data.csv where present; otherwise translate and keep a list for research-scientist review.
- The anchor move (#297):
  - remove `anchor.event` from Core data and NodeTable;
  - update Core, Lab (SynthWorld.cs:380, SynthChecks.cs:72) and tests (CoreNodesTests.cs:55 moves to Localization.Tests);
  - DataHash changes once.
  - Run the EXP-0002-det matrix: `golden_set_hash` must be unchanged, which proves the dynamics are unchanged.
  - Report the old and new DataHash; the Orchestrator records them.
- Add the Unicode V3 notice to the licence files.
- A ModLoader rule that skips locale/ explicitly.

## Acceptance
- Full `dotnet test` green; Lab selftest green.
- The EXP-0002-det golden_set_hash is unchanged.
- No text in Core state.

## Результат
Статус работы: сделано, не закоммичено (коммитит Orchestrator).

**Что сделано**
- Сборка `src/Singularity.Localization` (net10.0, Linguini.Bundle 0.11.0, без Godot и Client): `FluentIds` (отображение id в обе стороны, виды, атрибуты), `LocaleCatalog` (загрузка `mods/<mod>/locale/<lang>/*.ftl`, конфликты модов на уровне AST по правилам ADR-0010, громкие ошибки, `_sources.tsv`), `Loc` (фасад: цепочка lang → en → `[[id]]`, одно предупреждение на id, `T`/`A`/`Name`/`Number`/`Date`/`SetLanguage`/`LanguageChanged`), `NUMBER`/`DATE`/`NAME`/`GENDER` по erratum #304 (подкласс `FluentNumber`, без `FormatterFunc`), `Staleness` (хеш, `Stale`, `Stamp`), `PseudoLocale` (`xx`), `LocaleChecks` (L1–L4, L6), `ClientReferences` (L5), `LengthBudgets`.
- Данные: `anchor.event` убран из `nodes.yaml`, схемы, `NodeTable` (Raw, Validation, поле `AnchorEvent`), Lab (`SynthWorld.cs`, `SynthChecks.cs`), тесты (`CoreNodesTests`: проверка переехала в `CoreLocaleTests.RussianAnchors_EqualTheTask0011Csv`). Поле `event` теперь неизвестно строгому загрузчику (тест).
- `ModLoader.LocaleDirectory = "locale"`: каталог зарезервирован, загрузчик в него не заходит; файл с именем `locale` — ошибка. Тесты: удаление и правка `locale/` не меняют `DataHash` и `StateHash` (3 месяца среза, seed 7).
- `mods/core/locale/{en,ru}/{data,fmt}.ftl`: 61 узел (`.name`, `.short`, `.anchor`), `jet_fighter` (класс, 5 слотов, 18 альтернатив), сценарий, 5 акторов 1945 (СССР / USSR с падежами и `.gender`), 95 стран карты (`-country-<ADM0_A3>`), кластеры, дисциплины, типы узлов, `enum-*` (dimension, hazard, actor_class, variant), форматы чисел и дат, месяцы. `ru/_sources.tsv` (223 строки) создан скриптом и подтверждён тестом `RussianCore_HasNoStaleTranslations`.
- Уведомление Unicode V3 и лицензия Linguini: `THIRD_PARTY_LICENSES.md` (корень). Строка про Natural Earth: `mods/core/ATTRIBUTION.md`.
- Тесты: `tests/Singularity.Localization.Tests` (117, все зелёные): id и точки, конфликты, ошибки, цепочка запасных, CLDR RU/EN и «1,0», NUMBER/DATE (в т. ч. 9 культур потока), падеж и род, 16 потоков, L1–L6 на реальных данных и на негативных примерах, устаревание, глифы (мини-парсер cmap, 4 шрифта), `xx`, независимость от `locale/`. `tests/Singularity.Architecture.Tests/LocalizationIsolationTests.cs` (3): Kernel, Core, Session, Lab не ссылаются на Localization и Linguini.
- Sln: оба проекта добавлены командой `dotnet sln add`.

**DataHash (мод core)**
- Старый: `6eba0cea3e6af277446c6017cf5765ed4f0813d047304c4c14dff9ccfc585bce`
- Новый: `7deed8f095d0e09084b4e8300af2ac46ce4fba537811e63acf6133497de95a53` (константа в `DataHashTests.cs` обновлена один раз). `ScenarioHash1945` не менялся.

**EXP-0002-det** (`matrix --manifest lab/manifests/EXP-0002-det.json --out lab/results/task0074-det --jobs 4`, Release): ok=60, failed=0, thread_mismatch=0, `golden_set_hash` = `824f4ded07216828ee6354f95f6bd942dcefd6059f70fdff24fbf9f9ab91ea5b`, совпадает с записанным. Динамика не изменилась. Манифест не правился.

**Проверка**: `dotnet test Singularity.sln` зелёный (Localization 117, Architecture 101, Core 1060, Kernel 322 (1 пропущен), Lab 20, Session 353 (2 пропущены), Audio 54); `Singularity.Lab selftest`: 48/48.

**Отклонения и решения внутри ADR (для записи)**
- Хеш устаревания считается от нормализованного исходного текста сущности (разбиение по строкам `id =` в колонке 0), как в §3.4. Колонка-0 `}` внутри селектора не начинает сущность.
- Linguini бросает `IndexOutOfRangeException` при выборе формы числа для тега без таблицы CLDR (например `xx`). Фасад строит бандл `xx` по правилам `ru`; другие неизвестные теги дают `LocaleLoadException` при загрузке (`BundleFactory.PluralCulture`).
- Аргумент `useGrouping` задаётся строкой `"false"` (в Fluent `false` не литерал).
- L6: `.short` данных — плоский предел 12 графем без «+30%» (иначе RU-имена узлов не вмещаются). Кнопки, вкладки и прочее `ui-*` — правило §13.2 с +30%. Если нужна другая трактовка, правится `LengthBudgets`.
- Проверка id узла в `NodeTable` уже строже требуемой (`IsNodeId`: `sN.type.name`), новая не нужна.
- В `xx` скобки `[! !]`: ⟦ ⟧ нет в Golos Text и Jura (тест следит за изменением).
- Русский `γ′` в названии узла заменён: греческой γ нет в Golos Text. RU `.name` узла gamma_prime — «Сплавы с гамма-штрих-фазой».

**Не сделано (вне объёма задачи или заблокировано зоной)**
- `Singularity.Localization.Tool` (`check`, `stamp`, `pseudo`): ADR §7, но в задаче его нет. API `Staleness.Stamp` и `PseudoLocale.AddTo` готовы.
- Уведомление Unicode V3 не добавлено в `src/Singularity.Client.Spike/LICENSES.md` (запрещено трогать): нужно строкой-ссылкой на `THIRD_PARTY_LICENSES.md` при миграции клиента.
- L5 проверен на синтетических примерах; к реальному клиенту применится после миграции CSV.
- `ui.ftl` и `enum-*` для перечислений Session (Stage, Profile…) не заведены (нет таблицы имён, тексты за game-designer).
- Правка `UI_DESIGN_SYSTEM.md` (§6 ADR) за ui-art-director.

**Для research-scientist (проверка перевода)**: все EN `.anchor` 61 узла (перевод с русского; без изменений только «Nimonic 80», «Ti-6Al-4V»), все `.short` (EN и RU), русские названия 95 стран и падежи 5 акторов, а также EN-названия узлов (взяты из client data.csv как есть). После проверки — `stamp ru` (пока вручную скриптом, Tool нет).

**Правки по ревью (round 2)**
- `Loc.Name(who, Field.Short|Adj)` читает атрибут терма через `TryGetAstTerm` (раньше всегда возвращал `[[…]]`); тесты на `.short`, `.adj`, отсутствующий атрибут.
- `_warned` под блокировкой; тест на параллельные промахи (одно предупреждение на id).
- `THIRD_PARTY_LICENSES.md` (корень): полный текст MIT с авторской строкой Linguini.
- Страны: 95 термов `-country-*` в `ru/data.ftl` с селектором `$case` (nom, gen, dat, acc, ins, loc; в ADR падеж называется `loc`) и `.gender`. Склонение сгенерировано правилами, несклоняемые (Мали, Чили, Перу, Конго, ДР Конго, ЮАР, США, Марокко, Зимбабве, Сомали) и особые случаи заданы вручную. `ExpectedKeys.MapCountries` (список ADM0_A3) входит в L1; L1 теперь требует все шесть падежей у русских термов (акторы, страны, регионы). Страны и актор-термы без падежей не пройдут тест.
- `NaN`, бесконечность и числа вне диапазона `decimal`: маркер `[[number]]` (`Loc.Number`, `NUMBER`, голые аргументы); тест.
- Глифы: названия акторов и стран проверяются в Golos Text и Jura в дополнение к Literata; цифры и разделители в PT Mono.
- `OtherLanguageTags` разбит по тегам; оставлен один тест смены DataHash (`DataHashTests.CoreDataHash_IsFrozen`); бюджет `inset_title` 28 и `inset_title_wide` 40.
- Долг (записывает Orchestrator): отметки `ru/_sources.tsv` поставлены скриптом до проверки человеком. research-scientist должен проверить EN и RU переводы (узлы, `.short`, `.anchor`, названия и падежи стран и акторов) до релиза, затем `stamp ru`.
- [x] Второе ревью (codex, head 6514af88) — changes_requested: 1 high, 3 medium; report sha256 9cc8e65e85fff5f816bdfcf2dd9679ebc289835b2388fe326e08568461e4b175; fixes in TASK-0076
