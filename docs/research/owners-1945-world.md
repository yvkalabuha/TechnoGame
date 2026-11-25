# Владельцы и контролёры мира вне Европы на 1945-10-01 (T4, ADR-0011) + геометрия секторов Берлина и Вены

Дата: 2026-11-18. Автор: research-scientist. Статус: ЧЕРНОВИК предложения; `owners.yaml` не менялся (импорт делает developer). Не регистровый документ (без frontmatter).
Метки: **[Факт]** с источником, **[Предп.]** предположение, **[Предл.]** предложение.

## Краткий вывод

1. Ниже 6 таблиц, покрывающих все регионы из `mods/core/map/regions.yaml`, которых нет в `owners.yaml` (272 неевропейских + 8 «территорий» без геометрии: Тенда–Бриг, Сазан, Братиславский плацдарм, Эльтен/Зельфкант, Ифни, Танжер, Порт-Артур, Шандернагор; если считать их отдельно от 272 — строки с ними помечены в примечании `terr`). Почти для всех есть однозначный ответ.
2. **Правило протекторатов [Предл.]**: как уже сделано в `polities.yaml` («GBR … and its dependencies»), колония, протекторат, мандат и княжество под сюзеренитетом получают `owner` = метрополия/держава-протектор. Самостоятельные внутри государства (Бутан, Непал, Оман, Афганистан и др.) — свои полития. Единый подход упрощает события 1946–1975 (RR-0020).
3. **Оккупация [Факт, #313]**: Япония — owner JPN, controller USA; Корея — своя полития KOR (без правительства), controller SUN (север) / USA (юг); Тайвань — owner JPN, controller ROC, смена owner на ROC 1945-10-25 (#313, #338); Маньчжурия — owner ROC, controller SUN; Мандатные острова Микронезии — owner JPN, controller USA.
4. **Нужны новые полития [Предл.]**: JPN, ROC, KOR, TIB, MNG, BTN, NPL, AFG, THA, IRN, IRQ, SAU, OMN, SYR, LBN, TUR, EGY, ETH, LBR, ZAF, AUS, NZL, CAN, ISO-коды Латинской Америки, а также `SDN_AE` (kind joint: GBR+EGY), `NHEB` (joint: GBR+FRA, Новые Гебриды), `TANGIER` (международная зона), `IDN` (Республика Индонезия, только как controller).
5. **Регион ≠ одна принадлежность (нарушение #350 «без смешанных регионов»)** — вынесено в FINDINGS: Южный Сахалин и Курилы внутри `russia_primorye`; Андаманские о-ва внутри `india_odisha`; Индонезия (`indonesia_*`: Ява/Борнео/Суматра смешаны); Йемен (Аден и королевство); Иран (оккупация юга и севера); Марокко (испанская зона, Тарфая); Китай (Маньчжурия + Северный Китай в одном регионе); Индокитай (16-я параллель режет `vietnam_central`/Лаос). В таблице взято решение «по большей площади/по столице» и честная пометка.
6. Формат: строки с несколькими `region_id` через запятую относятся к каждому из них одинаково (разворачивает developer). Достоверность: H — суверенные страны и основные колонии, M — переходные и спорные, L — Камбоджа, Лаос, Йемен, Спратли, Индонезия-Риау (подсчёт по долям не делался). Источники: в этой сессии выполнены только поисковые запросы и чтение репозитория; полностью открыты лишь страницы, указанные «открыт». Остальное — по общеизвестным фактам с официальными адресами, которые нужно сверить (критик, T4-проверка). Wikipedia — только наводка.
7. Геометрия секторов Берлина и Вены (§ ниже): готовой PD/CC0/CC BY геометрии **не найдено**. В OHM есть Западный Берлин целиком (2692239, уже в проекте), нет трёх секторов; для Вены нет ничего. Рекомендуемый путь — склейка из границ районов (Bezirke) 1945 года: Берлин — 20 районов, Вена — районы 1945; полигоны Вены — Stadt Wien Open Data (CC BY 4.0), Берлин — Geoportal Berlin (dl-de/by-2.0, требует решения владельца по ADR-0009).

## Правки по замечаниям критика (2026-11-18)

- Коды: Китайская Республика = ROC везде, КНР с 1949-10-01 = PRC, CHN не используется (RR-0020 B01, B02, B113, C34 обновлены).
- Подтверждение «H» снято (→ M) со всех строк оккупации, опирающихся только на K1/K3 (Япония, Корея, Окинава, Вьетнам, Ливия, Сомали, Восточный Китай), пока источник не открыт.
- Полития IDN — только controller: Республика Индонезия на t0 не имеет признанного суверенитета (owner = NLD по K1), поэтому как owner не используется; сохранится ли она как полития после признания (1949-12-27), решает отдельное событие.
- Условия правил: мандаты — owner = управляющая держава (AUS для Науру, PNG, NZL для Самоа, USA/JPN-мандат — см. Микронезия); кондоминиумы — controller = совместная полития (NHEB: GBR, FRA; SDN_AE: GBR, EGY; TANGIER: Комитет контроля).
- Тибет: две модели (owner ROC/controller TIB или owner TIB/controller TIB) — открытый вопрос владельцу; в таблице оставлена первая.
- Тест протектората «держава контролирует внешние сношения → owner = протектор» [Предл., вопрос владельцу Q61], результат: owner = протектор для Бахрейна, Катара, Кувейта, Договорных государств (UAE), Брунея, Малдив, Занзибара, Тонга, Тунисa, Марокко, Лаоса, Камбоджи, Аннама/Тонкина (FRA), княжеств (в т. ч. Сикким); Бутан — BTN (договор 1910 оставляет внутреннее управление, внешние сношения «руководит» Британия — граничный случай, прежнее решение сохранено, подлежит владельцу); Оман — OMN (договор о дружбе, не протекторат); Непал, Афганистан — суверены.
- Новая полития YEM (Королевство Йемен). Полития KOR `government: false`, JPN `government: true`.

## Соглашения и словарь кодов

- Коды политий: существующие из `polities.yaml` (USA GBR FRA SUN SWE DEU AUT POL …, ITA ESP PRT NLD BEL DNK NOR) + предложенные новые (см. п. 4). Для Китая выбран `ROC`: в RR-0020 использованы и `CHN`, и `ROC`; финальный код фиксирует T4/владелец.
- Достоверность H/M/L — как в RR-0020 §0.
- В колонке `note`: `колония`, `протекторат`, `мандат`, `оккупация`, `суверен`, `спор` (flag из RR-0020).
- Источники (ключи). Страницы «открыт» — читались; «поиск» — только по результатам поиска 2026-11-18; «не открывался» — адрес приведён для сверки.
  - **K1** — ГА ООН, рез. 66(I) от 1946-12-14, список несамоуправляющихся территорий (метрополии GBR, FRA, NLD, BEL, AUS, NZL, USA, DNK) — https://undocs.org/A/RES/66(I) (не открывался; П).
  - **K2** — Офис историка Госдепартамента США, «Milestones / Countries» — https://history.state.gov/countries (по RR-0020 раунд 3, ≈25 страниц стран открывались 2026-11-18; PD США).
  - **K3** — Акт о капитуляции Японии 1945-09-02 и Общий приказ № 1 (зоны приёма капитуляции: СССР — Маньчжурия, Сев. Корея (севернее 38-й), Южный Сахалин, Курилы; Чан Кайши — Китай (кроме Маньчжурии), Тайвань, Индокитай севернее 16-й; Маунтбеттен — ЮВА; Макартур/Нимиц/Австралия — Тихий океан) — https://avalon.law.yale.edu/wwii/j6.asp (не открывался; П).
  - **K4** — Потсдамский протокол 1945-08-02 — https://avalon.law.yale.edu/20th_century/decade17.asp (не открывался; П).
  - **K5** — FRUS 1945 vol. VIII (Танжер): https://history.state.gov/historicaldocuments/frus1945v08/d579 и /d583 (поиск 2026-11-18; PD) и соглашение Париж 1945-08-31, UK TS 24 (1946): https://treaties.fcdo.gov.uk/data/Library2/pdf/1946-TS0024.pdf (поиск; OGL): временное управление зоной с 1945-10-11, Испания ушла 1945-10-11.
  - **K6** — RR-0020 (`docs/research/RR-0020-scripted-ownership-events.md`): строки C19, C34, B01–B138 (даты событий после 1945-10-01 подтверждают состояние «до»).
  - **K7** — DECISIONS_LOG #313, #337, #338, #349, #350 (`docs/decisions/DECISIONS_LOG.md`).
  - **K8** — Лига Наций, реестр мандатов (A/B/C), https://www.un.org/dppa/decolonization/en/history/trusteeship-council (не открывался; П) — о мандатах Африки, Ближнего Востока, Океании.
  - **K9** — МИД Австралии/Австралийский военный мемориал о приёме капитуляции в Борнео, Тиморе, Науру, Новой Гвинее (AWM, https://www.awm.gov.au/ — не открывался; В).
  - **K10** — Imperial War Museum, film/collections о капитуляции Андаманских о-вов 1945-10-07..09 (поиск; В).
  - **K11** — Национальный архив США, Cairo Declaration 1943 и Каирская декларация; Ялтинское соглашение по Дальнему Востоку 1945-02-11, FRUS Malta/Yalta: https://history.state.gov/historicaldocuments/frus1945Malta (не открывался; PD).
  - **K12** — Taipei Times 2013/2017 (как в RR-0020 C19; поиск; В).

## Таблица A. Азия

| region_id | owner | controller | source | confidence | note |
|---|---|---|---|---|---|
| afghanistan | AFG | AFG | K2 | H | суверен |
| bangladesh | GBR | GBR | K1, K6 B04–B07 | H | Британская Индия (Бенгалия, Ассам-Силхет); до 1947-08-15. Мятежей и военной администрации нет |
| bhutan | BTN | BTN | K2 | M | суверен; договор Пунакха 1910: Британия «направляет» внешнюю политику. [Предл.] BTN, не GBR |
| brunei | GBR | AUS | K1, check S2 (TASK-0166) | M | протекторат; британская военная администрация Борнео 1945-09-12…1946-07-01 силами Австралии (как malaysia_borneo); TASK-0184 |
| cambodia | FRA | JPN | K3, K2 | L | Френч. Индокитай; южнее 16-й параллели капитуляцию принимали британцы (SEAC). [Предп.] на t0 controller JPN (японцы на месте), GBR с ~1945-10-09 (британцы в Пномпене). Смешан с Таиландом: Баттамбанг и Сиемреап были под THA (с 1941), возврат THA → FRA 1946-11-17 (соглашение; передача — 1946-12). Событие смены нужно добавить в RR-0020 |
| china_guangxi | ROC | ROC | K3, K2 | M | Вост./Юж. Китай: Шанхай, Гуанчжоу (Гуандун, Гуанси, Хайнань, Паратель); приём капитуляции Чан Кайши; суверен |
| china_heilongjiang | ROC | SUN | K3, K4, K11 | M | Маньчжурия (Хэйлунцзян, Цзилинь, Ляонин): оккупация Красной Армией с авг. 1945, вывод к 1946-05-03; Пекин, Тяньцзинь, Хэбэй, Шаньси — в одном регионе, там ROC. [Предл.] «по площади» SUN; нужен разрез, см. FINDINGS |
| china_inner_mongol | ROC | ROC | K2, K3 | L | Внутренняя Монголия, Ганьсу, Цинхай, Нинся: осенью 1945 советские и монгольские войска были в Чахаре, Жэхэ и Синъане (Маньчжурская операция), КПК — локально; источник для вывода к 1945-10-01 не открыт, поэтому L; [Предп.] ROC/ROC |
| china_sichuan | ROC | ROC | K2, K3 | H | Сычуань, Юньнань, Гуйчжоу, Хэнань, Хубэй, Шэньси, Шаньдун; КПК контролирует часть Шаньдуна и Шэньси, но центр ROC |
| china_tibet | ROC | TIB | K2, K11 | M | де-факто независимый Тибет (изгнание китайского представителя 1949); де-юре сюзеренитет ROC признаёт большинство держав. [Предл.] owner ROC, controller TIB; решение игровое (game-designer/владелец) |
| china_xinjiang | ROC | ROC | K2 | M | Восточно-Туркестанская республика с 1944-11 на севере (Или, Тарбагатай, Алтай) — меньшая часть; спор |
| hong_kong_s_a_r | GBR | GBR | K1, K3 | H | колония; британское управление с 1945-08-30, BMA до 1946-05-01 |
| indonesia_kalimantan_timur | NLD | AUS | K3, K9 | M | Борнео (Калимантан), Сулавеси, Малые Зондские, Молуккские, Бали: приём капитуляции австралийскими силами; Центр./Вост. Ява (Республика) в том же регионе. [Предп.] controller AUS по площади; смешанный регион |
| indonesia_riau | NLD | IDN | K3, K9 | L | Суматра + Бантен/Зап. Ява: Республика объявлена 1945-08-17; британские части вошли в Батавию 1945-09-29; controller IDN — реальное управление Суматры; смешанный регион |
| indonesia_west_new_guinea | NLD | NLD | K1, K9 | M | Голландия сохраняла Мерауке; администрация NICA/ANGAU, к 1945-10 Нидерланды |
| iran_esfahan | IRN | IRN | K2 | M | суверен, но Север (Гилян, Мазендеран, Азербайджан, Гулистан, Хорасан) под СССР, центр (Тегеран) — британские и советские войска (Тегеранская декларация 1943, вывод к 1946-03-02). Автономный Азербайджан — с 1945-12-12 (после старта) |
| iran_kerman | IRN | IRN | K2 | M | южный/восточный Иран: британские части на юге, вывод 1946-03 |
| japan | JPN | USA | K3, K4, K7 #313 | M | оккупация SCAP; #313; полития JPN с `government: true` |
| japan_okinawa | JPN | USA | K3 | M | военное управление США с 1945-04 (Окинава); owner JPN до договора (1951-09-08, 1952-04-28), далее потенциальные события |
| korea_north_38 | KOR | SUN | K3, K7 #313 | M | полития KOR с `government: false`, оккупация севернее 38-й параллели |
| korea_south_38 | KOR | USA | K3, K7 #313 | M | то же, южнее 38-й; военная администрация армии США с 1945-09-08 |
| kuwait | GBR | GBR | K1 | M | протекторат (договор 1899); [Предл.] owner GBR по правилу п. 2 |
| kyrgyzstan | SUN | SUN | — | H | союзная республика |
| laos | FRA | ROC | K3, K2 | L | протекторат Франции; Лаос севернее 16-й параллели — китайская оккупация, южнее — британская; Лао Исара с 1945-10-12 (после старта). Смешан с Таиландом: Саяборы и правобережье Тямпасака под THA с 1941, возврат THA → FRA 1946-11-17/12 (как Камбоджа). [Предп.] ROC по большей части; L |
| macau_s_a_r | PRT | PRT | K1 | H | колония; нейтралитет в войне, японское давление, но администрация португальская |
| malaysia | GBR | GBR | K1, K3 | H | Малайя: Федерированные/Нефедерированные штаты и Straits; BMA с 1945-09-12; протектораты по правилу п. 2 |
| malaysia_borneo | GBR | AUS | K3, K9 | M | Лабуан, Сабах (Северное Борнео, Chartered Company), Саравак (раджи Брук): Борнео приняли австралийцы; BMA с 1945-10, Саравак — к британской Короне 1946-07-01 |
| maldives | GBR | GBR | K1 | M | протекторат с 1887; [Предл.] GBR |
| mongolia_govi_altay | MNG | MNG | K6 C34, #550 | M | МНР де-факто независима с 1921; ROC признала независимость 1946-01-05 [Факт: FRUS 1946 vol. X, doc. 961, https://history.state.gov/historicaldocuments/frus1946v10/d961, дата обращения 2026-11-25: "the notification of Chinese recognition of independence was originally made on January 5, 1946"]. На 1945-10-01 owner MNG по решению владельца #550 (де-факто, не де-юре) |
| mongolia_mn_govi | MNG | MNG | K6 C34, #550 | M | то же |
| myanmar | GBR | GBR | K1, K3 | H | Британская Бирма, военная администрация до 1945-10-16 |
| nepal | NPL | NPL | K2 | H | суверен |
| pakistan | GBR | GBR | K6 B04 | H | Британская Индия (Северо-Запад, Синд, Белуджистан, Западный Пенджаб); независимость 1947-08-15 |
| philippines | USA | USA | K1, K6 B03 | H | Содружество Филиппин (США), правительство Осменья восстановлено 1945-02-27; независимость 1946-07-04 |
| singapore | GBR | GBR | K1, K3 | H | BMA с 1945-09-12 |
| siachen_glacier | GBR | GBR | K6 B09 | M | часть княжества Джамму и Кашмир, под британским верховенством |
| spratly_is | — (#401) | JPN | K3 | L | Спратли: Франция заявляла права в 1933, Япония аннексировала 1939; гарнизоны японцев до репатриации; [Предп.] owner FRA, controller JPN; неясный статус — на согласование владельцем |
| sri_lanka | GBR | GBR | K1, K6 B13 | H | колония Цейлон |
| taiwan | JPN | ROC | K7 #313, #338, K6 C19 | M | owner JPN до церемонии 1945-10-25 (событие меняет owner на ROC; #338); controller ROC на t0 — конвенция решения владельца #313, а не установленный факт на 1945-10-01 |
| tajikistan, turkmenistan, uzbekistan | SUN | SUN | — | H | союзные республики (по одной строке в YAML) |
| thailand | THA | THA | K2 | H | суверен; британские индийские части присутствуют, оккупации нет |
| vietnam_central | FRA | ROC | K3, K2 | M | Аннам (Хюэ): китайская оккупация севернее 16-й параллели; ДРВ управляет де-факто с 1945-09-02. Регион пересекает 16-ю параллель (Куангнам-Север, Куангчи-Юг…) |
| vietnam_north | FRA | ROC | K3, K2 | M | Тонкин: китайская оккупация (Лу Хань), ДРВ де-факто. Каждый шаг владельца — спорный; [Предл.] controller ROC |
| vietnam_south_16 | FRA | GBR | K3, K2 | M | Кохинхина и юг: британская оккупация (генерал Грейси с 1945-09-12), французская администрация восстанавливается с 1945-09-23; передача французам 1946-01..03 |
| yemen | YEM | YEM | K1, K2 | L | Регион смешан: Королевство Йемен (Муттавакилит, суверен YEM) на севере и Аден + протектораты (GBR) юга/востока. [Предп., по решению оркестратора] YEM/YEM; лучше разрез по линии границы 1934 (юг: GBR/GBR). Новая полития YEM |
| qatar, united_arab_emirates | GBR | GBR | K1 | M | протектораты (Договорные государства) |
| bahrain | GBR | GBR | K1 | M | протекторат |
| oman | OMN | OMN | K2 | M | Маскат и Оман: формально независимый султанат, британский политический контроль |
| saudi_arabia_al_madinah, saudi_arabia_ash_sharqiyah | SAU | SAU | K2 | H | суверен |
| iraq | IRQ | IRQ | K2 | H | суверен; британские базы по договору 1930 |
| israel, palestine_gaza, palestine_west_bank | GBR | GBR | K1, K8 | H | Подмандатная Палестина (Британский мандат); регионы по линии раздела 1947-11-29 |
| jordan | GBR | GBR | K1, K8 | M | Транс-Иордания под британским мандатом; независимость по договору 1946-03-22, королевство 1946-05-25; [Предл.] owner GBR |
| lebanon, syria | LBN / SYR | LBN / SYR | K2 | M | независимы с 1943/44; французские и британские части выведены 1946-04..12, Левантийские «специальные войска» переданы 1945; [Предл.] суверен, controller своё |
| turkey | TUR | TUR | K2 | H | суверен |
| armenia, azerbaijan, georgia | SUN | SUN | — | H | союзные республики |
| kazakhstan_aqt_be, kazakhstan_mangghystau, kazakhstan_qaraghandy | SUN | SUN | — | H | союзная республика |
| baykonur_cosmodrome | SUN | SUN | — | H | космодрома нет; территория Казахской ССР |
| russia_irkutsk (Тува) | SUN | SUN | K2 | H | Тува вошла в СССР 1944-10-11 |
| russia_khabarovsk, russia_krasnoyarsk, russia_sakha_yakutia, russia_yamal_nenets | SUN | SUN | — | H | |
| russia_primorye | SUN | SUN | K3, K4 | M | регион содержит провинцию `sakhalin` (вся область, 85 676 км²): Южный Сахалин (Карафуто) и Курилы на t0 (1945-10-01) — JPN/SUN (оккупация с 1945-08/09); одно событие передачи owner JPN → SUN на 1946-02-02 (RR-0020 B107, указ о Южно-Сахалинской области), дата 1945-11-02 не используется. Смешанный регион — см. FINDINGS |
| port_arthur (terr) | ROC | SUN | K11, K6 B01–B02 | H | Сино-советский договор 1945-08-14: совместная база Порт-Артур (30 лет); Красная Армия с 1945-08; owner ROC, controller SUN |

## Таблица B. Океания, Тихий океан, Австралазия

| region_id | owner | controller | source | confidence | note |
|---|---|---|---|---|---|
| american_samoa | USA | USA | K1 | H | |
| ashmore_and_cartier_islands | AUS | AUS | K1 | M | британские владения под управлением Австралии с 1931/1934; [Предп.] AUS |
| australia_queensland, australia_south_australia, australia_victoria, australia_western_australia | AUS | AUS | K1 | H | |
| cook_islands | NZL | NZL | K1 | H | |
| coral_sea_islands | AUS | AUS | K1 | L | юридически безхозные до 1969; [Предп.] AUS |
| fiji | GBR | GBR | K1 | H | колония |
| federated_states_of_micronesia | JPN | USA | K3, K8 | M | правило мандатов: owner = управляющая держава мандата (Япония, мандат Лиги Наций 1920, K8), занятие США 1944–45 → controller USA; Доверительная территория США с 1947-04-02. Согласуется с #313 (owner до передачи остаётся у прежней державы) |
| guam | USA | USA | K1 | H | военная администрация ВМС США (отвоёван 1944-08) |
| heard_island_and_mcdonald_islands | GBR | GBR | K1 | M | британская территория; передача Австралии 1947-12-26 (потенциальное событие) |
| indian_ocean_territories | GBR | GBR | K1 | H | о. Рождества и Кокосовые: колония Straits Settlements; японская оккупация до 1945-09; передача Австралии 1958 |
| kiribati | GBR | GBR | K1 | H | колония Гилберта и Эллис (острова Гилберта японцами оккупированы 1941–43) |
| marshall_islands | JPN | USA | K3, K8 | M | то же, что FSM |
| nauru | AUS | AUS | K8, K9 | M | единое правило мандатов: owner = управляющая держава (как PNG, Samoa); мандат Британской империи с управлением Австралии; японская оккупация закончена капитуляцией 1945-09-13 |
| new_caledonia | FRA | FRA | K1 | H | |
| new_zealand | NZL | NZL | K1 | H | |
| niue | NZL | NZL | K1 | H | |
| norfolk_island | AUS | AUS | K1 | H | |
| northern_mariana_islands | JPN | USA | K3, K8 | M | то же, что FSM; не путать с Гуамом |
| palau | JPN | USA | K3, K8 | M | то же |
| papua_new_guinea | AUS | AUS | K1, K9 | H | Папуа (владение) + Новая Гвинея (мандат); военная администрация ANGAU до 1945-10-24, затем гражданская |
| pitcairn_islands | GBR | GBR | K1 | H | |
| samoa | NZL | NZL | K1, K8 | H | Западное Самоа: мандат Новой Зеландии |
| solomon_islands | GBR | GBR | K1 | H | протекторат |
| tonga | GBR | GBR | K1 | M | британский защищённый статус (договор 1900/1905); [Предл.] GBR по правилу п. 2 |
| tuvalu | GBR | GBR | K1 | H | |
| united_states_minor_outlying_islands | USA | USA | K1 | H | |
| vanuatu | NHEB | NHEB | K1 | H | Англо-французский кондоминиум Новые Гебриды; [Предл.] полития `NHEB` (kind joint, members: GBR, FRA) |
| wallis_and_futuna | FRA | FRA | K1 | H | |
| french_polynesia | FRA | FRA | K1 | H | |
| french_southern_and_antarctic_lands | FRA | FRA | K1 | M | |
| clipperton_island | FRA | FRA | K1 | H | |
| east_timor | PRT | PRT | K9 | M | японская оккупация закончена капитуляцией 1945-09-11; португальский губернатор вернулся 1945-09 |

## Таблица C. Африка

| region_id | owner | controller | source | confidence | note |
|---|---|---|---|---|---|
| algeria_oran, algeria_alger, algeria_constantine (core); algeria_ain_sefra, algeria_ghardaia, algeria_oasis, algeria_touggourt (colony) | FRA | FRA | K2, K1 | M | департаменты и Территории Юга, границы по вилаям (TASK-0114) |
| angola_lunda_norte, angola_moxico | PRT | PRT | K2 | H | |
| benin | FRA | FRA | K1 | H | Дагомея |
| botswana | GBR | GBR | K1 | H | Бечуаналенд (протекторат) |
| burkina_faso | FRA | FRA | K1 | H | Верхняя Вольта (упразднена 1932, восстановлена 1947) |
| burundi, rwanda | BEL | BEL | K1, K8 | H | Руанда-Урунди: мандат Бельгии; опека ООН 1946-12-13 |
| cameroon | FRA | FRA | K1, K8 | H | французский мандат |
| cameroon_southern_british | GBR | GBR | K1, K8 | H | британский мандат Южный Камерун |
| cape_verde | PRT | PRT | K2 | H | |
| central_african_republic, chad_batha, chad_borkou, republic_of_the_congo, gabon | FRA | FRA | K1 | H | Французская Экваториальная Африка |
| comoros, madagascar | FRA | FRA | K1 | H | |
| democratic_republic_of_the_congo_bandundu, democratic_republic_of_the_congo_orientale | BEL | BEL | K1 | H | Бельгийское Конго |
| djibouti | FRA | FRA | K1 | H | Французский Берег Сомали |
| egypt_al_wadi_at_jadid, egypt_matruh | EGY | EGY | K2 | H | суверен; британские базы Суэцкого канала (договор 1936) |
| equatorial_guinea | ESP | ESP | K2 | H | |
| eritrea | ITA | GBR | K1, K6 | M | Итальянская Эритрея: британская военная администрация с 1941; Италия отказалась по мирному договору (1947-09-15) |
| ethiopia_amhara, ethiopia_oromiya | ETH | ETH | K2 | M | суверен; Огаден и Хауд — британская военная администрация до 1948-1954 (смешанный: вес Огадена в `ethiopia_oromiya`), [Предп.] controller ETH |
| ethiopia_ogaden | ETH | GBR | check (TASK-0166), англо-эфиопское соглашение 1944–1945 | M | суверенитет Эфиопии, британская военная администрация до 1948; TASK-0184 |
| gambia | GBR | GBR | K1 | H | |
| ghana | GBR | GBR | K1 | H | Золотой Берег + Британское Того |
| guinea, ivory_coast, mali_kayes, mali_timbuktu, mauritania_hodh_ech_chargui, mauritania_tiris_zemmour, niger_agadez, niger_tahoua, senegal | FRA | FRA | K1 | H | Французская Западная Африка |
| guinea_bissau | PRT | PRT | K2 | H | |
| ifni (terr) | ESP | ESP | K2 | H | Испанская Западная Африка |
| kenya | GBR | GBR | K1 | H | |
| lesotho, swaziland | GBR | GBR | K1 | H | |
| liberia | LBR | LBR | K2 | H | суверен |
| libya_al_kufrah | ITA | GBR | K1, K6 | M | Киренаика: британская военная администрация с 1943; Италия отказалась 1947-09-15 |
| libya_ash_shati | ITA | FRA | K1, K6 | M | Триполитания — британская администрация; Феццан — французская (Леклерк, 1943). Регион `ash_shati` содержит и Триполитанию, и Феццан: смешан; [Предп.] по площади FRA (Феццан) |
| malawi | GBR | GBR | K1 | H | Ньясаленд |
| mauritius, seychelles | GBR | GBR | K1 | H | |
| morocco | FRA | FRA | K1, K2 | M | французский протекторат; в регионе также Испанский Марокко (Тетуан) и Тарфая — смешан. Танжер — отдельный регион |
| mozambique | PRT | PRT | K2 | H | |
| namibia | ZAF | ZAF | K8 | H | мандат Южной Африки; ООН опеку не получили (1946) |
| nigeria_borno, nigeria_niger | GBR | GBR | K1 | H | |
| s_sudan, sudan_north_kordufan, sudan_northern | SDN_AE | SDN_AE | K1 | H | Англо-Египетский Судан (кондоминиум 1899); единое правило кондоминиумов: controller = совместная полития; `SDN_AE` (kind joint, members: GBR, EGY); фактически управляет британский генерал-губернатор |
| sao_tome_and_principe | PRT | PRT | K2 | H | |
| sierra_leone | GBR | GBR | K1 | H | |
| somalia | ITA | GBR | K1, K6 | M | Итальянское Сомали: британская военная администрация; опека Италии с 1950-04-01 |
| somaliland | GBR | GBR | K1 | H | Британское Сомали |
| south_africa_free_state, south_africa_northern_cape | ZAF | ZAF | K2 | H | суверен (доминион) |
| tanzania_zanzibar | GBR | GBR | K1 | H | протекторат (султан) |
| togo | FRA | FRA | K1, K8 | H | французский мандат |
| tunisia | FRA | FRA | K1 | H | протекторат (по правилу п. 2) |
| uganda | GBR | GBR | K1 | H | протекторат |
| united_republic_of_tanzania_morogoro, united_republic_of_tanzania_tabora | GBR | GBR | K1, K8 | H | Танганьика: британский мандат (опека 1946-12-13) |
| western_sahara | ESP | ESP | K2 | H | Испанская Сахара |
| zambia | GBR | GBR | K1 | H | Северная Родезия |
| zimbabwe | GBR | GBR | K1 | M | самоуправляющаяся колония Южная Родезия; [Предп.] owner GBR |
| tangier (terr) | TANGIER | ESP | K5 | M | международная зона с 1923; Испания оккупировала 1940-06-14 (нарушение статуса); Парижская конференция, соглашение 1945-08-31; Испания ушла 1945-10-11, временное управление Комитета контроля. Событие: 1945-10-11 controller ESP → TANGIER (международная администрация); полития `TANGIER` [Предл.] (kind joint; members после 1945-10-11: FRA, GBR, ESP, ITA, BEL, NLD, PRT — Комитет контроля по статуту 1923, плюс USA (представитель с сент. 1945) и SUN (право назначить представителя); участники Парижской конференции 1945 — USA, GBR, FRA, SUN, BEL, NLD, PRT, SWE, Испания присоединилась; источники — резюме Cambridge Int. Organization и Hansard 1952-12-08 (поиск 2026-11-18), текст UK TS 24 (1946) не открылся (504) — M; по правилу кондоминиумов после 1945-10-11 controller = TANGIER). На t0 controller ESP — оккупация |

## Таблица D. Америка и Карибы

| region_id | owner | controller | source | confidence | note |
|---|---|---|---|---|---|
| anguilla, antigua_and_barbuda, barbados, british_virgin_islands, cayman_islands, dominica, grenada, jamaica, montserrat, saint_kitts_and_nevis, saint_lucia, saint_vincent_and_the_grenadines, the_bahamas, trinidad_and_tobago, turks_and_caicos_islands, bermuda | GBR | GBR | K1 | H | |
| argentina_buenos_aires, argentina_salta, argentina_santa_cruz | ARG | ARG | K2 | H | |
| aruba, caribbean_netherlands, cura_ao, sint_maarten | NLD | NLD | K1 | H | Кюрасао и зависимые (Нидерландские Антильские) |
| belize | GBR | GBR | K1 | H | Британский Гондурас |
| bolivia_el_beni, bolivia_santa_cruz | BOL | BOL | K2 | H | |
| brazil_amazonas, brazil_bahia, brazil_mato_grosso, brazil_minas_gerais, brazil_par | BRA | BRA | K2 | H | |
| canada_british_columbia, canada_northwest_territories, canada_nunavut, canada_ontario, canada_qu_bec | CAN | CAN | K2 | H | |
| canada_newfoundland | GBR | GBR | K1, K2 | H | Ньюфаундленд с 1934 под Комиссией правления (статус доминиона в приостановке); в 1949-03-31 присоединился к Канаде |
| chile | CHL | CHL | K2 | H | |
| colombia_amazonas, colombia_antioquia | COL | COL | K2 | H | |
| costa_rica | CRI | CRI | K2 | H | |
| cuba | CUB | CUB | K2 | H | |
| dominican_republic | DOM | DOM | K2 | H | |
| ecuador | ECU | ECU | K2 | H | |
| el_salvador | SLV | SLV | K2 | H | |
| falkland_islands, south_georgia_and_the_islands | GBR | GBR | K1 | H | |
| france | FRA | FRA | K1 | H | Гвиана, Гваделупа, Мартиника, Реюньон и т. д. в регионе по капитальной провинции Guyane; [Предл.] FRA |
| greenland_kommuneqarfik_sermersooq, greenland_nationalparken | DNK | DNK | K1, K2 | M | Дания под оккупацией Германии до 1945-05-05; Гренландия под американским протекторатом с 1941 (Соглашение Кауфмана 1941-04-09), датская администрация действует; [Предп.] controller DNK |
| guatemala | GTM | GTM | K2 | H | |
| guyana | GBR | GBR | K1 | H | |
| haiti | HTI | HTI | K2 | H | |
| honduras | HND | HND | K2 | H | |
| mexico_chihuahua, mexico_oaxaca | MEX | MEX | K2 | H | |
| nicaragua | NIC | NIC | K2 | H | |
| panama | PAN | PAN | K2 | H | зона канала — отдельного региона нет |
| paraguay | PRY | PRY | K2 | H | |
| peru_loreto, peru_madre_de_dios | PER | PER | K2 | H | |
| puerto_rico, united_states_virgin_islands, united_states_of_america_alaska, united_states_of_america_hawaii, united_states_of_america_michigan, united_states_of_america_montana, united_states_of_america_texas | USA | USA | K1, K2 | H | Аляска и Гавайи — территории США |
| saint_barthelemy, saint_martin, saint_pierre_and_miquelon | FRA | FRA | K1 | H | |
| suriname | NLD | NLD | K1 | H | |
| us_naval_base_guantanamo_bay | CUB | USA | K2 | H | аренда 1903/1934 |
| uruguay | URY | URY | K2 | H | |
| venezuela_apure, venezuela_bol_var | VEN | VEN | K2 | H | |
| saint_helena | GBR | GBR | K1 | H | включая Тристан-да-Кунья |
| akrotiri_sovereign_base_area, cyprus, dhekelia_sovereign_base_area, northern_cyprus | GBR | GBR | K1 | H | Кипр — британская колония; базы появились 1960 |
| british_indian_ocean_territory | GBR | GBR | K1 | H | Чагос — зависимость Маврикия |

## Таблица E. Британская Индия и сопредельные

Британская Индия до 1947-08-15 — целиком GBR/GBR; княжества (Хайдарабад, Кашмир, Майсур, Траванкор и др.) под британским верховенством — по правилу п. 2 тоже GBR. Андаманские о-ва — оккупация Японией до 1945-10-09 (капитуляция в Порт-Блэре; K10; поиск 2026-11-18: 7 и 9 октября; источники расходятся), контроль сменится вскоре после даты старта — потенциальное событие 1945-10-09.

| region_id | owner | controller | source | confidence | note |
|---|---|---|---|---|---|
| india_goa_daman_diu | PRT | PRT | K2, K6 B17 | H | Португальская Индия |
| india_maharashtra | GBR | GBR | K6 B05 | H | Мадрас, Бомбей, Майсур, Хайдарабад (регион смешан с княжествами) |
| india_odisha | GBR | GBR | K6 B05 | M | Бенгалия, Ассам, Бихар, Ориса, Андаманские о-ва (японская оккупация до 1945-10-09 — смешан); [Предп.] GBR |
| india_puducherry | FRA | FRA | K6 B15 | H | Французская Индия (Пондишери, Карикал, Янам, Маэ) |
| india_rajasthan | GBR | GBR | K6 B05, B09 | H | Раджпутана, Пенджаб, Кашмир, Гуджарат: княжества — GBR |
| india_sikkim | GBR | GBR | K6 B18 | M | королевство под британским верховенством; [Предл.] GBR; в 1950-12-05 договор с Индией (протекторат) |
| chandernagore (terr) | FRA | FRA | K6 B14 | H | |

## Таблица F. Европа без записи в `owners.yaml`

| region_id | owner | controller | source | confidence | note |
|---|---|---|---|---|---|
| portugal_azores | PRT | PRT | K2 | H | |
| norway | NOR | NOR | K2 | H | Шпицберген, Ян-Майен: Норвегия; Бувет не регион |
| spain | ESP | ESP | K2 | H | Канарские о-ва, Сеута, Мелилья по столице провинции Las Palmas |
| german_enclaves_nl (terr) | DEU | GBR | K4, K6 | M | Эльтен/Зельфкант: британская зона; голландская временная администрация с 1949-04-23; [Предп.] DEU/GBR; проверить, были ли в 1945 нидерландские части (L по дням) |
| sazan (terr) | ITA | ALB | K6 | M | остров Сазан: итальянский с 1920, оставлен Италией 1943/44 и занят албанскими партизанами; мирный договор 1947-09-15 передаёт Албании |
| tenda_brig (terr) | ITA | ITA | K6 C18 | L | Тенда и Бриг: итальянские де-юре до 1947-09-15; [Предп.] controller ITA (оккупация французами в 1945 не подтверждена источником) |
| bratislava_bridgehead (terr) | HUN | HUN | K6 C33 | M | до мирного договора 1947-09-15 (de jure) и до 1947-10-15 (de facto) под венгерским управлением при союзной контрольной комиссии |

## Находки и спорные пункты (для оркестратора, не для импорта)

- **F1 (высокая)**: регион должен иметь один owner/controller (#350), но в данных смешаны: `russia_primorye` (Южный Сахалин + Курилы: owner JPN, controller SUN), `india_odisha` (Андаманы), `indonesia_*`, `yemen`, `china_heilongjiang` (Маньчжурия и Северный Китай), `libya_ash_shati`, `morocco`, `vietnam_central` и `laos` (линия 16-й параллели). [Предл.] для T3: линии событий в `spec/events/*.yaml` для Карафуто/Курил (указы 1945-11-02, 1946-02-02), Андаманов (1945-10-09), Йемена (граница 1934), Маньчжурии, 16-й параллели.
- **F2**: Тайвань: на 1945-10-01 `owner JPN`, `controller ROC`; на 1945-10-25 событие меняет owner → ROC (#313/#338). Строка C20 RR-0020 (undecided 1952) отменена #338.
- **F3**: Спратли, Тибет, Огаден/Хауд — игровые решения (game-designer/владелец).
- **F4**: Если принять правило протекторатов п. 2, список новых политий ограничен (§ Краткий вывод п. 4); альтернативное правило (отдельные полития для протекторатов) увеличит число политий на ≈25.
- **F5**: Подсчёт строк: 280 в 6 таблицах; регионы с объединёнными строками подсчитывать по запятым.

## Геометрия секторов Берлина и Вены 1945–1955

Статус: **готовой PD/CC0/CC BY геометрии границ секторов не найдено**. Проверено поиском 2026-11-18; сами страницы OHM целиком не открывались (403 на WebFetch), выводы по выдаче поиска. Лицензия OHM: данные CC0 (с 2024-… проверить на https://wiki.openstreetmap.org/wiki/OpenHistoricalMap — не открывалось) — [Предп.], проект уже использует CC0-снимки OHM (TASK-0091: ATTRIBUTION.md).

### Берлин (три западных сектора)

| что | где | лицензия | доступ | заметка |
|---|---|---|---|---|
| OHM, Западный Берлин целиком (US+UK+FR) | relation 2692239 (версия 13), уже в `sources.lock.yaml` | CC0 (по TASK-0091) | 2026-11-18 (в репозитории) | один полигон без деления на секторы |
| OHM, прочие Берлинские объекты | relation 2694276, 2694826, 2694131, 2694824, 2694645 (Берлин-Шпандау 1920–1945, Вест-Штернберг 1945–46) | CC0 | поиск 2026-11-18 | не секторы. Поиск релейшна «Allied sectors»/«Sektor» ничего не дал; Overpass-запрос не выполнялся [Предл.: развернуть] |
| Карта «Berlin (September 1, 1945)», GHDI | https://ghdi.ghi-dc.org/map.cfm?map_id=522 | правообладатель GHI — лицензия неясна; не PD по умолчанию | поиск 2026-11-18 | только растр-иллюстрация |
| Wikimedia Commons «West & East Berlin (1945-1990).png» | https://en.wikipedia.com/wiki/File:West_%26_East_Berlin_(1945-1990).png | лицензия файла не читалась | поиск 2026-11-18 | растр, не векторная геометрия |
| Princeton Library, «Allied occupation sectors of Berlin» (Aug 1946) | https://maps.princeton.edu/catalog/princeton-cr56n264h | в выдаче «Access: Public», права не подтверждены | поиск 2026-11-18 | сканированная карта; страница вернула 403 при открытии |
| Библиотека Конгресса / CIA | https://cia.gov/readingroom/docs/the%20exclaves%20of%20west%20berl%5B15339013%5D.pdf (CIA Reading Room, исследование «Эксклавы Западного Берлина»); https://www.loc.gov/item/75693376 | CIA FOIA — документы США, PD (17 U.S.C. §105); LoC: «free to use unless Rights Advisory» | поиск 2026-11-18 | растровые карты и тексты; для границ района можно оцифровать (оцифровка PD-карты = PD) |
| UT Austin PCL, Germany maps (CIA 1985, «Berlin Region» 1:75 000) | https://maps.lib.utexas.edu/maps/germany.html | карты ЦРУ — PD США; права страницы UT не указаны | поиск 2026-11-18 | 1985, граница секторов та же |
| Berlin Geoportal / FIS-Broker, «Bezirksgrenzen» | https://daten.berlin.de/ (набор данных «Bezirksgrenzen») | обычно dl-de/by-2.0 (Datenlizenz Deutschland, Namensnennung) — **не CC BY** формально; для ADR-0009 нужно решение владельца | не открывалось | границы 12 районов после 2001, а секторы 1945 состояли из 20 районов 1920 → требуется Ortsteile |

[Факт] Секторы по состоянию 1945–1990 проходили по границам 20 районов (Bezirke) 1920 года: США — Zehlendorf, Steglitz, Kreuzberg, Tempelhof, Neukölln, Schöneberg; Великобритания — Charlottenburg, Spandau, Wilmersdorf, Tiergarten; Франция — Reinickendorf, Wedding; СССР — Mitte, Prenzlauer Berg, Friedrichshain, Pankow, Weißensee, Lichtenberg, Treptow, Köpenick (Лондонский протокол 1944-09-12; сводка по общеизвестным данным; в этой сессии по источнику не сверено — [Предп.]). Проверка по FRUS: https://history.state.gov/historicaldocuments/frus1945Berlinv01 (не открывалось).

[Предл.] Практический путь: полигоны секторов = объединение полигонов Ortsteile (Berlin Open Data, OHM или OSM-производные — **ODbL недопустима** по ADR-0009) по таблице районов выше. Либо OHM: завести (по правилам OHM) relation «British/American/French sector» с датами 1945-07-04 … 1990-10-03; по лицензии CC0 данные пойдут в проект без ограничений (работа, потребует ручной оцифровки по PD-карте CIA/LoC).

### Вена (пять регионов)

| что | где | лицензия | доступ | заметка |
|---|---|---|---|---|
| OHM, зоны Австрии 1945–1955 | relation 2746443 (британская, версия 2/3), 2746442 (американская), 2746444 (французская), 2747810 (советская), родитель 2692360 (список зон), Wien (1955-) 2738972 | CC0 | поиск 2026-11-18 | зоны страны, но **не секторы Вены**; в проект уже вошли как `austria_*_zone` (TASK-0091). Секторов Вены в выдаче нет |
| Commons, «Allied occupation sectors in Vienna (1945-1955).png», «Vienna Allied sectors (1945-1955).png», «Wien Besatzungszonen.png» | категории «Maps of Vienna», «Maps of Allied occupation of Austria» (https://download.osmand.net/wiki/Category:Maps_of_Vienna) | лицензия файлов не подтверждена (в выдаче только «Austria 1945-55.png» — {{PD-self}}, Christoph Lingg, 2005) | поиск 2026-11-18 | растровые схемы; нужна проверка страницы файла |
| Princeton, «Vienna: zones of occupation» (1945, карта США, сектора: UK, French, Russian, US, joint) | https://maps.princeton.edu/catalog/princeton-n583xw79g (403 при открытии; Verify You Are Human) | «Access: Public», права не подтверждены; оригинал — правительственный (США) | поиск 2026-11-18 | растр |
| Госдепартамент США 1946 «Vienna sectors of occupation» (вставка) / HMSO 1946 Treaty Map | поиск 2026-11-18 | госдокумент США — PD; HMSO — Crown copyright (нет; OGL не подтверждена) | поиск | растр, не открывалось |
| Stadt Wien Open Data, «Bezirksgrenzen» | https://www.data.gv.at/ , https://data.wien.gv.at/ | CC BY 4.0 (политика Stadt Wien) — [Предп.], не открывалось | — | границы 23 районов текущие; в 1945 был «Gross-Wien» из 26 районов (1938–1954), районы 22–26 — бывшие нижнеавстрийские общины, возвращённые 1954-09-01 |

[Предп.] Состав секторов Вены (по общеизвестным данным; в этой сессии по источнику не сверялся): СССР — районы 2, 4, 10, 20 + 21, 22 и присоединённые в 1938 зоны; США — 7, 8, 9, 17, 18, 19; Великобритания — 3, 5, 11, 12, 13; Франция — 6, 14, 15, 16; Внутренний город (1) — международная зона с ротацией. Окончательный список нужно сверить с Контрольным соглашением 1945-07-09, FRUS 1945 (https://history.state.gov/historicaldocuments/frus1945v03), и учесть границы районов 1945.

[Предл.] Практический путь Вены: полигоны текущих 23 районов Stadt Wien (CC BY 4.0, после проверки лицензии) объединить по списку выше; районы 22–26 Gross-Wien (1938–1954) и их граница с Нижней Австрией потребуют отдельного OHM-снимка (в OHM для зон Австрии 1945–55 есть «регулировки» границ с Штирией). Сроки: 1 день на сборку при готовых источниках.

## Не проверено

- В этой сессии выполнялись только поисковые запросы и чтение репозитория; ни одна из страниц OHM, Princeton, GHDI, UT и CIA целиком не открылась (403 или только фрагменты выдачи). Статус лицензий — по выдаче поиска.
- Строки с L (Камбоджа, Лаос, Йемен, Спратли, Индонезия-Риау, Тенда) нужны критику и второй проверке (T4 «независимая»).
- Состав секторов Берлина/Вены — по памяти, [Предп.].
