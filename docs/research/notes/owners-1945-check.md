# Сверка владельцев 1945-10-01 (TASK-0166, T-M1-10)

Дата: 2026-11-25. Автор: research-scientist. Объект: `mods/core/scenarios/1945/owners.yaml` (379 регионов: H 215, M 156, L 8). Метки: [Факт] с источником, [Предп.], [Предл.]. `mods/` не менялся.

## Краткий вывод

1. [Факт] Файл содержит 379 строк: Европа (черновик TASK-0075, 92 строки, все M) и мир вне Европы (из `owners-1945-world.md`, 287 строк). Монголия = MNG/MNG (#550) учтена.
2. **Полная сверка не выполнена.** В этой сессии не было оболочки и сетевых запросов к API OHM: проверены веб-поиском и знаниями только спорные регионы (все 8 L и ~20 M). Остальные 350 строк — только внутренняя непротиворечивость и общеизвестные факты (H/M ниже — по ним, не по OHM).
3. Предложено **8 правок данных** (раздел «Правки») и 3 вопроса владельцу. Наиболее уверенные: `brunei` controller GBR → AUS; `ethiopia_ogaden` owner GBR → ETH (controller GBR); `tenda_brig` controller — спор источников (ITA или FRA).
4. [Факт] Лицензия OHM: по умолчанию CC0, но отдельные объекты могут нести `license=*` (FAQ/Export OHM). Проверку лицензии по каждому объекту нужно делать при импорте, здесь не делалась.
5. Wikipedia использована только как наводка (результаты поиска), в данные не копировалась. Первичные тексты (Общий приказ № 1, договоры) не открывались.

## Источники (доступ 2026-11-25, через поиск; страницы целиком не открывались)

- S1 Tende/La Brigue: https://en.wikipedia.org/wiki/Second_Battle_of_the_Alps ; https://fr.wikipedia.org/wiki/Occupation_fran%C3%A7aise_de_la_vall%C3%A9e_de_la_Roya ; https://www.retronews.fr/politique/echo-de-presse/2020/09/01/1947-tende-et-la-brigue-deviennent-francaises (договор подписан 1947-02-10, вступил 1947-09-15).
- S2 BMA Borneo: https://en.wikipedia.org/wiki/British_Military_Administration_(Borneo) (1945-09-12 – 1946-07-01).
- S3 Огаден/Хауд: https://en.wikipedia.org/wiki/British_Military_Administration_(Ogaden) ; https://www.cambridge.org/core/books/abs/modern-history-of-the-somali/restoration-of-colonial-frontiers-194050/BE726D28ED879496DD693AB22CF8BE8D (соглашение 1945: БМА продолжается «без ущерба для суверенитета Эфиопии»; Огаден передан 1948).
- S4 Тайвань: https://en.wikipedia.org/wiki/Retrocession_of_Taiwan (передача 1945-10-25, Андо — Чэнь И).
- S5 OHM: https://www.openhistoricalmap.org/export ; https://wiki.openstreetmap.org/wiki/OpenHistoricalMap/License (CC0 кроме отмеченного `license=*`).
- S6 Синьцзян: https://en.wikipedia.org/wiki/Ili_Rebellion ; https://en.wikipedia.org/wiki/Second_East_Turkestan_Republic (на октябрь 1945 ВТР держит Или, Тарбагатай, Алтай; переговоры с 1945-10-14).
- S7 Внутренняя Монголия: https://en.wikipedia.org/wiki/Inner_Mongolian_People's_Republic (1945-09-09 – 11-06, реально — окрестности Сонид-Юци) ; https://src-h.slav.hokudai.ac.jp/publictn/eurasia_border_review/Vol3SI/radchenko.pdf.
- S8 Спратли: https://en.wikipedia.org/wiki/Spratly_Islands (Япония 1939–1945; после — суверенитет спорен; приём капитуляции поручен ROC).
- S9 Камбоджа: https://en.wikipedia.org/wiki/Japanese_occupation_of_Cambodia (союзные войска в Пномпене в октябре 1945, даты расходятся).
- S10 `docs/research/owners-1945-world.md` (ключи K1–K12), RR-0020, DECISIONS_LOG #313, #338, #350, #550.

## Уверенность по регионам

### L (8 регионов — приоритет)

| Регион | Сейчас | Оценка | Расхождение / вывод |
|---|---|---|---|
| `tenda_brig` | ITA/ITA | L | [Факт] S1: французские войска заняли с апреля 1945; EN: ушли летом, кроме двух коммун; FR: американцы вернули итальянскую администрацию. Источники расходятся. Суверенитет ITA до 1947-09-15 бесспорен. controller неясен |
| `cambodia` | FRA/JPN | L | S9: союзники (брит.) в Пномпене в сентябре–октябре, японцы разоружались. controller 10-01 JPN правдоподобен, но дата не подтверждена. Баттамбанг и Сиемреап — де-факто THA до 1946-11. Регион смешанный |
| `laos` | FRA/ROC | L | не пересмотрено; [Предп.] Север (по Общему приказу № 1) — ROC, юг — GBR; регион режется 16-й параллелью |
| `china_inner_mongol` | ROC/ROC | L | S7: на 1945-10-01 существует самопровозглашённая МНР Внутренней Монголии (до 11-06), но реальный охват мал. Восточная часть — под СССР/КПК. Оставить ROC/ROC, уверенность L |
| `yemen` | YEM/YEM | L | Аден (колония GBR) и Протекторат внутри региона; смешан. Не пересверялось |
| `indonesia_riau` | NLD/IDN | L | Республика на t0 без признания; контроль Суматры оспорен/японский. Не пересверялось |
| `coral_sea_islands` | AUS/AUS | L | Территория образована 1969; ранее — Квинсленд/Папуа. owner AUS верен; L из-за отсутствия собственного юридического статуса |
| `spratly_is` | null/JPN | L | S8: японская администрация закончилась с капитуляцией; по Общему приказу № 1 приём капитуляции — ROC (гарнизон на о. Итуаба с 1946-12). controller JPN на 10-01 сомнителен, owner null верен как «спорный» |

### M (проверено выборочно)

| Регион | Сейчас | Оценка | Замечание |
|---|---|---|---|
| `brunei` | GBR/GBR (H) | **M** | S2: БМА Борнео охватывала и Бруней/Лабуан, австралийские войска; controller AUS как в `malaysia_borneo` |
| `ethiopia_ogaden` | GBR/GBR (M) | M | S3: суверенитет Эфиопии не оспаривался («без ущерба»), БМА до 1948. owner → ETH |
| `taiwan` | JPN/ROC (H) | **M** | S4: формальная передача 1945-10-25; на 10-01 японская администрация на месте, Чэнь И прибыл 10-24. controller ROC на 10-01 — решение #313, фактически спорно |
| `china_xinjiang` | ROC/ROC (M) | M | S6: север (Или, Тарбагатай, Алтай) в руках ВТР, поддерживаемой СССР. По площади ROC |
| `danzig` | POL/POL (M) | M | Потсдам: администрация Польши, не принадлежность. Рассогласование с `poland_former_german` (DEU/POL): владелец DEU неточен для Гданьска (Вольный город) |
| `china_tibet` | TIB/TIB (M) | M | Открытый вопрос владельцу (модель из `owners-1945-world.md`) |
| `morocco`, `india_odisha`, `russia_primorye`, `china_heilongjiang`, `vietnam_*` | — | M | Известные смешанные регионы (#350), решение «по площади» принято в документе |
| Европа (92 строки, все M) | — | M | Согласуется с общеизвестными фактами и RR-0020; первичные источники не открывались. `bratislava_bridgehead` HUN/HUN (H) — не проверялась, предлагаю M |

Остальные ~340 строк: H/M по прежней оценке, не пересверялись по OHM (см. «Не проверено»).

## Расхождения с источниками (сводка)

1. `brunei`: GBR vs факт БМА (AUS) — S2.
2. `ethiopia_ogaden`: owner GBR vs суверенитет ETH — S3.
3. `tenda_brig`: ITA vs FRA — S1 (источники расходятся между собой).
4. `taiwan`: controller ROC vs японская администрация до 1945-10-25 — S4.
5. `danzig`: owner POL vs статус Вольного города / Потсдам.
6. `spratly_is`: controller JPN vs прекращение японского контроля — S8.

## Правки данных (предложения, `mods/` не менялся)

1. [Предл.] `brunei`: controller GBR → AUS; confidence H → M. (D2)
2. [Предл.] `ethiopia_ogaden`: owner GBR → ETH; controller GBR сохранить; M.
3. [Предл.] `tenda_brig`: confidence L; controller оставить ITA, добавить в RR-0020 событие 1947-09-15 (ITA→FRA); при подтверждении первичным источником — controller FRA на 1945-04…
4. [Предл.] `taiwan`: confidence H → M; в примечании: controller на 10-01 фактически JPN; решение #313 сохраняется (вопрос владельцу).
5. [Предл.] `danzig`: owner → DEU (как `poland_former_german`) либо отдельная метка «Вольный город»; решение владельца.
6. [Предл.] `spratly_is`: controller JPN → null (никто); L.
7. [Предл.] `bratislava_bridgehead`: confidence H → M до проверки.
8. [Предл.] RR-0020: C34 исправлена (MNG/MNG на t0, событие `no-effect`); в сводке RR-0020 число `no-effect` теперь 8 (+C34), «ядро» 33 рабочие строки — Orchestrator пересчитает.

## Вопросы владельцу

1. Тибет: owner TIB или ROC?
2. Тайвань: controller на 10-01 — ROC (#313) или JPN (факт)?
3. Гданьск: owner DEU (как остальные бывшие германские) или отдельная метка?

## Не проверено

- OHM: объекты и лицензия `license=*` по регионам не запрашивались (нет оболочки/API). Первичные документы (Общий приказ № 1, FRUS, договоры) не открывались.
- Все H-строки и 92 строки Европы по OHM не сверялись; `laos`, `yemen`, `indonesia_riau` — без нового поиска.
- Коммит не сделан: в сессии не было оболочки (git недоступен). Изменённые файлы: эта заметка и RR-0020 (C34).
