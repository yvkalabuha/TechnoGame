# Реальные предпосылки для ранних узлов G5/G6 (TASK-0180, шаг 1)

Дата: 2026-11-25. Автор: research-scientist. Данные: `mods/core/nodes.yaml` (99 узлов), `mods/` не менялся.

## Краткий вывод
- [Факт] Из 38 узлов №62–99 помечено 12 (порог: открытие раньше anchor.year более чем на 15 лет или раньше 1955). Для 11 предложены новые requires только из существующих id; для №79 (CMC) правка не нужна: разрыв 16 лет, но открытие в 2000 разумно (см. примечание).
- [Факт] Хуже всего: №92 (вектор тяги, разрыв 38 лет), №86 (цифровая обработка сигнала, 34), №82 (ФАР, 30), №95 (SiC-волокно, 30), №62 (теория электронного управления, 25), №97 (EBC, 25), №99 (третий контур, 23), №90 (электронное управление двигателем, 19), №91 (углеволокно, 18), №63 (дифракция краевых волн, 17).
- [Предп.] «Год открытия сейчас» = max(anchor.year прямых requires), для any_of = min по группе, без requires = 1945. Это нижняя граница: реальное открытие зависит ещё от стартовых знаний сценария и скорости исследований.
- После правок самые ранние открытия: 1953–1976 вместо 1945–1953; разрывы 5–16 лет. Цикл проверен скриптом на полном графе с новыми requires: циклов нет.
- [Факт] В nodes.yaml нет узлов по химии полимеров/углерода и по электромагнитному рассеянию. №91, №63, №83 привязаны к ближайшему узлу-заменителю (прокси) и всё равно открываются в 1953–1960. Нужны новые узлы (решение задачи данных/владельца; я их не выдумываю).
- Каскад: открытие потомков (№64–81, 84–85, 87–89, 93–94, 96, 98) сдвигается автоматически через anchor предков; их собственный разрыв ≤13 лет, правок не требуется.

## Предложения (только помеченные узлы)
Колонки: узел, anchor, текущие requires, самый ранний год открытия сейчас, предлагаемые requires, год после, источник (доступ 2026-11-25).

| Узел | Anchor | Текущие requires | Открытие сейчас | Предлагаемые requires | Открытие после | Источник |
|---|---|---|---|---|---|---|
| 62 s8.fnd.electronic_flight_control_theory | 1972 | s8.fnd.transonic_aerodynamics | 1947 | s8.fnd.transonic_aerodynamics, s8.eng.variable_geometry_inlet | 1960 (разрыв 12) | F-8 DFBW 1972: https://en.wikipedia.org/wiki/Fly-by-wire |
| 63 s8.fnd.edge_wave_diffraction | 1962 | s8.fnd.compressible_flow | 1945 | s8.fnd.compressible_flow, s8.eng.delta_wing_airframe | 1954 (разрыв 8) | Ufimtsev 1962: https://en.wikipedia.org/wiki/Pyotr_Ufimtsev |
| 82 s8.prn.phased_array_radar_pesa | 1975 | — | 1945 | s8.fnd.edge_wave_diffraction | 1962 (разрыв 13) | https://en.wikipedia.org/wiki/Phased_array |
| 83 s5.prn.gaas_wafer_growth | 1965 | s5.fnd.solidification_science | 1953 | s5.fnd.solidification_science, s7.fnd.turbine_heat_transfer | 1960 (разрыв 5) | https://en.wikipedia.org/wiki/Gallium_arsenide |
| 86 s8.prn.programmable_signal_processing | 1979 | — | 1945 | s8.prn.electronic_engine_control | 1969 (разрыв 10) | https://en.wikipedia.org/wiki/Digital_signal_processor |
| 90 s8.prn.electronic_engine_control | 1969 | s8.eng.turbojet_g3 | 1950 | s8.eng.turbojet_g3, s8.eng.variable_geometry_inlet, s8.eng.turbofan_g4 | 1964 (разрыв 5) | https://en.wikipedia.org/wiki/FADEC |
| 91 s5.cmp.high_modulus_fibre | 1963 | — | 1945 | s5.fnd.solidification_science | 1953 (разрыв 10) | https://en.wikipedia.org/wiki/Carbon_fiber |
| 92 s8.prn.thrust_vectoring_nozzle | 1989 | s8.eng.afterburner | 1951 | s8.eng.afterburner, s8.cmp.digital_flight_control_computer | 1976 (разрыв 13) | https://en.wikipedia.org/wiki/Thrust_vectoring |
| 95 s5.cmp.silicon_carbide_fibre | 1975 | — | 1945 | s5.cmp.high_modulus_fibre | 1963 (разрыв 12) | https://en.wikipedia.org/wiki/Silicon_carbide_fibers |
| 97 s5.prn.environmental_barrier_coating | 2000 | s5.cmp.silicon_carbide_fibre | 1975 | s5.cmp.silicon_carbide_fibre, s8.eng.thrust_vectoring_supercruise_engine | 1990 (разрыв 10) | https://en.wikipedia.org/wiki/Ceramic_matrix_composite |
| 99 s8.prn.adaptive_third_stream_fan | 2013 | s8.prn.variable_cycle_engine | 1990 | s8.prn.variable_cycle_engine, s8.eng.aesa_sensor_fusion | 1997 (разрыв 16) | https://en.wikipedia.org/wiki/Adaptive_Versatile_Engine_Technology |

