---
id: ADR-0018
title: "Localization: keys, locale files in mods, EN and RU"
status: PROPOSED         # PROPOSED | ACCEPTED | SUPERSEDED | DEPRECATED | REJECTED
date: 2026-11-17
supersedes:              # ADR-NNNN, если заменяет
superseded_by:           # заполняется при замене
related: [ADR-0002, ADR-0009, ADR-0010, ADR-0013, ADR-0014, RR-0003]
approved_by:             # owner — обязательно для ACCEPTED
approved_at:
approval_ref:
---

# ADR-0018 — Localization: keys, locale files in mods, EN and RU

> После ACCEPTED текст не меняется. Изменение решения — новый ADR с `supersedes`.

## Краткий вывод
[Предл.] Каждая видимая строка — ключ. Ключи данных выводятся из стабильных id (`data.<kind>.<id>.<field>`), ключи интерфейса — `ui.<screen>.<element>`. Строки лежат в модах: `mods/<mod>/locale/<lang>/*.yaml` (строгий YAML ADR-0010, `define`/`override`). Отдельная сборка `Singularity.Localization` (чистый .NET, без Godot) загружает и проверяет каталоги, выбирает формы множественного числа по CLDR и форматирует числа и даты по собственным правилам. Ядро текста не видит: `ModLoader`, `StateHash`, `DataHash`, сохранения и golden от языка не зависят. Клиент Godot при запуске строит из каталога объекты `Translation` (runtime, без импорта ресурсов). Полнота EN+RU, плейсхолдеры, формы мн. числа и бюджеты длины проверяет `dotnet test`.

## Контекст
- [Факт] Решения владельца #83 (2026-11-03) и #295 (2026-11-17, `DECISIONS_LOG.md`): с первой версии два базовых языка, EN и RU; все строки UI и данных через ключи, с запасом длины.
- [Факт] `docs/ui/UI_DESIGN_SYSTEM.md` §13: ключи `ui.<screen>.<element>[.short]` и `data.<domain>.<id>.name`, шаблоны с именованными местами, CLDR-формы (RU one/few/many/other, EN one/other), бюджеты длины §13.2 (кнопка 24 знака, вкладка 16 …; бюджет вмещает длинную версию +30%), псевдолокаль `xx`. §14 предполагает `TranslationServer` + `.po` и ждёт подтверждения systems-architect.
- [Факт] Данные `mods/core` (на 2026-11-17): в `nodes.yaml`, `product_classes.yaml`, `scenario_1945.yaml` отображаемых имён нет — только id (`s8.eng.turbojet_g1`, `jet_fighter`, `eng_g1`, актор `USA`). Единственный текст — `anchor.event` узлов (61 узел, русский: «Прандтль: пограничный слой»); он входит в `GameData.DataHash` (`NodeTable.Validation.cs`, `w.Str(AnchorEvent[r])`).
- [Факт] Клиент (`src/Singularity.Client.Spike/Names.cs`) держит черновые имена в коде: `RuOfActor`, `Stage`, `Profile`, `Short(id)`. TASK-0070 добавляет клиентскую таблицу `TranslationServer` (CSV) EN/RU с переключением; её не трогаем, мигрируем позже (§5).
- [Факт] ADR-0014: представления Session несут id и коды, не текст; `SliceSession` пишет исходы команд как `reason` (код) + `Message` (диагностика на английском).
- [Факт] `ModLoader.CheckUnlistedFiles` проверяет только корень каталога мода (`Directory.GetFiles` без рекурсии), поэтому подкаталог `locale/` загрузчик данных не видит.

## Решение

### 1. Схема ключей [Предл.]
Ключ — сегменты через точку, `[a-z0-9_]`, кроме сегмента id: id данных входит дословно (`USA`, `s8.eng.turbojet_g1`). Последний сегмент всегда поле из закрытого списка, поэтому id с точками разбирается однозначно.

