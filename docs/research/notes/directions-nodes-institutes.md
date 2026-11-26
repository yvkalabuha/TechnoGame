# Направления: список, узлы знаний, институты (rev 1, 2026-11-25)

Автор: research-scientist. Основание: решения владельца #571, #572; docs/game-design/SCIENCE_SLOTS.md §2a. Данные: mods/core/nodes.yaml (99 узлов), mods/core/institutes.yaml (27 институтов). mods/ не менялся.

## Краткий вывод
- [Предл.] 13 направлений (ред. 2, 2026-11-25, O-093: добавлено `space`): aero, prop, met, mat, chem, struct, mfg, elec, sens, ctrl, nuc, ord, space. Из пяти старых дисциплин (aero, prop, metal, chem, mech) aero и prop сохраняются как есть, metal→met, mech расщепляется на mfg и struct, chem расщепляется на chem, mat, elec.
- [Предл.] Всем 99 узлам назначено 2–4 направления (главное первым). Распределение главных: см. сводку ниже. Новые направления sens, ctrl, elec, nuc, ord сейчас почти пусты по главным узлам: на jet-срезе нет узлов с главным nuc/ord, а sens/ctrl/elec — только узлы 1965+. Это ограничение данных, а не ошибка списка; nuc и ord нужны институтам (CEA, ICP, РНИИ, JPL) и будущим срезам.
- [Предл.] 27 институтам назначено 2–4 направления с уровнями 1–5. Уровни старых областей перенесены как есть (aero→aero, prop→prop, metal→met, mech→mfg/struct, chem→chem/mat); добавленные направления получили уровни 1–3 по профилю института, максимум 4 не превышен. Для малых стран (FRA, SWE) правило «сумма проекта ≤3» не перепроверялось.
- [Факт] Институты «1945» без месяца: usa_westinghouse_agt (источник: «established in 1945», месяц не указан), swe_foa (только год). fra_cea: 18 октября 1945 — после t0 1945-10-01, значит на t0 его нет (источник en.wikipedia.org/wiki/French_Alternative_Energies_and_Atomic_Energy_Commission, доступ 2026-11-25). Предложение: считать founded 1945 с month, CEA сделать кандидатом на постройку; Westinghouse и FOA месяц не подтверждён [не проверено].
- Уровни — [Предп.]: исторические источники дают профиль, не числа; уровни калибруются в Lab. Ссылки — страницы, уже указанные в institutes.yaml (доступ 2026-11-24); текст проверен повторно только для CEA, FOA, Westinghouse (2026-11-25).

> Ред. 2: 13-е направление `space` добавлено по O-093; 99 существующим узлам и 27 институтам оно не назначено (уровни `space` у институтов — шаг 2 TASK-0188). Новые узлы с `space`: `docs/research/notes/tree-expansion-575.md`.

## 1. Направления
| Код | Название | Определение | Из старой дисциплины |
|---|---|---|---|
| aero | Аэродинамика | течение, подъёмная сила, сопротивление, устойчивость, аэротрубы | aero |
| prop | Двигатели | термодинамика, компрессор, камера, турбина, сопла, ракетные двигатели | prop |
| met | Металлургия | сплавы, выплавка, литьё, термообработка, ползучесть | metal |
| mat | Неметаллические материалы | композиты, керамика, волокна, покрытия | chem (композиты, керамика) |
| chem | Химия и топлива | топлива, окислители, горение, очистка, полупроводниковая химия | chem |
| struct | Конструкции | прочность, схема планера, обшивка, нагрузки | aero/mech (планер) |
| mfg | Технологии производства | обработка, ковка, литьё, сборка, точность | mech |
| elec | Электроника | лампы, полупроводники, СВЧ-компоненты, микросхемы | chem (GaAs) — новое |
| sens | Радиолокация и сигнатуры | радар, ЭПР, РЭБ, малозаметность | aero (радар) — новое |
| ctrl | Управление и вычисления | автоматика, СУ, цифровые ЭВМ, ПО, авионика | aero/prop (управление) — новое |
| nuc | Ядерная физика | реакторы, деление, материалы для ядерной энергетики | новое |
| ord | Вооружения | боеприпасы, ракеты-носители боевой части, снаряды | новое |
| space | Космос | орбитальная механика, спутники, пилотируемые полёты, космические аппараты и наука с орбиты; ракетные двигатели остаются в prop | новое (O-093, ред. 2) |

