---
title: "M1 scripted ownership events: broad list (decision #328) — new events beyond RR-0020"
author: research-scientist
date: 2026-11-24
verified: 2026-11-24 (раунд сверки строк M/L)
related: [RR-0020, ADR-0011, MS-M1]
---

# Широкий список событий владения для M1 (решение #328)

Пометки: **[Факт]** — подтверждено источником; **[Предп.]** — предположение; **[Предл.]** — предложение.

## Краткий вывод

1. [Факт] RR-0020 уже содержит всё запрошенное за 1945–1975: Индия B04–B18 (раздел, княжества, Джунагадх, Кашмир, Гоа, Пондишери, Сикким), Палестина B39–B42, Индонезия B21–B24, Индокитай B29–B34 и B139, Корея C21–C24, Германия C01–C13, Тайвань C19–C20, кондоминиумы и совместные администрации (C14–C18, C25–C29), Африка B43–B98. Повторять их не нужно; перекрёстная таблица — §1.
2. [Факт] Новое — это (а) период **1975–2026**, которого в RR-0020 нет (его окно кончается 1975-10-01), и (б) **пробелы 1945–1975** (§3, строки N01–N14). Всего 72 строки (N01–N14, N20–N45, N50–N66, N70–N84), из них 2 дубля (N10, N11) → **70 новых**; ~25 с датой, подтверждённой Wikidata.
3. [Предл.] Горизонт: строки после 2026-11-24 не вводить; сценарии 2027–2100 — спекулятивные (counterfactual-analyst), это не «исторические события» #328.
4. Достоверность: **H** — дата подтверждена двумя источниками либо первичным текстом; **M** — дата одна/из справочника; **L** — не сверена. [Исправлено сверкой 2026-11-24: см. раздел «Результаты сверки»; пометки H/M/L в таблицах обновлены.] Исходно открыт был **один** источник — запрос к Wikidata (CC0), 2026-11-24, он подтвердил дату у ~25 строк (колонка «ист.» = `WD`). Остальные даты — [Предп.] из знаний автора, URL указан как адрес, где проверять, **страницы не открывались**; они помечены M/L. Перед вводом в данные нужен раунд сверки (как раунды 2–3 RR-0020).
5. Wikidata: у ряда стран `P571` — заглушка «1 января года» (Намибия 1990-01-01, Азербайджан 1991-01-01, Доминика 1978-01-01, Св. Винсент 1979-01-01 и т. д.); такие даты **не использовать**, взята дата акта. Джибути: WD даёт 1977-06-23, ООН и справочники — 1977-06-27 → **L, конфликт**.
6. [Факт] Решение #420 (DECISIONS_LOG, 2026-11-19): широкий список #328 **отложен до вердикта M-play** (не отменён). Это заметка — материал для будущего ввода, не задача на реализацию.
7. Карта: в `mods/core/map/regions.yaml` есть регионы для многих новых событий (список в колонке «регион»); отсутствующие помечены `—`. [Факт, grep 2026-11-24] Существуют: namibia, western_sahara, east_timor, hong_kong_s_a_r, macau_s_a_r, palau, marshall_islands, kosovo, montenegro, slovenia, croatia, slovakia, czech_republic, eritrea, vanuatu, zimbabwe, belize, brunei, suriname, djibouti, yemen, panama, palestine_gaza, palestine_west_bank, india_goa_daman_diu, india_sikkim, india_puducherry, indonesia_west_new_guinea, british_indian_ocean_territory, indian_ocean_territories, cook_islands, niue, aruba, sint_maarten, taiwan, korea_north_38, korea_south_38, laos, cambodia, vietnam_*, bangladesh, bhutan.

## Результаты сверки (2026-11-24, раунд 2)

