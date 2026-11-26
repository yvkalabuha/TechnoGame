---
tags: [research, data, history, active]
---
# Срез «реактивная авиация»: исторический контент 1970–2030 (решение #423)

> research-scientist, 2026-11-19. Заметка, не элемент реестра. Читал: `mods/core/nodes.yaml` (61 узел), `mods/core/product_classes.yaml`,
> `docs/research/data/jet-slice/README.md`, `docs/simulation/m-play-horizon-2030.md`. Доступ ко всем URL: 2026-11-19.
> Факты (даты событий) не охраняются; текст источников не копировался. Реальная траектория — только для правдоподобия (RISK-0011).

## Краткий вывод
- [Факт] В данных G1–G4 — **собственная шкала игры**, а не «поколения истребителей». Шаг шкалы = новый класс двигателя и планера. Якоря APP: G1 1944, G2 1947, G3 1953, G4 1972.
  Грубое соответствие общепринятым: G1–G2 ≈ 1-е (дозвуковые, потом стреловидные), G3 ≈ 2-е (сверхзвук), G4 ≈ 3-е/4-е (ТРДД + Мах 2, F-15). Точного совпадения нет.
- [Предл.] Продолжение: **G5** («цифровой» истребитель: ЭДСУ с неустойчивой компоновкой, композиты, FADEC, монокристаллические лопатки; якорь 1986, это реальное «4-е/4+»),
  **G6** (малозаметность, сверхкрейсерский двигатель, АФАR, слияние данных; якорь 1997, реальное «5-е»). **G7** (6-е: адаптивный цикл, бесхвостая схема, беспилотные ведомые; якорь 2024) — **опционально**:
  к 2030 году в мире он, скорее всего, не на вооружении.
- [Предл.] 43 новых узла (№ 62–104): 5 до 1970 (недостающие основы), 11 в 1970-е, 11 в 1980-е, 7 в 1990-е, 2 в 2000-е, 3 в 2010-е, 4 в 2020–2030. Без G7 — 36 узлов. 2000-е тонкие: честная история тогда шла в основном освоением G6, а не новыми принципами (см. §3).
- [Предп.] Много дат взято из памяти и поисковой выдачи (вторичные сайты). Достоверно подтверждены не все. Список — §5. Перед переносом в `nodes.yaml` проверить по первоисточникам.

## 1. Что значат G1–G4 в данных
| G | APP-узел, якорь | Двигатель (`eng_gen`, perf_mult) | Планер (`af_class`, ранг, `platform_min_rank`) | Определяющие технологии | Реальные примеры [Факт из S7, S26, S27 в README] |
|---|---|---|---|---|---|
| G1 | №58, 1944 | ТРД g1, ×1.0 | прямое крыло, ранг 1 | ТРД, прямое крыло, дюраль | Meteor (строй 07.1944), P-80 |
| G2 | №59, 1947 | ТРД g2, ×1.3 | стреловидное, ранг 2 | стреловидное крыло, никелевые сплавы, форсаж (узел 21, 1951) | F-86 (01.10.1947), МиГ-15 |
| G3 | №60, 1953 | ТРД g3, ×1.7 | интегральная обшивка, ранг 3 (площадь/дельта) | многовальный ТРД (J57), закон площадей, титан в планере (ранг 4) | F-100 (25.05.1953), YF-102A (1954) |
| G4 | №61, 1972 | ТРДД g4, ×2.2 | Мах 2, `af_class` 4, ранг 5 (титан) | ТРДД (TF30, 1964), охлаждаемые лопатки, DS-литьё (1969), регулируемый воздухозаборник | F-15 (1972) |
- [Факт, из кода] Шаг шкалы нерегулярный: 3 года (G1→G2), 6 лет (G2→G3), 19 лет (G3→G4). Середины окон по RULE-MID: 1951 / 1960 / 1970.
- [Факт, из кода] Новое поколение требует APP-узел + двигатель с `eng_gen = g` + планер с рангом ≥ `platform_min_rank[g]` (для G4 — ранг 5, не 4).
- [Предл.] Продолжать ту же логику: по G5–G7 нужны новые альтернативы в слотах `engine` (eng_g5…), `hot_section` (монокристалл, ранг 5…), `airframe` (композит, малозаметный). Для АФАР и малозаметности вероятно нужен новый слот или поле класса (электроника) — решает Core/game-designer, это не данные узлов.
- [Предл.] Отличия от общепринятой шкалы фиксировать в UI («G4 ≈ реальное 3–4 поколение»), чтобы игрок не путал шкалы.