### Обоснования (одна строка на узел)
- 62: [Факт] первый цифровой fly-by-wire без механического резерва — F-8, 1972; предпосылка — высокоскоростные самолёты с настраиваемыми входными устройствами/аэроупругостью, ближайший узел 1960 — variable_geometry_inlet. [Предп.] Строже: mach2_airframe_g4 (1972) даст разрыв 0.
- 63: [Факт] теория краевых волн Уфимцева, 1962. Узла по ЭМ-рассеянию нет; прокси — delta_wing_airframe (острые кромки, 1954). Остаётся открытие в 1954 (<1955), нужен новый узел-основа.
- 82: [Факт] фазированные решётки: AN/SPS-33 ~1961, PAVE PAWS 1979. Волновая теория (63) — единственная подходящая предпосылка. Пока anchor 1975 — [Предп.] стоит проверить год в данных.
- 83: [Факт] GaAs: свойства 1951–52, кристаллы с 1954; рост кристаллов управляется теплопереносом — turbine_heat_transfer (1960) как прокси.
- 86: [Факт] программируемые ЦОС-микросхемы ~1980; предпосылка — цифровое электронное управление (1969). Цикла нет (90 не зависит от 86).
- 90: [Факт] аналоговое полное управление Olympus 593 (1960-е), цифровая система 1968. Подходят входной аппарат (1960) и турбовентиляторный двигатель (1964).
- 91: [Факт] ранняя карбоновая арматура (вентилятор Hyfil, 1968). Подходящего узла химии углерода нет; ближайший по материаловедению — solidification_science (1953). Нужен новый узел.
- 92: [Факт] F-22 с вектором тяги — 2005 (задолго до — демонстраторы). Сопло интегрируется с цифровым управлением полётом — digital_flight_control_computer (1976).
- 95: [Факт] процесс Яджимы, 1975 (полимерный предшественник, пиролиз) — родственен технологии углеволокна.
- 97: [Факт] защитные покрытия для SiC/SiC испытаны в 2002; потребность задавал горячий тракт двигателей 1990-х — supercruise_engine (1990). Цикла нет (79 зависит от 97, не наоборот).
- 99: [Факт] ADVENT 2007, цель — снизить температуру охлаждающего воздуха (тепловой менеджмент нагрузки авионики) — предпосылка aesa_sensor_fusion (1997); разрыв 16 — граничный. Альтернатива: jet_fighter_g6 (2005), но это делает истребитель условием знания — не рекомендую.
- 79 (без правки): [Факт] CMC в серийных двигателях с 2016–2017 (GE9X, LEAP), исследования с 1990-х; открытие в 2000 допустимо.