| Что | Ключ | Поля |
|---|---|---|
| Узел знания | `data.node.<node_id>.<field>` | `name` (обяз.), `short`, `desc`, `anchor` (бывш. `anchor.event`) |
| Класс продукции | `data.product_class.<class_id>.<field>` | `name`, `short`, `desc` |
| Слот / альтернатива | `data.product_class.<class_id>.slot.<slot_id>.<field>`, `….alt.<alt_id>.<field>` | `name`, `short` |
| Сценарий | `data.scenario.<scenario_id>.<field>` | `name`, `desc` |
| Актор (страна сценария) | `data.actor.<actor_id>.<field>` | `name`, `short`, `adj` (прилагательное) |
| Регион, страна мира, событие (когда появятся) | `data.region.<id>.*`, `data.country.<id>.*`, `data.event.<id>.title / .body / .option.<opt_id>` | по виду |
| Кластер, дисциплина, тип узла (имена из `DataNames`) | `data.cluster.<name>.name`, `data.discipline.<name>.name`, `data.node_type.<name>.name` | `name`, `short` |
| Перечисления кода (стадия, профиль, причины отказа, слова-состояния) | `enum.<enum_snake>.<member_snake>` | — (`.short` по бюджету) |
| Интерфейс | `ui.<screen>.<element>[.short]`, общее — `ui.common.*` | — |
| Единицы и форматы | `unit.<id>`, `fmt.<id>` | — |

- В YAML данных имён нет: ключ — чистая функция `(kind, id, field)`. Переименование id = переименование ключа; это ловит проверка «сирот» (§3).
- `.short` — отдельный ключ с тем же префиксом (`data.node.<id>.short`, `ui.tree.legend.short`); многоточие вместо `.short` запрещено там, где запрещает §13.2.
- Имена с датой (СССР → Россия) — не в этом ADR; когда понадобятся, отдельный ключ-вариант по событию, а не по году в коде [Предл.].

### 2. Где лежат строки [Предл.]
- `mods/<mod>/locale/<lang>/<any>.yaml`, `<lang>` — тег BCP 47 вида `en`, `ru`, `pt-BR`. Для `core`: `locale/en/data.yaml`, `locale/en/ui.yaml`, `locale/ru/data.yaml`, `locale/ru/ui.yaml`. Файлы языка грузятся в порядке ordinal-сортировки имён; файлы в `locale/` вне каталога языка и не `.yaml` — ошибка.
- Формат файла (строгий YAML ADR-0010):
  ```yaml
  kind: locale
  language: ru
  define:
    data.node.s8.eng.turbojet_g1.name: "Турбореактивный двигатель, 1-е поколение"
    ui.common.cost: "{price} · {eta}"
    ui.signal.engineers_short:            # форма мн. числа: карта CLDR-категорий
      one: "{n} инженер"
      few: "{n} инженера"
      many: "{n} инженеров"
      other: "{n} инженера"
  override:
    data.actor.SUN.name: "…"
  ```
- Моды: порядок — тот же, что у `ModLoader` (зависимости, затем id мода). Внутри языка `define` ключа, уже определённого другим модом, — ошибка; замена — только `override` (ключ должен существовать). Мод может добавить язык (`locale/de/`), не трогая данных; языковой мод зависит от `core`. Удаления строк нет.
- `mod.yaml` не меняется: `locale/` находится по соглашению, загрузчик данных его не читает.
- Лицензии (ADR-0009): переводы — собственная работа проекта. Импорт списков имён (например, названия стран Natural Earth, PD) — с записью в `ATTRIBUTION.md`; тексты описаний из CC BY-SA (Википедия) не копируются.