## 2. Предлагаемые поколения G5–G7
Даты «первый полёт / на вооружение». Вторичные источники помечены (в); S-номера — раздел «Источники».
| G | Определяющие возможности | Реальные примеры по странам | Что нужно знать/уметь |
|---|---|---|---|
| **G5** (якорь 1986 [Предп.]) | неустойчивая схема с ЭДСУ; ~20–40 % композита по массе конструкции; ТРДД с цифровым управлением (FADEC) и монокристаллическими лопатками; РЛС с доплеровской обработкой (радар «вниз-вперёд») | USA: F-16 1974/1978 [Факт, S1]; SWE: Gripen 1988-12-09 / 1996–97 [Факт, S2]; GBR/EAP 1986-08-06 [Факт, S3]; GBR+Европа: Typhoon 1994 / 2003 [Факт, S3, S4]; FRA: Mirage 2000 1978-03-10 / 1984 (в дате службы источники расходятся) [Факт, S5]; Rafale (1986 демонстратор) / 2001 флот, 2004 ВВС [Факт, S4]; SUN: Су-27 1977-05-20 / 1985 [Факт, S6 (в)], МиГ-29 1977 / 1984 (в) [Предп.] | ЭДСУ, FADEC, SX-литьё, КМ-обшивка, CNC и CAD/CAM |
| **G6** (якорь 1997 [Предп.]) | малозаметный планер (форма и покрытие), сверхкрейсер, АФАР, слияние данных, внутренние отсеки | USA: F-22 1997 / 2005 [Факт, S7 (в)]; F-35 2006 / 2015 (USMC) [Факт, S7 (в)]; Предшественники: Have Blue 1977-12-01, F-117 1981-06-18 [Факт, S8]; CHN: J-20 2011-01-11 / 2017 [Факт, S7 (в)]; SUN-преемник RUS: Су-57 2010-01-29 / 2020-12-25 [Факт, S7 (в)] | SX 2–3 поколения, вариабельная тяга/форма сопла, АФАР, малозаметные покрытия |
| **G7** (якорь 2024 [Предл.], опционально) | адаптивный цикл, бесхвостая схема, управляемое «стая из ведомых» | CHN: J-36/J-50 летают с 2024-12 [Предп., S9 (в)]; USA: F-47 выбран 2025-03, первый полёт цель 2028 [Предп., S9 (в)]; GBR+ITA+JPN: GCAP, демонстратор 2027, служба 2035 [Предп., S9 (в)]; FRA/DEU: FCAS (истребитель) прекращён в 2026-06 [Предп., S9 (в)] | адаптивный цикл, КМК-детали, аддитивное производство, автономность |
- [Предп.] Решение о G7 — за Orchestrator/game-designer: оно (а) добавляет 5–6 узлов, (б) к 2030 году даёт «известно, но не на вооружении» (правдоподобно), (в) требует Core ≥ 7 поколений (§ в m-play-horizon: колонки `g1..g4` могут быть зашиты на 4 — не проверено).
- [Предл.] Оговорка по якорям: G5 — 1986 (EAP и Rafale A летали в 1986); если взять первый полёт F-16 (1974), якорь G5 окажется всего через 2 года после G4 (1972). Поэтому предлагаю 1986: это сохраняет окна ~10–15 лет.

## 3. Предлагаемые узлы (продолжение нумерации 62–104)
Формат как в `nodes.yaml`. Кластеры: ELEC существует (RR-0006, RR-0008 §«кластеры»); в срезе пока нет узлов ELEC и композитов. [Предл.] композиты отнести к MET (`discipline: metal`), электронику — к `ELEC` (`discipline: elec`, новая; нужна проверка валидатора).
`tier` только у PRC (шкала 3–5 в данных, ≥6 есть в RR-0008: wafer_fab tier 6 — [Предл.] 6 для самых тонких). Источник: «F» факт (S-номер), «П» память/не проверено, «Е» оценка по правилу (`estimated: true`).