[Факт] Проверены все строки M/L. Шкала: **H** — запись Wikidata (CC0, SPARQL P571/P576 2026-11-24) или первичный документ (history.state.gov, Reagan Library, текст договора), открытые; **M** — один вторичный источник открыт (WP = en.wikipedia.org, только как наводка, ADR-0009: факты-даты без текста) или search-snippet; **L** — конфликт источников или не найдено.
Итог: строки с H — N20–N22, N26, N29, N31–N37, N39, N41–N42, N45, N50–N52, N56, N58, N61–N64, N66, N76–N78, N80 (N54, N57: первичный текст не открыт, оставлены M); L — N12, N25, N53, N55, N65, N70, N72, N84 (+N27 при вводе по WD). Исправлены даты: N09 (12-29 вместо 12-15), N72 (05-10 вместо 05-30), N73 (07-05 вместо 07-01, Арафат в Газе 07-01 не сверен), N25 (аннексия 08-11 вместо 08-14), N55/N53 (конфликт). Не открылись: refworld.org, oas.org, mofa.go.jp, nzhistory.govt.nz, cmcarchive.org (403/404). UNTS PDF Макао и Рио — сканы, текст не извлечён.
Открытые страницы (доступ 2026-11-24, check=opened): https://query.wikidata.org/sparql (запрос P571/P576 по 45 QID, CC0); https://history.state.gov/milestones/1977-1980/panama-canal ; https://www.reaganlibrary.gov/archives/speech/white-house-statement-israeli-withdrawal-sinai-peninsula ; https://jta.org/1988/09/30/archive/egypt-is-awarded-taba-in-final-arbitration ; страницы en.wikipedia.org по соответствующим статьям (Heligoland, Cocos_(Keeling)_Islands, Christmas_Island, United_Arab_Republic, Cook_Islands, Niue, Charter_for_the_Kingdom_of_the_Netherlands, Karachi_Agreement, Simla_Agreement, Madrid_Accords, Tiris_al-Gharbiyya, Western_Sahara_partition_agreement, Solomon_Islands, Dominica, Saint_Vincent_and_the_Grenadines, Saint_Kitts_and_Nevis, Namibia, Walvis_Bay, Sino-British_Joint_Declaration, Sino-Portuguese_Joint_Declaration, German_reunification, Dissolution_of_the_Soviet_Union, Latvia, Estonia, Singing_Revolution, Bosnia_and_Herzegovina, Bosnian_War, Aouzou_Strip, Chad–Libya_relations, Gaza–Jericho_Agreement, Israeli_disengagement_from_Gaza, Bakassi, Dissolution_of_the_Netherlands_Antilles, Hans_Island, Annexation_of_Crimea_by_the_Russian_Federation, Chagos_Archipelago_sovereignty_dispute, Djibouti, UNTAET, Aruba, Act_of_Free_Choice, Dissolution_of_Czechoslovakia, Eritrea, Palestinian_Declaration_of_Independence, Palestinian_National_Authority, Bhutan–India_relations).

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
| N01 | 1952-03-01 | — (Гельголанд) | GBR → FRG (передача) | transfer | Wikipedia-наводка https://en.wikipedia.org/wiki/Heligoland ; искать: Bundesgesetzblatt 1952 | M, сверено (WP opened) |
| N02 | 1955-11-23 | indian_ocean_territories | GBR → AUS (Кокосовые о-ва) | transfer | Cocos Islands Act 1955 (AUS), https://www.legislation.gov.au/ ; Order in Council | M, сверено (WP opened; Cocos Islands Act 1955 royal assent 1955-03-29) |
| N03 | 1958-10-01 | indian_ocean_territories | GBR/Сингапур → AUS (о-в Рождества) | transfer | Christmas Island Act 1958, https://www.legislation.gov.au/ | M, сверено (WP opened) |
| N04 | 1958-02-01 | — (Египет, Сирия) | EGY + SYR → UAR | union | сообщение ООН / Египет, https://www.wikidata.org/wiki/Q82943 (не открыт) | M, сверено (WP opened; в инфобоксе 1958-02-22 — дата референдума) |
| N05 | 1961-09-28 | — | UAR → EGY + SYR (выход Сирии) | dissolution | то же | M, сверено (WP opened) |
| N06 | 1965-08-04 | cook_islands | NZL → Острова Кука (самоуправление в свободной ассоциации) | status | Cook Islands Constitution Act 1964 (NZ), https://www.legislation.govt.nz/ | M, сверено (WP opened) |
| N07 | 1974-10-19 | niue | NZL → Ниуэ (самоуправление) | status | Niue Constitution Act 1974 (NZ), https://www.legislation.govt.nz/ | M, сверено (WP opened; плебисцит 1974-09-03, инфобокс 1974-10-19) |
| N08 | 1969-08-02 (Act of Free Choice 14 июля–2 авг.) ; ГА ООН 1969-11-19 | indonesia_west_new_guinea | NLD→UNTEA→IDN уже B24 (1963); N08 — подтверждение: Res. 2504(XXIV) | status (no-effect) | https://digitallibrary.un.org/ (A/RES/2504) | M (даты Акта 14.07–02.08 сверены WP opened; дата Res. 2504 не найдена) |
| N09 | 1954-12-29 | suriname | NLD → автономия (Statuut) | status | Statuut voor het Koninkrijk der Nederlanden, https://wetten.overheid.nl/ | M, дата исправлена (WP opened: Statuut провозглашён и вступил в силу 1954-12-29; 1954-12-15 — утверждение королевой) |
| N10 | 1965-07-26 | — | см. B20 Мальдивы | — | уже есть | — |
| N11 | 1949-04-01 | — | Ньюфаундленд — уже B99 | — | уже есть | — |
| N12 | 1949-08-08 | bhutan | BTN: договор с Индией о дружбе (внешние сношения под ведением IND) | status | https://treaties.un.org/ (UNTS 1949 Treaty of Friendship India–Bhutan) | L, конфликт 08-08 / 08-09 (WP opened: 08-09 в тексте, 08-08 по Bedjaoui 1991; refworld 403) |
| N13 | 1949-07-27 | — (Кашмир) | линия прекращения огня (Карачи) IND/PAK | transfer (controller, war) | https://peacemaker.un.org/ ; UN Res. 47 | M, сверено (WP opened), war, disp |
| N14 | 1972-07-02 | — (Кашмир) | Симла: линия контроля | status (no-effect) | https://peacemaker.un.org/ | M, сверено (WP opened; подписано технически 07-03 в 00:40, документ датирован 07-02) |

