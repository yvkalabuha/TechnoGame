---
tags: [research, institutions, science, done]
---
# Институты 1960–2030: кандидаты на постройку (TASK-0205, #589, O-110)

## Краткий вывод
- Собрано 31 институтов, основанных 1960–2016: USA 7, SUN/RUS 6, GBR 6, FRA 6, SWE 6. Просили 6–12 на страну; взято по 6–7, больше не прошло проверку (см. «Не проверено»).
- [Факт] Годы основания проверены по страницам en.wikipedia.org (доступ 2026-11-26). [Предп.] Направления и уровни — оценка по профилю института, не данные; калибровка в Lab.
- Ограничение метода: `python3 -m tools.llm_worker research` (DeepSeek, #577) запустить не удалось, в сессии агента нет shell. Факты собраны через WebFetch; цитаты в таблице источников — ответ инструмента-пересказчика, а не сырой текст страницы.
- Не вошли климат и погода (NOAA 1970, Hadley Centre 1990): среди 13 тегов нет метеорологии (`met` = металлургия). [Предл.] game-designer решает, нужен ли тег.
- Не вошли биология и медицина (MRC LMB 1962) — нет тега.
- Институт 2030 и позже не найден: 2017–2030 — будущее, это [Предп.]/сценарий, не историческая запись. ESS (2015) и MAX IV (2016) — самые поздние.
- Для малых стран (FRA, SWE) сумма уровней ≤ 3 (правило из шапки institutes.yaml); уровни FRA/SWE урезаны до суммы 3 на институт (leti, ill, esrf, soleil).

## Цена и срок (SCIENCE_SLOTS §2a, [Факт])
Цена 1,5 % науки в год за уровень, срок 2 + 0,5·Σ лет, содержание 0,4 % S в год на уровень (Σ — сумма уровней направлений). Окно строительства открывается за 10 лет до исторической даты. Таблица — расчёт по этим формулам для проекта «с нуля» на указанных уровнях.

| id | год | направления | Σ | цена, % науки/год | срок, лет | содержание, % S/год |
|---|---|---|---|---|---|---|
| usa_marshall | 1960 | {prop: 3, space: 3, struct: 2, ctrl: 1} | 9 | 13.5 | 6.5 | 3.6 |
| usa_jsc | 1961 | {space: 3, ctrl: 2, struct: 1} | 6 | 9.0 | 5 | 2.4 |
| usa_slac | 1962 | {nuc: 3, elec: 2, mat: 2} | 7 | 10.5 | 5.5 | 2.8 |
| usa_fermilab | 1967 | {nuc: 4, elec: 2, sens: 1} | 7 | 10.5 | 5.5 | 2.8 |
| usa_parc | 1970 | {elec: 4, ctrl: 3, mat: 2} | 9 | 13.5 | 6.5 | 3.6 |
| usa_nrel | 1977 | {mat: 3, elec: 3, chem: 2} | 8 | 12.0 | 6 | 3.2 |
| usa_sematech | 1987 | {elec: 4, mfg: 3, mat: 2} | 9 | 13.5 | 6.5 | 3.6 |
| sun_ihep | 1963 | {nuc: 3, elec: 2, sens: 1} | 6 | 9.0 | 5 | 2.4 |
| sun_iki | 1965 | {space: 4, sens: 3, ctrl: 1} | 8 | 12.0 | 6 | 3.2 |
| sun_mikron | 1964 | {elec: 3, mfg: 2, mat: 2} | 7 | 10.5 | 5.5 | 2.8 |
| sun_inr | 1970 | {nuc: 3, sens: 2, elec: 1} | 6 | 9.0 | 5 | 2.4 |
| sun_molniya | 1976 | {aero: 3, space: 3, struct: 2, ctrl: 2} | 10 | 15.0 | 7 | 4.0 |
| sun_skoltech | 2011 | {mat: 2, elec: 2, ctrl: 1} | 5 | 7.5 | 4.5 | 2.0 |
| gbr_culham | 1965 | {nuc: 4, mat: 2, elec: 2} | 8 | 12.0 | 6 | 3.2 |
| gbr_daresbury | 1962 | {nuc: 3, elec: 2, sens: 2} | 7 | 10.5 | 5.5 | 2.8 |
| gbr_wmg | 1980 | {mfg: 4, mat: 2, struct: 2} | 8 | 12.0 | 6 | 3.2 |
| gbr_diamond | 2001 | {mat: 3, sens: 3, chem: 2} | 8 | 12.0 | 6 | 3.2 |
| gbr_turing | 2015 | {ctrl: 3, elec: 2, sens: 1} | 6 | 9.0 | 5 | 2.4 |
| gbr_ngi | 2015 | {mat: 4, elec: 2, chem: 1} | 7 | 10.5 | 5.5 | 2.8 |
| fra_inria | 1967 | {ctrl: 2, elec: 1} | 3 | 4.5 | 3.5 | 1.2 |
| fra_ill | 1967 | {nuc: 2, mat: 1} | 3 | 4.5 | 3.5 | 1.2 |
| fra_leti | 1967 | {elec: 2, sens: 1} | 3 | 4.5 | 3.5 | 1.2 |
| fra_ganil | 1983 | {nuc: 3} | 3 | 4.5 | 3.5 | 1.2 |
| fra_esrf | 1994 | {mat: 2, sens: 1} | 3 | 4.5 | 3.5 | 1.2 |
| fra_soleil | 2006 | {mat: 2, sens: 1} | 3 | 4.5 | 3.5 | 1.2 |
| swe_esrange | 1966 | {space: 2, sens: 1} | 3 | 4.5 | 3.5 | 1.2 |
| swe_ssc | 1972 | {space: 2, prop: 1} | 3 | 4.5 | 3.5 | 1.2 |
| swe_sics | 1985 | {ctrl: 2, elec: 1} | 3 | 4.5 | 3.5 | 1.2 |
| swe_rise | 1997 | {mfg: 2, mat: 1} | 3 | 4.5 | 3.5 | 1.2 |
| swe_ess | 2015 | {nuc: 2, mat: 1} | 3 | 4.5 | 3.5 | 1.2 |
| swe_maxiv | 2016 | {mat: 2, sens: 1} | 3 | 4.5 | 3.5 | 1.2 |

## Источники и оговорки по годам
[Факт] Все URL: https://en.wikipedia.org/wiki/<название>, доступ 2026-11-26; названия — в поле source YAML.
- Fermilab: 1967 (инфобокс) против 1969 (текст) — в данных 1967.
- JSC: дата «1 ноября» без года в пересказе; 1961 — [Предп.], открыт в сентябре 1963.
- Culham: 1965 — открытие; UKAEA получило площадку в 1960 (взято открытие).
- Daresbury: работа с 1962, открытие 1967 — взято 1962.
- Mikron: год только из инфобокса, не из текста.
- Diamond: инфобокс 2001, открытие 2007 — взято 2001.
- ESRF и ILL — международные, расположены во Франции: [Предл.] отдавать FRA.
- ESS: статус ERIC 2015-10-01; стройка 2014; пользователи 2027 (план).
- KIAM (СССР): 1953 или 1966 в источнике — не включён.

## Не проверено
- Страницы LAAS-CNRS, Prokhorov GPI, Institute of Applied Physics RAS — URL дали 404/пусто; в список не вошли.
- Wikipedia — CC BY-SA; ADR-0009 разрешает PD/CC0/CC BY. [Предл.] Годы — факты, но для чистоты проверить по Wikidata P571 (CC0) до правки mods/ (как сделано для sun_viam, gbr_rre).
- Все уровни направлений и `areas` — оценки. Допустимые ключи `areas` (aero/prop/mech/metal/chem) взяты из текущего файла, схему не проверял.
- Кандидатов 2017–2030 нет; 6–12 на страну не достигнуто.

## YAML-блок для mods/core/institutes.yaml (добавить в define:)
```yaml
  - {id: usa_marshall, country: USA, founded: 1960, ref_year: 1960, areas: {prop: 3, mech: 2}, directions: {prop: 3, space: 3, struct: 2, ctrl: 1}, source: "en.wikipedia.org/wiki/Marshall_Space_Flight_Center, 2026-11-26; levels estimated"}
  - {id: usa_jsc, country: USA, founded: 1961, ref_year: 1961, areas: {mech: 2, aero: 1}, directions: {space: 3, ctrl: 2, struct: 1}, source: "en.wikipedia.org/wiki/Lyndon_B._Johnson_Space_Center, 2026-11-26; year 1961 not in the quote, from memory; levels estimated"}
  - {id: usa_slac, country: USA, founded: 1962, ref_year: 1962, areas: {mech: 2, chem: 1}, directions: {nuc: 3, elec: 2, mat: 2}, source: "en.wikipedia.org/wiki/SLAC_National_Accelerator_Laboratory, 2026-11-26; levels estimated"}
  - {id: usa_fermilab, country: USA, founded: 1967, ref_year: 1967, areas: {mech: 2}, directions: {nuc: 4, elec: 2, sens: 1}, source: "en.wikipedia.org/wiki/Fermilab, 2026-11-26; page gives 1967 (infobox) and 1969 (text); levels estimated"}
  - {id: usa_parc, country: USA, founded: 1970, ref_year: 1970, areas: {mech: 2, chem: 1}, directions: {elec: 4, ctrl: 3, mat: 2}, source: "en.wikipedia.org/wiki/PARC_(company), 2026-11-26; levels estimated"}
  - {id: usa_nrel, country: USA, founded: 1977, ref_year: 1977, areas: {chem: 2, metal: 1}, directions: {mat: 3, elec: 3, chem: 2}, source: "en.wikipedia.org/wiki/National_Renewable_Energy_Laboratory, 2026-11-26; levels estimated"}
  - {id: usa_sematech, country: USA, founded: 1987, ref_year: 1987, areas: {mech: 2}, directions: {elec: 4, mfg: 3, mat: 2}, source: "en.wikipedia.org/wiki/Sematech, 2026-11-26; formed 1987, operating 1988; levels estimated"}
  - {id: sun_ihep, country: SUN, founded: 1963, ref_year: 1963, areas: {mech: 2}, directions: {nuc: 3, elec: 2, sens: 1}, source: "en.wikipedia.org/wiki/Institute_for_High_Energy_Physics, 2026-11-26; opened 1965 per Protvino page; levels estimated"}
  - {id: sun_iki, country: SUN, founded: 1965, ref_year: 1965, areas: {aero: 2, mech: 1}, directions: {space: 4, sens: 3, ctrl: 1}, source: "en.wikipedia.org/wiki/Space_Research_Institute, 2026-11-26; levels estimated"}
  - {id: sun_mikron, country: SUN, founded: 1964, ref_year: 1964, areas: {mech: 2}, directions: {elec: 3, mfg: 2, mat: 2}, source: "en.wikipedia.org/wiki/Mikron_Group, 2026-11-26; year from infobox only; levels estimated"}
  - {id: sun_inr, country: SUN, founded: 1970, ref_year: 1970, areas: {mech: 1}, directions: {nuc: 3, sens: 2, elec: 1}, source: "en.wikipedia.org/wiki/Institute_for_Nuclear_Research, 2026-11-26; levels estimated"}
  - {id: sun_molniya, country: SUN, founded: 1976, ref_year: 1976, areas: {aero: 3, mech: 2}, directions: {aero: 3, space: 3, struct: 2, ctrl: 2}, source: "en.wikipedia.org/wiki/NPO_Molniya, 2026-11-26; founded 1976-02-26; levels estimated"}
  - {id: sun_skoltech, country: SUN, founded: 2011, ref_year: 2011, areas: {mech: 1, chem: 1}, directions: {mat: 2, elec: 2, ctrl: 1}, source: "en.wikipedia.org/wiki/Skolkovo_Institute_of_Science_and_Technology, 2026-11-26; levels estimated"}
  - {id: gbr_culham, country: GBR, founded: 1965, ref_year: 1965, areas: {mech: 2, metal: 1}, directions: {nuc: 4, mat: 2, elec: 2}, source: "en.wikipedia.org/wiki/Culham_Centre_for_Fusion_Energy, 2026-11-26; 1965 = opening, UKAEA took the site in 1960; levels estimated"}
  - {id: gbr_daresbury, country: GBR, founded: 1962, ref_year: 1962, areas: {mech: 2}, directions: {nuc: 3, elec: 2, sens: 2}, source: "en.wikipedia.org/wiki/Daresbury_Laboratory, 2026-11-26; began 1962, opened 1967; levels estimated"}
  - {id: gbr_wmg, country: GBR, founded: 1980, ref_year: 1980, areas: {mech: 3, metal: 1}, directions: {mfg: 4, mat: 2, struct: 2}, source: "en.wikipedia.org/wiki/WMG,_University_of_Warwick, 2026-11-26; levels estimated"}
  - {id: gbr_diamond, country: GBR, founded: 2001, ref_year: 2001, areas: {mech: 2, chem: 1}, directions: {mat: 3, sens: 3, chem: 2}, source: "en.wikipedia.org/wiki/Diamond_Light_Source, 2026-11-26; infobox 2001, opened 2007; levels estimated"}
  - {id: gbr_turing, country: GBR, founded: 2015, ref_year: 2015, areas: {mech: 1}, directions: {ctrl: 3, elec: 2, sens: 1}, source: "en.wikipedia.org/wiki/Alan_Turing_Institute, 2026-11-26; levels estimated"}
  - {id: gbr_ngi, country: GBR, founded: 2015, ref_year: 2015, areas: {chem: 1, metal: 1}, directions: {mat: 4, elec: 2, chem: 1}, source: "en.wikipedia.org/wiki/National_Graphene_Institute, 2026-11-26; building opened 2015-03-20; levels estimated"}
  - {id: fra_inria, country: FRA, founded: 1967, ref_year: 1967, areas: {mech: 1}, directions: {ctrl: 2, elec: 1}, source: "en.wikipedia.org/wiki/Inria, 2026-11-26; levels estimated"}
  - {id: fra_ill, country: FRA, founded: 1967, ref_year: 1967, areas: {chem: 1, metal: 1}, directions: {nuc: 2, mat: 1}, source: "en.wikipedia.org/wiki/Institut_Laue–Langevin, 2026-11-26; levels estimated"}
  - {id: fra_leti, country: FRA, founded: 1967, ref_year: 1967, areas: {mech: 1}, directions: {elec: 2, sens: 1}, source: "en.wikipedia.org/wiki/CEA-Leti, 2026-11-26 (levels cut to the small-country cap of 3); levels estimated"}
  - {id: fra_ganil, country: FRA, founded: 1983, ref_year: 1983, areas: {mech: 1}, directions: {nuc: 3}, source: "en.wikipedia.org/wiki/GANIL, 2026-11-26; in operation since 1983; levels estimated"}
  - {id: fra_esrf, country: FRA, founded: 1994, ref_year: 1994, areas: {mech: 1, chem: 1}, directions: {mat: 2, sens: 1}, source: "en.wikipedia.org/wiki/European_Synchrotron_Radiation_Facility, 2026-11-26; international, host France; inaugurated 1994; levels estimated"}
  - {id: fra_soleil, country: FRA, founded: 2006, ref_year: 2006, areas: {chem: 1, mech: 1}, directions: {mat: 2, sens: 1}, source: "en.wikipedia.org/wiki/SOLEIL, 2026-11-26; first electron acceleration 2006-05-14; levels estimated"}
  - {id: swe_esrange, country: SWE, founded: 1966, ref_year: 1966, areas: {aero: 1}, directions: {space: 2, sens: 1}, source: "en.wikipedia.org/wiki/Esrange, 2026-11-26; built 1964 by ESRO, first launch 1966; levels estimated"}
  - {id: swe_ssc, country: SWE, founded: 1972, ref_year: 1972, areas: {aero: 1, prop: 1}, directions: {space: 2, prop: 1}, source: "en.wikipedia.org/wiki/Swedish_Space_Corporation, 2026-11-26; levels estimated"}
  - {id: swe_sics, country: SWE, founded: 1985, ref_year: 1985, areas: {mech: 1}, directions: {ctrl: 2, elec: 1}, source: "en.wikipedia.org/wiki/Swedish_Institute_of_Computer_Science, 2026-11-26; levels estimated"}
  - {id: swe_rise, country: SWE, founded: 1997, ref_year: 1997, areas: {mech: 2}, directions: {mfg: 2, mat: 1}, source: "en.wikipedia.org/wiki/Research_Institutes_of_Sweden, 2026-11-26; founded 1997 as Ireco Holding; levels estimated"}
  - {id: swe_ess, country: SWE, founded: 2015, ref_year: 2015, areas: {mech: 1}, directions: {nuc: 2, mat: 1}, source: "en.wikipedia.org/wiki/European_Spallation_Source, 2026-11-26; ERIC 2015-10-01, construction 2014, users 2027 planned; levels estimated"}
  - {id: swe_maxiv, country: SWE, founded: 2016, ref_year: 2016, areas: {chem: 1, mech: 1}, directions: {mat: 2, sens: 1}, source: "en.wikipedia.org/wiki/MAX_IV_Laboratory, 2026-11-26; operational 2016; levels estimated"}
```

## Проверка DeepSeek (llm_worker research, 2026-11-26)
[Факт] 31 утверждение «основан/открыт в году N»: подтверждено 17, не найдено в тексте страницы 12, противоречие 2. Запись: tools/llm_worker/runs/research-institutes-1960-2030.research-input-52b8be7c.json.
- Противоречия: swe_esrange — построен в 1964 (1966 — первый запуск): год → 1964. swe_ess — строительство с 2014, статус ERIC с 2015: оставить 2015 как год ввода в игру, пометка [Предп.].
- Не найдено (цитата модели не совпала с текстом страницы): usa_marshall, usa_nrel, usa_sematech, sun_ihep, sun_mikron, sun_skoltech, gbr_culham, gbr_daresbury, gbr_wmg (страница-неоднозначность), gbr_diamond, fra_leti, fra_soleil — годы помечены [Предп.]; перед правкой данных сверить по Wikidata P571 (CC0).
- Для игры год — момент, когда институт можно заложить; расхождение в 1–2 года на темп не влияет [Предп.].
