# Швеция 1945–1990: партнёры по лицензиям и поставкам авиатехники (TASK-0179)

> research-scientist, 2026-11-25. Вопрос: у Швеции в `alliances.yaml` нет списка «друзей» (O-056), поэтому ИИ-Швеция ничего не покупает (`docs/simulation/licence-market-calibration.md`). Какие записи friends обосновать историей.

## Краткий вывод
- [Факт] Формат: `mods/core/scenarios/1945/alliances.yaml`, схема 1; блок `friends:` — симметричные пары `{a, b, from, to}` (годы включительно); Швеция в `blocs` только как `nato` с 2024. Нейтралитет = «нет блока», значит единственный путь купить лицензию — запись в friends.
- [Факт] Главный партнёр 1945–1960-х — Великобритания: Vampire (1946 первая партия 70, 1949 ещё 310 FB.50), Hunter (заказ 120 шт. 29.06.1954), двигатели de Havilland Goblin/Ghost (RM1, RM2 для Saab 29) и Rolls-Royce Avon (RM5, RM6 для Saab 32, Saab 35) по лицензии Svenska Flygmotor.
- [Факт] США — партнёр с 1960-х: Viggen летает на RM8 (лицензионная, глубоко переработанная JT8D Pratt & Whitney) вместо отменённого Rolls-Royce Medway; Gripen — RM12 (производная GE F404). Точные годы лицензионных договоров в источниках не найдены.
- [Предл.] Две записи: `SWE–GBR 1945–1990` и `SWE–USA 1960–1990` (SWE–USA с 1960 — [Предп.], см. «Не проверено»). Франции и СССР в друзья не вносить: закупок авиатехники у них в источниках нет.
- Эффект на калибровку не измерен: после добавления нужно перегнать §8-d (Швеция получит лицензии G1 у GBR уже в 1945–1950-х; это может вернуть сдвиги).

## Как хранятся друзья [Факт, файл прочитан]
```yaml
friends:
  - {a: GBR, b: SUN, from: 1945, to: 1955}   # существующая строка
```
Загрузчик `src/Singularity.Core/Policy/AllianceTable.cs` (по заметке `alliances-1945-2030.md`) отвергает неизвестные ключи; примечаний в строке быть не может — источники только в комментариях и этой заметке. Блок-пересечения для друзей не проверяются так, как для блоков [не проверено в коде].

## Хронология [Факт, по английской Википедии: факты, не текст; страницы под CC BY-SA, для ADR-0009 нужна перепроверка по первоисточнику]
| Год | Событие | Партнёр | Источник |
|---|---|---|---|
| 1946 | Первая партия 70 Vampire FB 1 (J 28A); осенью заказ 3 прототипов Saab 29 | GBR | https://en.wikipedia.org/wiki/De_Havilland_Vampire ; https://en.wikipedia.org/wiki/Saab_29_Tunnan |
| 1948 | 1 сентября первый полёт Saab 29 с Ghost; декабрь — контракт на Saab 32 с Avon | GBR | там же; https://en.wikipedia.org/wiki/Saab_32_Lansen |
| 1949–52 | 310 Vampire FB.50 (J 28B), последний 1952 | GBR | Vampire |
| 1951 | Saab 29 в строю (май); двигатель RM2 = Ghost по лицензии Svenska Flygmotor | GBR | Tunnan; https://en.wikipedia.org/wiki/De_Havilland_Ghost |
| 1954–55 | Заказ 120 Hunter Mk 50 (29.06.1954), поставки с 26.08.1955 | GBR | https://en.wikipedia.org/wiki/Hawker_Hunter |
| 1955–60 | Saab 35 (первый полёт 25.10.1955) на RM6 = Avon; Lansen: RM5 1955, RM6A 1958–60 | GBR | https://en.wikipedia.org/wiki/Saab_35_Draken ; Lansen |
| 1960 | По сообщениям, США тайно обещали военную помощь | USA | https://en.wikipedia.org/wiki/Swedish_neutrality |
| 1967–70 | Viggen: первый полёт 8.02.1967, серия 1970–1990; RM8 из JT8D; Medway (Rolls-Royce) отменён; США помогали с разработкой, блокировали экспорт в Индию | USA (GBR как несостоявшийся) | https://en.wikipedia.org/wiki/Saab_37_Viggen |
| 1988–96 | Gripen: первый полёт 9.12.1988, служба 1996; RM12 — производная GE F404 | USA | https://en.wikipedia.org/wiki/Saab_JAS_39_Gripen ; https://en.wikipedia.org/wiki/Svenska_Flygmotor |

Доступ ко всем страницам: 2026-11-25.

## Предлагаемая запись [Предл.]
```yaml
  # Sweden (neutral, no bloc) bought British jets and built British engines under licence 1946-1960s:
  # Vampire 1946, Ghost/RM2, Avon/RM5-RM6 (Svenska Flygmotor), Hunter 1954. Source: docs/research/notes/sweden-licence-partners.md.
  - {a: SWE, b: GBR, from: 1945, to: 1990}
  # US engines: JT8D-derived RM8 (Viggen, 1967-1990), F404-derived RM12 (Gripen, 1988). Start year 1960 is an assumption.
  - {a: SWE, b: USA, from: 1960, to: 1990}
```
Обоснование границ: [Предп.] `from: 1945` для GBR — первая закупка 1946 (в 1945 купить ещё нечего, но симметричность не вредит); `to: 1990` — рамка задания, фактического разрыва нет (RM12 и Gripen позже — после 1990 действует прежний круг; продлить до 2023, если нужно, решает game-designer/simulation-engineer).

## Не проверено
- Годы подписания лицензионных договоров (Ghost, Avon, JT8D, F404) — источники их не дают.
- Начальный год SWE–USA 1960: опора только на сообщение о секретной гарантии 1960 и дату Viggen; нужен источник по первой закупке американской авиатехники (Швеция покупала и раньше, например Mustang — в загруженных страницах не подтверждено).
- Закупки Meteor и Mustang не подтверждены (страница Meteor не показала шведский раздел).
- Загрузчик: не проверено, допускает ли он друга у страны с `bloc` пустым и двойные записи; прогон `registry`/тестов не выполнялся.
- Влияние на §8-d калибровки не измерено.
- Не использован первоисточник (Flygvapnet, Rolls-Royce, Volvo Aero); Википедия — вторичный источник.
- Коммит: оболочка в этой сессии недоступна, файл не закоммичен.
