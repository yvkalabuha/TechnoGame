---
type: research-note
task: TASK-0154
author: research-scientist
accessed: 2026-11-24
---
# Союзы и блоки 1945–2030 для круга покупателей лицензий (TASK-0154)

## Краткий вывод
- [Факт] Готовые академические наборы союзов **нельзя** брать: Correlates of War (Formal Alliances v4.1, 1816–2012) и общие Terms: «not to be used for any commercial activity», передача третьим лицам только с письменного разрешения (https://correlatesofwar.org/data-sets/, 2026-11-24; ранее RR-0005 P17). Страница самого набора лицензии не называет, просит только цитировать (https://correlatesofwar.org/data-sets/formal-alliances/, 2026-11-24). ATOP 5.1 (1815–2018): лицензии на сайте нет (http://www.atopdata.org/, 2026-11-24) = «все права защищены», не использовать. Списки из Википедии — CC BY-SA, не проходят ADR-0009.
- [Факт] Пригодный источник: **Wikidata**, структурированные данные в основном, Property и Lexeme пространствах — CC0 (https://www.wikidata.org/wiki/Wikidata:Licensing, 2026-11-24). Свойство «member of» (P463) с датами начала/конца (P580/P582) через SPARQL (https://query.wikidata.org/). Данные неполны и местами шумные (дубли «Венгрия» и «Венгерская НР», пустые SEATO, CENTO, СЭВ), поэтому это сверка, а не единственный источник.
- [Предл.] Данные собирать вручную из первичных фактов (дата подписания и выхода — факты, не охраняются авторским правом), сверять с Wikidata CC0, в mods/ писать собственный файл с атрибуцией «факты: тексты договоров; сверка: Wikidata CC0».
- [Предл.] «Блок» = эксклюзивное членство страны в одном военно-политическом союзе в данный год (0 или 1 блок). «Друзья» = симметричные пары двусторонних связей вне блока и за его пределами. Круг «свой блок / друзья / все» из дизайна покупает по ним.

## Источники и лицензии
| Источник | Лицензия | Статус | URL, доступ |
|---|---|---|---|
| Wikidata (P463, P580, P582) | CC0 | **разрешён** | https://www.wikidata.org/wiki/Wikidata:Licensing, 2026-11-24 |
| Тексты договоров (НАТО 1949, Варшавский 1955 и др.) | факты дат — не охраняются; текст не копируем | разрешены как факты | первичные договоры |
| COW Formal Alliances v4.1 | нет CC, «not for commercial activity» | **запрещён** (читать для сверки нельзя копировать) | https://correlatesofwar.org/data-sets/, 2026-11-24 |
| ATOP 5.1 | лицензии нет | **запрещён** | http://www.atopdata.org/, 2026-11-24 |
| Википедия (списки) | CC BY-SA | **запрещён** | — |

[Не проверено] Лицензия сайта nato.int на копирование текста; даты берём как факты, текст не берём.

## Блоки (эксклюзивные) — таблица
Пометки: [Факт-W] = подтверждено запросом Wikidata CC0 2026-11-24; [Факт] = общеизвестный факт из первичного договора, **не** сверено через Wikidata (запрос вернул пусто или не делался).

| bloc id | Члены среза и партнёры | С | По | Пометка |
|---|---|---|---|---|
| nato | USA, GBR, FRA | 1949-04-04 | действует | [Факт-W] |
| nato | SWE | 2024-03-07 | действует | [Факт-W] |
| nato | CAN, DNK, NOR, ISL, ITA, BEL, NLD, LUX, PRT (основатели); TUR, GRC | 1949-04-04; TUR, GRC 1952-02-18 | действует | [Факт], подтверждено: nato.int (opened), history.state.gov (opened) |
| nato | DEU (ФРГ) | 1955-05-06 | действует (единая DEU с 1990) | [Факт], исправлено с 05-09: nato.int (opened) |
| nato (особое) | FRA вышла из интегрированного командования | 1966-03-07 | вернулась 2009-04 (членство сохранялось) | [Факт] |
| warsaw | SUN, POL, CSK, HUN, ROU, BGR, DDR | 1955-05-14 (в силе 1955-06-06) | 1991-07-01 (DDR до 1990-09-24) | [Факт-W]; подписание и вступление в силу: Avalon Project, текст договора (opened) |
| warsaw | ALB | 1955-06-04 | 1968-09-13 | [Факт-W] |
| sino_soviet | SUN, PRC (договор о дружбе, союзе и взаимной помощи, 1950-02-14) | 1950-02-14 | практически начало 1960-х (раскол), срок договора 30 лет (т.е. 1980) | [Факт]: подписан 14.02.1950 и срок 30 лет: МИД КНР (opened); Wilson Center blog (opened): раскол «с самого начала 1960-х»; уведомление КНР о непродлении 1979-04-03, окончание срока 1980-04-11 [M, search-snippet: cmcarchive.org (403, не открыт), Wilson Center; ратификация завершена 1950-04-11 → 30 лет; инфобокс Wikipedia/Wilson «1979-02-16» — скорее начало окна уведомления, не используем] |
| anzus | USA, AUS, NZL | 1951-09-01 (San Francisco; в силе 1952-04-29) | действует (NZL приостановлена 1986) | [Факт, H]: текст договора «DONE at San Francisco this first day of September, 1951», сноска к ст. IX: ратификации депонированы 1952-04-29 (https://australianpolitics.com/1951/09/01/anzus-treaty-text.html/, opened 2026-11-24); WP ANZUS opened совпадает; Oxford PIL даёт 1952-05-22 в UNTS-цитате — выброс |
| seato | USA, GBR, FRA, AUS, NZL, PHL, THA, PAK (PAK вышел в 1973 по history.state.gov) | 1954-09-08 | 1977-06-30 | [Факт-W]: Wikidata Q277252 (opened) даёт оба дня; history.state.gov (opened): сентябрь 1954, 1977 |
| cento | GBR, TUR, IRN, IRQ (до 1959), PAK; USA ассоциирована | 1955-02-24 | 1979-03-16 | [Факт-W]: Wikidata Q379850 (opened); выход Ирака 1959-03-24 [M, search-snippet: научная статья Middle Eastern Studies 2020, Wikiwand, 1959 in Iraq; первичный документ не найден; по FRUS 1958–60 v12 d29 договор запрещал выход до 1960-02-24, т.е. выход был односторонним]; переименование в CENTO 1959-08-19 [M] |
| rio | USA + государства Латинской Америки | 1947-09-02 | действует (в силе 1948-12-03) | [Факт, H]: подписан 1947-09-02 в Рио-де-Жанейро, в силе 1948-12-03 (ст. 22), регистрация ООН 1948-12-20: en.wikipedia.org Inter-American_Treaty_of_Reciprocal_Assistance (opened 2026-11-24) + encyclopedia.com (opened); UNTS 21 I-324 PDF — скан, текст не извлечён; Wikidata Q1410645 — только год 1947 |
| brussels_wu | GBR, FRA, BEL, NLD, LUX (Брюссельский 1948-03-17; WEU с 1954, DEU, ITA) | 1948 | 2011 | [Факт-W частично: DEU 1954–2011] |
| dunkirk | GBR, FRA | 1947-03-04 | поглощён Брюсселем 1948 | [Факт] |
| usa_japan | USA, JPN | 1951-09-08 (в силе 1952-04-28); новый договор 1960-01-19 (в силе 1960-06-23) | действует | [Факт]: 1951 — текст «signed September 8, 1951, San Francisco», Avalon Project https://avalon.law.yale.edu/20th_century/japan001.asp (opened, H); 1952-04-28, 1960 — en.wikipedia.org Treaty_of_Mutual_Cooperation_and_Security... (opened, M); mofa.go.jp 403 |
| usa_rok | USA, KOR | 1953-10-01 (в силе 1954-11-17) | действует | [Факт, H]: Avalon https://avalon.law.yale.edu/20th_century/kor001.asp (opened): подписан 1953-10-01 Вашингтон, обмен ратификациями 1954-11-17; «18 ноября» встречается только во вторичных источниках |
| swe | SWE | 1945–2024-03-07 | **вне блока (нейтралитет)**: bloc=none | [Факт-W: вступление 2024] |

## Экономические объединения (не блок, но круг «друзья/торговля»)
| id | Члены среза | С | По | Пометка |
|---|---|---|---|---|
| eec_eu | FRA (основатель EEC, договор Рима 1957-03-25, в силе 1958-01-01); GBR 1973-01-01 – 2020-01-31; SWE 1995-01-01 | см. | см. | [Факт] (даты GBR и SWE: EFTA в W подтверждает выход 1973 и 1995) |
| efta | SWE 1960-05-03 – 1995-01-01; GBR 1960-05-03 – 1973-01-01 | | | [Факт-W] |
| comecon | SUN + восточный блок | 1949-01-25 | 1991-06-28 | [Факт-W]: Wikidata Q191582 (opened) |

Договоры «великой коалиции» (двусторонние, дружба): [Факт, H] англо-советский подписан 1942-05-26 в Лондоне (Иден, Молотов), часть II на 20 лет и далее с уведомлением за 12 месяцев (Avalon https://avalon.law.yale.edu/wwii/brsov42.asp и ibiblio https://www.ibiblio.org/pha/policy/1942/420526a.html, оба opened 2026-11-24); франко-советский подписан 1944-12-10 в Москве (Бидо, Молотов; fr.wikipedia opened, M). Денонсация СССР: [Факт, M] «в мае 1955» после ратификации Парижских соглашений (энциклопедия, search-snippet; fr.wikipedia: Le Monde 1955-04-11 «СССР собирается денонсировать»); день **не подтверждён** — «1955-05-07» в данных НЕ использовать, ставить «1955» (год), fr.wikipedia infobox «1955-03-18» без объяснения [L, конфликт]; англо-советский: подтверждения отдельной денонсации не найдено [L] (FRUS 1952–54 v05p2 d233/d237: ожидалась по аналогии).

## Блок и друзья: чем отличаются
- [Предл.] **bloc** — одно значение на страну и год (enum: nato, warsaw, none, …, плюс свой список союзов). Основан на взаимной оборонительной статье. «Свой блок» = все с тем же bloc в этот год. Выход/вступление (FRA 1966 — всё ещё nato, но `integrated: false`; DEU 1955; SWE 2024) меняет круг в указанный год.
- [Предл.] **friends** — симметричный граф: (а) двусторонние договоры о союзе или сотрудничестве вне блока (GBR–SUN 1942–55, FRA–SUN 1944–55, USA–JPN, USA–KOR, SUN–PRC 1950–60); (б) устойчивые неформальные связи, которые задаём явно (USA–GBR UKUSA 1946/48, SWE–Запад по поставкам технологий); (в) ключевые экономические объединения (EEC, EFTA). Члены своего блока автоматически в «друзьях»; нейтральные (SWE) имеют только друзей.
- [Предл.] «Все» = любой покупатель, кроме страны под санкциями (определяет ECON, не эти данные).
- [Предп.] Для SWE до 2024 «друзья» требуют явного списка, у них нет формального союза; без него SWE не сможет продавать лицензии «своему блоку».

## Формат данных (для разработчика → mods/core/scenarios/1945/alliances.yaml)
```yaml
schema: 1
sources: "Fact: treaty dates; cross-check: Wikidata CC0 (2026-11-24)"
blocs:
  nato:    { members: [ {id: USA, from: 1949}, {id: GBR, from: 1949}, {id: FRA, from: 1949, note: "integrated command left 1966-2009"}, {id: DEU, from: 1955}, {id: SWE, from: 2024} ] }
  warsaw:  { members: [ {id: SUN, from: 1955, to: 1991}, {id: POL, from: 1955, to: 1991} ] }
friends:      # symmetric; year range inclusive
  - { a: GBR, b: SUN, from: 1945, to: 1955, kind: treaty_1942 }
  - { a: FRA, b: SUN, from: 1945, to: 1955, kind: treaty_1944 }
  - { a: USA, b: GBR, from: 1945, kind: ukusa }
  - { a: SUN, b: PRC, from: 1950, to: 1960, kind: treaty_1950 }
```
- [Предл.] Хранить интервалами (год «с–по»), код разворачивает в годовую таблицу; тест: у страны не более одного военного блока в год (экономические объединения отдельным полем).
- [Предп.] Коды стран: ISO-3 проекта, SUN = СССР; для 1991+ потребуется RUS как преемник (вне 5 стран среза).

## Сверка с историей для 5 стран среза
- USA: nato 1949+, rio 1947, anzus 1951, seato 1954–77, usa_japan 1951, usa_rok 1953 [Факт; NATO W]. До 1947: вне блока (друзья: GBR, FRA, SUN — союзники по войне до ~1946).
- GBR: dunkirk 1947 → brussels 1948 → nato 1949, seato 1954, cento 1955; EFTA 1960–73, EEC 1973–2020 [Факт, EFTA W].
- FRA: dunkirk 1947, brussels 1948, nato 1949, seato 1954; EEC 1958+; nato integrated 1966–2009 [Факт].
- SUN: вне блока до 1950; sino_soviet 1950; comecon 1949; warsaw 1955–1991; друзья GBR/FRA до 1955 [Факт].
- SWE: bloc=none 1945–2024-03-07, EFTA 1960–95, EEC/EU 1995+; PfP 1994 как дружеская связь с nato [Факт, PfP не сверено].

## Проверка источников (шаг закрытия, 2026-11-24)
Поле check: opened = страница открыта WebFetch и факт найден; search-snippet = только результат поиска.
| Факт | URL | check |
|---|---|---|
| NATO 1949-04-04; GRC, TUR 1952-02-18; FRG 1955-05-06; Finland 2023-04-04; Sweden 2024-03-07 | https://www.nato.int/cps/en/natohq/topics_52044.htm | opened |
| SEATO: сентябрь 1954, 1977, PAK вышел 1973 | https://history.state.gov/milestones/1953-1960/seato | opened |
| ANZUS 1951, в силе 1952 | https://history.state.gov/milestones/1945-1952/anzus | opened |
| SEATO 1954-09-08..1977-06-30 | https://www.wikidata.org/wiki/Special:EntityData/Q277252.json (CC0) | opened |
| CENTO 1955-02-24..1979-03-16 | https://www.wikidata.org/wiki/Special:EntityData/Q379850.json (CC0) | opened |
| COMECON 1949-01-25..1991-06-28 | https://www.wikidata.org/wiki/Special:EntityData/Q191582.json (CC0) | opened |
| Rio: Wikidata лишь год 1947 | https://www.wikidata.org/wiki/Special:EntityData/Q1410645.json (CC0) | opened |
| Rio в силе 1948-12-03 | https://www.encyclopedia.com/humanities/encyclopedias-almanacs-transcripts-and-maps/rio-treaty-1947 | opened (слабый, пересказ) |
| Warsaw 1955-05-14, в силе 1955-06-06 | https://avalon.law.yale.edu/20th_century/warsaw.asp | opened |
| SUN–PRC 1950-02-14, срок 30 лет | https://www.mfa.gov.cn/eng/zy/wjls/3604_665547/202405/t20240531_11367548.html | opened |
| Раскол SUN–PRC с начала 1960-х | https://www.wilsoncenter.org/blog-post/sino-soviet-alliance-70-years-later | opened |
| Rio 1947-09-02; выход Ирака март 1959 | результаты поиска (fandom, ebsco, iranicaonline и др.) | search-snippet |
Не открылись: oas.org (403), globalsecurity.org (403), Britannica (403), cvce.eu (блок бота), state.gov (407), geoinfo.amu.edu.pl (отказ соединения).

## Не проверено
- Закрыто повторной сверкой 2026-11-24 (раунд 2): Rio (H), ANZUS (H), USA–JPN 1951 (H) и 1960 (M), USA–ROK (H), англо-советский 1942 (H), франко-советский 1944 (M). Остаётся открытым: точный день денонсации 1955 (оба договора: ТОЛЬКО «май 1955», L), день выхода Ирака из CENTO (1959-03-24, M, первичного нет), советско-китайский 1979-04-03 / 1980-04-11 (M, cmcarchive 403). Закрыто 2026-11-24 (раунд 1): SEATO, CENTO, COMECON, Warsaw, NATO, Greece/Turkey, FRG, Rio (вступление в силу), договор 1950 (подпись, срок), Финляндия.
- Период 2000–2030 (CSTO, AUKUS 2021, EU-выход GBR, расширение NATO 1999–2024 по странам) не перечислен подробно: вне 5 стран среза нужны только Финляндия 2023-04-04 и Швеция 2024-03-07 [Факт: nato.int, opened 2026-11-24].
- Лицензия сайта nato.int и текстов договоров не изучалась.
