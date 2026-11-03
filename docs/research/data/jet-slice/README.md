# Данные среза «реактивная авиация» (TASK-0011)

> Автор: research-scientist, 2026-11-03. Вход: `docs/simulation/slice-jet-aviation.md` v0.3 §1, §8.1; `LAYER_CONTRACT.md` v2.2; RR-0005, RR-0008 §12; решения #23, #40, #41, #50; ADR-0009. Формат мода и валидатор — отдельная задача developer (после ADR-0010).

## Краткий вывод
- **61 узел** в `nodes.csv`: 8 FND, 8 PRN, 13 ENG, 8 CMP, 20 PRC, 4 APP. У каждого есть `cluster` и `discipline`, все `requires` ссылаются на существующие узлы, стартовые знания замкнуты по `requires`. 46 рёбер `informs` (`informs.csv`) построены по одному правилу.
- **Исторические даты — только ориентиры C4.** У 37 из 61 узла (61%) есть источник; у 24 (39%) дата выведена правилом «середина окна поколения» или «G0 — до старта» (`anchor_estimated: true`).
- **Класс `jet_fighter`** (`product_class.md`): 16 альтернатив в 5 слотах и эталонные `prc_set` двух вариантов для G1–G4. Найдены 3 пробела правил §1.4 (F-PC1…3).
- **Сценарий 1945** (`scenario_1945.md`): правила записаны раньше чисел. ВВП и темп роста взяты из Maddison 2023 (CC BY 4.0). `culture_H`, `Hcap`, `tooling_M` и `w_a` — по явным правилам, `estimated: true`.
- **Трофейные передачи (#50)** вошли в стартовые значения: known-узлы SUN, USA, GBR и FRA; `tooling_M` SUN; численность специалистов в пуле `engineer`. Передачи после 1945-09 предложено запечь в старт, продажу Nene в СССР — не запекать (OPEN 1).
- Главные слабые места: одинаковая доля авиации в бюджете, ВВП на душу как единственный индикатор `culture_H`, у SUN нет ВВП за 1945 г., нет данных об образовании с подходящей лицензией.

## Файлы
| Файл | Содержимое |
|---|---|
| `nodes.csv` | 61 узел: id, тип, поколение, дисциплина, кластер, tier и `capability_req` (PRC), `requires` (`;` — И, `\|` — alt_group), hazard (`key` = ★, `hz`), стартовые знания, ориентир даты |
| `informs.csv` | рёбра `informs` (from, to, weight) |
| `product_class.md` | слоты, альтернативы, эталонные варианты, расхождения F-PC |
| `scenario_1945.md` | правила R-*, значения по акторам, трофейные передачи, расхождения F-SC |
| `product_inputs.md` | входы слоя изделия (T5): денежная единица, `n_p`, `labour_p`, `base_cost_p`, `h_pm`, `wage`, `h_first`/`materials_floor`/`alloy_kg` по поколению, `alloy_cap_a`, `r_ref`/`V_ref`, запрос КБ; FINDINGS F-PI1…4 |

## Правила данных узлов
- **Кластер не-PRC узлов** [Предл.]: metal → MET, chem → CHEM, mech → MECH, aero/prop → AERO. У PRC кластер взят из §1.3. Дисциплина APP — `aero`.
- **`informs`** [Предл.]: от каждого FND ко всем ENG/PRC своей дисциплины с поколением ≥ поколения FND, кроме тех, что уже есть в `requires`. Вес 0.6, если разница поколений ≤ 1, иначе 0.3 (диапазон 0.3–0.6 из §1.3).
- **Ориентир даты:** первая документированная реализация где-либо. Если источник не найден: G0 → `<1945` (RULE-G0); G1 → 1945; G2/G3/G4 → середина окна 1951/1960/1970 (RULE-MID).
- `tier` и `capability_req` перенесены из §1.3 без изменений: это проектные параметры [Предп.], а не исторические данные.

## Проверка (вручную; повторить в валидаторе developer)
| Проверка | Итог |
|---|---|
| Узлов 61; FND/PRN/ENG/CMP/PRC/APP = 8/8/13/8/20/4 | ✓ |
| `cluster` и `discipline` есть у каждого узла | ✓ |
| Каждый id в `requires` существует; поколение предка ≤ поколения узла | ✓ |
| Стартовые знания каждого актора замкнуты по `requires` | ✓ (после снятия 58 у SUN, F-SC1) |
| `mastered` ⊆ known | ✓ |
| Число hazard-узлов: §1.3 говорит «10 ★ + 12 hz = 22» | ✗ в таблице §1.3 их **11 ★ + 9 hz = 20** (F-N1) |

## Доля оценок
- Ориентиры дат узлов: 24/61 = 39% `estimated`.
- Сценарий (17 строк полей): 10 строк целиком `estimated` (правило над источником или без него); у 4 оценена часть (SUN — ВВП 1946, стреловидное крыло SUN, даты трофейных кадров); 3 строки — по источнику или решению (#23).
- `perf_mult`, tier и `capability_req` — проектные параметры спецификации, все [Предп.].

## Лицензии (ADR-0009)
- Числовые данные импортируются только из Maddison Project Database 2023 (Bolt & van Zanden), CC BY 4.0, через OWID. Нужна атрибуция в `mods/core/ATTRIBUTION.md` (OPEN 4).
- Даты событий — факты, они не охраняются; текст Wikipedia (CC BY-SA) не копировался. COW, V-Dem и ATOP не использовались (RR-0005 P17).

## FINDINGS
- **F-N1 (средняя):** счёт hazard в §1.3 расходится с таблицей (20, а не 22; ★ 11, а не 10). Решение исхода HYP-0002 от этого не меняется, но меняется область абляции. → simulation-engineer: исправить текст или таблицу.
- **F-SC1 (средняя):** §1.1 даёт SUN APP G1 без сплава. Это нарушает `requires`, поэтому узел 58 у SUN снят.
- F-PC1…3, F-SC2…5 — в соответствующих файлах.

## OPEN
1. Трофейные передачи после 1945-09 (Осоавиахим 22.10.1946, демонтаж заводов, Surgeon 1946–47, контракт Эстриха 04.1946, Paperclip): предлагаю запечь в старт. Продажу Nene в СССР (1946–47) предлагаю не запекать: этот класс событий покрывает событие лицензии. Нужно решение Orchestrator или владельца.
2. Доля авиации в бюджете одинакова у всех (SUN 0.229 вместо заглушки 0.42). Источника военных расходов 1945–50 с лицензией CC BY или PD не найдено. Принять или искать дальше (SIPRI — лицензию проверить)?
3. Базовые размеры пулов `engineer`/`skilled` и их единицы спецификация не задаёт. Нужно правило simulation-engineer, иначе трофейные +86…+1385 не к чему прибавить.
4. Атрибуция Maddison в `mods/core/ATTRIBUTION.md` — вне моей зоны (developer).
5. Ряд образования для роста `Hcap` (F-SC3): лицензии Barro–Lee / Lee–Lee и Wittgenstein Centre не подтверждены.

## Источники (доступ 2026-11-03; «поиск» — сведения из поисковой выдачи, страница целиком не читалась)
- S1 Maddison 2023, ВВП, OWID — https://ourworldindata.org/grapher/gdp-maddison-project-database (CSV; метаданные: $2011, CC BY 4.0)
- S2 Maddison 2023, ВВП на душу, OWID — https://ourworldindata.org/grapher/gdp-per-capita-maddison-project-database (CSV)
- S3 Nimonic — https://en.wikipedia.org/wiki/Nimonic ; INCO Triangle 1957 — https://www.sudburymuseums.ca/triangle/data/INCOTriangle-19570701.pdf (поиск)
- S4 Rolls-Royce Nene — https://en.wikipedia.org/wiki/Rolls-Royce_Nene ; https://gracesguide.co.uk/Rolls-Royce_Engines:_Nene (поиск)
- S5 МиГ-9, РД-10/РД-20 — https://en.wikipedia.org/wiki/Mikoyan-Gurevich_MiG-9 ; https://afterburner.com.pl/?p=17546 (поиск)
- S6 P-80 — https://en.wikipedia.org/wiki/Lockheed_P-80_Shooting_Star (поиск)
- S7 Meteor, строй 07.1944 — https://migflug.com/cn/飞机/gloster-meteor/ (поиск, вторичный)
- S8 Operation Osoaviakhim — https://en.wikipedia.org/wiki/Operation_Osoaviakhim (прочитано)
- S9 Paperclip, Lusty — https://en.wikipedia.org/wiki/Operation_Paperclip ; https://www.airandspaceforces.com/article/our-german-scientists/ (поиск)
- S10 Operation Surgeon — https://en.wikipedia.org/wiki/Operation_Surgeon ; Völkenrode — https://www.cdvandtext2.org/bios-29-item-6.htm ; https://wikipedia.com/wiki/Fedden_Mission (поиск)
- S11 Atar, Эстрих — https://en.wikipedia.org/wiki/Snecma_Atar ; https://www.safran-group.com/group/history-and-heritage/timeline/1946-04-25-creation-groupe-o (поиск)
- S12 Nene в СССР — https://www.gracesguide.co.uk/Rolls-Royce_Engines:_Nene ; https://nationalinterest.org/node/24161 (поиск; число 25 или 40 не согласовано)
- S13 Стреловидное крыло (Буземан 1935, Джонс 1945, данные 05.1945) — https://warhistory.org/article/swept-wing (поиск, вторичный)
- S14 Уиткомб 1952 — https://www.airandspaceforces.com/article/0210whitcomb/ (поиск)
- S15 YF-102A 20.12.1954 — https://www.thisdayinaviation.com/tag/area-rule/ (поиск)
- S16 J57 — https://en.wikipedia.org/wiki/Pratt_%26_Whitney_J57 ; сверка Olympus 05.1950 — https://en.wikipedia.org/wiki/Rolls-Royce_Olympus (поиск)
- S18 Rolls-Royce Conway — https://en.wikipedia.org/wiki/Rolls-Royce_Conway (прочитано)
- S19 Первый форсаж США 1944 — https://www.nasa.gov/special-projects-laboratory-turbojet-enhancements/ (поиск)
- S20 Ti-6Al-4V 1954 — https://static.asminternational.org/amp/201504/files/assets/basic-html/page-35.html (поиск)
- S22 DS-лопатки: патент 1966, J58 — https://www.nationalacademies.org/read/25543/chapter/56 ; разработка 1958 — https://www.ethw.org/Directionally_solidified_columnar-grained_turbine_blade_developed (поиск; первый двигатель спорен)
- S23 VIM, начало 1950-х; Special Metals 1952 — https://en.wikipedia.org/wiki/Special_Metals_Corporation ; https://en.wikipedia.org/wiki/Vacuum_induction_melting (поиск)
- S25 TF30 — https://en.wikipedia.org/wiki/Pratt_%26_Whitney_TF30 (поиск)
- S26 F-15 — https://www.Wikipedia.com/wiki/F-15 (поиск)
- S27 F-86 01.10.1947, F-100 25.05.1953 — https://www.af.mil/News/Art/igpage/5/igtag/fighters/ (поиск)
- S28 Bell X-1 — https://airandspace.si.edu/collection-objects/bell-x-1 ; https://guinnessworldrecords.de/world-records/first-supersonic-flight (поиск)
- S29 Saab 21R, 10.03.1947 (у SWE на старте нет ТРД) — https://en.wikipedia.org/wiki/Saab_21R (поиск)
- S30 SO.6000 Triton, 11.11.1946 (у FRA на старте нет реактивного планера) — https://en.wikipedia.org/wiki/Sud-Ouest_Triton (поиск)
- S31 Вильм, дюралюминий — https://en.wikipedia.org/wiki/Alfred_Wilm (поиск; 1901 или 1906 — источники расходятся)
- S32 Прандтль 1904 — https://centennialofflight.net/essay/Theories_of_Flight/Prandtl/TH10.htm (поиск)
- S34 Набарро 1948 — https://dial.uclouvain.be/pr/boreal/object/boreal%3A248125/datastream/PDF_01/view (поиск, вторичный)
- S35 Раттер–Чалмерс 1953 — https://research-repository.rmit.edu.au/articles/journal_contribution/The_contribution_of_constitutional_supercooling_to_nucleation_and_grain_formation/27481569 (поиск, вторичный)
- S36 Брандтнер; демонтаж Junkers и BMW — https://en.wikipedia.org/wiki/Ferdinand_Brandner (поиск)
- S37 DuPont, титан 09.1948 — https://www.sciencelearn.org.nz/resources/1817-early-discoveries-about-titanium-timeline (поиск)
- S39 Годы обучения (Barro–Lee, Lee–Lee; лицензия не указана) — https://ourworldindata.org/grapher/mean-years-of-schooling-long-run (прочитано)
- S40 Wittgenstein Centre (лицензия не подтверждена) — https://dataexplorer.wittgensteincentre.org/wcde-v2 (поиск)
- RR-0008 §12 — стендовые ТРД 1937 (вторичный источник migflug.com, принят из RR-0008).

Не проверено в сессии: дата первого полёта МиГ-15; советский жаропрочный сплав 1940-х (ЭИ437) — источник не найден; дата ввода охлаждаемых лопаток Conway.