| № | id | Название RU / EN | Тип, gen, кластер | requires | Год | Tier | Основание |
|---|---|---|---|---|---|---|---|
| 62 | s2.fnd.digital_computation | Цифровые вычисления / Digital computation | FND g1 ELEC | — | 1945 (до) | — | Е (RULE-G0; у USA/GBR в старте) |
| 63 | s2.fnd.semiconductor_physics | Физика полупроводников / Semiconductor physics | FND g2 ELEC | — | 1947 | — | П (транзистор, Bell Labs) |
| 64 | s2.fnd.radar_signal_processing | Обработка радиолокационных сигналов / Radar signal processing | FND g3 ELEC | 62 | 1960 | — | Е RULE-MID |
| 65 | s7.fnd.em_scattering_rcs | Рассеяние волн, ЭПР / EM scattering, RCS | FND g4 ELEC | — | 1962 | — | F (Уфимцев; S8 в) |
| 66 | s5.fnd.composite_mechanics | Механика композитов / Composite mechanics | FND g4 MET | — | 1963 | — | П (углеволокно RAE) |
| 67 | s8.fnd.flight_dynamics_unstable | Динамика неустойчивого самолёта / Flight dynamics of unstable aircraft | FND g5 AERO | 62, s8.fnd.transonic_aerodynamics | 1970 | — | Е RULE-MID |
| 68 | s5.prn.composite_airframe_structure | Композитная несущая конструкция / Composite airframe structure | PRN g4 MET | 66, s5.cmp.aluminium_alloy_sheet | 1970 | — | П (F-14, бороэпоксид) |
| 69 | s2.prn.pulse_doppler_radar | Импульсно-доплеровская РЛС / Pulse-Doppler radar | PRN g4 ELEC | 63, 64 | 1972 | — | П (F-15) |
| 70 | s8.prn.digital_fly_by_wire | Цифровая ЭДСУ / Digital fly-by-wire | PRN g5 AERO | 62, 67 | 1972 | — | П (F-8 DFBW) |
| 71 | s5.prn.single_crystal_solidification | Монокристальная кристаллизация / Single-crystal solidification | PRN g5 MET | s5.fnd.solidification_science, s5.prc.ds_casting | 1975 | — | F+Е (S10: военные 1970-е) |
| 72 | s8.eng.relaxed_stability_airframe | Планер с неустойчивостью / Relaxed-stability airframe | ENG g5 AERO | 70, s8.eng.mach2_airframe_g4 | 1974 | — | F (S1: F-16) |
| 73 | s7.prn.low_observable_design | Проектирование малозаметности / Low-observable design | PRN g6 ELEC | 65 | 1977 | — | F (S8: Have Blue) |
| 74 | s5.cmp.cfrp_laminate | Углепластик для обшивки / CFRP skin laminate | CMP g5 MET | 68 | 1978 | — | П (AV-8B) |
| 75 | s6.prc.cnc_multiaxis_machining | ЧПУ-обработка 5 осей / CNC multi-axis machining | PRC g4 MECH | s6.prc.precision_gear_bearing, 62 | 1975 | 5 (prec 5, qc 4.5) | Е |
| 76 | s6.prc.cad_cam_design_chain | САПР/АСТПП / CAD/CAM chain | PRC g5 MECH | 62, 75 | 1977 | 5 (prec 4.5, qc 4.5) | П (CATIA, Dassault) |
| 77 | s5.cmp.single_crystal_blade | Монокристаллическая лопатка / Single-crystal blade | CMP g5 AERO | 71, s8.cmp.turbine_blade_cooled | 1980 | — | F (S10: гражданский 1980/1982) |
| 78 | s5.prc.single_crystal_casting | Литьё монокристаллов / Single-crystal casting | PRC g5 MET | 71, s5.prc.ds_casting | 1980 | 6 (prec 5.5, pur 5.5, qc 5.5) | F+Е |
| 79 | s5.cmp.thermal_barrier_coating | Теплозащитное покрытие / Thermal barrier coating | CMP g5 MET | s8.cmp.turbine_blade_cooled | 1980 | — | Е |
| 80 | s5.prc.autoclave_composite_layup | Автоклавная выкладка композитов / Autoclave composite layup | PRC g5 MET | 74 | 1980 | 5 (prec 4, qc 4.5) | Е |
| 81 | s8.prn.engine_digital_control | Цифровое управление двигателем (FADEC) / Digital engine control | PRN g5 AERO | 62, 70 | 1981 | — | F+спор (S11: F100 / Harrier II) |
| 82 | s8.eng.turbofan_g5 | ТРДД G5 с FADEC и SX / Turbofan G5 | ENG g5 AERO | s8.eng.turbofan_g4, 77, 79, 81 | 1986 | — | Е (hazard key) |
| 83 | s8.eng.composite_airframe_g5 | Композитный планер G5 / Composite airframe G5 | ENG g5 AERO | 72, 74 | 1986 | — | F (S3: EAP 1986-08-06) |
| 84 | s2.cmp.gaas_mmic | GaAs-СВЧ-микросхемы / GaAs MMIC | CMP g6 ELEC | 63 | 1985 | — | Е |
| 85 | s8.app.jet_fighter_g5 | Истребитель G5 / Jet fighter G5 | APP g5 AERO | 82, 83, 69 | 1986 | — | F+Е |
| 86 | s5.cmp.radar_absorbing_material | Радиопоглощающий материал / Radar-absorbing material | CMP g6 MET | 65 | 1977 | — | F (S8) |
| 87 | s8.eng.low_observable_airframe | Малозаметный планер / Low-observable airframe | ENG g6 AERO | 73, 72, 86 | 1981 | — | F (S8: F-117) |
| 88 | s8.prc.ram_coating_application | Нанесение РПП / RAM coating application | PRC g6 MET | 86 | 1983 | 5 (pur 5, qc 5) | Е |
| 89 | s8.eng.thrust_vectoring_nozzle | Управляемый вектор тяги / Thrust-vectoring nozzle | ENG g6 AERO | 82 | 1990 | — | П (X-31) |
| 90 | s8.eng.turbofan_g6 | ТРДД G6, сверхкрейсер / Supercruise turbofan G6 | ENG g6 AERO | 82, 89 | 1990 | — | П (YF-22/YF-23), hazard key |
| 91 | s2.prc.gaas_mmic_fab | Изготовление GaAs-МИС / GaAs MMIC fabrication | PRC g6 ELEC | 84 | 1990 | 6 (prec 5.5, pur 5.5, qc 5) | Е |
| 92 | s2.prc.trm_module_assembly | Сборка приёмо-передающих модулей / T/R module assembly | PRC g6 ELEC | 91 | 1995 | 6 (prec 5.5, qc 5.5) | Е |
| 93 | s2.eng.aesa_radar | АФАР / AESA radar | ENG g6 ELEC | 69, 92 | 1995 | — | Е (в службе: F-2, 2000; S12), hazard key |
| 94 | s2.prn.sensor_fusion | Слияние данных датчиков / Sensor fusion | PRN g6 ELEC | 62, 69 | 1997 | — | Е |
| 95 | s8.app.jet_fighter_g6 | Истребитель G6 / Jet fighter G6 | APP g6 AERO | 87, 90, 93, 94 | 1997 | — | F (S7: F-22 1997) |
| 96 | s8.prc.robotic_precision_assembly | Роботизированная прецизионная сборка / Robotic precision assembly | PRC g6 AERO | s8.prc.stressed_skin_assembly, 76, 75 | 2000 | 6 (prec 5.5, qc 5.5) | Е |
| 97 | s2.cmp.gan_power_amplifier | GaN-усилители мощности / GaN power amplifier | CMP g6 ELEC | 84 | 2008 | — | Е (не проверено) |
| 98 | s8.prc.metal_additive_manufacturing | Аддитивное производство металлов / Metal additive manufacturing | PRC g7 AERO | 75, 76, s5.prc.vacuum_induction_melt | 2013 | 6 (prec 5.5, pur 5.5, qc 5.5) | П |
| 99 | s5.cmp.ceramic_matrix_composite | Керамоматричный композит / CMC | CMP g7 MET | 79, 66 | 2016 | — | П (серийные CMC в гражданских двигателях) |
| 100 | s5.prc.cmc_fabrication | Производство КМК-деталей / CMC fabrication | PRC g7 MET | 99 | 2016 | 6 (prec 5, pur 5.5, qc 5.5) | П |
| 101 | s8.eng.adaptive_cycle_engine | Двигатель адаптивного цикла / Adaptive-cycle engine | ENG g7 AERO | 90, 99 | 2022 | — | П (стендовые испытания; hazard key) |
| 102 | s2.prn.autonomy_teaming | Автономность и «ведомые» БЛА / Autonomy and collaborative combat aircraft | PRN g7 ELEC | 94 | 2025 | — | П (после моего среза знаний; проверить) |
| 103 | s8.eng.tailless_blended_airframe | Бесхвостый интегрированный планер / Tailless blended airframe | ENG g7 AERO | 83, 87 | 2024 | — | Е (S9 в) |
| 104 | s8.app.jet_fighter_g7 | Истребитель G7 / Jet fighter G7 | APP g7 AERO | 101, 102, 103, 94 | 2024 | — | Е (S9 в) |