Главные направления узлов: prop 27, aero 19, met 15, chem 3, mfg 8, struct 4, ctrl 6, sens 7, mat 7, elec 3.

## 2. Узлы (главное направление первым)
| Узел | Старая дисц. | Направления | Причина |
|---|---|---|---|
| s7.fnd.brayton_cycle | prop | prop, chem | термодинамика цикла; топливо/горение вторично |
| s8.fnd.boundary_layer | aero | aero | чистая аэродинамика |
| s8.fnd.compressible_flow | aero | aero, prop | сжимаемость важна и для лопаток компрессора |
| s5.fnd.precipitation_hardening | metal | met, struct | механизм упрочнения сплава; применяется в обшивке |
| s8.fnd.transonic_aerodynamics | aero | aero, prop | околозвук: планер и воздухозаборник/лопатки |
| s5.fnd.high_temperature_creep | metal | met, prop | ползучесть сплавов; ограничивает турбину |
| s7.fnd.turbine_heat_transfer | prop | prop, met | теплообмен турбины; нагрузка на материал лопатки |
| s5.fnd.solidification_science | metal | met, mfg | кристаллизация; основа литья лопаток |
| s8.prn.gas_turbine | prop | prop, met, mfg | принцип двигателя; материалы и точное производство ограничивают |
| s8.prn.swept_wing | aero | aero, struct | стреловидность: аэродинамика и силовая схема крыла |
| s8.prn.afterburning | prop | prop, chem | форсаж: тяга и горение топлива |
| s5.prn.gamma_prime_alloy_design | metal | met, chem | фазовый состав никелевого сплава |
| s8.prn.area_rule | aero | aero, struct | правило площадей: форма фюзеляжа и компоновка |
| s8.prn.multi_spool_compression | prop | prop, aero, mfg | многовальная схема; аэродинамика лопаток; точность |
| s8.prn.bypass_engine | prop | prop, aero | двухконтурность; вентилятор и воздухозабор |
| s8.prn.film_cooling | prop | prop, mfg, met | охлаждение лопатки; сверление отверстий; жаростойкость |
| s8.eng.turbojet_g1 | prop | prop, mfg | первый серийный ТРД; ограничен качеством изготовления |
| s8.eng.straight_wing_jet_airframe | aero | aero, struct | планер с прямым крылом |
| s8.eng.turbojet_g2 | prop | prop, met | ТРД 2: жаропрочные сплавы |
| s8.eng.swept_wing_airframe | aero | aero, struct | стреловидное крыло |
| s8.eng.afterburner | prop | prop, chem | форсажная камера |
| s8.eng.all_moving_tail | aero | aero, ctrl | цельноповоротный стабилизатор: управляемость на трансзвуке |
| s8.eng.turbojet_g3 | prop | prop, met, mfg | ТРД 3 |
| s8.eng.area_ruled_airframe | aero | aero, struct | планер по правилу площадей |
| s8.eng.delta_wing_airframe | aero | aero, struct | треугольное крыло |
| s8.eng.variable_geometry_inlet | aero | aero, prop, ctrl | регулируемый воздухозаборник: поток, двигатель, автоматика |
| s8.eng.air_cooled_turbine_blade | prop | prop, met, mfg | охлаждаемая лопатка |
| s8.eng.turbofan_g4 | prop | prop, aero | ТРДД |
| s8.eng.mach2_airframe_g4 | aero | aero, struct, met | М2: нагрев и прочность |
| s5.cmp.heat_resistant_alloy | metal | met, prop | жаропрочный сплав для турбины |
| s5.cmp.aluminium_alloy_sheet | metal | met, struct | листовой алюминий для обшивки |
| s5.cmp.titanium_alloy | metal | met, struct, prop | титан: планер и компрессор |
| s8.cmp.turbine_blade_forged | prop | prop, mfg, met | кованая лопатка |
| s8.cmp.turbine_blade_cast | prop | prop, mfg, met | литая лопатка |
| s8.cmp.turbine_blade_cooled | prop | prop, mfg, met | охлаждаемая лопатка |
| s8.cmp.axial_compressor_assembly | prop | prop, mfg, aero | осевой компрессор |
| s8.cmp.afterburner_unit | prop | prop, chem | форсажный блок |
| s5.prc.light_alloy_rolling | metal | met, mfg | прокатка лёгких сплавов |
| s5.prc.nicr_alloy_melt | metal | met, chem | выплавка нихрома |
| s5.prc.superalloy_melt_g2 | metal | met, chem | выплавка суперсплавов |
| s5.prc.vacuum_induction_melt | metal | met, chem | вакуумная плавка; чистота |
| s5.prc.titanium_reduction_forging | metal | met, mfg, chem | восстановление и ковка титана |
| s5.prc.ds_casting | metal | met, mfg, prop | направленная кристаллизация |
| s5.prc.alloying_element_refining | chem | chem, met | очистка легирующих |
| s5.prc.high_purity_nickel_refining | chem | chem, met | рафинирование никеля |
| s5.prc.ceramic_shell_moulding | chem | chem, mfg, mat | керамическая форма для литья |
| s6.prc.precision_gear_bearing | mech | mfg, prop | точные шестерни и подшипники |
| s6.prc.centrifugal_impeller_machining | mech | mfg, prop | обработка центробежного колеса |
| s6.prc.precision_blade_forging | mech | mfg, met | точная ковка лопаток |
| s6.prc.axial_compressor_machining | mech | mfg, prop | обработка осевого компрессора |
| s6.prc.investment_cast_blades | mech | mfg, met | точное литьё лопаток |
| s6.prc.cooled_blade_drilling | mech | mfg, prop | сверление охлаждающих каналов |
| s8.prc.stressed_skin_assembly | aero | struct, mfg, aero | работающая обшивка |
| s8.prc.jet_engine_assembly_test | prop | prop, mfg | сборка и испытания двигателя |
| s8.prc.swept_wing_fabrication | aero | struct, mfg, aero | изготовление стреловидного крыла |
| s8.prc.machined_integral_skins | aero | struct, mfg, met | монолитные панели |
| s8.prc.afterburner_fabrication | prop | prop, mfg | изготовление форсажной камеры |
| s8.app.jet_fighter_g1 | aero | aero, prop, ord | истребитель 1: планер, двигатель, вооружение |
| s8.app.jet_fighter_g2 | aero | aero, prop, ord | истребитель 2 |
| s8.app.jet_fighter_g3 | aero | aero, prop, sens | истребитель 3: появляется радар |
| s8.app.jet_fighter_g4 | aero | aero, prop, sens, ctrl | истребитель 4 |
| s8.fnd.electronic_flight_control_theory | aero | ctrl, aero | теория электрической СУ |
| s8.fnd.edge_wave_diffraction | aero | sens, aero | дифракция на кромках; основа малозаметности |
| s5.prn.single_crystal_growth | metal | met, prop | монокристалл |
| s5.prn.fibre_composite_laminate_design | chem | mat, struct | расчёт композита |
| s8.prn.low_observable_shaping | aero | sens, aero, struct | формы малозаметности |
| s8.eng.fly_by_wire_relaxed_stability | aero | ctrl, aero | ЭДСУ |
| s8.eng.turbofan_g5 | prop | prop, ctrl, met | ТРДД 5 с цифровым управлением |
| s8.eng.composite_airframe | aero | struct, mat, aero | композитный планер |
| s8.eng.low_observable_airframe | aero | sens, struct, mat | малозаметный планер |
| s8.eng.thrust_vectoring_supercruise_engine | prop | prop, ctrl, aero | управление вектором тяги |
| s8.cmp.turbine_blade_single_crystal | prop | prop, met, mfg | монокристаллическая лопатка |
| s5.prc.single_crystal_casting | metal | met, mfg, prop | литьё монокристаллов |
| s8.prc.composite_layup_cure | aero | mfg, mat, struct | выкладка и отверждение |
| s5.prc.low_observable_finishing | chem | mat, sens, chem | радиопоглощающие покрытия |
| s8.app.jet_fighter_g5 | aero | aero, sens, ctrl, prop | истребитель 5 |
| s8.app.jet_fighter_g6 | aero | aero, sens, ctrl, prop | истребитель 6 |
| s8.eng.aesa_sensor_fusion | aero | sens, ctrl, elec | АФАР и слияние данных |
| s5.cmp.ceramic_matrix_composite | chem | mat, prop, chem | керамокомпозит |
| s5.prc.cmc_fabrication | chem | mfg, mat, chem | производство CMC |
| s8.eng.adaptive_cycle_engine | prop | prop, ctrl, mat | адаптивный цикл |
| s8.prn.phased_array_radar_pesa | aero | sens, elec | ПАР радар |
| s5.prn.gaas_wafer_growth | chem | elec, chem | арсенид галлия |
| s5.cmp.gaas_mmic | chem | elec, sens | GaAs МИС |
| s8.cmp.solid_state_tr_module | aero | elec, sens | твердотельный ПП модуль |
| s8.prn.programmable_signal_processing | aero | ctrl, sens, elec | цифровая обработка сигнала |
| s8.eng.digital_beamforming | aero | sens, ctrl, elec | цифровое формирование луча |
| s8.prn.relaxed_static_stability | aero | aero, ctrl | нестатическая устойчивость |
| s8.cmp.digital_flight_control_computer | aero | ctrl, elec, aero | цифровой компьютер СУ |
| s8.prn.electronic_engine_control | prop | ctrl, prop, elec | электронное управление двигателем |
| s5.cmp.high_modulus_fibre | chem | mat, chem, struct | высокомодульное волокно |
| s8.prn.thrust_vectoring_nozzle | prop | prop, ctrl, struct | сопло с вектором |
| s8.prn.rcs_computational_prediction | aero | sens, ctrl, aero | расчёт ЭПР |
| s8.eng.integrated_avionics_architecture | aero | ctrl, elec, sens | интегр. авионика |
| s5.cmp.silicon_carbide_fibre | chem | mat, chem, prop | SiC волокно |
| s5.prn.matrix_infiltration_cvi | chem | mat, chem, mfg | CVI-пропитка |
| s5.prn.environmental_barrier_coating | chem | mat, chem, prop | защитное покрытие CMC |
| s8.prn.variable_cycle_engine | prop | prop, ctrl, aero | переменный цикл |
| s8.prn.adaptive_third_stream_fan | prop | prop, aero, ctrl | третий контур |

