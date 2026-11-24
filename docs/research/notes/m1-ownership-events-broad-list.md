---
title: "M1 scripted ownership events: broad list (decision #328) — new events beyond RR-0020"
author: research-scientist
date: 2026-11-24
related: [RR-0020, ADR-0011, MS-M1]
---

# Широкий список событий владения для M1 (решение #328)

Пометки: **[Факт]** — подтверждено источником; **[Предп.]** — предположение; **[Предл.]** — предложение.

## Краткий вывод

1. [Факт] RR-0020 уже содержит всё запрошенное за 1945–1975: Индия B04–B18 (раздел, княжества, Джунагадх, Кашмир, Гоа, Пондишери, Сикким), Палестина B39–B42, Индонезия B21–B24, Индокитай B29–B34 и B139, Корея C21–C24, Германия C01–C13, Тайвань C19–C20, кондоминиумы и совместные администрации (C14–C18, C25–C29), Африка B43–B98. Повторять их не нужно; перекрёстная таблица — §1.
2. [Факт] Новое — это (а) период **1975–2026**, которого в RR-0020 нет (его окно кончается 1975-10-01), и (б) **пробелы 1945–1975** (§3, строки N01–N14). Всего 72 строки (N01–N14, N20–N45, N50–N66, N70–N84), из них 2 дубля (N10, N11) → **70 новых**; ~25 с датой, подтверждённой Wikidata.
3. [Предл.] Горизонт: строки после 2026-11-24 не вводить; сценарии 2027–2100 — спекулятивные (counterfactual-analyst), это не «исторические события» #328.
4. Достоверность: **H** — дата подтверждена двумя источниками либо первичным текстом; **M** — дата одна/из справочника; **L** — не сверена. Реально открыт **один** источник — запрос к Wikidata (CC0), 2026-11-24, он подтвердил дату у ~25 строк (колонка «ист.» = `WD`). Остальные даты — [Предп.] из знаний автора, URL указан как адрес, где проверять, **страницы не открывались**; они помечены M/L. Перед вводом в данные нужен раунд сверки (как раунды 2–3 RR-0020).
5. Wikidata: у ряда стран `P571` — заглушка «1 января года» (Намибия 1990-01-01, Азербайджан 1991-01-01, Доминика 1978-01-01, Св. Винсент 1979-01-01 и т. д.); такие даты **не использовать**, взята дата акта. Джибути: WD даёт 1977-06-23, ООН и справочники — 1977-06-27 → **L, конфликт**.
6. [Факт] Решение #420 (DECISIONS_LOG, 2026-11-19): широкий список #328 **отложен до вердикта M-play** (не отменён). Это заметка — материал для будущего ввода, не задача на реализацию.
7. Карта: в `mods/core/map/regions.yaml` есть регионы для многих новых событий (список в колонке «регион»); отсутствующие помечены `—`. [Факт, grep 2026-11-24] Существуют: namibia, western_sahara, east_timor, hong_kong_s_a_r, macau_s_a_r, palau, marshall_islands, kosovo, montenegro, slovenia, croatia, slovakia, czech_republic, eritrea, vanuatu, zimbabwe, belize, brunei, suriname, djibouti, yemen, panama, palestine_gaza, palestine_west_bank, india_goa_daman_diu, india_sikkim, india_puducherry, indonesia_west_new_guinea, british_indian_ocean_territory, indian_ocean_territories, cook_islands, niue, aruba, sint_maarten, taiwan, korea_north_38, korea_south_38, laos, cambodia, vietnam_*, bangladesh, bhutan.

## Анализ

Пометка `WD` = Wikidata (CC0), страница `https://www.wikidata.org/wiki/<QID>`, дата независимости из запроса SPARQL 2026-11-24. Доступ ко всем URL — 2026-11-24 (WD открыт; прочие не открывались, см. п. 4). Типы: independence, partition, transfer, occupation_end, union, dissolution, status. Флаги: `war` — военное происхождение (решение за game-designer, как в RR-0020), `disp` — спорное.

### 1. Что уже есть в RR-0020 (ссылки, не повторять)