Распределение якорей (ориентир для M-play): 1945–69: 5; 1970–79: 11 (№ 67–76, 86); 1980–89: 11; 1990–99: 7; 2000–09: 2 (№ 96–97); 2010–19: 3; 2020–30: 4.
- [Предп.] 2000-е тонкие. Предлагаю не придумывать узлы: либо принять (в 2000-е идёт освоение G6 у второго эшелона), либо добавить 1–2 узла по результату проверки (например, STOVL/подъёмный вентилятор, X-35B 2001 — не проверено).
- [Предл.] Дополнительно к `nodes.yaml`: стартовые знания USA/GBR должны включать №62 (до 1945), SUN — №62 к 1951 (МЭСМ; не проверено). Это вопрос сценария, не узлов.

## 4. Куда дошли страны среза после 1970 (только правдоподобие)
| Страна | Реально достигнуто (первый полёт / служба) | Уровень по шкале |
|---|---|---|
| USA | F-15 1972/1974 [П], F-16 1974/1978 [Факт S1], F/A-18 1978/1983 [П], F-117 1981/1983 [Факт S8, IOC П], F-22 1997/2005, F-35 2006/2015 [Факт S7], F-47 выбран 2025 [S9 в] | G6 в 2005, G7 в разработке |
| SUN → RUS | МиГ-23 1967/1970 [П], Су-27 1977/1985 [S6 в], МиГ-29 1977/1982–84 [споры], Су-57 2010/2020 [S7 в]. СССР распался в 1991 | G5 к 1985; G6 уже в РФ, не в СССР |
| GBR | Harrier 1966/1969 [П], Tornado 1974-08-14 / 1979 [Факт S13], Tornado ADV 1979/1985–86, EAP 1986, Typhoon 1994/2003 [Факт S3, S4], F-35B (партнёр уровня 1) [П], GCAP 2035 [S9 в] | G5 к 1986–2003, G6 по сути через F-35 |
| FRA | Mirage F1 1966/1973 [П], Mirage 2000 1978/1984, Rafale 1986/2001, АФАР RBE2-AA 2012–13 [Факт S12], nEUROn [П]; сам вышел из FCAS в 2026 [S9 в] | G5 к 2001, G6 самостоятельно не достиг |
| SWE | Viggen 1967/1971 [П], JAS 39 1988/1996–97 [Факт S2], Gripen E [П, не проверено] | G5 (2-е/4+), G6 не достиг |
- [Предп.] Историческая картина: лидер — USA на протяжении всего периода; конкуренты на уровень G5 вышли к 1985–2003; на G6 самостоятельно — только USA (2005), позже CHN и RUS. Это контекст, а не цель: M-play не должен подгоняться под него (RISK-0011).
- [Предп.] Для баланса без сценария: все пять стран имели физическую возможность выйти на G5, не все — на G6 (стоимость малозаметности и АФАР, потеря СССР/конверсия).

