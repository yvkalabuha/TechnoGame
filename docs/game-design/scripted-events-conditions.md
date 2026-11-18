# Предусловия сценарных событий владения (M1 1.6a/1.6b, M2, ветки #299)

> Статус: ЧЕРНОВИК game-designer для gate, **ревизия 3** (учтены critic, advocate, DeepSeek, CSD по ревизии 2; решения #338–#343, #365–#367). Развивает #339. Метки: **[Факт]**, **[Предп.]**, **[Предл.]**. Статусы реестра не менялись.
> Входы: RR-0020, MS-M1 1.6a/1.6b, HYP-0005, ADR-0019 п. 16–18, ADR-0020, ADR-0021 п. 16a, ADR-0011 rev 3, `polities.yaml`/`owners.yaml`, `docs/reviews/scripted-events-conditions-{critic,advocate,deepseek,csd}.md`.

## Краткий вывод

1. [Предл.] Событие = `(nominal_date, effects[], necessary, sufficient, flags)`. Условия — закрытый словарь атомов (§1): без текста, без RNG в базе (#342, #366), в M1 только структурные (#340).
2. [Предл.] **necessary** = осуществимо; **sufficient** = историческая причина жива. Исход провала определяет **только монотонность провалившегося атома**, независимо от N или S: монотонный (необратимый) ⇒ сразу `alt`/`skip`; транзитный ⇒ `defer` с ежедневной проверкой (#341). Флага `defer_on_sufficient` нет.
3. [Факт, #341] Окно: 6 мес. (мирные), 12 мес. (`decolonisation`, `war_related`), максимум 36; затем `skip`. `alt` — только вручную и только с источником.
4. [Предл.] Базовый M1 не зависит от seed (#329); прогон обязан дать «все `fired`, `lag_days = 0`»; иное — дефект данных.
5. [Предл.] Условия для ~153 строк генерируются из `effects`; руками — ядро (~15–20) и исключения.

## 1. Словарь условий

Условие — дерево `all | any | not`, глубина ≤ 3, ≤ 8 атомов; повторяющиеся блоки выносятся в именованные предикаты `conditions.yaml` (макросы атомов без новой семантики; раскрываются при загрузке, глубина и размер считаются после раскрытия). Каждый атом объявляет `reads:` (колонки, которые читает). Читают только `World`, целочисленно.

| атом | смысл | монотонность | M1 |
|---|---|---|---|
| `exists(P)` | `actors.active(P) = 1` | нет | да |
| `was_alive(P)` | **производный**: `active` на t0 или событие-создатель `fired` (ADR-0020, новых колонок нет) | монотонный | да |
| `absorbed(P)` | `was_alive ∧ active = 0 ∧` нет провинций и территорий; `active = 0` ставит эффект `absorb_polity` | монотонный | да |
| `dissolved(J)` | **производный**: событие `dissolve_joint` для `J` в статусе `fired`; совместная политию не актор (ADR-0020 п. 18) | монотонный | да |
| `polity_free(P)` | `P` не создана | монотонный | да |
| `owns(P,set)`, `owns_all`, `owns_any`, `owns_share(P,set,≥ppm)` | `owner` | транзитный | да |
| `controls(P,set)`, `controls_all(Ps,set)` | `controller` | транзитный | да |
| `at_war(A,B)`, `war_over(set)` | война | транзитный | **разрешены, всегда false**: в M1 их N вакуумно истинны (задел M2) |
| `fired(E)`, `skipped(E)`, `deferred(E)`, `alt_fired(E,name)` | статус события; ссылка только на событие с **меньшим ключом** (§5 п. 3) | `fired`, `skipped` монотонны | да |
| `has_institution(P,kind,min_capacity)`, `has_figure(P,tag)` | #322 | транзитный | **только `class: branch`** |
| `metric(P,name,op,k)`, `has_tech(P,id)` | белый список fixed-point | транзитный | **только ветки/M2** |
| `stream_outcome(stream,id)` | результат стохастического потока; имя зарезервировано [#366: ветки #299 могут, скрипты базы нет] | транзитный | **запрещён в `class: base`** |

- `trait(P,id)` не вводится: школа не становится флагом страны (#322). `metric(school_*)` — только в ветках.
- `absorbed(DEU)` в N не используется: DEU (`government: false`) держит землю за Одером–Нейсе и Берлин до 1950/1970.
- Набор (`set`) — в `sets.yaml`: id провинций или «территорий без геометрии» (ADR-0011 §226, категория B; карта M1 — Европа). [Предл.] `territories.yaml` — схема и семантика за systems-architect (1.6b): `owner`/`controller` на территории, участие в `DataHash` (ADR-0020), сейв. `events/*.yaml` и `sets.yaml` входят в `scenario_hash`.
- Время чтения: слот `ScenarioEventSystem`, merge первым (ADR-0019 п. 16–17) — состояние конца `D−1` плюс эффекты уже обработанных событий дня в порядке `(nominal_date, event_id)`.

## 2. Условия, порождаемые из `effects`

[Предл., critic MED] Атомы стороны «откуда» не пишутся вручную: для каждого `effect` с `from` генерируются `owns_all(from.owner, set)` и/или `controls_all(from.controller, set)` (это закрывает «from-side всё ещё владеет», #339). Автор добавляет только атомы цели и причины.

- **Частичное чужое владение** (часть провинций набора принадлежит третьей политии — абляция, мод, M2): эффект применяется ко всему набору или ни к какому, поштучно не применяется. Провал `owns_all` транзитный ⇒ `defer`; по истечении окна `skip`, reason `partial_from:<polity>`.
- Валидатор `owner ≠ controller` ADR-0011 §2.12 проверяет согласованность выводимого статуса «оккупация» (`controller ≠ owner ∧ status ≠ leased`) с полем `status` в таблице старта; расхождение владения и контроля само по себе не ошибка (Тайвань, Саар).

## 3. Примеры RR-0020

N — necessary, S — sufficient. `from`-атомы генерируются (§2) и не повторяются. В M1 S часто пуст (истинно): он пишется вручную только там, где отличается от N; валидатор предупреждает, если S ⊆ N.

| событие | effects | N (кроме `from`) | S |
|---|---|---|---|
| **India/Pakistan 1947** (B04+B05, `atomic_group: partition_1947`) | `create_polity IND`, `create_polity PAK`, `transfer_owner`, `transfer_controller` (из `british_india`) | `exists(GBR)`, `polity_free(IND)`, `polity_free(PAK)`, `not war_over(british_india)` | `exists(GBR)` |
| **FRG 1949** (C03) | `create_polity FRG`, `transfer_owner/controller` западных зон | `polity_free(FRG)` | державы держат зоны, `not at_war` между ними |
| **GDR 1949** (C05) | `create_polity GDR`, `transfer_owner`, `transfer_controller` восточной зоны (DEU→GDR, SUN→GDR по C06) | `polity_free(GDR)`; **без** `fired(C03)`: причина — раздел зон | `exists(SUN)` |
| **FRG sovereignty 1955** (C09) | `transfer_controller` USA/GBR/FRA→FRG | `fired(C03)`, `exists(FRG)` | державы держат зоны |
| **Saar** (C02b, присоединение 1957-01-01) | `transfer_owner SAAR→FRG`, `transfer_controller FRA→FRG`, `absorb_polity SAAR` | `fired(C09)`, `exists(SAAR)` | `exists(FRG)` |
| **Austria 1955** (C27, один шаг) | `transfer_controller` 4 державы→AUT и `dissolve_joint ALLIED_VIENNA` в одной записи: оба эффекта в один день, дня «AUT контролирует, ALLIED_VIENNA жива» нет (K9) | `fired(C26)`, `exists(AUT)` | `not at_war` внутри четвёрки |
| **Algeria 1962** (B56, `war_related`) | `create_polity DZA`, `transfer_owner/controller` | `polity_free(DZA)`, `not war_over(algeria)` | `exists(FRA)` |
| **Taiwan** (C19, #338) | `transfer_owner JPN→ROC` **и** `transfer_controller JPN→ROC` | `exists(ROC)` | `exists(ROC)` |

- **C26** (1955-07-27, вступление Договора в силу) — запись `no_effect` (журнал); `after: C26, lag_days: 90` у C27 даёт 1955-10-25. `after` задаёт только порядок и сдвиг, причинность — через N.
- **Saar.** Старый `alt saar_stays_protectorate` **удалён**: его эффект пуст, значит он равен `skip`; при `skipped(C09)` N провален навсегда ⇒ `skip` с `reason: necessary_permanent:skipped(C09)`. `alt` нужен только при отличающихся эффектах (§6.11). [Предл.] C02b в RR-0020 нет: строка и её `Rek.` — за research-scientist (не [Факт]; даты договора 1956-10-27, референдум 1955-10-23 — RR-0020 стр. 318).
- **Taiwan.** [Факт, #338, RR-0020 C19] На t0 `owner JPN`, `controller JPN`; 1945-10-25 оба поля → ROC. Конфликт: ADR-0011 rev 3 #313 пишет «owner JPN, controller ROC» на старте. Для «все fired» стартовая запись территории `taiwan` должна быть JPN/JPN; иначе генерируемый `controls_all(JPN,taiwan)` на t0 ложен. Правка #313/`territories.yaml` — OPEN (orchestrator, research-scientist). C20 (Сан-Франциско) — `no_effect` (#338).
- **Oder–Neisse** [Факт, #343; #337]. Три записи вместо одной с тремя исходами:
  - **C07** (1950-07-06): `transfer_owner DEU→POL`; N: `exists(GDR)`, `controls(POL,set)`.
  - **C07b_TRANSFER** (Варшавский договор 1970): тот же эффект; `excludes: C07` (ADR-0019 п. 18). Если C07 `fired`, статус `skipped`, reason `excluded_by:C07` (не провал: S5 исключает такие из доли skip). Если C07 `skipped/disabled`, `from` генерируют `owns_all(DEU,set)` ⇒ перевод владения запасным путём.
  - **C07b_CONFIRM** (та же дата): `no_effect`; N: `fired(C07)`, `owns(POL,set)`; пишет `confirmed` в журнал. Если `fired(C07)`, а `owns(POL,set)` ложно (набор у третьей политии), N транзитно провален ⇒ `defer`, затем `skip`: отличимо от `confirmed`.
  - **C01b Калининград** (Московский договор 1970, в силе 1972-06-03): отдельное событие `transfer_owner DEU→SUN`, независимо от C07; указ 1946 (C01) — `no_effect`.
  - [Предп.] Даты C07b/C01b — по строкам RR-0020, которые research-scientist добавит (сейчас строк нет).

## 4. Правила провала

Классификация по атому (монотонный = постоянный; транзитный = временный). Применяется одинаково к N и S.

| провал | по умолчанию |
|---|---|
| постоянный | сразу `alt` (если есть, с источником), иначе `skip`; `reason: permanent:<atom>` |
| временный | `defer`, окно по классу (флаги, §5), проверка каждый день; по истечении — `skip` (или `alt`) |
| `after`: предшественник `due`/`deferred` | ждёт; **окно зависимого начинается с момента разрешения предшественника**: `window_start = max(nominal_date, resolved(pred) + lag_days)`; ожидание окно не расходует |
| `after`: предшественник `fired`/`skipped`/`alt_fired`/`disabled` | `after` не каскадирует; зависимое проверяет N: `fired(pred)` в N провален навсегда ⇒ `skip` (reason: `permanent:skipped(pred)` или `ablation_cascade`, если `disabled`) |

- `lag_days` — уточнение ADR-0019 п. 18 (там только порядок); [Предл.] время хранится в `effective_tick` (ADR-0020), `effective_date` — его календарное представление.
- `deadline = AddMonths(nominal_date, N)` с обрезкой дня (31 янв + 1 мес. = 28/29 фев.); целые дни. Максимум 36 мес.
- Даты: `nominal_date` неизменна и равна **только** дате «Рек.» RR-0020; подписание/вступление/передача лежат в `dates` без влияния. Состояние и цепочки используют `effective_tick`; журнал/UI показывают обе даты и `lag_days`. Задним числом не подгоняется.
- Порядок в дне: ключ `(nominal_date, event_id)`; отложенные раньше новых.
- **Q50 (открыт, владелец).** Вариант A (по #341, как написано): итоговый `skip` окончателен до 2100; после окончания окна повторов нет. Вариант B (предложение advocate): статус `dormant` — после окна событие раз в сутки перепроверяется до конца сценария без RNG; при восстановлении N/S срабатывает с `lag_days` > окна. Цена B: событие может сработать через десятилетия и сменить мир; хэш и журнал те же. Документ считает A действующим; B помечен [Предл.], без изменений в коде до ответа.

### Атомарная группа (`atomic_group`)

[Предл.] Полные правила (critic HIGH, DeepSeek HIGH):
1. Все члены имеют одну `nominal_date` и один класс окна (валидатор).
2. Группа — **одно решение** на позиции минимального ключа члена; N группы = AND по членам, S = AND; окно = окно класса; `alt` — только групповой.
3. Условия оцениваются на **снимке состояния до применения любого члена** (`polity_free(IND)` у B05 истинно, пока B04 не применён); эффекты затем применяются в порядке ключей.
4. Один постоянный провал члена ⇒ вся группа по постоянной ветке (`alt`/`skip`); временный ⇒ вся группа `defer`.
5. Статус и `effective_tick` одинаковы у всех членов; журнал помечает `atomic_group`.
6. Тесты: сломать один член ⇒ не срабатывает ни один; снимок (`polity_free` у обоих); общий `effective_tick`; общий `defer`; групповой `alt`; K9 на границе дня.

## 5. Поля и данные

`class: base | branch` на каждую запись. Поля `ablation_group` и `atomic_group` разные.
- `ablation_group` — enum из HYP-0005: `germany_korea_taiwan_condominia`, `south_asia`, `southeast_asia`, `middle_east`, `other_decolonisation`. Рёбра `after` внутри группы абляции либо явный `cross_group`. C03/C05/C07 лежат в `germany_korea_taiwan_condominia`.
- `atomic_group` — свободный id для K9 (§4).
- Класс окна выводится из флагов `flags: [decolonisation | war_related | disputed | partial]`; `decolonisation`/`war_related` = 12 мес., иначе 6. В `class: base` поля `window_months` нет.

```yaml
- id: C09_FRG_SOVEREIGNTY             # id as in tables; sort key (nominal_date, id)
  class: base                         # base | branch
  nominal_date: 1955-05-05            # only the RR-0020 "Rek." date
  dates: {signed: 1954-10-23, in_force: 1955-05-05, handover: 1955-05-05}
  ablation_group: germany_korea_taiwan_condominia
  atomic_group: null
  flags: [disputed]
  effects:                            # list; ops: transfer_owner, transfer_controller, create_polity,
    - {op: transfer_controller, set: germany_west, from: [USA, GBR, FRA], to: FRG}   # absorb_polity, dissolve_joint, no_effect
  after: {event: C03_FRG, lag_days: 0}  # order and lag only, no cascade
  necessary: {all: [{fired: C03_FRG}, {exists: FRG}]}   # from-atoms generated from effects.from
  sufficient: {ref: west_powers_hold_zones}              # named predicate, conditions.yaml
  excludes: []
  alt: {}                             # name -> effects; base needs source: {...}
  source: {rr: RR-0020, row: C09, confidence: H}
```

[Предл.] Шаблон выводит N по `effects`; `on_fail` не пишется (всё из §4).

Проверки валидатора (CI):
1. Схема и закрытый словарь; глубина/размер после раскрытия предикатов; `class: base` запрещает `has_institution`, `has_figure`, `metric`, `has_tech`, `stream_outcome`, `window_months`.
2. **Taint-проверка** (ADR-0021 п. 16a): объединение `reads:` атомов события сверяется с правилами taint, а не списком имён (список имён отклонён в ADR-0021). Базовое событие не читает колонки, возмущённые #329 или потоками.
3. Ссылки существуют; **каждое событие, на которое ссылаются `fired/skipped/deferred/alt_fired`, `after`, `excludes`, имеет меньший ключ** (иначе порядок в дне зависел бы от вчерашнего статуса); циклов нет.
4. Эффекты: `from` согласован с стартом и с цепочкой более ранних событий (статика); K9: набор не пишется двумя событиями дня без порядка; `atomic_group` по §4 п. 1.
5. `ablation_group` из enum; `after` внутри группы или `cross_group`; `excludes`-цель в той же группе.
6. Каждый `alt` в `class: base` имеет `source` (скрытые степени свободы S5: `alt` меняет каскады); ветки/моды — `window_months ≤ 36` с `window_reason`.
7. **Заморозка:** `data_hash` пакета событий (`events/*.yaml`, `sets.yaml`, `conditions.yaml`, `territories.yaml`) фиксируется в пререгистрации HYP-0005 до первого прогона `full`; расхождение хэша блокирует отчёт. Кто фиксирует — OPEN (orchestrator).
8. Динамика (test-engineer): базовый прогон — все `fired`, `lag_days = 0`; на каждое событие ломается по одному **своему** атому N и (где S отличен от N) S: ожидается `defer`/`skip`/`alt`; повтор хэша L1/L2.

## 6. Взаимодействие с гипотезой M1, журнал, тесты

- [Факт, MS-M1] В M1 нет войны; владение меняют события; провалы — абляция, моды, ошибки данных, каскад. Любое `deferred/skipped` в базовом прогоне — блокирующий дефект (кроме `excluded_by`).
- **Seed (#329).** [Факт, #340] Условия только структурные, ownership-таблица не возмущается ⇒ журнал одинаков для всех seeds. Тест: журнал событий не зависит от seed.
- **RNG.** [Факт, #342, #366] Нет в базе; в ветках допустим `stream_outcome`. #365 (случайные утечки) не касается владения.
- **Статусы** `due | deferred | fired | alt_fired | skipped | disabled` (+`dormant`, если решит Q50=B) и `effective_tick` — в `StateHash` и сейве (колонки ADR-0020 `events.status`, `effective_tick`; новых колонок нет).
- **Журнал** (производный): `event_id`, `ablation_group`, `atomic_group`, `nominal_date`, `status`, `effective_date`, `lag_days`, `reason_code` (enum: `fired`, `permanent`, `deferred_transient`, `expired`, `excluded_by`, `ablation_cascade`, `partial_from`, `confirmed`), `failed_atoms[]` (id, значение), `alt_name`, `affected_provinces`, **`affected_polities`** (from/to, создание/поглощение). Запись при первом провале, смене статуса и срабатывании. `ablation_cascade` в S5 считается отдельно от `skipped` (иначе раздувает долю skip); `excluded_by` — не провал.
- S4: события выключены = `disabled`. S5: абляция по 5 группам; отчёт добавляет число `deferred/skipped/alt` и средний `lag_days` по группе.
- Игровая ценность: «почему не сработало» — по `failed_atoms` и шаблонам i18n; для стохастических событий ветки — шкала словами заранее, число после (#367).
- **Недостающие тесты** [Предл.]: атомарность группы (§4 п. 6); save/load посреди `defer` и между `D−1` и `D`; `AddMonths` (31 янв); потолок 36 мес.; каскад через `cross_group` при абляции; `why_not` по каждому `reason_code`; независимость журнала от seed; `excluded_by` не попадает в долю skip; `partial_from`; тайваньский t0: `controls_all(JPN,taiwan)` истинно.

## 7. Повторное использование: M2 и ветки #299

- **M2.** `at_war`, `war_over` оживают; #336: `war_related` договоры заменяются реальной войной. [Предл.] Событие становится запасным: при реальной войне `skipped`, `reason: superseded_by_war`. Транзитные атомы дают «отложить, пока идёт война». Для S — типизированные давления (CSD): `metropole_capacity`, `consent_to_hold(metropole,set)`, `external_guarantor(P,Q)`. **Условие (advocate):** до заморозки реестра атомов M2 перечислить рычаги игрока (институты, бюджеты), на которые давления реагируют; давление без рычага превращает событие в таблицу.
- **Ветки #299/#322.** `class: branch`: ручной `alt`, иной `nominal_date`/эффект, `stream_outcome` (#366), `has_institution`/`has_figure`, `metric(school_*)`, `window_months` с `window_reason`. Школа — производная метрика институтов (`school_H` после EXP-0002). counterfactual-analyst пишет `alt`.
- Реестр атомов версионируется в манифесте сейва.

## Разногласия

| # | Тема | Позиция A | Позиция B | Итог ред. 3 |
|---|---|---|---|---|
| 1 | Исход провала S | game-designer р.2: skip сразу | critic+advocate: по монотонности атома | принято B; `defer_on_sufficient` удалён |
| 2 | Окна | critic: нет поля `defer_months`; класс из флагов | advocate: `defer_months ≤ 36` + `window_reason` | **выбор GD:** база — только класс из флагов (скрытая степень свободы S5); `window_months` + причина — только `class: branch` и моды. Оба мнения записаны, решает orchestrator/gate |
| 3 | `soft_after` | CSD: нужен отдельно от `after` | advocate: слить в `after` | принято advocate: единый `after` (порядок и сдвиг), причинность через N; согласуется с CSD (абляция C03 не убивает ГДР) |
| 4 | `alt saar_stays_protectorate` | CSD: нужен | GD: эффект пуст = `skip` | `alt` удалён; нужен только при отличающихся эффектах |
| 5 | Итоговый `skip` | #341: окончателен | advocate: `dormant` | **Q50, открыт у владельца**, оба варианта помечены |
| 6 | `trait` | GD р.1: расширение | CSD: `has_institution`/`has_figure` | принято CSD |
| 7 | `absorbed` | GD р.1: «преемник в ожидании» | CSD: только мир + `dissolved` | принято CSD; `was_alive`/`dissolved` выводятся без новых колонок (critic) |
| 8 | Taiwan t0 | ADR-0011 #313: controller ROC | RR-0020 C19: controller JPN→ROC | принято RR-0020; правка #313 — OPEN |

## Зависимости и OPEN

- systems-architect: `territories.yaml` (схема, хэш, сейв), чтение слота, `dormant` при Q50=B, версия реестра атомов.
- research-scientist: строки C02b, C07b, C01b (даты «Рек.»), Taiwan t0 (JPN/JPN), `flags` для каждой строки; пересмотр C20/C24.
- orchestrator: правка #313; кто фиксирует `data_hash` в HYP-0005; решение по разногласию 2.
- test-engineer: тесты §5 п. 8 и §6.