### 3. Загрузка и проверки [Предл.]
Сборка `Singularity.Localization` (`net10.0`): зависит от `Singularity.Core` (только `StrictYaml`, `ModManifest`, порядок модов и `GameData` для перечня id). Kernel, Core, Session и Lab на неё не ссылаются.
- **Цепочка запасных:** выбранный язык → `en` → видимый маркер `⟦key⟧` + одно предупреждение в лог на ключ. Для `core` цепочка в release не срабатывает: полнота EN и RU обязательна (ниже).
- **Ошибки загрузки (`LocaleLoadException`, громко):** неверный ключ по грамматике §1; `define`-конфликт; `override` несуществующего ключа; неизвестный язык в имени каталога ≠ `language:`; плюрал-карта с категорией, которой нет у языка в CLDR.
- **Проверки полноты (тест для `core`; для сторонних модов — предупреждения, кроме `en`):**
  - L1: для каждого объекта данных и каждого `enum`-члена из реестра есть `name` (или ключ `enum.*`) в EN и RU;
  - L2: «сироты» — ключ `data.*`, чьего id нет в `GameData`, — ошибка;
  - L3: набор плейсхолдеров `{…}` одинаков во всех языках ключа;
  - L4: плюрал-ключ: RU — `one, few, many, other`; EN — `one, other` [Факт: CLDR plural rules, https://www.unicode.org/cldr/charts/latest/supplemental/language_plural_rules.html , доступ 2026-11-03, по UI_DESIGN_SYSTEM §13.1]; ключ плюральный во всех языках или ни в одном;
  - L5: ключи `ui.*`, на которые ссылается клиент, существуют (статический перечень литералов `L("ui.…")` в клиенте; динамические ключи — только через типизированные помощники `Keys.Node(id)`, `Keys.Enum(value)`).
- **Бюджеты длины (L6):** `mods/core/locale/budgets.yaml` сопоставляет шаблон ключа с ролью §13.2 (`ui.*.btn.*` → 24, вкладка → 16, `data.*.short` → 16 …). Правило: `ceil(1,3 · max(len_en, len_ru)) ≤ бюджет`, иначе должен быть `.short`, удовлетворяющий тому же. Длина — в графемах (`StringInfo.LengthInTextElements`), плейсхолдер считается по ширине, указанной в `budgets.yaml` (по умолчанию 8). Таблица бюджетов — копия §13.2; источник истины — §13.2, тест сверяет, что все роли §13.2 есть в файле.
- **Множественное число:** выбор категории — собственная функция в `Singularity.Localization` по целочисленным правилам CLDR для `en` и `ru` (дробные значения — категория `other`), с тестами на примерах CLDR. Не `tr_n()` Godot [Предп.: базовый `Translation` Godot 4 формы мн. числа не выбирает, это делает только `TranslationPO`; проверить в задаче клиента].
- **Числа и даты:** не `CultureInfo` [Предп.: данные ICU различаются по ОС и версиям, например пробел-разделитель групп; это ломает сравнение скриншотов]. Разделители — строки локали (`fmt.number.decimal`, `fmt.number.group`: RU «12 500,5» с U+00A0, EN «12,500.5»), форматирование через `InvariantCulture` + замена. Игровые даты (год, месяц, день; ADR-0006) — шаблоны `fmt.date.*` и ключи месяцев `date.month.<1..12>.nom / .gen`.
- **Псевдолокаль `xx`:** строится библиотекой из более длинной версии EN/RU: +30%, скобки ⟦…⟧, диакритика; плейсхолдеры не искажаются.

### 4. Ядро не видит текста [Предл.]
- `ModLoader` не читает `locale/`; `GameData` не содержит строк для показа. `anchor.event` переносится из `nodes.yaml` в `data.node.<id>.anchor` (EN+RU); поле `AnchorEvent` удаляется из `NodeTable`, `DataHash` меняется **один раз** (обновить golden и записи provenance одним коммитом). `anchor.source` (id источника `S32`) остаётся — это не текст.
- `StateHash`, сохранения (ADR-0013), replay, golden и `DataHash` от языка и от `locale/` не зависят. Lab `Provenance.DataFiles` хеширует только файлы из `mod.yaml`.
- Session (ADR-0014) отдаёт id и коды; исход команды для игрока — ключ `enum.<reason_enum>.<code>`. `Message` остаётся английской диагностикой для логов и harness, игроку не показывается.
- Lab, ошибки загрузчика, логи — только английский, без локализации.

### 5. Клиент Godot [Предл.]
- **Runtime-загрузчик, не сгенерированные ресурсы.** При запуске клиент вызывает `LocaleCatalog.Load(modDirs)` и для каждого языка строит `Translation` (`Locale = "en"|"ru"|"xx"`, `AddMessage(key, text)` для неплюральных ключей), регистрирует через `TranslationServer.AddTranslation`, запасной язык `en` (`internationalization/locale/fallback`). Причина: моды читаются с диска во время игры, а импорт `.translation`/CSV работает только в редакторе.
- Код клиента берёт строки через тонкий фасад `L` (`L.T(key)`, `L.Plural(key, n)`, `L.Fmt(key, args)`, `L.Number`, `L.Date`); `auto_translate` узлов сцены работает через `Translation`. Смена языка: `TranslationServer.SetLocale` + сигнал фасада; подписи карты пересчитываются (§13.3).
- Это уточняет UI_DESIGN_SYSTEM §14 (строка «Локализация»): `.po` и `tr_n()` заменяются YAML-каталогом и `L.Plural`; встроенная псевдолокализация Godot — только как дополнительная проверка обхода ключей.
- **Миграция таблицы TASK-0070** (отдельная задача после ACCEPTED): скрипт переводит строки CSV в `mods/core/locale/{en,ru}/ui.yaml` и `data.yaml`, переименовывая ключи по §1; CSV и его запись в `project.godot` удаляются; `Names.RuOfActor`, `Stage`, `Profile`, `Short()` заменяются ключами. `Names.CountryOfActor` (связь актора с полигоном Natural Earth) — не текст, а данные карты; остаётся до ADR карты.

### 6. Инструменты [Предл.]
- `tests/Singularity.Localization.Tests`: L1–L6 на `mods/core`, грамматика ключей, CLDR-выбор, форматы, псевдолокаль, тест «удаление `locale/` не меняет `DataHash` и `StateHash` после N тиков».
- `tests/Singularity.Architecture.Tests`: Kernel, Core, Session, Lab не ссылаются на `Singularity.Localization`; в Core нет типов `Translation`/`CultureInfo` для показа.
- `registry.py` не расширяется: это реестр документов, а не данных; проверка — в `dotnet test` (CI на 3 ОС).

### 7. План миграции данных [Предл.]
1. developer: сборка `Singularity.Localization` + тесты (§3, §6); `budgets.yaml` из §13.2.
2. research-scientist: EN и RU имена технических терминов — 61 узел (`name`, `short`, `anchor` из текущего `anchor.event`), класс `jet_fighter`, его слоты и альтернативы, кластеры, дисциплины, типы узлов; историческая терминология с источниками в `docs/research/data/jet-slice/` (термины эпохи 1945–1960: «ТРД», «форсажная камера»). Имена акторов-государств для 1945 (`SUN` = «СССР»/«USSR») — тоже research-scientist.
3. game-designer: `enum.*` (стадии, профили, слова-состояния — по §13.1 п. 8 обе версии вместе) и `ui.*` тексты; ui-art-director: проверка бюджетов и `.short` на скриншотах RU/EN/`xx`.
4. developer: перенос `anchor.event` из Core (§4), миграция клиента (§5), обновление golden `DataHash`.
5. reviewer + test-engineer: дифф и L1–L6 зелёные; критерий готовности — `dotnet test` зелёный, в клиенте нет строк в коде (псевдолокаль `xx` не показывает текста без ⟦…⟧).

## Рассмотренные альтернативы
- **Gettext `.po` (как в §14):** плюс — инструменты переводчиков (Poedit, Weblate), формы мн. числа в Godot через `TranslationPO`. Минус — ключ-как-msgid плохо сочетается с выводом ключей из id; второй формат данных рядом с YAML ADR-0010; моды пришлось бы импортировать в рантайме отдельным парсером. Отклонено; экспорт YAML → `.po` для внешних переводчиков можно добавить позже без смены источника.
- **CSV (как в TASK-0070, Godot import):** нет форм мн. числа, все языки в одной строке → конфликты слияния у мододелов, импорт только в редакторе. Отклонено как формат хранения.
- **Имена прямо в YAML данных (`name: {en: …, ru: …}`):** проще для одного мода, но новый язык требует `patch` каждого объекта, текст попадает в `GameData` и `DataHash`. Отклонено (#83: «через ключи»; ядро без текста).
- **Fluent (Project Fluent) / ICU MessageFormat:** богаче (род, падежи), но нет зрелой .NET-библиотеки без лишних зависимостей [Предп.], а нужны пока плейсхолдеры и CLDR-формы. Отложено; при необходимости — новый ADR.
- **Сгенерированные `.translation` Godot при сборке:** работает для `core`, но не для модов во время игры. Отклонено.
- **`CultureInfo` для чисел и дат:** меньше кода, но вывод зависит от ICU на ОС [Предп.]. Отклонено.

## Последствия
- Плюсы: один источник строк для данных, UI и модов; новый язык — мод без правки данных; ядро и хеши не зависят от языка; ошибки ключей видны при загрузке и в CI.
- Минусы: своя маленькая библиотека (CLDR для 2 языков, форматы); переводчикам нужен YAML, а не Poedit; `DataHash` меняется один раз при переносе `anchor.event`.
- Обязательно: в коде клиента нет видимых строк-литералов; каждый новый вид данных, `enum` для показа и экран приносят ключи EN+RU в DoD; изменение бюджета §13.2 — правка `budgets.yaml` в том же коммите.

## Проверка
- `tests/Singularity.Localization.Tests` (L1–L6, CLDR, форматы, `xx`) в CI на 3 ОС.
- Тест независимости: прогон среза без `locale/` и с ним даёт одинаковые `DataHash` и `StateHash` после N тиков; golden не содержит текста.
- `tests/Singularity.Architecture.Tests`: запрет ссылок Kernel/Core/Session/Lab → `Singularity.Localization`.
- Визуальная регрессия RU/EN/`xx` при 100% и 125% (UI_DESIGN_SYSTEM §13.3, test-engineer).
- Ревью каждого изменения в `mods/*/locale/` (reviewer; лицензия импортированных имён — ADR-0009).