## 5. Пробелы и непроверенное
- [Факт] Страницы источников целиком не читались: даты из выдачи поиска (в).
- Из памяти, без проверки сейчас: F-15 1972/74; F-8 DFBW 1972; F-14 (бороэпоксид 1970); AV-8B/F/A-18 1978; CATIA 1977; CNC 1975; YF-22 29.09.1990, X-31 1990; МиГ-23, Mirage F1, Viggen, Harrier; F-117 IOC 1983; CCA 2025; XA100; CMC/LEAP 2016; аддитивные детали 2013; GaN-АФАР.
- Споры источников: Mirage 2000 в строю 1979 или 1984; МиГ-29 1982 или 1984; SX-лопатка 1980 или 1982 (ASME двояко); FADEC (F100 или Harrier II); Gripen 1996 или 1997; F-15C APG-63(V)2 vs F-2 как первый АФАР; Уфимцев 1962 — один вторичный источник.
- Не найдено: лицензионные числовые ряды (доля композитов по годам, ресурс двигателей), только отдельные значения (Eurofighter ~40 % композита, F-22 ~24 % — в выдаче, не проверено).
- Не проверено в коде: поддерживает ли Core > 4 поколений и новый слот электроники; валидатор для `discipline: elec`.
- `tier`/`capability_req` новых PRC — проектные параметры [Предл.] без источника, как и `perf_mult` будущих альтернатив (их не задавал).
- Не выполнялся поиск по СССР/РФ/КНР по первичным источникам; G7 — самая слабая часть (2024–25, SOURCE: блоги).