Строки N10, N11 — дубли RR-0020 (оставлены для перекрёстной проверки номеров; при вводе удалить).

### 3. 1975–1991: деколонизация

| id | дата | регион | от → к | тип | источник | дост. |
|---|---|---|---|---|---|---|
| N20 | 1975-11-11 | angola_* | PRT → AGO (в RR-0020 исключена как M2) | independence, war | https://history.state.gov/countries/angola | H (WD, P571 1975-11-11), war |
| N21 | 1975-11-25 | suriname | NLD → SUR | independence | WD https://www.wikidata.org/wiki/Q730 | H (WD) |
| N22 | 1975-11-28 | east_timor | PRT → декларация Фретилин (одностороння) | independence, war | WD https://www.wikidata.org/wiki/Q574 | H (WD 1975-11-28), disp |
| N23 | 1975-12-07 | east_timor | → IDN (вторжение) ; аннексия закон 1976-07-17 | transfer, war | https://digitallibrary.un.org/ (S/RES/384 1975-12-22) | M (WP opened: вторжение 1975-12-07, S/RES/384 1975-12-22, Act of Integration 1976-05-31, провинция 1976-07-17), war, disp |
| N24 | 1975-11-14 | western_sahara | ESP → MAR + MRT (Мадридское соглашение) ; уход ESP 1976-02-26 | transfer | https://treaties.un.org/ (UNTS 988? не сверен) ; https://www.wikidata.org/wiki/Q6250 | M, сверено (WP opened: подписано 1975-11-14, в силе 1975-11-19, уход ESP 1976-02-26), disp |
| N25 | 1979-08-05 (аннексия MAR 08-11) | western_sahara (юг) | MRT → MAR (Алжирское соглашение, Тирис-эль-Гарбия) | transfer, war | https://digitallibrary.un.org/ (A/34/…) | L, конфликт (WP opened: уход MRT 1979-08-05, аннексия MAR 1979-08-11; search-snippet даёт 08-14; «Алжирского соглашения» WP не подтверждает), disp |
| N26 | 1976-06-29 | — | GBR → SYC | independence | WD https://www.wikidata.org/wiki/Q1042 | H (WD) |
| N27 | 1977-06-27 (WD: 06-23) | djibouti | FRA → DJI | independence | WD https://www.wikidata.org/wiki/Q977 ; UN | M (WP opened и ООН-справочники: 1977-06-27; WD 06-23 — не использовать) |
| N28 | 1978-07-07 | — (Соломоновы о-ва) | GBR → SLB | independence | https://www.legislation.gov.uk/ (Solomon Islands Independence Order 1978) | M, сверено (WP opened) |
| N29 | 1978-10-01 | — | GBR → TUV | independence | WD https://www.wikidata.org/wiki/Q672 | H (WD) |
| N30 | 1978-11-03 | — | GBR → DMA | independence | https://www.legislation.gov.uk/ ; (WD: заглушка) | M, сверено (WP opened) |
| N31 | 1979-02-22 | — | GBR → LCA | independence | WD https://www.wikidata.org/wiki/Q760 | H (WD) |
| N32 | 1979-07-12 | — | GBR → KIR | independence | WD https://www.wikidata.org/wiki/Q710 | H (WD) |
| N33 | 1979-10-27 | — | GBR → VCT | independence | https://www.legislation.gov.uk/ (WD: заглушка) | M, сверено (WP opened) |
| N34 | 1980-04-18 | zimbabwe | UDI/RHO (де-факто) / GBR (де-юре) → ZWE | independence | WD https://www.wikidata.org/wiki/Q954 ; Zimbabwe Independence Order 1980 | H (WD) |
| N35 | 1980-07-30 | vanuatu | GBR + FRA (кондоминиум Новые Гебриды) → VUT | independence | WD https://www.wikidata.org/wiki/Q686 | H (WD) |
| N36 | 1981-09-21 | belize | GBR → BLZ | independence | WD https://www.wikidata.org/wiki/Q242 | H (WD) |
| N37 | 1981-11-01 | — | GBR → ATG | independence | WD https://www.wikidata.org/wiki/Q781 | H (WD) |
| N38 | 1983-09-19 | — | GBR → KNA | independence | https://www.legislation.gov.uk/ | M, сверено (WP opened) |
| N39 | 1984-01-01 | brunei | GBR (протекторат) → BRN | independence | WD https://www.wikidata.org/wiki/Q921 | H (WD) |
| N40 | 1986-01-01 | aruba | Нид. Антилы → Aruba (отдельный статус) | status | https://wetten.overheid.nl/ | M, сверено (WP opened) |
| N41 | 1986-10-21 | marshall_islands | USA (опека ООН) → MHL (свободная ассоциация) | independence | WD https://www.wikidata.org/wiki/Q709 | H (WD) |
| N42 | 1986-11-03 | — | USA → FSM | independence | WD https://www.wikidata.org/wiki/Q702 | H (WD) |
| N43 | 1990-03-21 | namibia | ZAF → NAM | independence | https://history.state.gov/countries/namibia ; (WD: заглушка 1990-01-01 — не использовать) | M, сверено (WP opened: 1990-03-21) |
| N44 | 1994-03-01 | namibia (Уолфиш-Бэй) | ZAF → NAM | transfer | https://history.state.gov/countries/namibia | M, сверено (WP opened: 1994-03-01; договор о передаче датирован 1994-02-28) |
| N45 | 1994-10-01 | palau | USA → PLW | independence | WD https://www.wikidata.org/wiki/Q695 | H (WD) |

