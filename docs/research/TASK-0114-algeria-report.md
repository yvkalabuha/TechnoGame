---
tags: [research, map, history, done]
---
# TASK-0114 — Алжир 1945: департаменты и Территории Юга (шаг 1, research-scientist)

Не RR-документ, frontmatter нет. Доступ ко всем URL: 2026-11-19. Файлы `tools/`, `mods/` не менялись.

## Краткий вывод
- [Факт: `tools/map/spec/ids/world.yaml`, `provinces.yaml`] Посылка задачи неверна: северный Алжир на карте **не отсутствует**, он слит в регион `algeria_ouargla` (30 единиц: Tlemcen, Mascara, Tiaret, Tipaza, Skikda, Batna, Souk Ahras ... плюс Уаргла, Гардая, Лагуат). `algeria_tamanghasset` (18 единиц) — Адрар, Бешар, Тиндуф, Иллизи, Таманрассет. Всего 48 единиц = 48 вилай Natural Earth (после слияний 9 групп в `10-merge-world.yaml`).
- Следствие: сейчас весь север Алжира (около 210 тыс. км² департаментов) получил `status: colony` по правке TASK-0109. Это ошибка данных, а не «отсутствие региона».
- [Факт] В 1945 три департамента (Oran, Alger, Constantine) — часть Франции с 1848; Территории Юга — отдельное военное управление с 1902 (четыре территории с 1905). Границу между ними закрепил закон 24.12.1902 по линии постов Maghnia, El Aricha, Saïda, Tiaret, Aflou, Boughar, Ksar Chellala, Sidi Aïssa, Bou Saâda, Barika, T'Kout, Khenchela, Tébessa.
- Закреплённые источники (NE admin-1 v5.1.2, public-domain) дают только современные 48 вилай. Линия 1945 года по вилаям не идёт, поэтому разбиение возможно **только с точностью до вилаи** ([Предп.], ошибка на пограничных вилаях: Djelfa, M'Sila, Biskra, Batna, Khenchela, Tébessa, Saïda, Tiaret). Новый pin не нужен для варианта «по вилаям».
- [Предл.] Вариант A: 7 регионов вместо 2 (3 департамента `core` + 4 Территории Юга `colony`). Итого регионов 374 -> 379 (ADR-0022: предел 400, не превышен). FRA `core`: 4 -> 7 (в диапазоне 5–15, предупреждение «< 5» исчезает).

## 1. Правовая и административная схема 1945
| Что | Факт | Источник |
|---|---|---|
| Департаменты | Oran, Alger, Constantine созданы постановлением 9.12.1848 на гражданской территории трёх провинций; часть Франции | https://fr.wikipedia.org/wiki/Alg%C3%A9rie_fran%C3%A7aise |
| Территории Юга | Закон 24.12.1902; подчинены генерал-губернатору, управляют военные; 1905 — четыре территории | https://fr.wikipedia.org/wiki/Territoires_du_Sud_(Alg%C3%A9rie) ; https://worldstatesmen.org/Algeria.html |
| Состав 1945 | Aïn-Sefra: кружки Mécheria, Colomb-Béchar, Géryville (El Bayadh), аннексы Aïn-Sefra, Beni-Ounif, Beni-Abbès. Oasis: аннексы Touat, Timimoun, In-Salah (центр Ouargla с 1920). Ghardaïa: кружки Ghardaïa, Laghouat, аннексы Djelfa, Ouargla (центр Laghouat 1945). Touggourt: кружок Touggourt, аннексы Biskra, El Oued | та же страница Wikipedia (по статье 1905) |
| Площади (до 1957) | Oran 67 262 км², Alger 54 861 км², Constantine 87 578 км² (в границах 1905) | https://fr.wikipedia.org/wiki/D%C3%A9partement_d%27Oran ; .../D%C3%A9partement_d%27Alger ; .../D%C3%A9partement_de_Constantine |
| Позже | 1947 — Территории приравнены к департаментам (24.9.1947); 1957 — вместо них Oasis и Saoura (декрет 57-903) | Wikipedia (Territoires du Sud) |

- Для `status` это значит: `core` — три департамента; `colony` — Территории Юга (согласуется с ADR-0022 п. 2.6, TASK-0109). Статус «зависимой территории» в enum отсутствует.
- Приблизительные границы: север Территорий — линия Maghnia–Saïda–Tiaret–Aflou–Boughar–Chellala–Sidi Aïssa–Bou Saâda–Barika–T'Kout–Khenchela–Tébessa (закон 1902). Граница Oran/Alger — около 1,2–1,5° в.д. (между Mostaganem/Relizane и Chlef/Orléansville) [Предп.: по составу арондисманов Alger 1944 — Alger, Blida, Médéa, Miliana, Orléansville, Tizi-Ouzou, Aumale; не по карте]. Alger/Constantine — восточнее Bouira/Bordj Bou Arréridj.
- Не проверено: принадлежность Tindouf (пост с 1934) к Aïn-Sefra; Hoggar/Ajjer (Таманрассет, Иллизи) к Oasis в 1945; Saïda и Tiaret к Oran на линии; Bou Saâda (Alger) против M'Sila (Constantine); точная доля Touggourt-кружка в вилае Ouargla. Статья Wikipedia не сопоставляет территории с вилаями. Не найдено: текст декрета 1905 (Journal officiel) и карта 1945.

## 2. Могут ли закреплённые источники дать эти регионы
- [Факт: `sources.lock.yaml`] `ne_admin1` (NE 10m admin-1, v5.1.2, public-domain): 48 вилай Алжира, id вида `DZA-2143`. Нужный уровень — admin-1. `ne_map_units` Алжир не делит. OHM-объекты в pin — только Европа и Берлин.
- Вывод: регионы по вилаям — да, без нового pin. Резать вилаи по линии 1902 — нельзя: ни одного закреплённого источника границ 1945 для Алжира нет. Линии `parallel` (`20-cuts-world.yaml`) к ней не подходят, это не параллели.
- [Факт: Overpass OHM, поиск по имени, 2026-11-19, не исчерпывающий] Найдено только внешнее «Algérie française» (rel 2690948, 1890–1962, admin_level 4, relation 2690946/2690953/2690954 — более ранние периоды); департаментов и Территорий Юга в OHM по имени нет. Если OHM их добавит, источник CC0 подошёл бы, но потребует нового pin (ADR-0009: CC0 / CC BY; лицензия объекта проверяется заново).
- Лицензионно допустимых альтернатив вне OHM не найдено; Wikipedia/Wikimedia-карты и данные GADM/IGN исключаются (ADR-0009: только PD, CC0, CC BY; GADM не соответствует).
- Не проверено: кэш `tools/map/.cache` пуст, поэтому соответствие NE id (`DZA-2143` и т. п.) именам вилай я не подтверждал; для 9 групп слияния имена единиц взяты по крупнейшему члену. Developer должен вывести таблицу «NE id -> вилая -> единица» перед правкой.

## 3. Где север сейчас
- [Факт: `tools/map/spec/ids/world.yaml:33-80`, `15-subdivide-world.yaml:73-78`, `regions/world.yaml:5-6`] Регион = префикс id. Весь Алжир — 48 единиц в двух регионах (30 + 18). Нет ни региона «departement», ни отдельных единиц Oran, Alger, Constantine, Chlef, Mostaganem и др.: они вошли в слитые единицы с именами `tipaza`, `skikda`, `sidi_bel_abb_s`, `relizane` и т. д. (`10-merge-world.yaml:87-95`).
- [Факт: `mods/core/scenarios/1945/owners.yaml:106-107`] Обе строки сейчас `colony`; в `docs/research/TASK-0109-edge-case-report.md` `algeria_ouargla` назван «Территорией Юга» — для северных вилай это неверно.
- Событие `algeria` (1962-07-03, `events/world.yaml:75`) задано через `adm0: [DZA]`, поэтому деление внутри страны его не ломает.

## 4. Предлагаемая спецификация для developer
[Предл.] Вариант A (рекомендуется), все строки владельца `owner: FRA, controller: FRA`:

| Регион | Статус | Единицы (по текущим именам id) | Столица |
|---|---|---|---|
| algeria_oran | core | tlemcen, sidi_bel_abb_s, mascara, relizane, sa_da, tiaret + единицы, поглотившие Oran, Mostaganem, Aïn Témouchent | единица с Oran (35.70N, -0.63E) |
| algeria_alger | core | a_n_defla, m_d_a, tipaza, bouira + единицы с Alger, Blida, Chlef, Tizi Ouzou (если слиты) | единица с Alger (36.77N, 3.06E) |
| algeria_constantine | core | bordj_bou_arr_ridj, s_tif, mila, skikda, souk_ahras, t_bessa, oum_el_bouaghi, khenchela, batna, msila + единицы с Constantine, Annaba, Béjaïa, Jijel | единица с Constantine (36.37N, 6.61E) |
| algeria_ain_sefra | colony | el_bayadh, na_ma, b_char_1, b_char_2, tindouf_1, tindouf_2 | b_char_1 |
| algeria_ghardaia | colony | laghouat, gharda_a, djelfa | laghouat |
| algeria_oasis | colony | adrar_1..5, tamanghasset_1..7, illizi_1, illizi_2, ouargla_1..3 | ouargla_1 |
| algeria_touggourt | colony | biskra, el_oued | biskra (временный центр 1905) |

Что менять (для developer; я не правил):
1. `spec/ids/world.yaml`: заменить префикс `algeria_ouargla.` и `algeria_tamanghasset.` по таблице; id внутри региона уникальны. Сначала проверить, что слитые единицы не пересекают линию департамент/Территории (по якорям таблицы выше широты 34,5–35,5°N).
2. `spec/15-subdivide-world.yaml:73-78`: заменить `base:` (adrar, b_char, tindouf, illizi, tamanghasset, ouargla) на новые префиксы; число частей не менять.
3. `spec/regions/world.yaml`: убрать две строки, добавить семь с столицами из таблицы.
4. `mods/core/scenarios/1945/owners.yaml`: убрать `algeria_ouargla`, `algeria_tamanghasset`; добавить семь строк (core x3, colony x4, confidence M, source: «TASK-0114 report; границы по вилаям, закон 24.12.1902»). Пересобрать карту; обновить `docs/research/owners-1945-world.md:152`, `mods/core/map/geo/manifest.json`, `adjacency.yaml`, `provinces.yaml`, `regions.yaml` (генерируются).
5. Тесты MapBuilder: единый `status` на регион; квота `great_powers` FRA.

Ожидаемые числа [Факт: счёт `regions.yaml` = 374; `owners.yaml`: FRA core = france, france_ain, france_gers, france_marne = 4]:
- FRA `core`: 4 + 3 = **7** (диапазон 5–15 по ADR-0022 п. 2.5; предупреждение «below the #171 minimum of 5» исчезает).
- Регионов: 374 - 2 + 7 = **379** (≤ 400, предупреждения предела нет; запас до порога 21).
- Вариант B (минимум, 5 регионов: 3 департамента + 2 Территории Юга): 377, но Территории слитые, FRA core всё равно 7. Не рекомендуется: границы расходятся с 1905.

## Риски и допущения
- [Предп.] Граница по вилаям даёт ошибку площади порядка нескольких процентов на ряде вилай; точное разделение требует источника границ 1945, которого нет.
- [Предп.] Кружок Touggourt лежит в северной части вилаи Ouargla и попадёт в `algeria_oasis` (единица `ouargla_1` содержит и Ouargla, и Touggourt). Развести можно только новой линией (cuts) — не в рамках этой задачи.
- Расхождение источников: Wikipedia называет центр территории Ghardaïa Laghouat, Touggourt — Biskra; LLMpedia (ИИ-энциклопедия, без ссылок) пишет о шести департаментах 1957 — как источник не использован.

## Источники
1. https://fr.wikipedia.org/wiki/Territoires_du_Sud_(Alg%C3%A9rie) — 2026-11-19
2. https://fr.wikipedia.org/wiki/Alg%C3%A9rie_fran%C3%A7aise — 2026-11-19
3. https://fr.wikipedia.org/wiki/D%C3%A9partement_d%27Oran ; .../D%C3%A9partement_d%27Alger ; .../D%C3%A9partement_de_Constantine — 2026-11-19
4. https://worldstatesmen.org/Algeria.html (через поисковую выдачу, не открыт) — 2026-11-19
5. https://overpass-api.openhistoricalmap.org/api/interpreter (запросы по имени) — 2026-11-19
6. Репозиторий: `development/tasks/TASK-0114-northern-algeria-regions.md`, `docs/decisions/ADR-0022-region-limit-amendment.md`, `tools/map/sources.lock.yaml`, `tools/map/spec/*`, `mods/core/scenarios/1945/owners.yaml`
