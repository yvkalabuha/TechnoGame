# Предусловия сценарных событий владения (M1 1.6a/1.6b, M2, ветки #299)

> Статус: ЧЕРНОВИК game-designer для gate, ревизия 2 (учтены вход civilization-systems-designer `docs/reviews/scripted-events-conditions-csd.md` и решения владельца #340–#343). Развивает #339 (уточняет #325, #328, #336, #337/#338). Метки: **[Факт]** (источник), **[Предп.]**, **[Предл.]**. Статусы реестра не менялись.
> Входы: RR-0020 (C01–C34, B01–B134), MS-M1 п. 1.6a/1.6b, S4/S5, ADR-0019 п. 11, ADR-0020, ADR-0011 rev 3, `polities.yaml`/`owners.yaml`.

## Краткий вывод

1. [Предл.] Событие = `(nominal_date, эффект, necessary, sufficient, on_fail)`. Каждый день `ScenarioEventSystem` проверяет события `due` и `deferred`. Условия — только из закрытого словаря атомов (§1): без текста, без RNG (#342), в M1 только структурные (#340).
2. [Предл.] **necessary** = «событие осуществимо» (структура мира); **sufficient** = «историческая причина жива» (в M1 — структурная осуществимость причины: актёр жив и держит набор; типизированные давления — только M2).
3. [Факт, #341] Транзитный провал откладывает событие: 6 мес. (мирные), 12 мес. (деколонизация и `war-related`), максимум 36, затем `skip` (территория не меняется). `alt` — только написанный вручную.
4. [Предл.] Базовый M1 не зависит от seed (#329), поэтому прогон обязан дать «все события `fired`, `lag_days = 0`»; иное — дефект данных. Отклонения — только абляция S4/S5, моды, ветки, M2.
5. [Предл.] Условия для ~153 рабочих строк RR-0020 не пишутся вручную: шаблон `kind` даёт N и дефолтный S; руками — ядро (~15–20 строк) и исключения.

## 1. Словарь условий

Условие — дерево `all | any | not` над атомами, глубина ≤ 3, ≤ 8 атомов. Атом: `AtomId` из реестра C#, типизированные аргументы (polity, set, event, fixed-point число). Атомы читают только `World` (`provinces`, `polities`, `event_status`), целочисленно, без `double`.

| атом | смысл | монотонность | M1 |
|---|---|---|---|
| `exists(P)` | `P` есть в таблице и `alive` | нет | да |
| `absorbed(P)` | `was_alive(P) ∧ ¬alive(P) ∧` нет провинций и территорий. Только состояние мира, не очередь событий (CSD); `was_alive` — колонка `polities`, входит в хэш | **монотонный** | да |
| `dissolved(J)` | совместная политию `J` (ALLIED_*) распущена: флаг `dissolved`, у `J` нет собственных территорий, поэтому `absorbed` не применим | монотонный | да |
| `polity_free(P)` | `P` ещё не создана | монотонный | да |
| `owns(P,set)`, `owns_all`, `owns_any`, `owns_share(P,set,≥ppm)` | `owner` провинций набора = `P` | транзитный | да |
| `controls(P,set)`, `controls_all(Ps,set)` | `controller` = `P` (совместная политию допустима) | транзитный | да |
| `at_war(A,B)` | активная война акторов | транзитный | **false** |
| `war_over(set)` | идёт/завершилась война, чья цель включает набор | транзитный | **false** |
| `fired(E)`, `skipped(E)`, `deferred(E)`, `alt_fired(E,name)` | статус другого события | `fired`, `skipped` монотонны | да |
| `has_institution(P,kind,min_capacity)`, `has_figure(P,tag)` | институт/личность (#322: «ресурс, институт, личность, событие») | транзитный | нет (M2/ветки) |
| `metric(P,name,op,k)`, `has_tech(P,id)` | белый список fixed-point метрик; `metric(school_*)` — только в ветках #299 | транзитный | **запрещены валидатором** |

- `trait(P,id)` не вводится: риск превратить национальную школу в флаг страны, что #322 запрещает (CSD). Резерв имени, ни в M1, ни в M2.
- `absorbed(DEU)` в N не используется: DEU (`government: false`) не поглощена созданием ФРГ/ГДР, пока держит землю за Одером–Нейсе и Берлин (CSD). Польша, потерявшая провинции, не поглощена.
- `set` — именованный список в `mods/core/scenarios/1945/sets.yaml`: id провинций или «территорий без геометрии» (ADR-0011 §226, категория B; карта M1 — только Европа, RR-0020 §3). [Предл.] Новый `territories.yaml`: без него `owns` для неевропейских событий не вычислить (зависимость 1.6b).
- Вычисление в слоте `ScenarioEventSystem` (ADR-0019 п. 11): состояние конца дня `D−1` плюс эффекты событий дня, уже обработанных в порядке `(nominal_date, event_id)`. [Предп.] Точное «до/после merge» — за systems-architect.

## 2. Necessary и sufficient: примеры RR-0020

N = necessary, S = sufficient. В M1 S тривиален (актёр жив и держит набор); S-поля пишутся вручную только для ядра.

| событие | N | S | примечание |
|---|---|---|---|
| **India/Pakistan 1947** (B04, B05; группа `partition_1947`) | `exists(GBR)`, `owns(GBR,british_india)`, `polity_free(IND)`, `polity_free(PAK)`, `not war_over(british_india)` | `exists(GBR)` | M2: S = `consent_to_hold(GBR,set) < k` (типизированное давление, §6) |
| **FRG 1949** (C03) | `exists(DEU)`, `controls_all([USA,GBR,FRA],west_zones)`, `polity_free(FRG)` | державы держат зоны, `not at_war` между ними | |
| **GDR 1949** (C05) | `controls(SUN,east_zone)`, `polity_free(GDR)`; **без** `fired(C03)` | `exists(SUN)` | `soft_after: C03` (только порядок): причина — раздел зон, а не событие ФРГ (CSD); абляция C03 не убивает ГДР |
| **FRG sovereignty 1955** (C09) | `fired(C03)`, `exists(FRG)` | державы держат зоны | права союзников в Берлине — отдельная запись `no_effect` |
| **Saar** (C02 1947; **C02b** — присоединение к ФРГ 1957-01-01) | C02b: `exists(SAAR)`, `controls(FRA,saar)` | `exists(FRG)` | `kind: transfer_owner SAAR→FRG`, `soft_after: C09`; при `skipped(C09)` → `alt: saar_stays_protectorate`; [Факт] дата: RR-0020 стр. 318 (договор 1956-10-27, референдум 1955-10-23, Tag X 1959-07-06); строки C02b в таблице нет (research-scientist) |
| **Austria 1955** (C26; группа `austria_1955_withdrawal` = C27+C28) | C26: `controls_all([USA,GBR,FRA,SUN],austria_zones)`, `exists(AUT)`; C27: `fired(C26)` | `not at_war` внутри четвёрки | `after: C26, lag_days: 90`; один групповой эффект (K9): `controller→AUT` и `dissolved(ALLIED_VIENNA)` в один день, дня «AUT контролирует, ALLIED_VIENNA жива» нет |
| **Algeria 1962** (B56, `war-related`) | `owns(FRA,algeria)`, `polity_free(DZA)`, `not war_over(algeria)` | `exists(FRA)` | M2: событие — запасной путь реальной войны (§6) |
| **Oder–Neisse** (C07 1950, #337, #343) | C07: `exists(GDR)` (подписант), `controls(POL,east_of_oder)`, `owns(DEU,set)` | `exists(POL)` | см. ниже |
| **Taiwan** (C19, #338) | `exists(ROC)`, `controls(ROC,taiwan)`, `owns(JPN,taiwan)` (старт: owner JPN, controller ROC) | `exists(ROC)` | меняет только `owner`; C20 (Сан-Франциско) — `no_effect` |

**Oder–Neisse** [Факт, #343]. `E_ODER_1950` (C07): `owner DEU→POL`, если ГДР существует. `E_ODER_1970` (Варшавский договор, в силе 1972-06-03; дата — RR): запасной путь **и** запись `confirmed`. Его N: `owns(DEU,set)` и `not fired(E_ODER_1950)` ⇒ выполняет смену; если `fired(E_ODER_1950)` и `owns(POL,set)` ⇒ только `confirmed`; если ни то ни другое (набор у третьей политии) ⇒ `skip` с reason, отличимым от `confirmed` (CSD). Калининград (C01): указ 1946 даёт `controller SUN` уже на старте (`no_effect`); owner SUN — по договору 1970 (#337).

## 3. Правила провала и умолчания

Монотонный провал атома (`absorbed`, `dissolved`, `skipped(pred)`, `fired` взаимоисключающего) = **постоянный**; транзитный = **временный**.

| провал | по умолчанию |
|---|---|
| N, постоянный | сразу `alt` (если написан вручную), иначе `skip`; reason `necessary_permanent:<atom>` |
| N, временный | `defer`, окно 6 мес. (12 для деколонизации и `war-related`; макс. 36, #341), проверка каждый день; по истечении `skip` (или `alt`, если написан) |
| N верно, S провалено | `skip` сразу (или `alt`); `defer` только при явном `defer_on_sufficient: true` |
| `after`: предшественник отложен | ждёт; окно считается от `max(nominal_date, effective(pred)+lag)` |
| `after`: предшественник `skipped`/`disabled` | каскад `skip`, reason `pred_skipped:<E>` (или собственный `alt`) |
| `soft_after`: любой исход предшественника | порядок сохраняется (не раньше его `nominal_date` по ключу), каскада нет; зависимое ждёт предшественника только пока тот `deferred` |
| группа | все члены срабатывают в один день или ни один (K9) |

- Окно в календарных месяцах: `deadline = AddMonths(nominal_date, N)` с обрезкой дня, целые дни. [Предп.]
- **Какая дата записывается.** `nominal_date` неизменна; `effective_date` — день применения. Состояние и цепочки используют `effective_date`; журнал и UI показывают обе и `lag_days`. Задним числом не подгоняется.
- **Порядок в дне.** Ключ `(nominal_date, event_id)`: отложенные идут раньше новых событий дня. Предшественник имеет меньший ключ (валидатор).
- **Цепочки.** `after: {event, lag_days}`: не раньше `effective(pred)+lag_days` и своей `nominal_date`. Пример: C26 отложено на 40 дней ⇒ C27/C28 на `effective(C26)+90`, а не на 1955-10-25. `excludes: [E…]` снимает взаимоисключающее событие.
- `alt` — именованный альтернативный эффект, написанный вручную (#341); без него провал даёт `skip`: территория как есть.

## 4. Взаимодействие с гипотезой M1

- [Факт, MS-M1] В M1 нет войны; владение меняют только события, поэтому провалы возможны лишь при абляции, в модах, при ошибках данных и по каскаду. [Предл.] Базовый прогон: все `fired`, `lag_days = 0` (golden-тест); любое `deferred/skipped` в базе — блокирующий дефект.
- **Seed (#329).** [Факт, #340] Условия M1 только структурные; атом `metric` запрещён валидатором. Таблица владения не возмущается. Журнал событий идентичен для всех seeds, S4/S5 измеряют экономику против скриптов (RISK-0011).
- **RNG.** [Факт, #342] Нет ни в условиях, ни в исходах. Разнообразие — от возмущения старта (#329) и науки.
- **Журнал для S4/S5.** `event_status ∈ due | deferred | fired | alt_fired | skipped | disabled` и `effective_date` входят в `StateHash` (L2) и сейв. Запись журнала (производная, не хэшируется): `event_id`, `group`, `nominal_date`, `status`, `effective_date`, `lag_days`, `reason_code` (enum), `failed_atoms[]` (id, значение), `alt_name`, `affected_provinces`. Пишется при первом провале, при смене статуса и при срабатывании, не ежедневно.
- **S4**: события выключены = `disabled`, не `skipped`. **S5**: абляция по 5 группам; [Предл.] рёбра `after` внутри одной группы либо `cross_group`; `soft_after` не создаёт каскада (CSD: иначе абляция C03 молча убила бы ГДР). Отчёт S5 добавляет число `deferred/skipped/alt` и средний `lag_days` по группам.
- «Почему не сработало»: UI собирает причину из `failed_atoms` по шаблонам i18n: «Индия остаётся в составе GBR: не выполнено `owns(GBR, british_india)`».

## 5. Формат данных и валидатор

`mods/core/scenarios/1945/events/*.yaml`, одна запись на событие.

```yaml
- id: C09_FRG_SOVEREIGNTY            # unique; sort key (nominal_date, id)
  kind: transfer_controller          # transfer_owner | transfer_controller | create_polity | dissolve_joint | no_effect
  nominal_date: 1955-05-05           # chosen date ("Rek." RR-0020); signed/in_force/handover in `dates`
  dates: {signed: 1954-10-23, in_force: 1955-05-05, handover: 1955-05-05}
  group: germany_korea               # ablation group (S5), one of 5
  flags: [disputed]                  # war_related | disputed | partial
  effects:
    - {set: germany_west, controller: {from: [USA, GBR, FRA], to: FRG}}
  after: {event: C03_FRG, lag_days: 0}   # hard: cascade skip; `soft_after` = order only
  necessary: {all: [{fired: C03_FRG}, {exists: FRG}]}
  sufficient: {controls_all: {polities: [USA, GBR, FRA], set: germany_west}}
  on_fail: {necessary_temporary: {defer_months: 6, on_expire: skip}, sufficient: skip}
  alt: {}                            # name -> hand-written alternative effects
  source: {rr: RR-0020, row: C09, confidence: H}
```

Проверки валидатора (CI, `tools/` + тест в `tests/`):
1. Схема и закрытый словарь: неизвестный атом, тип аргумента, глубина > 3, свободный текст — ошибка. `metric`, `has_tech`, `has_institution`, `has_figure`, `trait` запрещены в M1-пакете.
2. Ссылки (polity, set, event, `after`, `soft_after`, `excludes`, `alt`) существуют; `polity_free` — только у `create_polity`; `dissolved` — только для `kind: joint`.
3. Граф: нет циклов; ключ предшественника < ключа зависимого; `nominal_date` ≥ дата предшественника + `lag_days` (иначе предупреждение).
4. Эффекты: `from` согласован с `owners.yaml` после всех более ранних событий (статический прогон цепочки); K9: набор не делится между событиями одного дня без порядка; `owner ≠ controller ∧ status ≠ leased` (ADR-0011 §2.12); групповой эффект (Австрия) атомарен.
5. Окна: `defer_months` = 6/12 по классу, ≤ 36; срок не выходит за конец сценария; `alt` не генерируется шаблоном.
6. Группы: рёбра `after` внутри группы абляции или `cross_group`.
7. Динамика (test-engineer): базовый прогон — все `fired`, `lag_days = 0`; на каждое событие синтетически ломается по одному атому N и S, ожидается заявленный путь `defer/skip/alt`; повтор хэша L1/L2.

## 6. Повторное использование: M2 и ветки #299

- **M2 (война).** `at_war`, `war_over` оживают; #336: `war-related` договоры заменяются реальной войной. [Предл.] Событие остаётся запасным: срабатывает, только если `not war_over(set)`; при реальной войне `skipped`, reason `superseded_by_war`. Транзитные атомы дают «отложить, пока идёт война». Для S в M2 [Предл., CSD] не одна `metric(war_exhaustion)`, а типизированные давления: `metropole_capacity`, `consent_to_hold(metropole,set)`, `external_guarantor(P,Q)`; деколонизация: S = `consent_to_hold < k`; Алжир: S = запасной путь войны. Включаются отдельным решением M2 (вместе с `metric` и детерминизмом fixed-point).
- **Ветки #299/#322.** [Предп.: #299/#322 по журналу решений] Ветка = событие с вручную написанным `alt` или другим `nominal_date`/эффектом, с `branch: counterfactual`; при проваленной причине `alt` — готовая развилка вместо `skip`. Школа #322 — производная метрика институтов (`school_H` после EXP-0002, #192); читается через `has_institution`/`has_figure`/`fired(E)`, `metric(school_*)` — только в ветках, не в базе. counterfactual-analyst поставляет `alt`-исходы и «ранние» ветки.
- Версия реестра атомов в манифесте сейва; новый атом = новая версия.
- [Предл.] Открыто для M2 (не M1): у игрока нет рычагов на S (давление через институты и бюджеты) — иначе событие остаётся таблицей; решение game-designer при проектировании M2.

## Разногласия

| # | Тема | game-designer (ред. 1) | CSD | Итог |
|---|---|---|---|---|
| 1 | `absorbed` | «нет преемника в ожидании» | только состояние мира + `dissolved(J)` | принято CSD |
| 2 | ГДР | жёсткое N `fired(C03)` | `soft_after` | принято CSD |
| 3 | `trait` | атом-расширение | `has_institution`/`has_figure` | принято CSD |
| 4 | Saar C02b | нужна строка | нужна строка | согласовано; ждёт research-scientist |

Сильных возражений нет. Остаточный риск (не спор): `soft_after` оставляет возможным порядок «ГДР без ФРГ»; сценарий допустим (абляция), M2 может его осмыслить как отдельную ветку.

## Зависимости

- systems-architect: чтение состояния в слоте, `event_status`/`was_alive`/`dissolved` в `StateHash` и сейве, `territories.yaml`, 1.6b.
- research-scientist: C02b, дата Варшавского договора, `no_effect` C20/C24 после #338, `kind` и класс окна для каждой строки.
- test-engineer: динамические тесты §5 п. 7.
- Владельцу вопросы по этому документу сняты (#340–#343).