### 4. 1975–2026: суверенитет, воссоединение, распад

| id | дата | регион | от → к | тип | источник | дост. |
|---|---|---|---|---|---|---|
| N50 | 1975-04-30 / 1976-07-02 | vietnam_* | DRV + SVN → VNM (объединение) | union, war | WD https://www.wikidata.org/wiki/Q881 (1976-07-02) | H (WD: 1976-07-02), war |
| N51 | 1977-09-07 подп.; 1979-10-01 в силе; 1999-12-31 | panama | USA → PAN (зона канала, затем канал) | transfer | https://treaties.un.org/ (UNTS 1161, Panama Canal Treaty) ; https://www.govinfo.gov/ | H: подписание 1977-09-07 и передача 1999-12-31 — history.state.gov (opened, PD); 1979-10-01 в силе — search-snippet (посольство Панамы) |
| N52 | 1982-04-25 | — (Синай) | ISR → EGY (конец оккупации; мир 1979-03-26) | occupation_end | https://treaties.un.org/ (UNTS 1138) | H: вывод завершён 1982-04-25 — Reagan Library, заявление Белого дома (opened); мир 1979-03-26 — WP opened |
| N53 | 1989-03-15? (см. дост.) | — (Табa) | ISR → EGY | occupation_end | arbitration 1988-09-29, https://legal.un.org/riaa/ | L: WP opened — переговоры завершены 1989-02-26, флаг Египта 1989-03-19; дата 03-15 не подтверждена; решение арбитража 1988-09-29 (JTA, косвенно) |
| N54 | 1984-12-19 подп.; 1997-07-01 | hong_kong_s_a_r | GBR → PRC | transfer | Joint Declaration, UNTS 1399 (https://treaties.un.org/) | M: подпись 1984-12-19, передача 1997-07-01 (WP opened); в силе 1985-05-27 (WP противоречит себе: 05-27 / 07-27); UNTS PDF не открыт |
| N55 | 1987-04-13 (или 03-26) подп.; 1999-12-20 | macau_s_a_r | PRT → PRC | transfer | Joint Declaration, https://treaties.un.org/ | L, конфликт: WP opened — подпись 1987-03-26; search-snippet UNTS 25805 и справки — 1987-04-13 (03-26 вероятно парафирование, не сверено); передача 1999-12-20 (WP opened) |
| N56 | 1990-05-22 | yemen | YAR + PDRY → YEM | union | WD https://www.wikidata.org/wiki/Q805 | H (WD) |
| N57 | 1990-10-03 | germany_* (все) | FRG + GDR → DEU (Единство); договор 1990-08-31; Два плюс четыре подп. 1990-09-12, в силе 1991-03-15 | union | https://www.bundesregierung.de/ ; BGBl. 1990 II S. 889 ; https://www.wikidata.org/wiki/Q183 | M: 1990-10-03, 08-31, 09-12, 1991-03-15 (WP opened; первичный текст BGBl не открыт) |
| N58 | 1990-03-11 | — (Литва) | SUN → LTU (акт восстановления) | independence | WD https://www.wikidata.org/wiki/Q37 | H (WD 1990-03-11) |
| N59 | 1991-08-20 / 08-21 | — (Эстония / Латвия) | SUN → EST / LVA (де-юре: восстановление) | independence | https://www.wikidata.org/wiki/Q191 ; Q211 (не открыты) | M: Эстония 1991-08-20 (WP opened), Латвия 1991-08-21 (WP opened; декларация 1990-05-04 — иной акт) |
| N60 | 1991-09-06 | — (Балтика) | SUN признаёт EST, LVA, LTU (Госсовет) | transfer | https://history.state.gov/countries/estonia | M (WP opened: признание СССР 1991-09-06) |
| N61 | 1991-06-25 | slovenia, croatia | YUG → SVN, HRV (декларации) | independence | WD Q215, Q224 | H (WD 1991-06-25, обе), disp |
| N62 | 1991-08-24 (Украина), 08-25 (Белоруссия), 08-27 (Молдова) | ukraine_* | SUN → UKR, BLR, MDA | independence | WD Q212, Q184, Q217 | H (WD: UKR 08-24, BLR 08-25, MDA 08-27) |
| N63 | 1991-09-09 … 1991-12-25 | — (Таджикистан 09-09, Армения 09-23, Туркмения 10-27, Грузия 12-25) | SUN → TJK, ARM, TKM, GEO | independence | WD Q863, Q399, Q874, Q230 | H (WD: TJK 09-09, ARM 09-23, TKM 10-27, GEO 12-25; у Грузии акт 1991-04-09 не сверен) |
| N64 | 1991-12-08 / 12-21 / 12-26 | Россия и остальные | SUN → RUS и др. (Беловежское, Алма-Атинская декларация, самороспуск) | dissolution | https://history.state.gov/ ; https://www.wikidata.org/wiki/Q15180 | H (WP opened: 12-08, 12-21, 12-25, 12-26; WD Q15180 P576 1991-12-26) |
| N65 | 1991-09-08 (референдум), 1991-11-20 (конституция) | — | YUG → MKD | independence | WD Q221 (заглушка 1991-01-01) | L: референдум 1991-09-08 (WP opened); дата декларации и конституции не подтверждены; WD-заглушка 1991-01-01 |
| N66 | 1991-05-24 / 1993-05-24 | eritrea | ETH → ERI (де-факто / де-юре) | independence | WD https://www.wikidata.org/wiki/Q986 | H (WD 1993-05-24; WP opened: де-факто 1991-05-24) |

### 5. После 1991 (в горизонте 2030, до 2026-11-24)

| id | дата | регион | от → к | тип | источник | дост. |
|---|---|---|---|---|---|---|
| N70 | 1992-03-06 | — | YUG → BIH (референдум 1992-02-29/03-01) | independence, war | WD https://www.wikidata.org/wiki/Q225 | L, конфликт: WD 1992-03-06; WP opened — референдум 02-29/03-01, декларация 03-03, признание ЕС/США 04-06, war |
| N71 | 1993-01-01 | czech_republic, slovakia | CSK → CZE + SVK | dissolution | WD Q213, Q214 (заглушка) ; Constitutional Act 542/1992 | M: Constitutional Act 542 принят 1992-11-25, СССР... ČSFR прекращена 1992-12-31, государства с 1993-01-01 (WP opened + WD 1993-01-01) |
| N72 | 1994-05-10 (было 05-30) | — (полоса Аузу) | LBY → TCD | transfer | ICJ 1994-02-03, https://www.icj-cij.org/ | L, конфликт: ICJ 1994-02-03 (WP opened); вывод ливийских войск 1994-04-15…05-10 (WP opened); дата 05-30 не подтверждена |
| N73 | 1994-05-04 / 1994-07-05 (было 07-01) | palestine_gaza, palestine_west_bank | ISR → ПНА (Каирское соглашение; Администрация) | transfer (controller, частично) | https://peacemaker.un.org/ (Oslo I 1993-09-13, Cairo 1994-05-04) | M: Каирское соглашение 1994-05-04 (WP opened); ПНА — WP: 1994-07-05 (инаугурация), прибытие Арафата в Газу 07-01 не сверено, disp |
| N74 | 1988-11-15 | palestine_* | — → PSE (декларация); ООН «государство-наблюдатель» 2012-11-29 (A/RES/67/19) | status | WD https://www.wikidata.org/wiki/Q219060 ; https://digitallibrary.un.org/ | M (WP opened: 1988-11-15; A/RES/67/19 2012-11-29 не открыта), disp |
| N75 | 2005-09-12 | palestine_gaza | ISR → ПНА (завершение вывода) | occupation_end | https://mfa.gov.il/ | M (WP opened: 2005-09-12) |
| N76 | 1999-10-25 / 2002-05-20 | east_timor | IDN → UNTAET → TLS | independence | S/RES/1272 (1999), https://digitallibrary.un.org/ ; WD Q574 | H (WP opened: S/RES/1272 1999-10-25; независимость 2002-05-20) |
| N77 | 2006-06-03 | montenegro | SCG → MNE | independence | WD https://www.wikidata.org/wiki/Q236 | H (WD) |
| N78 | 2008-02-17 | kosovo | SRB → XKX (декларация; признание частичное) | independence | WD https://www.wikidata.org/wiki/Q1246 ; ICJ Advisory Opinion 2010-07-22 | H (WD), disp |
| N79 | 2008-08-14 | — (Бакаси) | NGA → CMR | transfer | Greentree Agreement 2006-06-12 ; https://treaties.un.org/ | M: передача 2008-08-14 (WP opened); Greentree — WP: переговоры 2006-06-13, дата подписания 06-12 не подтверждена |
| N80 | 2011-07-09 | — | SDN → SSD | independence | WD https://www.wikidata.org/wiki/Q958 | H (WD) |
| N81 | 2010-10-10 | sint_maarten, aruba (не затр.) | Нид. Антилы → страны Curaçao и Sint Maarten | status | https://wetten.overheid.nl/ | M, сверено (WP opened) |
| N82 | 2022-06-14 | — (о. Ганса) | CAN/DNK: соглашение о разделе | transfer | https://www.regjeringen.no/ ? ; https://www.canada.ca/ | M, сверено (WP opened: подписано 2022-06-14, ратификация Фолketинг 2023-12-19) |
| N83 | 2014-03-18 | — (Крым) | UKR → RUS (договор) | transfer, war | https://digitallibrary.un.org/ (A/RES/68/262, 2014-03-27 не признаёт) | M (WP opened: договор 2014-03-18; A/RES/68/262 2014-03-27 не открыта), war, disp |
| N84 | 2025-05-22 подп. | british_indian_ocean_territory | GBR → MUS (Чагос; ратификация и вступление в силу на 2026-11-24 **не проверены**) | transfer | https://www.gov.uk/ ; https://treaties.fcdo.gov.uk/ | L: WP opened — подписано 2025-05-22, в силу НЕ вступило; законопроект лёг в апреле 2026, ратификация застопорена. Не вводить |

N83 и N84 — **не вводить** без решения: N83 — военное и не признан ООН; N84 — статус вступления в силу неизвестен автору.

### 6. Предложения

- [Предл.] Вводить в игру только строки H и M без `war`; L — после сверки; `war` — по решению game-designer (как в RR-0020).
- [Предл.] Для строк с `status` и `no-effect` (N06–N09, N12, N14, N40, N74, N81) в данных события не создавать, пока нет механики статусов «свободная ассоциация / автономия».
- [Предл.] Нужен раунд сверки (research-scientist): открыть и подтвердить строки M/L; приоритет — N43, N51, N54–N57, N60–N66, N71.
- [Предл.] Недостающие регионы (`—`): большинство малых государств (SYC, SLB, TUV, DMA, LCA, KIR, VCT, ATG, KNA, FSM, Балтика, Беларусь, Молдова, Закавказье, Центральная Азия) надо проверить по `mods/core/map/regions.yaml` при вводе; grep автора покрыл только часть имён.

## Не проверено

- (Устарело после сверки 2026-11-24, см. выше; остаётся: первичные тексты UNTS/Bundesgesetzblatt по N54, N55, N57 не прочитаны.) Не открыты страницы всех источников, кроме одного запроса Wikidata; даты H выше — только те, что Wikidata подтвердил **как точную дату**, остальное M/L.
- Не читались подробно решения #312, #313, #314 (смешанные и оккупированные политии, лицензии OHM) и `docs/game-design/scripted-events-conditions.md`; соответствие формату условий не проверено. Лицензии: Wikidata CC0; договоры — только даты и факты (ADR-0009); Wikipedia — наводка.
- Коды полити (SUR, VNM, …) предложены без сверки с `polities.yaml`.
- Полное покрытие регионов `regions.yaml` не выверено.

## Источники

- https://query.wikidata.org/ (SPARQL, CC0), запрос P31=Q3624078 + P571 1975–2012, доступ 2026-11-24 [открыт].
- Остальные URL выше — адреса для сверки, **не открывались** (доступ не состоялся 2026-11-24).