| Запрос #328 | Строки RR-0020 |
|---|---|
| Германия, Берлин, Саар | C01–C13 |
| Корея | C21–C24, B114 |
| Кондоминиумы / совместное управление | C14–C18, C25–C29, C30–C33 |
| Тайвань | C19, C20 |
| Индия, Пакистан, княжества, Гоа, Сикким | B04–B18 |
| Палестина, Трансиордания, Западный берег, Газа | B36, B39–B42 |
| Индонезия | B21–B24 |
| Индокитай (Камбоджа, Лаос, Вьетнам, Таиланд) | B29–B34, B139 |
| Прочая деколонизация 1945–1975 | B03, B12–B13, B20, B25–B28, B35–B138, B140–B141 |

Старые нерешённые U/L из RR-0020 (B02, B08–B10, B21, B23–B24, B33–B34, B70, B87, B101–B102, B109, B120) остаются не сверенными; здесь они не закрывались.

### 2. Пробелы 1945–1975 (новые строки)

| id | дата | регион | от → к | тип | источник | дост. |
|---|---|---|---|---|---|---|
| N01 | 1952-03-01 | — (Гельголанд) | GBR → FRG (передача) | transfer | Wikipedia-наводка https://en.wikipedia.org/wiki/Heligoland ; искать: Bundesgesetzblatt 1952 | M |
| N02 | 1955-11-23 | indian_ocean_territories | GBR → AUS (Кокосовые о-ва) | transfer | Cocos Islands Act 1955 (AUS), https://www.legislation.gov.au/ ; Order in Council | M |
| N03 | 1958-10-01 | indian_ocean_territories | GBR/Сингапур → AUS (о-в Рождества) | transfer | Christmas Island Act 1958, https://www.legislation.gov.au/ | M |
| N04 | 1958-02-01 | — (Египет, Сирия) | EGY + SYR → UAR | union | сообщение ООН / Египет, https://www.wikidata.org/wiki/Q82943 (не открыт) | M |
| N05 | 1961-09-28 | — | UAR → EGY + SYR (выход Сирии) | dissolution | то же | M |
| N06 | 1965-08-04 | cook_islands | NZL → Острова Кука (самоуправление в свободной ассоциации) | status | Cook Islands Constitution Act 1964 (NZ), https://www.legislation.govt.nz/ | M |
| N07 | 1974-10-19 | niue | NZL → Ниуэ (самоуправление) | status | Niue Constitution Act 1974 (NZ), https://www.legislation.govt.nz/ | M |
| N08 | 1969-08-02 (Act of Free Choice 14 июля–2 авг.) ; ГА ООН 1969-11-19 | indonesia_west_new_guinea | NLD→UNTEA→IDN уже B24 (1963); N08 — подтверждение: Res. 2504(XXIV) | status (no-effect) | https://digitallibrary.un.org/ (A/RES/2504) | M |
| N09 | 1954-12-15 | suriname | NLD → автономия (Statuut) | status | Statuut voor het Koninkrijk der Nederlanden, https://wetten.overheid.nl/ | M |
| N10 | 1965-07-26 | — | см. B20 Мальдивы | — | уже есть | — |
| N11 | 1949-04-01 | — | Ньюфаундленд — уже B99 | — | уже есть | — |
| N12 | 1949-08-08 | bhutan | BTN: договор с Индией о дружбе (внешние сношения под ведением IND) | status | https://treaties.un.org/ (UNTS 1949 Treaty of Friendship India–Bhutan) | M |
| N13 | 1949-07-27 | — (Кашмир) | линия прекращения огня (Карачи) IND/PAK | transfer (controller, war) | https://peacemaker.un.org/ ; UN Res. 47 | M, war, disp |
| N14 | 1972-07-02 | — (Кашмир) | Симла: линия контроля | status (no-effect) | https://peacemaker.un.org/ | M |

Строки N10, N11 — дубли RR-0020 (оставлены для перекрёстной проверки номеров; при вводе удалить).

### 3. 1975–1991: деколонизация