## Все узлы 62–99 (текущее состояние)
| № | Узел | Anchor | Текущие requires | Открытие сейчас | Флаг |
|---|---|---|---|---|---|
| 62 | s8.fnd.electronic_flight_control_theory | 1972 | s8.fnd.transonic_aerodynamics | 1947 | да |
| 63 | s8.fnd.edge_wave_diffraction | 1962 | s8.fnd.compressible_flow | 1945 | да |
| 64 | s5.prn.single_crystal_growth | 1974 | s5.prc.ds_casting | 1969 | нет |
| 65 | s5.prn.fibre_composite_laminate_design | 1970 | s5.cmp.aluminium_alloy_sheet, s5.cmp.high_modulus_fibre | 1963 | нет |
| 66 | s8.prn.low_observable_shaping | 1975 | s8.fnd.edge_wave_diffraction, s8.prn.rcs_computational_prediction | 1975 | нет |
| 67 | s8.eng.fly_by_wire_relaxed_stability | 1974 | s8.fnd.electronic_flight_control_theory, s8.eng.all_moving_tail, s8.prn.relaxed_static_stability | 1972 | нет |
| 68 | s8.eng.turbofan_g5 | 1981 | s8.eng.turbofan_g4, s8.fnd.electronic_flight_control_theory, s8.prn.electronic_engine_control | 1972 | нет |
| 69 | s8.eng.composite_airframe | 1978 | s5.prn.fibre_composite_laminate_design, s8.eng.mach2_airframe_g4 | 1972 | нет |
| 70 | s8.eng.low_observable_airframe | 1977 | s8.prn.low_observable_shaping, s8.eng.fly_by_wire_relaxed_stability | 1975 | нет |
| 71 | s8.eng.thrust_vectoring_supercruise_engine | 1990 | s8.eng.turbofan_g5, s8.cmp.turbine_blade_single_crystal, s8.prn.thrust_vectoring_nozzle | 1989 | нет |
| 72 | s8.cmp.turbine_blade_single_crystal | 1982 | s5.prn.single_crystal_growth, s8.cmp.turbine_blade_cooled | 1974 | нет |
| 73 | s5.prc.single_crystal_casting | 1982 | s5.prn.single_crystal_growth, s5.prc.ds_casting | 1974 | нет |
| 74 | s8.prc.composite_layup_cure | 1978 | s5.prn.fibre_composite_laminate_design | 1970 | нет |
| 75 | s5.prc.low_observable_finishing | 1981 | s8.prn.low_observable_shaping | 1975 | нет |
| 76 | s8.app.jet_fighter_g5 | 1984 | s8.eng.fly_by_wire_relaxed_stability, any_of(s8.eng.composite_airframe, s8.eng.turbofan_g5), any_of(s8.prn.electronic_engine_control, s8.cmp.digital_flight_control_computer) | 1978 | нет |
| 77 | s8.app.jet_fighter_g6 | 2005 | s8.eng.thrust_vectoring_supercruise_engine, s8.eng.low_observable_airframe, s8.eng.aesa_sensor_fusion, s8.eng.integrated_avionics_architecture | 1997 | нет |
| 78 | s8.eng.aesa_sensor_fusion | 1997 | s8.cmp.solid_state_tr_module | 1988 | нет |
| 79 | s5.cmp.ceramic_matrix_composite | 2016 | s8.cmp.turbine_blade_single_crystal, s5.prc.ceramic_shell_moulding, s5.cmp.silicon_carbide_fibre, s5.prn.environmental_barrier_coating | 2000 | да |
| 80 | s5.prc.cmc_fabrication | 2016 | s5.cmp.ceramic_matrix_composite, s5.prc.ceramic_shell_moulding, s5.prn.matrix_infiltration_cvi | 2016 | нет |
| 81 | s8.eng.adaptive_cycle_engine | 2021 | s8.eng.thrust_vectoring_supercruise_engine, s5.cmp.ceramic_matrix_composite, s8.prn.adaptive_third_stream_fan | 2016 | нет |
| 82 | s8.prn.phased_array_radar_pesa | 1975 | — | 1945 | да |
| 83 | s5.prn.gaas_wafer_growth | 1965 | s5.fnd.solidification_science | 1953 | да |
| 84 | s5.cmp.gaas_mmic | 1976 | s5.prn.gaas_wafer_growth | 1965 | нет |
| 85 | s8.cmp.solid_state_tr_module | 1988 | s5.cmp.gaas_mmic, s8.prn.phased_array_radar_pesa | 1976 | нет |
| 86 | s8.prn.programmable_signal_processing | 1979 | — | 1945 | да |
| 87 | s8.eng.digital_beamforming | 1990 | s8.prn.programmable_signal_processing, s8.prn.phased_array_radar_pesa | 1979 | нет |
| 88 | s8.prn.relaxed_static_stability | 1972 | s8.fnd.electronic_flight_control_theory, s8.eng.all_moving_tail | 1972 | нет |
| 89 | s8.cmp.digital_flight_control_computer | 1976 | s8.fnd.electronic_flight_control_theory | 1972 | нет |
| 90 | s8.prn.electronic_engine_control | 1969 | s8.eng.turbojet_g3 | 1950 | да |
| 91 | s5.cmp.high_modulus_fibre | 1963 | — | 1945 | да |
| 92 | s8.prn.thrust_vectoring_nozzle | 1989 | s8.eng.afterburner | 1951 | да |
| 93 | s8.prn.rcs_computational_prediction | 1975 | s8.fnd.edge_wave_diffraction | 1962 | нет |
| 94 | s8.eng.integrated_avionics_architecture | 1986 | s8.cmp.digital_flight_control_computer, s8.prn.programmable_signal_processing | 1979 | нет |
| 95 | s5.cmp.silicon_carbide_fibre | 1975 | — | 1945 | да |
| 96 | s5.prn.matrix_infiltration_cvi | 1977 | s5.cmp.silicon_carbide_fibre | 1975 | нет |
| 97 | s5.prn.environmental_barrier_coating | 2000 | s5.cmp.silicon_carbide_fibre | 1975 | да |
| 98 | s8.prn.variable_cycle_engine | 1990 | s8.eng.turbofan_g5 | 1981 | нет |
| 99 | s8.prn.adaptive_third_stream_fan | 2013 | s8.prn.variable_cycle_engine | 1990 | да |

## Не проверено
- Год открытия не учитывает стартовые знания 1945 (scenario_1945.yaml) и цепочки через any_of; не моделировался темп исследований.
- Источники — страницы Википедии (проверены выдержки через WebFetch, не первичные работы; CC BY-SA, данные не копировались). Первичные: Ufimtsev 1962, Yajima 1975 — не открывались.
- Годы anchor в данных (напр. №82 PESA = 1975) не пересматривались.

## OPEN
- Решение владельца/data-задачи: добавить узлы «ЭМ-рассеяние/радиолокация», «углеродные материалы/полимерная химия» (для №63, №91, №83, №82).