## Источники (доступ 2026-11-19; «в» — вторичный/выдача поиска)
- S1 F-16: первый полёт 1974-02-02 (рулёжка 01-20), служба 1978-08-17 — https://migflug.com/afterburner/aircraft/f-16-fighting-falcon/ (в)
- S2 Gripen: 1988-12-09, служба 1996–97 — https://aerocorner.com/blog/european-fighter-jets/ ; https://www.globalsecurity.org/military/world/fighter-aircraft-gen-4.5.htm (в)
- S3 Typhoon/EAP 1986, 1994, 2003 — https://www.globalsecurity.org/military/world/fighter-aircraft-gen-4.5.htm ; https://thedefensepost.com/2026/07/05/eurofighter-typhoon-guide/ (в)
- S4 Rafale 2001/2004 — https://www.globalsecurity.org/military/world/fighter-aircraft-gen-4.5.htm (в)
- S5 Mirage 2000 1978-03-10 — https://airvectors.net/avmir2k.html (в)
- S6 Су-27 1977/1985 — https://wiki.krisyotam.com/content/wikipedia_en_all_nopic_2026-03/SU-27 (в, зеркало Wikipedia)
- S7 F-22, F-35, J-20, Су-57 — https://aerocorner.com/blog/5th-generation-fighter-jets/ ; https://www.businessinsider.nl/only-3-countries-have-managed-to-build-5th-generation-fighters-but-nearly-a-dozen-are-already-working-on-6th-gen-jets/ (в)
- S8 Have Blue 1977-12-01, F-117 1981 — https://www.holloman.af.mil/News/Display/Article/317620/december-1st-is-an-historic-day-for-stealth-fighters/ ; https://www.airandspaceforces.com/article/1192stealth/ (в)
- S9 6-е поколение — https://www.edrmagazine.eu/sixth-generation-fighter-showdown-f-47-gcap-fcas-and-j-36-baidi ; https://migflug.com/afterburner/wp-content/uploads/sites/4/2026/08/sixth-generation-fighter-programs-scorecard-v2.html (в)
- S10 Монокристаллическая лопатка P&W — https://asme.org/about-asme/engineering-history/landmarks/265-pratt-amp-whitney-single-crystal-turbine ; https://ntrs.nasa.gov/citations/19830022158
- S11 FADEC — https://migflug.com/afterburner/fadec-full-authority-digital-engine-control-explained/ (в)
- S12 АФАР, F-2, Rafale RBE2-AA — https://armadainternational.com/2022/05/airborne-aesa-fighter-radars ; https://www.aviationtoday.com/2012/10/02/thales-dassault-deliver-rafale-c137-to-france (в)
- S13 Tornado 1974-08-14 / 1979 — https://theaviationist.com/2024/08/14/tornado-turns-50-the-history/ (в)
- Прежние S-номера README (S7, S26, S27 и др.) — `docs/research/data/jet-slice/README.md`.