## 3. Институты
Уровни [Предп.]; старые areas в скобках для сравнения. Источники: wikipedia/wikidata как в institutes.yaml (доступ 2026-11-24).
| Институт | founded | Направления:уровни | Причина | Источник |
|---|---|---|---|---|
| usa_naca | 1915 | aero:4,prop:3,struct:2 | Лэнгли: аэродинамические трубы, двигатели (Lewis), конструкции | wiki NACA |
| usa_wright_field | 1917 | prop:3,aero:3,met:2,ctrl:1 | лётные испытания и ЛИ материалов ВВС США | wiki Wright Field |
| usa_ge_research | 1900 | prop:3,met:3,elec:2 | турбонаддув/ТРД I-A (1941), электротехника и вакуумные приборы | wiki GE Research |
| usa_westinghouse_agt | 1945 | prop:3,mfg:2,met:1 | J30/J34, газотурбинное подразделение; 1945 без месяца | wiki Westinghouse AGT |
| usa_nrl | 1923 | sens:4,chem:3,met:3,elec:2 | радар (Тейлор, Янг), металловедение, химия | wiki NRL |
| usa_galcit_jpl | 1936 | prop:3,aero:3,chem:3,ord:2 | ракетные топлива и РДТТ (Парсонс), JATO, снаряды | wiki JPL |
| gbr_rae | 1905 | aero:4,prop:3,struct:3,ctrl:1 | Фарнборо: аэродинамика, двигатели, прочность | wiki RAE |
| gbr_power_jets_ngte | 1936 | prop:4,met:2,aero:2 | Уиттл; национализирован 1944, NGTE 1946 | wiki Power Jets |
| gbr_npl | 1900 | aero:3,met:2,mfg:2,elec:1 | аэротрубы, метрология, металлургия | wiki NPL |
| sun_tsagi | 1918 | aero:4,struct:3,prop:1 | ЦАГИ: трубы, прочность, флаттер | wiki TsAGI |
| sun_ciam | 1930 | prop:3,mfg:2,chem:2 | ЦИАМ: двигатели, испытательные стенды, топлива | wiki CIAM |
| sun_viam | 1932 | met:4,mat:3,chem:3 | ВИАМ: сплавы, неметаллические материалы | wikidata Q4127502 |
| sun_rnii | 1933 | prop:3,ord:3,aero:2,chem:2 | РНИИ: РСЗО «Катюша», ЖРД | wiki Keldysh Center |
| sun_lii | 1941 | aero:3,ctrl:2,struct:2 | ЛИИ: лётные испытания, устойчивость, управляемость | wiki Gromov LII |
| sun_icp | 1931 | chem:4,nuc:2,met:1 | Семёнов: цепные реакции, горение, взрыв; ядерный проект | wiki ICP |
| fra_stae | 1916 | aero:2,prop:2,struct:1 | технич. служба авиации Франции | fr.wiki STAé |
| fra_cnrs | 1939 | chem:3,met:2,nuc:1 | CNRS: фундаментальная наука | wiki CNRS |
| fra_cea | 1945 | nuc:3,chem:2,met:2 | атомная энергия; 18.10.1945 — после t0 | wiki CEA |
| swe_ffa | 1940 | aero:3,struct:2,prop:1 | FFA: аэротрубы, прочность | wiki FOI |
| swe_saab | 1937 | aero:3,struct:2,mfg:2,ord:1 | Saab: проектирование и производство самолётов | wiki Saab |
| swe_foa | 1945 | chem:3,ord:2,nuc:1 | FOA: оборонная химия, баллистика; 1945 без месяца | wiki FOI |
| swe_flygmotor | 1930 | prop:2,mfg:2,met:1 | Flygmotor: двигатели (лицензия Goblin/Avon) | wiki Volvo Aero |
| fra_onera | 1946 | aero:2,prop:1,struct:1 | ONERA: 3.5.1946 (после t0) | wiki ONERA |
| sun_nii88 | 1946 | prop:3,ord:2,ctrl:1 | НИИ-88: ракеты Р-1/Р-2 (с 1946) | wiki TsNIIMash |
| gbr_rre | 1953 | sens:3,elec:2,mat:1 | RRE Малверн: радар/электроника → точнее sens/elec | wikidata Q632106 |
| usa_darpa | 1958 | ctrl:2,elec:2,mat:1 | ARPA 1958: компьютеры, материалы | wiki DARPA |
| fra_cnes | 1961 | prop:2,aero:1,ctrl:1 | CNES 1961: космос | wikidata Q48756 |