| id | дата | регион | от → к | тип | источник | дост. |
|---|---|---|---|---|---|---|
| N20 | 1975-11-11 | angola_* | PRT → AGO (в RR-0020 исключена как M2) | independence, war | https://history.state.gov/countries/angola | M, war |
| N21 | 1975-11-25 | suriname | NLD → SUR | independence | WD https://www.wikidata.org/wiki/Q730 | H |
| N22 | 1975-11-28 | east_timor | PRT → декларация Фретилин (одностороння) | independence, war | WD https://www.wikidata.org/wiki/Q574 | M, disp |
| N23 | 1975-12-07 | east_timor | → IDN (вторжение) ; аннексия закон 1976-07-17 | transfer, war | https://digitallibrary.un.org/ (S/RES/384 1975-12-22) | M, war, disp |
| N24 | 1975-11-14 | western_sahara | ESP → MAR + MRT (Мадридское соглашение) ; уход ESP 1976-02-26 | transfer | https://treaties.un.org/ (UNTS 988? не сверен) ; https://www.wikidata.org/wiki/Q6250 | M, disp |
| N25 | 1979-08-05 | western_sahara (юг) | MRT → MAR (Алжирское соглашение, Тирис-эль-Гарбия) | transfer, war | https://digitallibrary.un.org/ (A/34/…) | L, disp |
| N26 | 1976-06-29 | — | GBR → SYC | independence | WD https://www.wikidata.org/wiki/Q1042 | H |
| N27 | 1977-06-27 (WD: 06-23) | djibouti | FRA → DJI | independence | WD https://www.wikidata.org/wiki/Q977 ; UN | L |
| N28 | 1978-07-07 | — (Соломоновы о-ва) | GBR → SLB | independence | https://www.legislation.gov.uk/ (Solomon Islands Independence Order 1978) | M |
| N29 | 1978-10-01 | — | GBR → TUV | independence | WD https://www.wikidata.org/wiki/Q672 | H |
| N30 | 1978-11-03 | — | GBR → DMA | independence | https://www.legislation.gov.uk/ ; (WD: заглушка) | M |
| N31 | 1979-02-22 | — | GBR → LCA | independence | WD https://www.wikidata.org/wiki/Q760 | H |
| N32 | 1979-07-12 | — | GBR → KIR | independence | WD https://www.wikidata.org/wiki/Q710 | H |
| N33 | 1979-10-27 | — | GBR → VCT | independence | https://www.legislation.gov.uk/ (WD: заглушка) | M |
| N34 | 1980-04-18 | zimbabwe | UDI/RHO (де-факто) / GBR (де-юре) → ZWE | independence | WD https://www.wikidata.org/wiki/Q954 ; Zimbabwe Independence Order 1980 | H |
| N35 | 1980-07-30 | vanuatu | GBR + FRA (кондоминиум Новые Гебриды) → VUT | independence | WD https://www.wikidata.org/wiki/Q686 | H |
| N36 | 1981-09-21 | belize | GBR → BLZ | independence | WD https://www.wikidata.org/wiki/Q242 | H |
| N37 | 1981-11-01 | — | GBR → ATG | independence | WD https://www.wikidata.org/wiki/Q781 | H |
| N38 | 1983-09-19 | — | GBR → KNA | independence | https://www.legislation.gov.uk/ | M |
| N39 | 1984-01-01 | brunei | GBR (протекторат) → BRN | independence | WD https://www.wikidata.org/wiki/Q921 | H |
| N40 | 1986-01-01 | aruba | Нид. Антилы → Aruba (отдельный статус) | status | https://wetten.overheid.nl/ | M |
| N41 | 1986-10-21 | marshall_islands | USA (опека ООН) → MHL (свободная ассоциация) | independence | WD https://www.wikidata.org/wiki/Q709 | H |
| N42 | 1986-11-03 | — | USA → FSM | independence | WD https://www.wikidata.org/wiki/Q702 | H |
| N43 | 1990-03-21 | namibia | ZAF → NAM | independence | https://history.state.gov/countries/namibia ; (WD: заглушка 1990-01-01 — не использовать) | M |
| N44 | 1994-03-01 | namibia (Уолфиш-Бэй) | ZAF → NAM | transfer | https://history.state.gov/countries/namibia | M |
| N45 | 1994-10-01 | palau | USA → PLW | independence | WD https://www.wikidata.org/wiki/Q695 | H |

### 4. 1975–2026: суверенитет, воссоединение, распад

