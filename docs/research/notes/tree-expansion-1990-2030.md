# Расширение дерева знаний 1990–2030: 55 узлов (TASK-0193, шаг 1)

Автор: research-scientist, 2026-11-26. Статус: [Предл.] — предложение для шага 2 (developer). mods/ не менялся. Метод и формат — `tree-expansion-575.md`. Входной файл для проверки фактов: `docs/research/notes/tree-expansion-1990-2030-input.yaml` (формат `llm_worker research`, #577).

## Краткий вывод
- [Предл.] **55 новых узлов** 1990–2023 (рамка 40–60): авионика и сенсоры 13, малозаметность 4, композиты и технологии материалов 7, вычисления 9, космос 11, ракеты 11. Номера 160–214 (после 156 из #575), id в шаблоне `sN.kind.name`, все уникальны.
- [Факт] Проверено скриптом (REPL): все `requires` указывают на существующие 99 id, на 24 узла предложения #575 или на узлы этого списка; год предпосылки ≤ года узла у всех новых и #575-узлов; циклов нет (порядок номеров топологический). Годы существующих узлов не сверялись.
- [Предл.] **Зависимость**: 24 предпосылки лежат в узлах #575 (TASK-0188, ещё не в mods/): CFD, микропроцессор, ИС/СБИС, GPS 1978, шаттл, возвращаемая ступень 2015 и др. Шаг 2 этой задачи идёт после посадки TASK-0188. Существующие `requires` не менял; связь с jet-веткой — рёбра `informs` (вес 0,3) на 10 существующих узлов (g5/g6, aesa_sensor_fusion, low_observable_airframe, composite_airframe, turbofan_g5, adaptive_cycle_engine, …).
- [Предл.] **research_speed** — у 7 узлов вычислений, по **0,005** (всемирная паутина — всем направлениям). Сумма по направлениям: aero 0,025, prop/nuc/mat 0,02, ctrl 0,015, met/elec/chem 0,01, остальные 0,005. Причина малости: по #575 сумма к 2021 у aero/prop 0,22 при потолке R 0,25 [ЗАГЛУШКА]; запас ≈0,03. Реальный потолок #568 я не сверял (см. OPEN).
- [Факт, ограничено] Годы и факты ниже — по памяти, **не проверены**: страницы я не открывал (нет оболочки). 14 узлов с `~` (`estimated: true`) — год приблизительный. Проверку делает DeepSeek (#577); до вердикта узлы не пригодны для шага 2.
- [Предл.] Поколение: gen 5 для 1990–2000, 6 после; тип = kind узла; мост к институтам — направления из 13 (space у космоса и носителей).
- [Предп.] Перекрытие: существующий `s8.eng.aesa_sensor_fusion` и `s8.eng.low_observable_airframe` уже могут охватывать часть тем (первая АФАР-РЛС, F-35). Нужна сверка названий и годов с mods/ на шаге 2; при совпадении узел снимается.
- [Предл.] Мера «простой слотов после 1990»: всего 55 узлов на 40 лет — 1,4 узла в год; ориентир критерия приёмки (<20 %) проверит simulation-engineer замером.

## Правила
Как в #575: воен. ценность direct/prep/base/— по SCIENCE_SLOTS §3.2 (ракеты и спутники вне jet-среза: «—» либо «основа» при `informs` в jet-ветку); hazard none/hz/key; Wikipedia (CC BY-SA) — только указатель, текст не копируется (ADR-0009); первичные источники (NTRS, патенты, официальные страницы ВВС/ВМС) нужны до шага 2. Ключ источника `R1990-NN` = номер строки «№» в таблицах = id в input YAML.

## Авионика и сенсоры (13)
| № | id | RU / EN | Год | Тип·gen·hz | requires | Напр. | research_speed | Воен. |
|---|---|---|---|---|---|---|---|---|
| 6 | s8.eng.tactical_data_link16 | Тактическая сеть передачи данных (Link 16) / Tactical data link (Link 16) | 1994~ | ENG·5·none | s9.cmp.microprocessor, s8.eng.integrated_avionics_architecture | ctrl, elec, sens | — | прямо усиливает |
| 8 | s8.eng.aesa_fighter_radar_fielded | Первая АФАR-РЛС на серийном истребителе (J/APG-1) / First AESA radar on a production fighter (J/APG-1) | 1995 | ENG·5·hz | s8.cmp.solid_state_tr_module, s8.prn.phased_array_radar_pesa, s5.cmp.gaas_mmic | sens, elec, ctrl | — | прямо усиливает |
| 9 | s8.prn.digital_rf_memory_jamming | Цифровая радиочастотная память (DRFM) постановщик помех / Digital radio-frequency memory (DRFM) jamming | 1995~ | PRN·5·none | s9.prn.vlsi_design_methodology, s8.prn.programmable_signal_processing | elec, sens, ctrl | — | прямо усиливает |
| 12 | s8.eng.cooperative_engagement_capability | Совместное боевое применение сил (CEC) / Cooperative engagement capability (CEC) | 1996~ | ENG·5·none | s8.eng.tactical_data_link16, s9.eng.vector_supercomputer | ctrl, sens, elec | — | основа |
| 21 | s8.eng.armed_reconnaissance_uav | Вооружённый разведывательный БПЛА (Predator) / Armed reconnaissance UAV (MQ-1 Predator) | 2001 | ENG·6·none | s10.eng.gps_operational_constellation, s9.cmp.microprocessor | aero, ctrl, ord, sens | — | основа |
| 24 | s8.prn.helmet_mounted_cueing | Шлемовая система целеуказания / Helmet-mounted cueing system (JHMCS) | 2003 | PRN·6·none | s8.eng.infrared_air_to_air_missile, s9.cmp.microprocessor | sens, ctrl, ord | — | прямо усиливает |
| 28 | s8.eng.electro_optical_targeting_pod | Оптико-электронный прицельный контейнер / Electro-optical targeting pod | 2004 | ENG·6·none | s9.prc.mos_lsi_manufacturing, s9.prn.laser_principle | sens, ord, ctrl | — | прямо усиливает |
| 30 | s8.cmp.gan_tr_module | Модуль АФАР на нитриде галлия / Gallium-nitride AESA transmit/receive module | 2005~ | CMP·6·none | s8.cmp.solid_state_tr_module, s5.cmp.gaas_mmic, s8.eng.aesa_fighter_radar_fielded | sens, elec, mat | — | прямо усиливает |
| 32 | s8.prn.irst_infrared_search_track | Инфракрасный поиск и сопровождение (IRST) на истребителе / Fighter infrared search and track (IRST) | 2007~ | PRN·6·none | s8.eng.infrared_air_to_air_missile, s9.cmp.microprocessor | sens, elec, ord | — | прямо усиливает |
| 44 | s8.eng.distributed_aperture_system | Распределённая оптическая система кругового обзора / Distributed aperture system (F-35 EO DAS) | 2015 | ENG·6·hz | s8.prn.irst_infrared_search_track, s9.eng.massively_parallel_hpc | sens, elec, ctrl | — | прямо усиливает |
| 45 | s8.eng.multifunction_rf_aperture_ew | Многофункциональная ВЧ-система РЭБ и связи / Integrated multifunction RF/EW suite (AN/ASQ-239) | 2015~ | ENG·6·none | s8.prn.digital_rf_memory_jamming, s8.cmp.gan_tr_module | elec, sens, ctrl | — | прямо усиливает |
| 48 | s8.prn.ml_target_recognition | Распознавание целей машинным обучением / Machine-learning target recognition (Project Maven) | 2017 | PRN·6·hz | s9.eng.gpu_accelerated_computing | ctrl, sens, ord | — | основа |
| 49 | s8.eng.loyal_wingman_uav | Ведомый беспилотник (XQ-58) / Loyal wingman UAV (XQ-58 Valkyrie) | 2019 | ENG·6·hz | s8.eng.armed_reconnaissance_uav, s8.prn.ml_target_recognition | aero, ctrl, sens | — | прямо усиливает |

## Малозаметность (4)
| № | id | RU / EN | Год | Тип·gen·hz | requires | Напр. | research_speed | Воен. |
|---|---|---|---|---|---|---|---|---|
| 15 | s8.prn.passive_coherent_location | Пассивная когерентная локация (против малозаметных) / Passive coherent location radar | 1998~ | PRN·5·none | s9.cmp.microprocessor, s8.prn.programmable_signal_processing | sens, elec, ctrl | — | основа |
| 25 | s7.eng.stealth_cruise_missile | Малозаметная крылатая ракета (JASSM) / Stealth cruise missile (AGM-158 JASSM) | 2003 | ENG·6·none | s5.prn.single_crystal_growth, s8.eng.inertial_navigation_system, s10.eng.gps_operational_constellation | ord, prop, sens, ctrl | — | основа |
| 31 | s8.eng.lo_multirole_fighter_f35 | Малозаметный многоцелевой истребитель с интеграцией датчиков (F-35) / Low-observable multirole fighter with sensor fusion (F-35) | 2006 | ENG·6·hz | s8.eng.low_observable_airframe, s8.eng.integrated_avionics_architecture, s8.eng.aesa_fighter_radar_fielded | aero, sens, ctrl, mat | — | прямо усиливает |
| 40 | s8.eng.lo_unmanned_combat_air_vehicle | Малозаметный ударный БПЛА (X-47B) / Low-observable unmanned combat air vehicle (X-47B) | 2011 | ENG·6·none | s8.eng.low_observable_airframe, s8.eng.armed_reconnaissance_uav | aero, ctrl, mat | — | основа |

## Композиты, керамика, технологии (7)
| № | id | RU / EN | Год | Тип·gen·hz | requires | Напр. | research_speed | Воен. |
|---|---|---|---|---|---|---|---|---|
| 1 | s5.prc.automated_fibre_placement | Автоматизированная выкладка волокна / Automated fibre placement | 1990~ | PRC·5·none | s5.prn.fibre_composite_laminate_design, s5.cmp.high_modulus_fibre, s6.prc.precision_gear_bearing | mfg, mat, struct | — | готовит производство |
| 2 | s5.prc.friction_stir_welding | Сварка трением с перемешиванием / Friction stir welding | 1991 | PRC·5·none | s5.cmp.aluminium_alloy_sheet, s6.prc.precision_gear_bearing | mfg, met, struct | — | готовит производство |
| 13 | s5.cmp.ultra_high_temperature_ceramic | Керамика сверхвысоких температур (ZrB2/HfB2) / Ultra-high-temperature ceramics (ZrB2, HfB2) | 1997~ | CMP·5·none | s5.cmp.carbon_carbon_composite, s5.cmp.ceramic_matrix_composite | mat, prop, struct | — | основа |
| 20 | s5.prn.metamaterial_electromagnetic | Метаматериалы с отрицательным показателем преломления / Electromagnetic metamaterials (negative index) | 2000 | PRN·5·hz | s9.prn.vlsi_design_methodology, s9.eng.massively_parallel_hpc | mat, elec, sens | — | основа |
| 36 | s5.cmp.composite_airliner_wing_box | Композитное крыло и фюзеляж лайнера (787) / Composite airliner wing and fuselage (Boeing 787) | 2009 | CMP·6·none | s5.prc.automated_fibre_placement, s8.prc.composite_layup_cure, s5.prc.friction_stir_welding | struct, mat, mfg | — | основа |
| 41 | s5.cmp.titanium_aluminide_blade | Лопатки из алюминида титана / Titanium aluminide turbine blades | 2011~ | CMP·6·none | s5.cmp.titanium_alloy, s5.prc.vacuum_induction_melt, s5.prc.hot_isostatic_pressing | mat, prop, met | — | прямо усиливает |
| 46 | s5.cmp.cmc_hot_section_in_service | Керамокомпозит в горячей части двигателя в эксплуатации / Ceramic matrix composite hot-section parts in service (LEAP) | 2016 | CMP·6·none | s5.cmp.ceramic_matrix_composite, s5.prc.cmc_fabrication, s5.prc.additive_manufacturing_metal | mat, prop, met | — | прямо усиливает |

## Вычисления (research_speed) (9)
| № | id | RU / EN | Год | Тип·gen·hz | requires | Напр. | research_speed | Воен. |
|---|---|---|---|---|---|---|---|---|
| 3 | s9.eng.world_wide_web | Всемирная паутина и открытый обмен данными / World Wide Web | 1991 | ENG·5·none | s9.cmp.microprocessor | ctrl, elec | все +0.5% | — |
| 5 | s9.prn.rans_cfd_industrial | Промышленная CFD на уравнениях Рейнольдса (RANS) / Industrial Reynolds-averaged Navier–Stokes CFD (Spalart–Allmaras) | 1992 | PRN·5·none | s9.prn.computational_fluid_dynamics, s9.eng.vector_supercomputer | aero, prop, ctrl | aero/prop +0.5% | основа |
| 7 | s9.eng.beowulf_commodity_cluster | Кластер из серийных ЭВМ (Beowulf) / Commodity computer cluster (Beowulf) | 1994 | ENG·5·none | s9.cmp.microprocessor, s9.eng.world_wide_web | ctrl, aero, nuc | ctrl/aero/nuc +0.5% | — |
| 14 | s9.prn.computational_materials_design | Расчётное проектирование материалов / Computational materials design (ICME) | 1997~ | PRN·5·none | s9.eng.massively_parallel_hpc, s5.prn.gamma_prime_alloy_design | mat, met, chem | mat/met +0.5% | основа |
| 22 | s9.cmp.multicore_processor | Многоядерный процессор / Multicore processor | 2001 | CMP·6·none | s9.cmp.microprocessor, s9.prn.vlsi_design_methodology | elec, ctrl | ctrl/elec +0.5% | основа |
| 35 | s9.eng.petaflop_supercomputer | Суперкомпьютер петафлопсного класса (Roadrunner) / Petaflop supercomputer (IBM Roadrunner) | 2008 | ENG·6·none | s9.eng.massively_parallel_hpc, s9.cmp.multicore_processor | ctrl, nuc, mat, aero | nuc/mat/aero/prop +0.5% | — |
| 42 | s9.prc.finfet_manufacturing | Транзисторы FinFET (трёхмерный затвор) / FinFET tri-gate transistor manufacturing | 2011 | PRC·6·none | s9.prc.mos_lsi_manufacturing, s9.prn.vlsi_design_methodology | mfg, elec, chem | — | готовит производство |
| 50 | s9.prn.quantum_advantage_demo | Демонстрация квантового превосходства / Quantum supremacy demonstration (Sycamore) | 2019 | PRN·6·hz | s9.eng.petaflop_supercomputer, s9.prn.laser_principle | ctrl, elec, mat | — | — |
| 54 | s9.eng.exascale_supercomputer | Суперкомпьютер экзафлопсного класса (Frontier) / Exascale supercomputer (Frontier) | 2022 | ENG·6·none | s9.eng.petaflop_supercomputer, s9.eng.gpu_accelerated_computing | ctrl, nuc, mat, chem | nuc/mat/aero/prop/chem +0.5% | — |

## Космос (11)
| № | id | RU / EN | Год | Тип·gen·hz | requires | Напр. | research_speed | Воен. |
|---|---|---|---|---|---|---|---|---|
| 10 | s10.eng.gps_operational_constellation | Полная орбитальная группировка GPS / GPS full operational capability | 1995 | ENG·5·none | s10.eng.satellite_navigation_gps, s9.cmp.microprocessor | space, ctrl, sens | — | основа |
| 11 | s10.eng.sar_earth_observation_satellite | Радиолокационный спутник наблюдения (RADARSAT-1) / SAR Earth-observation satellite (RADARSAT-1) | 1995 | ENG·5·none | s10.eng.scientific_satellite, s9.eng.vector_supercomputer | space, sens, elec | — | основа |
| 16 | s10.eng.international_space_station | Международная космическая станция / International Space Station | 1998 | ENG·5·none | s10.eng.space_shuttle_orbiter, s10.eng.crewed_orbital_flight | space, struct, ctrl, mat | — | — |
| 18 | s10.eng.commercial_high_res_imaging | Коммерческая съёмка из космоса высокого разрешения (IKONOS) / Commercial high-resolution imaging satellite (IKONOS) | 1999 | ENG·5·none | s10.eng.reconnaissance_satellite, s9.cmp.microprocessor | space, sens, elec | — | основа |
| 23 | s10.eng.heavy_expendable_launcher | Тяжёлая ракета-носитель семейства EELV (Atlas V) / Evolved expendable launch vehicle (Atlas V) | 2002 | ENG·6·none | s7.eng.staged_combustion_hydrogen_engine, s9.eng.beowulf_commodity_cluster | space, prop, ctrl | — | — |
| 26 | s10.eng.cubesat_small_satellite | Малые спутники формата CubeSat / CubeSat small satellites | 2003 | ENG·6·none | s10.eng.scientific_satellite, s9.cmp.multicore_processor | space, elec, mfg | — | — |
| 33 | s10.eng.kinetic_antisatellite_test | Кинетическое поражение спутника / Kinetic anti-satellite test (2007) | 2007 | ENG·6·hz | s10.eng.reconnaissance_satellite, s7.eng.hit_to_kill_interceptor | space, ord, ctrl | — | — |
| 37 | s10.eng.reusable_uncrewed_spaceplane | Многоразовый беспилотный космоплан (X-37B) / Reusable uncrewed spaceplane (X-37B) | 2010 | ENG·6·hz | s10.eng.space_shuttle_orbiter, s5.cmp.ultra_high_temperature_ceramic | space, struct, mat, ctrl | — | — |
| 43 | s10.eng.missile_warning_satellite_geo | Спутник раннего обнаружения пусков (SBIRS GEO) / Geosynchronous missile-warning satellite (SBIRS GEO-1) | 2011 | ENG·6·none | s10.eng.reconnaissance_satellite, s9.eng.massively_parallel_hpc | space, sens, ord | — | — |
| 51 | s10.eng.leo_megaconstellation | Низкоорбитальная мегагруппировка (Starlink) / LEO mega-constellation (Starlink) | 2019 | ENG·6·hz | s10.eng.cubesat_small_satellite, s7.eng.reusable_booster_landing | space, elec, mfg | — | — |
| 55 | s10.eng.fully_reusable_heavy_launcher | Полностью многоразовая сверхтяжёлая ракета (Starship) / Fully reusable super-heavy launcher (Starship) first integrated flight | 2023 | ENG·6·hz | s7.eng.reusable_booster_landing, s10.eng.heavy_expendable_launcher | space, prop, ctrl, struct | — | — |

## Ракеты (12)
| № | id | RU / EN | Год | Тип·gen·hz | requires | Напр. | research_speed | Воен. |
|---|---|---|---|---|---|---|---|---|
| 4 | s7.eng.amraam_bvr_missile | Ракета «воздух–воздух» за пределами видимости с активной РЛС ГСН (AIM-120) / Active-radar beyond-visual-range air-to-air missile (AIM-120 AMRAAM) | 1991 | ENG·5·none | s8.prn.pulse_doppler_look_down_radar, s9.cmp.microprocessor, s7.prn.composite_solid_propellant | ord, sens, elec, prop | — | прямо усиливает |
| 17 | s7.eng.satellite_guided_bomb | Бомба с наведением по GPS (JDAM) / GPS-guided bomb kit (JDAM) | 1998~ | ENG·5·none | s10.eng.gps_operational_constellation, s8.eng.inertial_navigation_system | ord, ctrl, sens | — | основа |
| 19 | s7.eng.hit_to_kill_interceptor | Перехватчик «попадание в цель» (кинетический) / Hit-to-kill ballistic missile interceptor | 1999 | ENG·5·hz | s7.eng.intercontinental_ballistic_missile, s9.cmp.microprocessor | ord, ctrl, sens | — | основа |
| 25 | s7.eng.stealth_cruise_missile | Малозаметная крылатая ракета (JASSM) / Stealth cruise missile (AGM-158 JASSM) | 2003 | ENG·6·none | s5.prn.single_crystal_growth, s8.eng.inertial_navigation_system, s10.eng.gps_operational_constellation | ord, prop, sens, ctrl | — | основа |
| 27 | s7.prn.imaging_ir_seeker_high_offboresight | Ракета ближнего боя с матричной ИК-ГСН и вектором тяги (AIM-9X) / Imaging-infrared high off-boresight short-range missile (AIM-9X) | 2003 | PRN·6·none | s8.eng.infrared_air_to_air_missile, s8.prn.helmet_mounted_cueing | ord, sens, ctrl | — | прямо усиливает |
| 29 | s7.eng.scramjet_flight_x43 | Лётный испытательный ГПВРД (X-43A) / Scramjet flight test (X-43A, Mach 9.6) | 2004 | ENG·6·hz | s9.prn.computational_fluid_dynamics, s5.cmp.carbon_carbon_composite, s8.prn.variable_cycle_engine | prop, aero, mat | — | — |
| 34 | s7.eng.long_range_sam_s400 | Дальняя зенитная ракетная система (С-400) / Long-range surface-to-air missile system (S-400) | 2007 | ENG·6·none | s8.eng.aesa_fighter_radar_fielded, s7.eng.amraam_bvr_missile | ord, sens, ctrl | — | основа |
| 38 | s7.eng.antiship_ballistic_missile | Противокорабельная баллистическая ракета (DF-21D) / Anti-ship ballistic missile (DF-21D) | 2010~ | ENG·6·hz | s7.eng.hit_to_kill_interceptor, s10.eng.commercial_high_res_imaging | ord, ctrl, sens, space | — | — |
| 39 | s7.eng.scramjet_sustained_x51 | Длительный полёт на ГПВРД (X-51A) / Sustained scramjet flight (X-51A Waverider) | 2010 | ENG·6·hz | s7.eng.scramjet_flight_x43, s5.cmp.ultra_high_temperature_ceramic | prop, aero, mat, ord | — | — |
| 47 | s7.eng.ramjet_bvr_missile | Ракета с прямоточным двигателем (Meteor) / Ramjet-powered beyond-visual-range missile (Meteor) | 2016 | ENG·6·none | s7.eng.amraam_bvr_missile, s8.eng.tactical_data_link16 | ord, prop, sens | — | прямо усиливает |
| 52 | s7.eng.hypersonic_glide_vehicle | Гиперзвуковой планирующий боевой блок (Авангард) / Hypersonic glide vehicle (Avangard) | 2019 | ENG·6·hz | s7.eng.scramjet_flight_x43, s5.cmp.ultra_high_temperature_ceramic, s9.eng.petaflop_supercomputer | ord, aero, mat, ctrl | — | — |
| 53 | s7.eng.hypersonic_cruise_missile | Гиперзвуковая крылатая ракета с ГПВРД (Циркон) / Scramjet hypersonic cruise missile (3M22 Zircon) | 2020~ | ENG·6·hz | s7.eng.scramjet_sustained_x51, s7.eng.hypersonic_glide_vehicle | ord, prop, aero, sens | — | — |

## Что не проверено
- Все 55 годов и фактов (нет доступа к страницам). Особенно сомнительны: GPS FOC (1995; страница может дать 17.07.1995 или апрель), Link 16 (1994~), IRST Typhoon (2007~), ICME (1997~), DRFM (1995~), CEC (1996~), AFP (1990~), TiAl-лопатки (2011~), DF-21D (2010~), Zircon (2020~, служба 2022?), JDAM (1998 принят, 1999 бой).
- URL кандидатов — по памяти; часть может не открыться (вердикт not_found/ошибка загрузки).
- Бонусы 0,005 — [ЗАГЛУШКА] до TASK-0185 и сверки с #568.
- Институты и направление space для 1990–2030 не оценивались.

## OPEN
1. Orchestrator: запустить `python3 -m tools.llm_worker research --input docs/research/notes/tree-expansion-1990-2030-input.yaml`; по вердиктам исправить годы и снять непроверенные узлы (#577).
2. Сверить потолок research_speed из #568 с суммами выше.
3. Сверить с mods/: перекрытие с aesa_sensor_fusion и low_observable_airframe; годы существующих предпосылок.
4. Порядок посадки: TASK-0188 (шаг 2) раньше TASK-0193.

## Блок для nodes.yaml (YAML-ready; поля как в #575; `source` — ключ R1990-NN)
```yaml
  - id: s5.prc.automated_fibre_placement
    number: 160
    type: PRC
    gen: 5
    discipline: chem
    cluster: CHEM
    dirs: [mfg, mat, struct]
    requires: [s5.prn.fibre_composite_laminate_design, s5.cmp.high_modulus_fibre, s6.prc.precision_gear_bearing]
    hazard: none
    mil_value: prep
    informs:
      - {to: s8.eng.composite_airframe, weight: 0.3}
      - {to: s8.prc.composite_layup_cure, weight: 0.3}
    anchor:
      year: 1990
      source: "R1990-01"
      estimated: true
  - id: s5.prc.friction_stir_welding
    number: 161
    type: PRC
    gen: 5
    discipline: chem
    cluster: CHEM
    dirs: [mfg, met, struct]
    requires: [s5.cmp.aluminium_alloy_sheet, s6.prc.precision_gear_bearing]
    hazard: none
    mil_value: prep
    informs:
      - {to: s8.prc.stressed_skin_assembly, weight: 0.3}
    anchor:
      year: 1991
      source: "R1990-02"
      estimated: false
  - id: s9.eng.world_wide_web
    number: 162
    type: ENG
    gen: 5
    discipline: aero
    cluster: COMP
    dirs: [ctrl, elec]
    requires: [s9.cmp.microprocessor]
    hazard: none
    mil_value: none
    effects:
      - {kind: research_speed, dirs: all, bonus: 0.005}
    anchor:
      year: 1991
      source: "R1990-03"
      estimated: false
  - id: s7.eng.amraam_bvr_missile
    number: 163
    type: ENG
    gen: 5
    discipline: aero
    cluster: AERO
    dirs: [ord, sens, elec, prop]
    requires: [s8.prn.pulse_doppler_look_down_radar, s9.cmp.microprocessor, s7.prn.composite_solid_propellant]
    hazard: none
    mil_value: direct
    informs:
      - {to: s8.app.jet_fighter_g5, weight: 0.3}
    anchor:
      year: 1991
      source: "R1990-04"
      estimated: false
  - id: s9.prn.rans_cfd_industrial
    number: 164
    type: PRN
    gen: 5
    discipline: aero
    cluster: COMP
    dirs: [aero, prop, ctrl]
    requires: [s9.prn.computational_fluid_dynamics, s9.eng.vector_supercomputer]
    hazard: none
    mil_value: base
    informs:
      - {to: s8.eng.turbofan_g5, weight: 0.3}
      - {to: s8.prn.rcs_computational_prediction, weight: 0.3}
    effects:
      - {kind: research_speed, dirs: [aero, prop], bonus: 0.005}
    anchor:
      year: 1992
      source: "R1990-05"
      estimated: false
  - id: s8.eng.tactical_data_link16
    number: 165
    type: ENG
    gen: 5
    discipline: aero
    cluster: AERO
    dirs: [ctrl, elec, sens]
    requires: [s9.cmp.microprocessor, s8.eng.integrated_avionics_architecture]
    hazard: none
    mil_value: direct
    informs:
      - {to: s8.eng.aesa_sensor_fusion, weight: 0.3}
      - {to: s8.app.jet_fighter_g5, weight: 0.3}
    anchor:
      year: 1994
      source: "R1990-06"
      estimated: true
  - id: s9.eng.beowulf_commodity_cluster
    number: 166
    type: ENG
    gen: 5
    discipline: aero
    cluster: COMP
    dirs: [ctrl, aero, nuc]
    requires: [s9.cmp.microprocessor, s9.eng.world_wide_web]
    hazard: none
    mil_value: none
    effects:
      - {kind: research_speed, dirs: [ctrl, aero, nuc], bonus: 0.005}
    anchor:
      year: 1994
      source: "R1990-07"
      estimated: false
  - id: s8.eng.aesa_fighter_radar_fielded
    number: 167
    type: ENG
    gen: 5
    discipline: aero
    cluster: AERO
    dirs: [sens, elec, ctrl]
    requires: [s8.cmp.solid_state_tr_module, s8.prn.phased_array_radar_pesa, s5.cmp.gaas_mmic]
    hazard: hz
    mil_value: direct
    informs:
      - {to: s8.app.jet_fighter_g5, weight: 0.3}
      - {to: s8.eng.aesa_sensor_fusion, weight: 0.3}
    anchor:
      year: 1995
      source: "R1990-08"
      estimated: false
  - id: s8.prn.digital_rf_memory_jamming
    number: 168
    type: PRN
    gen: 5
    discipline: aero
    cluster: AERO
    dirs: [elec, sens, ctrl]
    requires: [s9.prn.vlsi_design_methodology, s8.prn.programmable_signal_processing]
    hazard: none
    mil_value: direct
    informs:
      - {to: s8.eng.aesa_sensor_fusion, weight: 0.3}
    anchor:
      year: 1995
      source: "R1990-09"
      estimated: true
  - id: s10.eng.gps_operational_constellation
    number: 169
    type: ENG
    gen: 5
    discipline: aero
    cluster: SPACE
    dirs: [space, ctrl, sens]
    requires: [s10.eng.satellite_navigation_gps, s9.cmp.microprocessor]
    hazard: none
    mil_value: base
    informs:
      - {to: s8.eng.aesa_sensor_fusion, weight: 0.3}
    anchor:
      year: 1995
      source: "R1990-10"
      estimated: false
  - id: s10.eng.sar_earth_observation_satellite
    number: 170
    type: ENG
    gen: 5
    discipline: aero
    cluster: SPACE
    dirs: [space, sens, elec]
    requires: [s10.eng.scientific_satellite, s9.eng.vector_supercomputer]
    hazard: none
    mil_value: base
    anchor:
      year: 1995
      source: "R1990-11"
      estimated: false
  - id: s8.eng.cooperative_engagement_capability
    number: 171
    type: ENG
    gen: 5
    discipline: aero
    cluster: AERO
    dirs: [ctrl, sens, elec]
    requires: [s8.eng.tactical_data_link16, s9.eng.vector_supercomputer]
    hazard: none
    mil_value: base
    informs:
      - {to: s8.eng.aesa_sensor_fusion, weight: 0.3}
    anchor:
      year: 1996
      source: "R1990-12"
      estimated: true
  - id: s5.cmp.ultra_high_temperature_ceramic
    number: 172
    type: CMP
    gen: 5
    discipline: chem
    cluster: CHEM
    dirs: [mat, prop, struct]
    requires: [s5.cmp.carbon_carbon_composite, s5.cmp.ceramic_matrix_composite]
    hazard: none
    mil_value: base
    informs:
      - {to: s8.eng.adaptive_cycle_engine, weight: 0.3}
    anchor:
      year: 1997
      source: "R1990-13"
      estimated: true
  - id: s9.prn.computational_materials_design
    number: 173
    type: PRN
    gen: 5
    discipline: aero
    cluster: COMP
    dirs: [mat, met, chem]
    requires: [s9.eng.massively_parallel_hpc, s5.prn.gamma_prime_alloy_design]
    hazard: none
    mil_value: base
    informs:
      - {to: s5.prc.single_crystal_casting, weight: 0.3}
    effects:
      - {kind: research_speed, dirs: [mat, met], bonus: 0.005}
    anchor:
      year: 1997
      source: "R1990-14"
      estimated: true
  - id: s8.prn.passive_coherent_location
    number: 174
    type: PRN
    gen: 5
    discipline: aero
    cluster: AERO
    dirs: [sens, elec, ctrl]
    requires: [s9.cmp.microprocessor, s8.prn.programmable_signal_processing]
    hazard: none
    mil_value: base
    informs:
      - {to: s8.eng.low_observable_airframe, weight: 0.3}
    anchor:
      year: 1998
      source: "R1990-15"
      estimated: true
  - id: s10.eng.international_space_station
    number: 175
    type: ENG
    gen: 5
    discipline: aero
    cluster: SPACE
    dirs: [space, struct, ctrl, mat]
    requires: [s10.eng.space_shuttle_orbiter, s10.eng.crewed_orbital_flight]
    hazard: none
    mil_value: none
    anchor:
      year: 1998
      source: "R1990-16"
      estimated: false
  - id: s7.eng.satellite_guided_bomb
    number: 176
    type: ENG
    gen: 5
    discipline: aero
    cluster: AERO
    dirs: [ord, ctrl, sens]
    requires: [s10.eng.gps_operational_constellation, s8.eng.inertial_navigation_system]
    hazard: none
    mil_value: base
    anchor:
      year: 1998
      source: "R1990-17"
      estimated: true
  - id: s10.eng.commercial_high_res_imaging
    number: 177
    type: ENG
    gen: 5
    discipline: aero
    cluster: SPACE
    dirs: [space, sens, elec]
    requires: [s10.eng.reconnaissance_satellite, s9.cmp.microprocessor]
    hazard: none
    mil_value: base
    anchor:
      year: 1999
      source: "R1990-18"
      estimated: false
  - id: s7.eng.hit_to_kill_interceptor
    number: 178
    type: ENG
    gen: 5
    discipline: aero
    cluster: AERO
    dirs: [ord, ctrl, sens]
    requires: [s7.eng.intercontinental_ballistic_missile, s9.cmp.microprocessor]
    hazard: hz
    mil_value: base
    anchor:
      year: 1999
      source: "R1990-19"
      estimated: false
  - id: s5.prn.metamaterial_electromagnetic
    number: 179
    type: PRN
    gen: 5
    discipline: chem
    cluster: CHEM
    dirs: [mat, elec, sens]
    requires: [s9.prn.vlsi_design_methodology, s9.eng.massively_parallel_hpc]
    hazard: hz
    mil_value: base
    informs:
      - {to: s8.prn.low_observable_shaping, weight: 0.3}
      - {to: s8.prn.rcs_computational_prediction, weight: 0.3}
    anchor:
      year: 2000
      source: "R1990-20"
      estimated: false
  - id: s8.eng.armed_reconnaissance_uav
    number: 180
    type: ENG
    gen: 6
    discipline: aero
    cluster: AERO
    dirs: [aero, ctrl, ord, sens]
    requires: [s10.eng.gps_operational_constellation, s9.cmp.microprocessor]
    hazard: none
    mil_value: base
    informs:
      - {to: s8.app.jet_fighter_g6, weight: 0.3}
    anchor:
      year: 2001
      source: "R1990-21"
      estimated: false
  - id: s9.cmp.multicore_processor
    number: 181
    type: CMP
    gen: 6
    discipline: aero
    cluster: COMP
    dirs: [elec, ctrl]
    requires: [s9.cmp.microprocessor, s9.prn.vlsi_design_methodology]
    hazard: none
    mil_value: base
    effects:
      - {kind: research_speed, dirs: [ctrl, elec], bonus: 0.005}
    anchor:
      year: 2001
      source: "R1990-22"
      estimated: false
  - id: s10.eng.heavy_expendable_launcher
    number: 182
    type: ENG
    gen: 6
    discipline: aero
    cluster: SPACE
    dirs: [space, prop, ctrl]
    requires: [s7.eng.staged_combustion_hydrogen_engine, s9.eng.beowulf_commodity_cluster]
    hazard: none
    mil_value: none
    anchor:
      year: 2002
      source: "R1990-23"
      estimated: false
  - id: s8.prn.helmet_mounted_cueing
    number: 183
    type: PRN
    gen: 6
    discipline: aero
    cluster: AERO
    dirs: [sens, ctrl, ord]
    requires: [s8.eng.infrared_air_to_air_missile, s9.cmp.microprocessor]
    hazard: none
    mil_value: direct
    informs:
      - {to: s8.app.jet_fighter_g5, weight: 0.3}
    anchor:
      year: 2003
      source: "R1990-24"
      estimated: false
  - id: s7.eng.stealth_cruise_missile
    number: 184
    type: ENG
    gen: 6
    discipline: aero
    cluster: AERO
    dirs: [ord, prop, sens, ctrl]
    requires: [s5.prn.single_crystal_growth, s8.eng.inertial_navigation_system, s10.eng.gps_operational_constellation]
    hazard: none
    mil_value: base
    informs:
      - {to: s8.eng.low_observable_airframe, weight: 0.3}
    anchor:
      year: 2003
      source: "R1990-25"
      estimated: false
  - id: s10.eng.cubesat_small_satellite
    number: 185
    type: ENG
    gen: 6
    discipline: aero
    cluster: SPACE
    dirs: [space, elec, mfg]
    requires: [s10.eng.scientific_satellite, s9.cmp.multicore_processor]
    hazard: none
    mil_value: none
    anchor:
      year: 2003
      source: "R1990-26"
      estimated: false
  - id: s7.prn.imaging_ir_seeker_high_offboresight
    number: 186
    type: PRN
    gen: 6
    discipline: aero
    cluster: AERO
    dirs: [ord, sens, ctrl]
    requires: [s8.eng.infrared_air_to_air_missile, s8.prn.helmet_mounted_cueing]
    hazard: none
    mil_value: direct
    informs:
      - {to: s8.app.jet_fighter_g5, weight: 0.3}
    anchor:
      year: 2003
      source: "R1990-27"
      estimated: false
  - id: s8.eng.electro_optical_targeting_pod
    number: 187
    type: ENG
    gen: 6
    discipline: aero
    cluster: AERO
    dirs: [sens, ord, ctrl]
    requires: [s9.prc.mos_lsi_manufacturing, s9.prn.laser_principle]
    hazard: none
    mil_value: direct
    informs:
      - {to: s8.eng.integrated_avionics_architecture, weight: 0.3}
    anchor:
      year: 2004
      source: "R1990-28"
      estimated: false
  - id: s7.eng.scramjet_flight_x43
    number: 188
    type: ENG
    gen: 6
    discipline: aero
    cluster: AERO
    dirs: [prop, aero, mat]
    requires: [s9.prn.computational_fluid_dynamics, s5.cmp.carbon_carbon_composite, s8.prn.variable_cycle_engine]
    hazard: hz
    mil_value: none
    informs:
      - {to: s8.eng.adaptive_cycle_engine, weight: 0.3}
    anchor:
      year: 2004
      source: "R1990-29"
      estimated: false
  - id: s8.cmp.gan_tr_module
    number: 189
    type: CMP
    gen: 6
    discipline: aero
    cluster: AERO
    dirs: [sens, elec, mat]
    requires: [s8.cmp.solid_state_tr_module, s5.cmp.gaas_mmic, s8.eng.aesa_fighter_radar_fielded]
    hazard: none
    mil_value: direct
    informs:
      - {to: s8.app.jet_fighter_g6, weight: 0.3}
      - {to: s8.eng.aesa_sensor_fusion, weight: 0.3}
    anchor:
      year: 2005
      source: "R1990-30"
      estimated: true
  - id: s8.eng.lo_multirole_fighter_f35
    number: 190
    type: ENG
    gen: 6
    discipline: aero
    cluster: AERO
    dirs: [aero, sens, ctrl, mat]
    requires: [s8.eng.low_observable_airframe, s8.eng.integrated_avionics_architecture, s8.eng.aesa_fighter_radar_fielded]
    hazard: hz
    mil_value: direct
    informs:
      - {to: s8.app.jet_fighter_g6, weight: 0.3}
    anchor:
      year: 2006
      source: "R1990-31"
      estimated: false
  - id: s8.prn.irst_infrared_search_track
    number: 191
    type: PRN
    gen: 6
    discipline: aero
    cluster: AERO
    dirs: [sens, elec, ord]
    requires: [s8.eng.infrared_air_to_air_missile, s9.cmp.microprocessor]
    hazard: none
    mil_value: direct
    informs:
      - {to: s8.app.jet_fighter_g5, weight: 0.3}
    anchor:
      year: 2007
      source: "R1990-32"
      estimated: true
  - id: s10.eng.kinetic_antisatellite_test
    number: 192
    type: ENG
    gen: 6
    discipline: aero
    cluster: SPACE
    dirs: [space, ord, ctrl]
    requires: [s10.eng.reconnaissance_satellite, s7.eng.hit_to_kill_interceptor]
    hazard: hz
    mil_value: none
    anchor:
      year: 2007
      source: "R1990-33"
      estimated: false
  - id: s7.eng.long_range_sam_s400
    number: 193
    type: ENG
    gen: 6
    discipline: aero
    cluster: AERO
    dirs: [ord, sens, ctrl]
    requires: [s8.eng.aesa_fighter_radar_fielded, s7.eng.amraam_bvr_missile]
    hazard: none
    mil_value: base
    informs:
      - {to: s8.prn.low_observable_shaping, weight: 0.3}
    anchor:
      year: 2007
      source: "R1990-34"
      estimated: false
  - id: s9.eng.petaflop_supercomputer
    number: 194
    type: ENG
    gen: 6
    discipline: aero
    cluster: COMP
    dirs: [ctrl, nuc, mat, aero]
    requires: [s9.eng.massively_parallel_hpc, s9.cmp.multicore_processor]
    hazard: none
    mil_value: none
    effects:
      - {kind: research_speed, dirs: [nuc, mat, aero, prop], bonus: 0.005}
    anchor:
      year: 2008
      source: "R1990-35"
      estimated: false
  - id: s5.cmp.composite_airliner_wing_box
    number: 195
    type: CMP
    gen: 6
    discipline: chem
    cluster: CHEM
    dirs: [struct, mat, mfg]
    requires: [s5.prc.automated_fibre_placement, s8.prc.composite_layup_cure, s5.prc.friction_stir_welding]
    hazard: none
    mil_value: base
    informs:
      - {to: s8.eng.composite_airframe, weight: 0.3}
    anchor:
      year: 2009
      source: "R1990-36"
      estimated: false
  - id: s10.eng.reusable_uncrewed_spaceplane
    number: 196
    type: ENG
    gen: 6
    discipline: aero
    cluster: SPACE
    dirs: [space, struct, mat, ctrl]
    requires: [s10.eng.space_shuttle_orbiter, s5.cmp.ultra_high_temperature_ceramic]
    hazard: hz
    mil_value: none
    anchor:
      year: 2010
      source: "R1990-37"
      estimated: false
  - id: s7.eng.antiship_ballistic_missile
    number: 197
    type: ENG
    gen: 6
    discipline: aero
    cluster: AERO
    dirs: [ord, ctrl, sens, space]
    requires: [s7.eng.hit_to_kill_interceptor, s10.eng.commercial_high_res_imaging]
    hazard: hz
    mil_value: none
    anchor:
      year: 2010
      source: "R1990-38"
      estimated: true
  - id: s7.eng.scramjet_sustained_x51
    number: 198
    type: ENG
    gen: 6
    discipline: aero
    cluster: AERO
    dirs: [prop, aero, mat, ord]
    requires: [s7.eng.scramjet_flight_x43, s5.cmp.ultra_high_temperature_ceramic]
    hazard: hz
    mil_value: none
    anchor:
      year: 2010
      source: "R1990-39"
      estimated: false
  - id: s8.eng.lo_unmanned_combat_air_vehicle
    number: 199
    type: ENG
    gen: 6
    discipline: aero
    cluster: AERO
    dirs: [aero, ctrl, mat]
    requires: [s8.eng.low_observable_airframe, s8.eng.armed_reconnaissance_uav]
    hazard: none
    mil_value: base
    informs:
      - {to: s8.app.jet_fighter_g6, weight: 0.3}
    anchor:
      year: 2011
      source: "R1990-40"
      estimated: false
  - id: s5.cmp.titanium_aluminide_blade
    number: 200
    type: CMP
    gen: 6
    discipline: chem
    cluster: CHEM
    dirs: [mat, prop, met]
    requires: [s5.cmp.titanium_alloy, s5.prc.vacuum_induction_melt, s5.prc.hot_isostatic_pressing]
    hazard: none
    mil_value: direct
    informs:
      - {to: s8.eng.turbofan_g5, weight: 0.3}
      - {to: s8.eng.adaptive_cycle_engine, weight: 0.3}
    anchor:
      year: 2011
      source: "R1990-41"
      estimated: true
  - id: s9.prc.finfet_manufacturing
    number: 201
    type: PRC
    gen: 6
    discipline: aero
    cluster: COMP
    dirs: [mfg, elec, chem]
    requires: [s9.prc.mos_lsi_manufacturing, s9.prn.vlsi_design_methodology]
    hazard: none
    mil_value: prep
    anchor:
      year: 2011
      source: "R1990-42"
      estimated: false
  - id: s10.eng.missile_warning_satellite_geo
    number: 202
    type: ENG
    gen: 6
    discipline: aero
    cluster: SPACE
    dirs: [space, sens, ord]
    requires: [s10.eng.reconnaissance_satellite, s9.eng.massively_parallel_hpc]
    hazard: none
    mil_value: none
    anchor:
      year: 2011
      source: "R1990-43"
      estimated: false
  - id: s8.eng.distributed_aperture_system
    number: 203
    type: ENG
    gen: 6
    discipline: aero
    cluster: AERO
    dirs: [sens, elec, ctrl]
    requires: [s8.prn.irst_infrared_search_track, s9.eng.massively_parallel_hpc]
    hazard: hz
    mil_value: direct
    informs:
      - {to: s8.app.jet_fighter_g6, weight: 0.3}
    anchor:
      year: 2015
      source: "R1990-44"
      estimated: false
  - id: s8.eng.multifunction_rf_aperture_ew
    number: 204
    type: ENG
    gen: 6
    discipline: aero
    cluster: AERO
    dirs: [elec, sens, ctrl]
    requires: [s8.prn.digital_rf_memory_jamming, s8.cmp.gan_tr_module]
    hazard: none
    mil_value: direct
    informs:
      - {to: s8.app.jet_fighter_g6, weight: 0.3}
    anchor:
      year: 2015
      source: "R1990-45"
      estimated: true
  - id: s5.cmp.cmc_hot_section_in_service
    number: 205
    type: CMP
    gen: 6
    discipline: chem
    cluster: CHEM
    dirs: [mat, prop, met]
    requires: [s5.cmp.ceramic_matrix_composite, s5.prc.cmc_fabrication, s5.prc.additive_manufacturing_metal]
    hazard: none
    mil_value: direct
    informs:
      - {to: s8.eng.adaptive_cycle_engine, weight: 0.3}
      - {to: s8.eng.turbofan_g5, weight: 0.3}
    anchor:
      year: 2016
      source: "R1990-46"
      estimated: false
  - id: s7.eng.ramjet_bvr_missile
    number: 206
    type: ENG
    gen: 6
    discipline: aero
    cluster: AERO
    dirs: [ord, prop, sens]
    requires: [s7.eng.amraam_bvr_missile, s8.eng.tactical_data_link16]
    hazard: none
    mil_value: direct
    informs:
      - {to: s8.app.jet_fighter_g6, weight: 0.3}
    anchor:
      year: 2016
      source: "R1990-47"
      estimated: false
  - id: s8.prn.ml_target_recognition
    number: 207
    type: PRN
    gen: 6
    discipline: aero
    cluster: AERO
    dirs: [ctrl, sens, ord]
    requires: [s9.eng.gpu_accelerated_computing]
    hazard: hz
    mil_value: base
    anchor:
      year: 2017
      source: "R1990-48"
      estimated: false
  - id: s8.eng.loyal_wingman_uav
    number: 208
    type: ENG
    gen: 6
    discipline: aero
    cluster: AERO
    dirs: [aero, ctrl, sens]
    requires: [s8.eng.armed_reconnaissance_uav, s8.prn.ml_target_recognition]
    hazard: hz
    mil_value: direct
    informs:
      - {to: s8.app.jet_fighter_g6, weight: 0.3}
    anchor:
      year: 2019
      source: "R1990-49"
      estimated: false
  - id: s9.prn.quantum_advantage_demo
    number: 209
    type: PRN
    gen: 6
    discipline: aero
    cluster: COMP
    dirs: [ctrl, elec, mat]
    requires: [s9.eng.petaflop_supercomputer, s9.prn.laser_principle]
    hazard: hz
    mil_value: none
    anchor:
      year: 2019
      source: "R1990-50"
      estimated: false
  - id: s10.eng.leo_megaconstellation
    number: 210
    type: ENG
    gen: 6
    discipline: aero
    cluster: SPACE
    dirs: [space, elec, mfg]
    requires: [s10.eng.cubesat_small_satellite, s7.eng.reusable_booster_landing]
    hazard: hz
    mil_value: none
    anchor:
      year: 2019
      source: "R1990-51"
      estimated: false
  - id: s7.eng.hypersonic_glide_vehicle
    number: 211
    type: ENG
    gen: 6
    discipline: aero
    cluster: AERO
    dirs: [ord, aero, mat, ctrl]
    requires: [s7.eng.scramjet_flight_x43, s5.cmp.ultra_high_temperature_ceramic, s9.eng.petaflop_supercomputer]
    hazard: hz
    mil_value: none
    anchor:
      year: 2019
      source: "R1990-52"
      estimated: false
  - id: s7.eng.hypersonic_cruise_missile
    number: 212
    type: ENG
    gen: 6
    discipline: aero
    cluster: AERO
    dirs: [ord, prop, aero, sens]
    requires: [s7.eng.scramjet_sustained_x51, s7.eng.hypersonic_glide_vehicle]
    hazard: hz
    mil_value: none
    anchor:
      year: 2020
      source: "R1990-53"
      estimated: true
  - id: s9.eng.exascale_supercomputer
    number: 213
    type: ENG
    gen: 6
    discipline: aero
    cluster: COMP
    dirs: [ctrl, nuc, mat, chem]
    requires: [s9.eng.petaflop_supercomputer, s9.eng.gpu_accelerated_computing]
    hazard: none
    mil_value: none
    effects:
      - {kind: research_speed, dirs: [nuc, mat, aero, prop, chem], bonus: 0.005}
    anchor:
      year: 2022
      source: "R1990-54"
      estimated: false
  - id: s10.eng.fully_reusable_heavy_launcher
    number: 214
    type: ENG
    gen: 6
    discipline: aero
    cluster: SPACE
    dirs: [space, prop, ctrl, struct]
    requires: [s7.eng.reusable_booster_landing, s10.eng.heavy_expendable_launcher]
    hazard: hz
    mil_value: none
    anchor:
      year: 2023
      source: "R1990-55"
      estimated: false
```