### Флаги дат (#311, t0 = 1945-10-01)
- fra_cea: 1945-10-18, после t0 → кандидат на постройку, на t0 нет. [Факт, en.wikipedia.org CEA, 2026-11-25]
- usa_westinghouse_agt: «1945», месяц в источнике не указан → [не проверено]; нужен месяц или пометка month unknown.
- swe_foa: «1945» из трёх организаций, месяц не указан → [не проверено].
- fra_onera (1946), sun_nii88 (1946), gbr_rre (1953), usa_darpa (1958), fra_cnes (1961): после t0 — кандидаты на постройку, флаг не нужен.
- gbr_power_jets_ngte: в данных founded 1936; NGTE образован 1946 из Power Jets (R&D) — [Предп., не проверено], на t0 Power Jets национализирована (1944).

## 4. YAML-блок
```yaml
# nodes: directions: [main, ...]
s7.fnd.brayton_cycle: [prop, chem]
s8.fnd.boundary_layer: [aero]
s8.fnd.compressible_flow: [aero, prop]
s5.fnd.precipitation_hardening: [met, struct]
s8.fnd.transonic_aerodynamics: [aero, prop]
s5.fnd.high_temperature_creep: [met, prop]
s7.fnd.turbine_heat_transfer: [prop, met]
s5.fnd.solidification_science: [met, mfg]
s8.prn.gas_turbine: [prop, met, mfg]
s8.prn.swept_wing: [aero, struct]
s8.prn.afterburning: [prop, chem]
s5.prn.gamma_prime_alloy_design: [met, chem]
s8.prn.area_rule: [aero, struct]
s8.prn.multi_spool_compression: [prop, aero, mfg]
s8.prn.bypass_engine: [prop, aero]
s8.prn.film_cooling: [prop, mfg, met]
s8.eng.turbojet_g1: [prop, mfg]
s8.eng.straight_wing_jet_airframe: [aero, struct]
s8.eng.turbojet_g2: [prop, met]
s8.eng.swept_wing_airframe: [aero, struct]
s8.eng.afterburner: [prop, chem]
s8.eng.all_moving_tail: [aero, ctrl]
s8.eng.turbojet_g3: [prop, met, mfg]
s8.eng.area_ruled_airframe: [aero, struct]
s8.eng.delta_wing_airframe: [aero, struct]
s8.eng.variable_geometry_inlet: [aero, prop, ctrl]
s8.eng.air_cooled_turbine_blade: [prop, met, mfg]
s8.eng.turbofan_g4: [prop, aero]
s8.eng.mach2_airframe_g4: [aero, struct, met]
s5.cmp.heat_resistant_alloy: [met, prop]
s5.cmp.aluminium_alloy_sheet: [met, struct]
s5.cmp.titanium_alloy: [met, struct, prop]
s8.cmp.turbine_blade_forged: [prop, mfg, met]
s8.cmp.turbine_blade_cast: [prop, mfg, met]
s8.cmp.turbine_blade_cooled: [prop, mfg, met]
s8.cmp.axial_compressor_assembly: [prop, mfg, aero]
s8.cmp.afterburner_unit: [prop, chem]
s5.prc.light_alloy_rolling: [met, mfg]
s5.prc.nicr_alloy_melt: [met, chem]
s5.prc.superalloy_melt_g2: [met, chem]
s5.prc.vacuum_induction_melt: [met, chem]
s5.prc.titanium_reduction_forging: [met, mfg, chem]
s5.prc.ds_casting: [met, mfg, prop]
s5.prc.alloying_element_refining: [chem, met]
s5.prc.high_purity_nickel_refining: [chem, met]
s5.prc.ceramic_shell_moulding: [chem, mfg, mat]
s6.prc.precision_gear_bearing: [mfg, prop]
s6.prc.centrifugal_impeller_machining: [mfg, prop]
s6.prc.precision_blade_forging: [mfg, met]
s6.prc.axial_compressor_machining: [mfg, prop]
s6.prc.investment_cast_blades: [mfg, met]
s6.prc.cooled_blade_drilling: [mfg, prop]
s8.prc.stressed_skin_assembly: [struct, mfg, aero]
s8.prc.jet_engine_assembly_test: [prop, mfg]
s8.prc.swept_wing_fabrication: [struct, mfg, aero]
s8.prc.machined_integral_skins: [struct, mfg, met]
s8.prc.afterburner_fabrication: [prop, mfg]
s8.app.jet_fighter_g1: [aero, prop, ord]
s8.app.jet_fighter_g2: [aero, prop, ord]
s8.app.jet_fighter_g3: [aero, prop, sens]
s8.app.jet_fighter_g4: [aero, prop, sens, ctrl]
s8.fnd.electronic_flight_control_theory: [ctrl, aero]
s8.fnd.edge_wave_diffraction: [sens, aero]
s5.prn.single_crystal_growth: [met, prop]
s5.prn.fibre_composite_laminate_design: [mat, struct]
s8.prn.low_observable_shaping: [sens, aero, struct]
s8.eng.fly_by_wire_relaxed_stability: [ctrl, aero]
s8.eng.turbofan_g5: [prop, ctrl, met]
s8.eng.composite_airframe: [struct, mat, aero]
s8.eng.low_observable_airframe: [sens, struct, mat]
s8.eng.thrust_vectoring_supercruise_engine: [prop, ctrl, aero]
s8.cmp.turbine_blade_single_crystal: [prop, met, mfg]
s5.prc.single_crystal_casting: [met, mfg, prop]
s8.prc.composite_layup_cure: [mfg, mat, struct]
s5.prc.low_observable_finishing: [mat, sens, chem]
s8.app.jet_fighter_g5: [aero, sens, ctrl, prop]
s8.app.jet_fighter_g6: [aero, sens, ctrl, prop]
s8.eng.aesa_sensor_fusion: [sens, ctrl, elec]
s5.cmp.ceramic_matrix_composite: [mat, prop, chem]
s5.prc.cmc_fabrication: [mfg, mat, chem]
s8.eng.adaptive_cycle_engine: [prop, ctrl, mat]
s8.prn.phased_array_radar_pesa: [sens, elec]
s5.prn.gaas_wafer_growth: [elec, chem]
s5.cmp.gaas_mmic: [elec, sens]
s8.cmp.solid_state_tr_module: [elec, sens]
s8.prn.programmable_signal_processing: [ctrl, sens, elec]
s8.eng.digital_beamforming: [sens, ctrl, elec]
s8.prn.relaxed_static_stability: [aero, ctrl]
s8.cmp.digital_flight_control_computer: [ctrl, elec, aero]
s8.prn.electronic_engine_control: [ctrl, prop, elec]
s5.cmp.high_modulus_fibre: [mat, chem, struct]
s8.prn.thrust_vectoring_nozzle: [prop, ctrl, struct]
s8.prn.rcs_computational_prediction: [sens, ctrl, aero]
s8.eng.integrated_avionics_architecture: [ctrl, elec, sens]
s5.cmp.silicon_carbide_fibre: [mat, chem, prop]
s5.prn.matrix_infiltration_cvi: [mat, chem, mfg]
s5.prn.environmental_barrier_coating: [mat, chem, prop]
s8.prn.variable_cycle_engine: [prop, ctrl, aero]
s8.prn.adaptive_third_stream_fan: [prop, aero, ctrl]
# institutes: directions: {dir: level}
usa_naca: {aero: 4, prop: 3, struct: 2}
usa_wright_field: {prop: 3, aero: 3, met: 2, ctrl: 1}
usa_ge_research: {prop: 3, met: 3, elec: 2}
usa_westinghouse_agt: {prop: 3, mfg: 2, met: 1}
usa_nrl: {sens: 4, chem: 3, met: 3, elec: 2}
usa_galcit_jpl: {prop: 3, aero: 3, chem: 3, ord: 2}
gbr_rae: {aero: 4, prop: 3, struct: 3, ctrl: 1}
gbr_power_jets_ngte: {prop: 4, met: 2, aero: 2}
gbr_npl: {aero: 3, met: 2, mfg: 2, elec: 1}
sun_tsagi: {aero: 4, struct: 3, prop: 1}
sun_ciam: {prop: 3, mfg: 2, chem: 2}
sun_viam: {met: 4, mat: 3, chem: 3}
sun_rnii: {prop: 3, ord: 3, aero: 2, chem: 2}
sun_lii: {aero: 3, ctrl: 2, struct: 2}
sun_icp: {chem: 4, nuc: 2, met: 1}
fra_stae: {aero: 2, prop: 2, struct: 1}
fra_cnrs: {chem: 3, met: 2, nuc: 1}
fra_cea: {nuc: 3, chem: 2, met: 2}
swe_ffa: {aero: 3, struct: 2, prop: 1}
swe_saab: {aero: 3, struct: 2, mfg: 2, ord: 1}
swe_foa: {chem: 3, ord: 2, nuc: 1}
swe_flygmotor: {prop: 2, mfg: 2, met: 1}
fra_onera: {aero: 2, prop: 1, struct: 1}
sun_nii88: {prop: 3, ord: 2, ctrl: 1}
gbr_rre: {sens: 3, elec: 2, mat: 1}
usa_darpa: {ctrl: 2, elec: 2, mat: 1}
fra_cnes: {prop: 2, aero: 1, ctrl: 1}
```