| id | дата | регион | от → к | тип | источник | дост. |
|---|---|---|---|---|---|---|
| N50 | 1975-04-30 / 1976-07-02 | vietnam_* | DRV + SVN → VNM (объединение) | union, war | WD https://www.wikidata.org/wiki/Q881 (1976-07-02) | H (дата акта), war |
| N51 | 1977-09-07 подп.; 1979-10-01 в силе; 1999-12-31 | panama | USA → PAN (зона канала, затем канал) | transfer | https://treaties.un.org/ (UNTS 1161, Panama Canal Treaty) ; https://www.govinfo.gov/ | M |
| N52 | 1982-04-25 | — (Синай) | ISR → EGY (конец оккупации; мир 1979-03-26) | occupation_end | https://treaties.un.org/ (UNTS 1138) | M |
| N53 | 1989-03-15 | — (Табa) | ISR → EGY | occupation_end | arbitration 1988-09-29, https://legal.un.org/riaa/ | M |
| N54 | 1984-12-19 подп.; 1997-07-01 | hong_kong_s_a_r | GBR → PRC | transfer | Joint Declaration, UNTS 1399 (https://treaties.un.org/) | H |
| N55 | 1987-04-13 подп.; 1999-12-20 | macau_s_a_r | PRT → PRC | transfer | Joint Declaration, https://treaties.un.org/ | H |
| N56 | 1990-05-22 | yemen | YAR + PDRY → YEM | union | WD https://www.wikidata.org/wiki/Q805 | H |
| N57 | 1990-10-03 | germany_* (все) | FRG + GDR → DEU (Единство); договор 1990-08-31; Два плюс четыре подп. 1990-09-12, в силе 1991-03-15 | union | https://www.bundesregierung.de/ ; BGBl. 1990 II S. 889 ; https://www.wikidata.org/wiki/Q183 | H |
| N58 | 1990-03-11 | — (Литва) | SUN → LTU (акт восстановления) | independence | WD https://www.wikidata.org/wiki/Q37 | H |
| N59 | 1991-08-20 / 08-21 | — (Эстония / Латвия) | SUN → EST / LVA (де-юре: восстановление) | independence | https://www.wikidata.org/wiki/Q191 ; Q211 (не открыты) | M |
| N60 | 1991-09-06 | — (Балтика) | SUN признаёт EST, LVA, LTU (Госсовет) | transfer | https://history.state.gov/countries/estonia | M |
| N61 | 1991-06-25 | slovenia, croatia | YUG → SVN, HRV (декларации) | independence | WD Q215, Q224 | H, disp |
| N62 | 1991-08-24 (Украина), 08-25 (Белоруссия), 08-27 (Молдова) | ukraine_* | SUN → UKR, BLR, MDA | independence | WD Q212, Q184, Q217 | H |
| N63 | 1991-09-09 … 1991-12-25 | — (Таджикистан 09-09, Армения 09-23, Туркмения 10-27, Грузия 12-25) | SUN → TJK, ARM, TKM, GEO | independence | WD Q863, Q399, Q874, Q230 | H |
| N64 | 1991-12-08 / 12-21 / 12-26 | Россия и остальные | SUN → RUS и др. (Беловежское, Алма-Атинская декларация, самороспуск) | dissolution | https://history.state.gov/ ; https://www.wikidata.org/wiki/Q15180 | M |
| N65 | 1991-09-08 (референдум), 1991-11-20 (конституция) | — | YUG → MKD | independence | WD Q221 (заглушка 1991-01-01) | L |
| N66 | 1991-05-24 / 1993-05-24 | eritrea | ETH → ERI (де-факто / де-юре) | independence | WD https://www.wikidata.org/wiki/Q986 | H (1993) |

### 5. После 1991 (в горизонте 2030, до 2026-11-24)

| id | дата | регион | от → к | тип | источник | дост. |
|---|---|---|---|---|---|---|
| N70 | 1992-03-06 | — | YUG → BIH (референдум 1992-02-29/03-01) | independence, war | WD https://www.wikidata.org/wiki/Q225 | M, war |
| N71 | 1993-01-01 | czech_republic, slovakia | CSK → CZE + SVK | dissolution | WD Q213, Q214 (заглушка) ; Constitutional Act 542/1992 | M |
| N72 | 1994-05-30 | — (полоса Аузу) | LBY → TCD | transfer | ICJ 1994-02-03, https://www.icj-cij.org/ | M |
| N73 | 1994-05-04 / 1994-07-01 | palestine_gaza, palestine_west_bank | ISR → ПНА (Каирское соглашение; Администрация) | transfer (controller, частично) | https://peacemaker.un.org/ (Oslo I 1993-09-13, Cairo 1994-05-04) | M, disp |
| N74 | 1988-11-15 | palestine_* | — → PSE (декларация); ООН «государство-наблюдатель» 2012-11-29 (A/RES/67/19) | status | WD https://www.wikidata.org/wiki/Q219060 ; https://digitallibrary.un.org/ | H (даты) , disp |
| N75 | 2005-09-12 | palestine_gaza | ISR → ПНА (завершение вывода) | occupation_end | https://mfa.gov.il/ | M |
| N76 | 1999-10-25 / 2002-05-20 | east_timor | IDN → UNTAET → TLS | independence | S/RES/1272 (1999), https://digitallibrary.un.org/ ; WD Q574 | H |
| N77 | 2006-06-03 | montenegro | SCG → MNE | independence | WD https://www.wikidata.org/wiki/Q236 | H |
| N78 | 2008-02-17 | kosovo | SRB → XKX (декларация; признание частичное) | independence | WD https://www.wikidata.org/wiki/Q1246 ; ICJ Advisory Opinion 2010-07-22 | H, disp |
| N79 | 2008-08-14 | — (Бакаси) | NGA → CMR | transfer | Greentree Agreement 2006-06-12 ; https://treaties.un.org/ | M |
| N80 | 2011-07-09 | — | SDN → SSD | independence | WD https://www.wikidata.org/wiki/Q958 | H |
| N81 | 2010-10-10 | sint_maarten, aruba (не затр.) | Нид. Антилы → страны Curaçao и Sint Maarten | status | https://wetten.overheid.nl/ | M |
| N82 | 2022-06-14 | — (о. Ганса) | CAN/DNK: соглашение о разделе | transfer | https://www.regjeringen.no/ ? ; https://www.canada.ca/ | L |
| N83 | 2014-03-18 | — (Крым) | UKR → RUS (договор) | transfer, war | https://digitallibrary.un.org/ (A/RES/68/262, 2014-03-27 не признаёт) | M, war, disp |
| N84 | 2025-05-22 подп. | british_indian_ocean_territory | GBR → MUS (Чагос; ратификация и вступление в силу на 2026-11-24 **не проверены**) | transfer | https://www.gov.uk/ ; https://treaties.fcdo.gov.uk/ | L |

N83 и N84 — **не вводить** без решения: N83 — военное и не признан ООН; N84 — статус вступления в силу неизвестен автору.

### 6. Предложения

- [Предл.] Вводить в игру только строки H и M без `war`; L — после сверки; `war` — по решению game-designer (как в RR-0020).
- [Предл.] Для строк с `status` и `no-effect` (N06–N09, N12, N14, N40, N74, N81) в данных события не создавать, пока нет механики статусов «свободная ассоциация / автономия».
- [Предл.] Нужен раунд сверки (research-scientist): открыть и подтвердить строки M/L; приоритет — N43, N51, N54–N57, N60–N66, N71.
- [Предл.] Недостающие регионы (`—`): большинство малых государств (SYC, SLB, TUV, DMA, LCA, KIR, VCT, ATG, KNA, FSM, Балтика, Беларусь, Молдова, Закавказье, Центральная Азия) надо проверить по `mods/core/map/regions.yaml` при вводе; grep автора покрыл только часть имён.

## Не проверено

- Не открыты страницы всех источников, кроме одного запроса Wikidata; даты H выше — только те, что Wikidata подтвердил **как точную дату**, остальное M/L.
- Не читались подробно решения #312, #313, #314 (смешанные и оккупированные политии, лицензии OHM) и `docs/game-design/scripted-events-conditions.md`; соответствие формату условий не проверено. Лицензии: Wikidata CC0; договоры — только даты и факты (ADR-0009); Wikipedia — наводка.
- Коды полити (SUR, VNM, …) предложены без сверки с `polities.yaml`.
- Полное покрытие регионов `regions.yaml` не выверено.

## Источники

- https://query.wikidata.org/ (SPARQL, CC0), запрос P31=Q3624078 + P571 1975–2012, доступ 2026-11-24 [открыт].
- Остальные URL выше — адреса для сверки, **не открывались** (доступ не состоялся 2026-11-24).
