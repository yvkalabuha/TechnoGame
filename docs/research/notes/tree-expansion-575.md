# Расширение дерева знаний: 60 узлов 1942–2021 (#575, TASK-0188, шаг 1)

Автор: research-scientist, 2026-11-25. Статус: [Предл.] — предложение для шага 2 (developer). mods/ не менялся. Основание: решение владельца #575; направления — `docs/research/notes/directions-nodes-institutes.md`; research_speed и слова военной ценности — `docs/game-design/SCIENCE_SLOTS.md` §3.2, §6.

## Краткий вывод
- [Предл.] **60 новых узлов** (в рамке 40–60): вычисления 14, электроника 10, мосты к реактивной ветке 4, материалы 6, ракеты 7, космос 10 (отдельная область), ядро 9. Все id в шаблоне `sN.kind.name`, номера 100–159, уникальны, ссылки `requires` на существующие id проверены по mods/core/nodes.yaml (0 несуществующих), граф с 99 старыми узлами ацикличен (проверено скриптом), год предпосылки ≤ года узла у всех.
- [Предл.] **research_speed** — у 15 узлов (вычисления, СБИС, Монте-Карло, телескоп): бонус 0,02–0,05 на узел (потолок 0,08 из SCIENCE_SLOTS §6 не превышен). Сумма к 2021 по направлениям: aero 0.22, prop 0.22, nuc 0.21, ctrl 0.15, mat 0.16, elec 0.12, struct 0.11, chem 0.08, sens 0.04; met/mfg 0,02, ord 0,04. Максимум 0,22 < потолка R 0,25 [ЗАГЛУШКА]. Ранние узлы (1946–1964) дают 0,02–0,03, основной прирост — 1964–1996: ускоритель окупается позже прямого военного узла (свойство из SCIENCE_SLOTS §10).
- [Предл.] **Связь с реактивной веткой**: 4 моста (РЛС управления огнём 1951, ИК-ракета 1953, инерциальная навигация 1953, импульсно-доплеровская РЛС 1966) и рёбра `informs` от 13 новых узлов к 21 существующим (avionics, радар, ЭДСУ, композиты, лопатки). Существующие `requires` не менял и менять не предлагаю.
- [Факт] 45 из 60 источников прочитаны прочтением страницы источника 2026-11-25; **15 узлов (04, 06, 09, 15, 17, 25, 27, 28, 30, 31, 32, 33, 34, 43, 59) не подтверждены страницей, из них 16 с `estimated: true`** (`estimated: true`) — страница не дала года или страница не найдена (панельный метод: 404). Эти даты надо перепроверить по первичному источнику до шага 2.
- [Предп.] Поколение `gen` и тип стоимости: тип узла = его kind (FND/PRN/ENG/CMP/PRC), `gen` по году (≤1950 → 1, ≤1960 → 2, ≤1970 → 3, ≤1985 → 4, ≤2000 → 5, иначе 6; «до 1945» → 0). У существующих узлов 1965–1990 стоит gen 5–6 (по поколению истребителя), поэтому новые узлы той же эпохи будут дешевле — масштаб цены калибрует TASK-0185.
- [Предл.] Новые префиксы стадий: **s9** вычисления/электроника, **s10** космос, **s11** ядро; материалы идут в s5, ракеты в s7, мосты в s8, станки в s6. Нужна проверка, не парсит ли код префикс (см. OPEN).

## Правила, принятые в таблицах
- Военная ценность по SCIENCE_SLOTS §3.2: **direct** — меняет `perf` или поколение изделия jet-среза; **prep** — процесс (PRC); **base** — цепочка/основа (FND или `informs` в jet-ветку); иначе «—» (в срезе нет изделия: ракеты, спутники — вне этой задачи).
- research_speed: `{dirs, bonus}`, бонус — доля к R для направлений (в SCIENCE_SLOTS §6 веса: основное направление 0,5, остальные делят 0,5).
- Hazard: none / hz / key по образцу существующих узлов (hz — рисковый, key — ключевой).
- Источник — номер в разделе «Источники»; везде доступ 2026-11-25. Данные из источников не копируются (ADR-0009), только годы и факты со ссылкой; страницы Wikipedia (CC BY-SA) — указатель, текст не цитируется.

## Вычислительная техника (ускорители науки) (14)
| № | id | RU / EN | Год | Тип·gen·hz | requires | Напр. | research_speed | Воен. | Обоснование |
|---|---|---|---|---|---|---|---|---|---|
| 01 | s9.fnd.stored_program_architecture | Архитектура с хранимой программой / Stored-program architecture | 1945 | FND·1·none | — | ctrl, elec | — | основа | Отчёт фон Неймана об EDVAC, 30.06.1945: программа и данные в одной памяти. [ист. 01] |
| 02 | s9.eng.electronic_computer_g1 | Ламповая ЭВМ (ENIAC) / Vacuum-tube electronic computer (ENIAC) | 1946 | ENG·1·none | — | ctrl, ord, aero | ord/aero +2% | — | ENIAC, открыт 15.02.1946, ~18 000 ламп; баллистические таблицы; быстрее ручного счёта на порядки. [ист. 02] |
| 03 | s9.eng.stored_program_computer | ЭВМ с хранимой программой (EDSAC) / Stored-program computer (EDSAC) | 1949 | ENG·1·none | s9.fnd.stored_program_architecture, s9.eng.electronic_computer_g1 | ctrl, elec | prop/nuc +2% | — | EDSAC, первая программа 06.05.1949; универсальная машина для расчётов науки. [ист. 03] |
| 04 | s6.prc.numerical_control_machining | Станки с числовым программным управлением / Numerically controlled machining | 1952~ | PRC·2·none | s6.prc.precision_gear_bearing | mfg, ctrl, struct | — | готовит производство | MIT (Servomechanisms Lab, ВВС США) показал фрезерование по перфоленте в 1952; путь к монолитным панелям авиации. [ист. 04] |
| 05 | s9.prn.high_level_language_compiler | Язык высокого уровня и компилятор (FORTRAN) / High-level language and compiler (FORTRAN) | 1957 | PRN·2·none | s9.eng.stored_program_computer | ctrl, elec | aero/prop/struct/nuc +2% | — | Первый компилятор FORTRAN, апрель 1957: инженеры пишут расчёты без машинных кодов. [ист. 05] |
| 06 | s9.prn.finite_element_method | Метод конечных элементов / Finite element method | 1956~ | PRN·2·none | s9.eng.stored_program_computer | struct, aero, ctrl | struct +4% | основа | Turner, Clough, Martin, Topp (Boeing) 1956: расчёт планера матрицами жёсткости; термин FEM — Clough 1960 [по памяти]. [ист. 06] |
| 07 | s9.cmp.transistor_computer_g2 | Транзисторная ЭВМ (IBM 7090) / Transistor computer (IBM 7090) | 1959 | CMP·2·none | s9.eng.stored_program_computer, s9.cmp.silicon_transistor | ctrl, elec | aero/prop/struct/nuc +3% | — | IBM 7090, первая поставка декабрь 1959, транзисторная; надёжность и скорость выше ламп. [ист. 07] |
| 08 | s9.eng.mainframe_family_g3 | Семейство мейнфреймов и суперЭВМ 3-го поколения (CDC 6600) / Third-generation mainframes (CDC 6600) | 1964 | ENG·3·none | s9.cmp.transistor_computer_g2, s9.cmp.silicon_transistor | ctrl, elec | все +2% | — | CDC 6600 (С. Крэй), сентябрь 1964, ~1 Мфлопс; IBM System/360 в апреле 1964 [по памяти]. [ист. 08] |
| 09 | s9.prn.computational_fluid_dynamics | Численная аэродинамика (панельные методы, CFD) / Computational fluid dynamics (panel methods) | 1967~ | PRN·3·none | s9.cmp.transistor_computer_g2, s8.fnd.compressible_flow | aero, prop, ctrl | aero/prop +5% | основа | Hess и Smith (Douglas), панельный метод, 1967 [по памяти]: обтекание без трубы. [ист. 09] |
| 10 | s9.cmp.microprocessor | Микропроцессор / Microprocessor | 1971 | CMP·4·none | s9.prc.mos_lsi_manufacturing | elec, ctrl | ctrl/elec +3% | основа | Intel 4004, 15.11.1971: процессор на одном кристалле; основа бортовых ЭВМ. [ист. 10] |
| 11 | s9.eng.vector_supercomputer | Векторный суперкомпьютер (Cray-1) / Vector supercomputer (Cray-1) | 1976 | ENG·4·hz | s9.eng.mainframe_family_g3, s9.prc.planar_monolithic_ic_manufacturing | ctrl, aero, nuc, prop | aero/prop/nuc/mat +4% | — | Cray-1, первая установка 1976 в Лос-Аламосе; расчёты течений и ядерных процессов. [ист. 11] |
| 12 | s9.eng.massively_parallel_hpc | Массово-параллельный суперкомпьютер (ASCI Red) / Massively parallel supercomputer (ASCI Red) | 1996 | ENG·5·none | s9.eng.vector_supercomputer, s9.prn.vlsi_design_methodology | ctrl, nuc, aero | nuc/aero/prop/mat +4% | — | ASCI Red, 1,06 Тфлопс, декабрь 1996; моделирование ядерного арсенала без испытаний. [ист. 12] |
| 13 | s9.eng.gpu_accelerated_computing | Ускорители GPU для науки (CUDA) / GPU-accelerated scientific computing (CUDA) | 2007 | ENG·6·none | s9.eng.massively_parallel_hpc | ctrl, elec, mat | mat/chem/ctrl/elec +3% | — | CUDA, выпуск 2007: универсальные расчёты на видеочипах. [ист. 13] |
| 14 | s9.prn.deep_learning_science | Глубокое обучение в науке (AlphaFold 2) / Deep learning for science (AlphaFold 2) | 2020 | PRN·6·hz | s9.eng.gpu_accelerated_computing | ctrl, chem, mat | chem/mat/ctrl +3% | — | AlphaFold 2 выиграл CASP14 в ноябре 2020: предсказание структур белков. [ист. 14] |

## Электроника (10)
| № | id | RU / EN | Год | Тип·gen·hz | requires | Напр. | research_speed | Воен. | Обоснование |
|---|---|---|---|---|---|---|---|---|---|
| 15 | s9.fnd.semiconductor_physics | Физика полупроводников / Semiconductor physics | до 1945 | FND·0·none | — | elec, mat | — | основа | Зонная теория (Wilson 1931), знание на старте; Нобелевская 1956 за полупроводники [по памяти]. [ист. 15] |
| 16 | s9.prn.point_contact_transistor | Точечный транзистор / Point-contact transistor | 1947 | PRN·1·none | s9.fnd.semiconductor_physics | elec, mat | — | — | Bell Labs (Bardeen, Brattain), 23.12.1947. [ист. 16] |
| 17 | s5.prc.zone_refining_semiconductor | Зонная очистка германия и кремния / Zone refining of semiconductors | 1952~ | PRC·2·none | s5.fnd.solidification_science | chem, met, elec | — | готовит производство | W. Pfann (Bell Labs), зонная плавка 1952 [по памяти]: сверхчистые кристаллы. [ист. 17] |
| 18 | s9.cmp.silicon_transistor | Кремниевый транзистор / Silicon transistor | 1954 | CMP·2·none | s9.prn.point_contact_transistor, s5.prc.zone_refining_semiconductor | elec, mat | — | — | Teal (Texas Instruments), апрель 1954: первый коммерческий кремниевый транзистор. [ист. 18] |
| 19 | s9.cmp.integrated_circuit | Интегральная схема / Integrated circuit | 1958 | CMP·2·none | s9.cmp.silicon_transistor | elec, ctrl, mat | — | основа | Kilby (Texas Instruments), 12.09.1958; военные заказы (ВВС США) ускорили развитие. [ист. 19] |
| 20 | s9.prc.planar_monolithic_ic_manufacturing | Планарная технология монолитных схем / Planar process for monolithic ICs | 1959 | PRC·2·none | s9.cmp.integrated_circuit | elec, mfg, chem | — | готовит производство | Fairchild, Hoerni 1959; серийные схемы с 1961. [ист. 20] |
| 21 | s9.prn.laser_principle | Лазер / Laser | 1960 | PRN·2·none | s9.fnd.semiconductor_physics | elec, sens, ord | — | — | Maiman, 16.05.1960, рубиновый лазер; дальномеры и целеуказание. [ист. 21] |
| 22 | s9.prc.mos_lsi_manufacturing | Кремниевый затвор и МОП БИС / Silicon-gate MOS LSI process | 1968 | PRC·3·none | s9.prc.planar_monolithic_ic_manufacturing | elec, mfg, chem | — | готовит производство | Fairchild, Faggin, рабочие кремний-затворные МОП транзисторы к апрелю 1968. [ист. 22] |
| 23 | s9.prn.vlsi_design_methodology | Проектирование СБИС (Mead–Conway) / VLSI design methodology (Mead-Conway) | 1979 | PRN·4·none | s9.prc.mos_lsi_manufacturing, s9.cmp.microprocessor | elec, ctrl | elec/ctrl +4% | — | Учебник «Introduction to VLSI Systems», 1979: стандартизированное проектирование схем на ЭВМ. [ист. 23] |
| 24 | s9.prc.euv_lithography | Литография в крайнем ультрафиолете (EUV) / Extreme ultraviolet lithography | 2019 | PRC·6·key | s9.prn.vlsi_design_methodology, s9.prn.laser_principle | mfg, elec, chem, mat | — | готовит производство | Серийное производство на EUV, 2019 (ASML). [ист. 24] |

## Мосты к существующим узлам: радиолокация, ракеты, навигация (4)
| № | id | RU / EN | Год | Тип·gen·hz | requires | Напр. | research_speed | Воен. | Обоснование |
|---|---|---|---|---|---|---|---|---|---|
| 25 | s8.eng.fire_control_radar_interceptor | Бортовая РЛС управления огнём перехватчика / Interceptor airborne fire-control radar | 1951~ | ENG·2·none | — | sens, ctrl, ord | — | прямо усиливает | F-86D «Sabre Dog», поступил 1951, всепогодный перехватчик с РЛС и ракетными залпами [по памяти]. [ист. 25] |
| 26 | s8.eng.infrared_air_to_air_missile | Ракета «воздух–воздух» с ИК-головкой (AIM-9) / Infrared air-to-air missile (AIM-9) | 1953 | ENG·2·none | — | ord, sens, elec | — | прямо усиливает | Первый перехват мишени 11.09.1953; на вооружение флота США в 1956. [ист. 26] |
| 27 | s8.eng.inertial_navigation_system | Инерциальная навигация / Inertial navigation system | 1953~ | ENG·2·none | s6.prc.precision_gear_bearing | ctrl, aero, ord | — | основа | MIT (Draper), система SPIRE в трансконтинентальном перелёте 1953 [по памяти]. [ист. 27] |
| 28 | s8.prn.pulse_doppler_look_down_radar | Импульсно-доплеровская РЛС с обзором вниз / Pulse-Doppler look-down radar | 1966~ | PRN·3·hz | s8.eng.fire_control_radar_interceptor, s9.cmp.integrated_circuit | sens, elec, ctrl | — | прямо усиливает | Импульсно-доплеровские РЛС на истребителях с 1960-х (F-4J, AWG-10 [по памяти]). [ист. 28] |

## Материалы (6)
| № | id | RU / EN | Год | Тип·gen·hz | requires | Напр. | research_speed | Воен. | Обоснование |
|---|---|---|---|---|---|---|---|---|---|
| 29 | s5.cmp.maraging_steel | Мартенситно-стареющая сталь / Maraging steel | 1959~ | CMP·2·none | s5.prc.vacuum_induction_melt | met, struct, ord | — | — | Inco (Bieber), конец 1950-х; корпуса ракет и роторы. [ист. 29] |
| 30 | s5.cmp.ablative_heat_shield | Абляционный теплозащитный экран / Ablative heat shield | 1958~ | CMP·2·none | s7.fnd.blunt_body_reentry | mat, struct, chem | — | — | Боевые части МБР 1950-х; Allen и Eggers (NACA) — затупленная форма 1951 [год абляции не проверен]. [ист. 30] |
| 31 | s5.cmp.carbon_carbon_composite | Углерод-углеродный композит / Carbon-carbon composite | 1959~ | CMP·2·none | s5.cmp.ablative_heat_shield | mat, struct, prop | — | — | RCC разработан для боевых частей МБР (Chance Vought 1958–59 [по памяти]); позже носовой конус «Шаттла». [ист. 31] |
| 32 | s5.prc.hot_isostatic_pressing | Горячее изостатическое прессование / Hot isostatic pressing | 1955~ | PRC·2·none | s5.prc.vacuum_induction_melt | mfg, met, mat | — | готовит производство | Изобретено в 1950-х в Battelle (год 1955 [по памяти]); уплотнение отливок и порошков. [ист. 32] |
| 33 | s5.cmp.thermal_barrier_coating | Теплозащитное покрытие лопаток / Thermal barrier coating | 1976~ | CMP·4·none | s5.cmp.heat_resistant_alloy, s8.cmp.turbine_blade_cooled | mat, prop, met | — | прямо усиливает | Керамика на лопатках, NASA Lewis, 1970-е (год оценка). [ист. 33] |
| 34 | s5.prc.additive_manufacturing_metal | Аддитивное производство металлов / Metal additive manufacturing | 2015~ | PRC·6·none | s9.prn.laser_principle, s5.prc.hot_isostatic_pressing | mfg, mat, met | — | готовит производство | Топливные форсунки LEAP, сертификация FAA ~2015: лазерное спекание. [ист. 34] |

## Ракетная техника (7)
| № | id | RU / EN | Год | Тип·gen·hz | requires | Напр. | research_speed | Воен. | Обоснование |
|---|---|---|---|---|---|---|---|---|---|
| 35 | s7.eng.liquid_rocket_engine_g1 | Жидкостный ракетный двигатель (А-4/V-2) / Liquid-propellant rocket engine (A-4/V-2) | 1942 | ENG·1·none | — | prop, ord, chem, ctrl | — | — | Первый удачный пуск 03.10.1942, боевое применение с сентября 1944. [ист. 35] |
| 36 | s7.prn.composite_solid_propellant | Смесевое твёрдое топливо / Composite solid propellant | 1954 | PRN·2·none | — | chem, prop, ord | — | — | Parsons (Caltech) асфальт и перхлорат с 1942; Atlantic Research 1954 — алюминий в смеси. [ист. 36] |
| 37 | s7.fnd.blunt_body_reentry | Затупленное тело: теория входа в атмосферу / Blunt-body re-entry theory | 1951 | FND·2·none | s8.fnd.transonic_aerodynamics | aero, struct, mat | — | основа | Allen и Eggers (NACA), 1951: затупленная форма снижает нагрев. [ист. 37] |
| 38 | s7.eng.intercontinental_ballistic_missile | Межконтинентальная баллистическая ракета (Р-7) / Intercontinental ballistic missile (R-7) | 1957 | ENG·2·key | s7.eng.liquid_rocket_engine_g1, s8.eng.inertial_navigation_system | prop, ord, ctrl, struct | — | — | Р-7: первый успешный полёт 21.08.1957. [ист. 38] |
| 39 | s7.eng.cryogenic_hydrogen_engine | Водородный криогенный двигатель (RL10) / Cryogenic hydrogen engine (RL10) | 1963 | ENG·3·hz | s7.eng.liquid_rocket_engine_g1 | prop, chem, mat | — | — | RL10, первый успешный полёт 27.11.1963 (Centaur). [ист. 39] |
| 40 | s7.eng.staged_combustion_hydrogen_engine | Двигатель замкнутого цикла на водороде (SSME) / Staged-combustion hydrogen engine (SSME/RS-25) | 1981 | ENG·4·hz | s7.eng.cryogenic_hydrogen_engine, s5.cmp.heat_resistant_alloy | prop, met, mat, ctrl | — | — | RS-25: многоразовый двигатель, первый полёт 12.04.1981 (STS-1), двухвальная схема с догоранием. [ист. 40] |
| 41 | s7.eng.reusable_booster_landing | Возвращаемая первая ступень (Falcon 9) / Reusable booster landing (Falcon 9) | 2015 | ENG·6·hz | s7.eng.staged_combustion_hydrogen_engine, s9.eng.massively_parallel_hpc | prop, ctrl, struct | — | — | Первая успешная посадка ступени, декабрь 2015. [ист. 41] |

## Космические исследования (отдельная область) (10)
| № | id | RU / EN | Год | Тип·gen·hz | requires | Напр. | research_speed | Воен. | Обоснование |
|---|---|---|---|---|---|---|---|---|---|
| 42 | s10.fnd.orbital_mechanics | Небесная механика полёта: астродинамика / Astrodynamics | до 1945 | FND·0·none | — | ctrl, prop | — | основа | Кеплер, Гоман (1925); Herrick с 1930-х. [ист. 42] |
| 43 | s10.eng.sounding_rocket_science | Исследования верхней атмосферы на ракетах / Sounding-rocket upper-atmosphere science | 1946~ | ENG·1·none | s7.eng.liquid_rocket_engine_g1 | sens, prop, ctrl | — | — | Трофейные А-4 с приборами, White Sands, 1946; наука с высот 100+ км [по памяти]. [ист. 43] |
| 44 | s10.eng.artificial_satellite | Искусственный спутник Земли / Artificial satellite | 1957 | ENG·2·none | s7.eng.intercontinental_ballistic_missile, s10.fnd.orbital_mechanics | prop, ctrl, elec, sens | — | — | Спутник-1, 04.10.1957. [ист. 44] |
| 45 | s10.eng.scientific_satellite | Научный спутник (Explorer 1) / Scientific satellite (Explorer 1) | 1958 | ENG·2·none | s10.eng.artificial_satellite, s9.cmp.silicon_transistor | sens, elec, nuc | — | — | Explorer 1, 31.01/01.02.1958: радиационные пояса Ван Аллена. [ист. 45] |
| 46 | s10.eng.reconnaissance_satellite | Спутник-разведчик (Corona) / Reconnaissance satellite (Corona) | 1960 | ENG·2·none | s10.eng.artificial_satellite, s5.cmp.ablative_heat_shield | sens, ord, ctrl | — | — | Discoverer 14, капсула с плёнкой поднята 20.08.1960. [ист. 46] |
| 47 | s10.eng.crewed_orbital_flight | Пилотируемый орбитальный полёт (Восток) / Crewed orbital flight (Vostok) | 1961 | ENG·3·none | s10.eng.artificial_satellite, s5.cmp.ablative_heat_shield | struct, ctrl, mat, prop | — | — | Восток-1, 12.04.1961. [ист. 47] |
| 48 | s10.eng.crewed_lunar_landing | Высадка на Луну (Apollo) / Crewed lunar landing (Apollo) | 1969 | ENG·3·key | s7.eng.cryogenic_hydrogen_engine, s10.eng.crewed_orbital_flight, s9.eng.mainframe_family_g3 | prop, ctrl, struct, elec | — | — | Apollo 11, 20.07.1969. [ист. 48] |
| 49 | s10.eng.satellite_navigation_gps | Спутниковая навигация (GPS) / Satellite navigation (GPS) | 1978 | ENG·4·none | s10.eng.scientific_satellite, s9.cmp.microprocessor | ctrl, sens, elec | — | — | Первый НАВСТАР Блок I, 22.02.1978. [ист. 49] |
| 50 | s10.eng.space_shuttle_orbiter | Многоразовый космический корабль («Шаттл») / Reusable orbiter (Space Shuttle) | 1981 | ENG·4·hz | s7.eng.staged_combustion_hydrogen_engine, s5.cmp.carbon_carbon_composite, s10.eng.crewed_orbital_flight | prop, struct, mat, ctrl | — | — | STS-1, 12.04.1981. [ист. 50] |
| 51 | s10.eng.space_telescope | Космический телескоп (Hubble) / Space telescope (Hubble) | 1990 | ENG·5·none | s10.eng.space_shuttle_orbiter, s10.eng.scientific_satellite | sens, ctrl, elec, mat | sens +2% | — | Hubble, 24.04.1990 (STS-31). [ист. 51] |

## Ядерные исследования (9)
| № | id | RU / EN | Год | Тип·gen·hz | requires | Напр. | research_speed | Воен. | Обоснование |
|---|---|---|---|---|---|---|---|---|---|
| 52 | s11.fnd.nuclear_fission_chain_reaction | Деление ядра и цепная реакция / Nuclear fission and chain reaction | до 1945 | FND·0·none | — | nuc, chem | — | основа | Hahn и Strassmann 1938; CP-1 1942. [ист. 52] |
| 53 | s11.prc.gaseous_diffusion_enrichment | Газодиффузионное обогащение урана / Gaseous diffusion enrichment | 1945 | PRC·1·hz | s11.fnd.nuclear_fission_chain_reaction | nuc, chem, mfg | — | готовит производство | K-25 (Oak Ridge) начал работу в 1945. [ист. 53] |
| 54 | s11.eng.fission_weapon_g1 | Ядерный заряд деления / Fission weapon | 1945 | ENG·1·key | s11.fnd.nuclear_fission_chain_reaction | nuc, ord, chem | — | — | Испытание Trinity, 16.07.1945. [ист. 54] |
| 55 | s11.prn.neutron_transport_monte_carlo | Метод Монте-Карло для переноса нейтронов / Monte Carlo neutron transport | 1948 | PRN·1·none | s9.eng.electronic_computer_g1, s11.fnd.nuclear_fission_chain_reaction | nuc, ctrl | nuc +4% | — | Ulam, von Neumann, Metropolis; расчёт на ENIAC весной 1948. [ист. 55] |
| 56 | s5.cmp.zirconium_nuclear_cladding | Цирконий для оболочек ТВЭЛ (циркалой) / Zirconium alloy fuel cladding (Zircaloy) | 1950~ | CMP·1·none | s5.prc.titanium_reduction_forging, s11.fnd.nuclear_fission_chain_reaction | met, nuc, chem | — | — | Циркалой для реакторов подлодок с конца 1940-х (первый — 1940-е; год 1950 оценка). [ист. 56] |
| 57 | s11.eng.naval_nuclear_propulsion | Ядерная силовая установка подлодки / Naval nuclear propulsion | 1955 | ENG·2·hz | s11.fnd.nuclear_fission_chain_reaction, s5.cmp.zirconium_nuclear_cladding | nuc, prop, met, struct | — | — | «Наутилус», сигнал «Underway on nuclear power» 17.01.1955. [ист. 57] |
| 58 | s11.eng.light_water_power_reactor | Энергетический реактор с водой под давлением / Pressurised-water power reactor | 1957 | ENG·2·none | s11.eng.naval_nuclear_propulsion | nuc, met, struct | — | — | Шиппингпорт: первая энергия 18.12.1957. [ист. 58] |
| 59 | s11.prc.gas_centrifuge_enrichment | Газовая центрифуга для обогащения / Gas centrifuge enrichment | 1960~ | PRC·2·hz | s11.prc.gaseous_diffusion_enrichment, s5.cmp.maraging_steel, s6.prc.precision_gear_bearing | nuc, mfg, met | — | готовит производство | Центрифуга Ципе, конец 1950-х — 1960 [по памяти]; роторы из высокопрочной стали. [ист. 59] |
| 60 | s11.prn.laser_inertial_confinement | Инерциальный термоядерный синтез лазером (NIF) / Laser inertial confinement fusion (NIF) | 2021 | PRN·6·key | s9.prn.laser_principle, s9.eng.massively_parallel_hpc | nuc, elec, mat | — | — | NIF: первая «зажжённая» плазма 08.08.2021; выход энергии > затраченной — 05.12.2022 [по памяти]. [ист. 60] |

## Рёбра informs к существующим узлам (новый → существующий)
| От | К | Вес |
|---|---|---|
| s6.prc.numerical_control_machining | s8.prc.machined_integral_skins | 0.6 |
| s6.prc.numerical_control_machining | s8.prc.swept_wing_fabrication | 0.3 |
| s9.prn.finite_element_method | s8.eng.composite_airframe | 0.3 |
| s9.prn.finite_element_method | s5.prn.fibre_composite_laminate_design | 0.3 |
| s9.prn.computational_fluid_dynamics | s8.prn.rcs_computational_prediction | 0.6 |
| s9.prn.computational_fluid_dynamics | s8.prn.low_observable_shaping | 0.3 |
| s9.prn.computational_fluid_dynamics | s8.eng.thrust_vectoring_supercruise_engine | 0.3 |
| s9.prn.computational_fluid_dynamics | s8.eng.turbofan_g5 | 0.3 |
| s9.cmp.microprocessor | s8.cmp.digital_flight_control_computer | 0.6 |
| s9.cmp.microprocessor | s8.prn.programmable_signal_processing | 0.6 |
| s9.cmp.microprocessor | s8.prn.electronic_engine_control | 0.3 |
| s5.prc.zone_refining_semiconductor | s5.prn.single_crystal_growth | 0.3 |
| s5.prc.zone_refining_semiconductor | s5.prn.gaas_wafer_growth | 0.3 |
| s9.cmp.integrated_circuit | s8.eng.integrated_avionics_architecture | 0.3 |
| s9.cmp.integrated_circuit | s8.cmp.digital_flight_control_computer | 0.3 |
| s9.prc.planar_monolithic_ic_manufacturing | s5.cmp.gaas_mmic | 0.3 |
| s8.eng.fire_control_radar_interceptor | s8.app.jet_fighter_g3 | 0.6 |
| s8.eng.fire_control_radar_interceptor | s8.app.jet_fighter_g2 | 0.3 |
| s8.eng.fire_control_radar_interceptor | s8.prn.phased_array_radar_pesa | 0.3 |
| s8.eng.infrared_air_to_air_missile | s8.app.jet_fighter_g2 | 0.3 |
| s8.eng.inertial_navigation_system | s8.eng.integrated_avionics_architecture | 0.3 |
| s8.prn.pulse_doppler_look_down_radar | s8.app.jet_fighter_g4 | 0.6 |
| s8.prn.pulse_doppler_look_down_radar | s8.prn.phased_array_radar_pesa | 0.3 |
| s8.prn.pulse_doppler_look_down_radar | s8.eng.aesa_sensor_fusion | 0.3 |
| s5.prc.hot_isostatic_pressing | s5.prc.single_crystal_casting | 0.3 |
| s5.cmp.thermal_barrier_coating | s8.eng.turbofan_g5 | 0.3 |
| s5.cmp.thermal_barrier_coating | s8.eng.thrust_vectoring_supercruise_engine | 0.3 |

## Источники (доступ 2026-11-25)
[Факт] «прочитано» — страница открыта и нужный факт найден; «не подтверждено» — страница открыта, но факт (год) на ней не найден или не открылась; год взят по памяти и помечен «~».

| № | URL | Статус |
|---|---|---|
| 01 | https://en.wikipedia.org/wiki/First_Draft_of_a_Report_on_the_EDVAC | прочитано |
| 02 | https://en.wikipedia.org/wiki/ENIAC | прочитано |
| 03 | https://en.wikipedia.org/wiki/EDSAC | прочитано |
| 04 | https://en.wikipedia.org/wiki/Numerical_control | не подтверждено |
| 05 | https://en.wikipedia.org/wiki/Fortran | прочитано |
| 06 | https://en.wikipedia.org/wiki/Finite_element_method | не подтверждено |
| 07 | https://en.wikipedia.org/wiki/IBM_7090 | прочитано |
| 08 | https://en.wikipedia.org/wiki/CDC_6600 | прочитано |
| 09 | https://en.wikipedia.org/wiki/Computational_fluid_dynamics | не подтверждено |
| 10 | https://en.wikipedia.org/wiki/Intel_4004 | прочитано |
| 11 | https://en.wikipedia.org/wiki/Cray-1 | прочитано |
| 12 | https://en.wikipedia.org/wiki/ASCI_Red | прочитано |
| 13 | https://en.wikipedia.org/wiki/CUDA | прочитано |
| 14 | https://en.wikipedia.org/wiki/AlphaFold | прочитано |
| 15 | https://www.nobelprize.org/prizes/physics/1956/summary/ | не подтверждено |
| 16 | https://en.wikipedia.org/wiki/History_of_the_transistor | прочитано |
| 17 | https://en.wikipedia.org/wiki/Zone_melting | не подтверждено |
| 18 | https://en.wikipedia.org/wiki/Gordon_Kidd_Teal | прочитано |
| 19 | https://en.wikipedia.org/wiki/Integrated_circuit | прочитано |
| 20 | https://en.wikipedia.org/wiki/Planar_process | прочитано |
| 21 | https://en.wikipedia.org/wiki/Laser | прочитано |
| 22 | https://en.wikipedia.org/wiki/Self-aligned_gate | прочитано |
| 23 | https://en.wikipedia.org/wiki/Mead_%26_Conway_revolution | прочитано |
| 24 | https://en.wikipedia.org/wiki/Extreme_ultraviolet_lithography | прочитано |
| 25 | https://en.wikipedia.org/wiki/North_American_F-86_Sabre | не подтверждено |
| 26 | https://en.wikipedia.org/wiki/AIM-9_Sidewinder | прочитано |
| 27 | https://en.wikipedia.org/wiki/Inertial_navigation_system | не подтверждено |
| 28 | https://en.wikipedia.org/wiki/Pulse-Doppler_radar | не подтверждено |
| 29 | https://en.wikipedia.org/wiki/Maraging_steel | прочитано |
| 30 | https://en.wikipedia.org/wiki/Atmospheric_entry | не подтверждено |
| 31 | https://en.wikipedia.org/wiki/Reinforced_carbon%E2%80%93carbon | не подтверждено |
| 32 | https://en.wikipedia.org/wiki/Hot_isostatic_pressing | не подтверждено |
| 33 | https://en.wikipedia.org/wiki/Thermal_barrier_coating | не подтверждено |
| 34 | https://en.wikipedia.org/wiki/CFM_International_LEAP | не подтверждено |
| 35 | https://en.wikipedia.org/wiki/V-2_rocket | прочитано |
| 36 | https://en.wikipedia.org/wiki/Solid-propellant_rocket | прочитано |
| 37 | https://en.wikipedia.org/wiki/Atmospheric_entry | прочитано |
| 38 | https://en.wikipedia.org/wiki/R-7_Semyorka | прочитано |
| 39 | https://en.wikipedia.org/wiki/RL10 | прочитано |
| 40 | https://en.wikipedia.org/wiki/RS-25 | прочитано |
| 41 | https://en.wikipedia.org/wiki/Falcon_9 | прочитано |
| 42 | https://en.wikipedia.org/wiki/Astrodynamics | прочитано |
| 43 | https://en.wikipedia.org/wiki/V-2_rocket | не подтверждено |
| 44 | https://en.wikipedia.org/wiki/Sputnik_1 | прочитано |
| 45 | https://en.wikipedia.org/wiki/Explorer_1 | прочитано |
| 46 | https://en.wikipedia.org/wiki/Corona_(satellite) | прочитано |
| 47 | https://en.wikipedia.org/wiki/Vostok_1 | прочитано |
| 48 | https://en.wikipedia.org/wiki/Apollo_11 | прочитано |
| 49 | https://en.wikipedia.org/wiki/Global_Positioning_System | прочитано |
| 50 | https://en.wikipedia.org/wiki/STS-1 | прочитано |
| 51 | https://en.wikipedia.org/wiki/Hubble_Space_Telescope | прочитано |
| 52 | https://en.wikipedia.org/wiki/Nuclear_fission | прочитано |
| 53 | https://en.wikipedia.org/wiki/Gaseous_diffusion | прочитано |
| 54 | https://en.wikipedia.org/wiki/Trinity_(nuclear_test) | прочитано |
| 55 | https://en.wikipedia.org/wiki/Monte_Carlo_method | прочитано |
| 56 | https://en.wikipedia.org/wiki/Zircaloy | прочитано |
| 57 | https://en.wikipedia.org/wiki/USS_Nautilus_(SSN-571) | прочитано |
| 58 | https://en.wikipedia.org/wiki/Shippingport_Atomic_Power_Station | прочитано |
| 59 | https://en.wikipedia.org/wiki/Gas_centrifuge | не подтверждено |
| 60 | https://en.wikipedia.org/wiki/National_Ignition_Facility | прочитано |

## Что не проверено
- Узлы 04, 06, 09, 15, 17, 25, 27, 28, 30, 31, 32, 33, 34, 43, 59 не подтверждены страницей (год по памяти; 16 помечены «~»); нужен первичный источник (NACA/NASA NTRS, патенты, NRO — публичный домен). Страница «Panel method» вернула 404; год 1967 по памяти.
- Узлы 15 (физика полупроводников, до 1945) и 52 (деление) — знание на старте; решение, пишет ли их сценарий старта, за шагом 2.
- Уровни bonus 0,02–0,05 — [ЗАГЛУШКА] до TASK-0185; численных цен/сроков не задавал.
- Правило «direct» для 25, 26, 27, 33 — моя оценка по `perf` слотов; для connectов в product_classes (`ref`) не проверялось.

## OPEN
1. [Предл.] Космос: в 12 направлениях нет «space»; узлы приписаны к prop/ctrl/sens/struct/mat. Решение O-091 «без слияния» не трогаю; если нужна 13-я область — это D1 владельцу.
2. Префиксы s9/s10/s11 и новые кластеры COMP/SPACE/NUC: проверить код (парсинг `sN`, enum cluster) — developer, шаг 2.
3. Дубли дат с существующими: s5.cmp.high_modulus_fibre и s5.prn.gaas_wafer_growth (1965) — в таблице не пересекаются, но стоит сверить ранги цен.
4. Предложение: рёбра informs в jet-ветку заменить на изменение requires существующих узлов (напр. jet_fighter_g3 требует РЛС 1951) — только по решению Orchestrator; сейчас не предлагается.

## Блок для nodes.yaml (YAML-ready; поля как в существующих узлах + `dirs`, `effects`, `mil_value`)
Слот `discipline` — прежняя область (временно, до перехода на `dirs`). `source` — ключ `R575-NN` = номер в «Источниках». Эффект `research_speed`: `dirs` — список или `all`.

```yaml
  - id: s9.fnd.stored_program_architecture
    number: 100
    type: FND
    gen: 1
    discipline: aero
    cluster: COMP
    dirs: [ctrl, elec]
    requires: []
    hazard: none
    mil_value: base
    anchor:
      year: 1945
      source: "R575-01"
      estimated: false
  - id: s9.eng.electronic_computer_g1
    number: 101
    type: ENG
    gen: 1
    discipline: aero
    cluster: COMP
    dirs: [ctrl, ord, aero]
    requires: []
    hazard: none
    mil_value: none
    effects:
      - {kind: research_speed, dirs: [ord, aero], bonus: 0.02}
    anchor:
      year: 1946
      source: "R575-02"
      estimated: false
  - id: s9.eng.stored_program_computer
    number: 102
    type: ENG
    gen: 1
    discipline: aero
    cluster: COMP
    dirs: [ctrl, elec]
    requires: [s9.fnd.stored_program_architecture, s9.eng.electronic_computer_g1]
    hazard: none
    mil_value: none
    effects:
      - {kind: research_speed, dirs: [prop, nuc], bonus: 0.02}
    anchor:
      year: 1949
      source: "R575-03"
      estimated: false
  - id: s6.prc.numerical_control_machining
    number: 103
    type: PRC
    gen: 2
    discipline: mech
    cluster: COMP
    dirs: [mfg, ctrl, struct]
    requires: [s6.prc.precision_gear_bearing]
    hazard: none
    mil_value: prep
    informs:
      - {to: s8.prc.machined_integral_skins, weight: 0.6}
      - {to: s8.prc.swept_wing_fabrication, weight: 0.3}
    anchor:
      year: 1952
      source: "R575-04"
      estimated: true
  - id: s9.prn.high_level_language_compiler
    number: 104
    type: PRN
    gen: 2
    discipline: aero
    cluster: COMP
    dirs: [ctrl, elec]
    requires: [s9.eng.stored_program_computer]
    hazard: none
    mil_value: none
    effects:
      - {kind: research_speed, dirs: [aero, prop, struct, nuc], bonus: 0.02}
    anchor:
      year: 1957
      source: "R575-05"
      estimated: false
  - id: s9.prn.finite_element_method
    number: 105
    type: PRN
    gen: 2
    discipline: mech
    cluster: COMP
    dirs: [struct, aero, ctrl]
    requires: [s9.eng.stored_program_computer]
    hazard: none
    mil_value: base
    effects:
      - {kind: research_speed, dirs: [struct], bonus: 0.04}
    informs:
      - {to: s8.eng.composite_airframe, weight: 0.3}
      - {to: s5.prn.fibre_composite_laminate_design, weight: 0.3}
    anchor:
      year: 1956
      source: "R575-06"
      estimated: true
  - id: s9.cmp.transistor_computer_g2
    number: 106
    type: CMP
    gen: 2
    discipline: aero
    cluster: COMP
    dirs: [ctrl, elec]
    requires: [s9.eng.stored_program_computer, s9.cmp.silicon_transistor]
    hazard: none
    mil_value: none
    effects:
      - {kind: research_speed, dirs: [aero, prop, struct, nuc], bonus: 0.03}
    anchor:
      year: 1959
      source: "R575-07"
      estimated: false
  - id: s9.eng.mainframe_family_g3
    number: 107
    type: ENG
    gen: 3
    discipline: aero
    cluster: COMP
    dirs: [ctrl, elec]
    requires: [s9.cmp.transistor_computer_g2, s9.cmp.silicon_transistor]
    hazard: none
    mil_value: none
    effects:
      - {kind: research_speed, dirs: all, bonus: 0.02}
    anchor:
      year: 1964
      source: "R575-08"
      estimated: false
  - id: s9.prn.computational_fluid_dynamics
    number: 108
    type: PRN
    gen: 3
    discipline: aero
    cluster: COMP
    dirs: [aero, prop, ctrl]
    requires: [s9.cmp.transistor_computer_g2, s8.fnd.compressible_flow]
    hazard: none
    mil_value: base
    effects:
      - {kind: research_speed, dirs: [aero, prop], bonus: 0.05}
    informs:
      - {to: s8.prn.rcs_computational_prediction, weight: 0.6}
      - {to: s8.prn.low_observable_shaping, weight: 0.3}
      - {to: s8.eng.thrust_vectoring_supercruise_engine, weight: 0.3}
      - {to: s8.eng.turbofan_g5, weight: 0.3}
    anchor:
      year: 1967
      source: "R575-09"
      estimated: true
  - id: s9.cmp.microprocessor
    number: 109
    type: CMP
    gen: 4
    discipline: chem
    cluster: COMP
    dirs: [elec, ctrl]
    requires: [s9.prc.mos_lsi_manufacturing]
    hazard: none
    mil_value: base
    effects:
      - {kind: research_speed, dirs: [ctrl, elec], bonus: 0.03}
    informs:
      - {to: s8.cmp.digital_flight_control_computer, weight: 0.6}
      - {to: s8.prn.programmable_signal_processing, weight: 0.6}
      - {to: s8.prn.electronic_engine_control, weight: 0.3}
    anchor:
      year: 1971
      source: "R575-10"
      estimated: false
  - id: s9.eng.vector_supercomputer
    number: 110
    type: ENG
    gen: 4
    discipline: aero
    cluster: COMP
    dirs: [ctrl, aero, nuc, prop]
    requires: [s9.eng.mainframe_family_g3, s9.prc.planar_monolithic_ic_manufacturing]
    hazard: hz
    mil_value: none
    effects:
      - {kind: research_speed, dirs: [aero, prop, nuc, mat], bonus: 0.04}
    anchor:
      year: 1976
      source: "R575-11"
      estimated: false
  - id: s9.eng.massively_parallel_hpc
    number: 111
    type: ENG
    gen: 5
    discipline: aero
    cluster: COMP
    dirs: [ctrl, nuc, aero]
    requires: [s9.eng.vector_supercomputer, s9.prn.vlsi_design_methodology]
    hazard: none
    mil_value: none
    effects:
      - {kind: research_speed, dirs: [nuc, aero, prop, mat], bonus: 0.04}
    anchor:
      year: 1996
      source: "R575-12"
      estimated: false
  - id: s9.eng.gpu_accelerated_computing
    number: 112
    type: ENG
    gen: 6
    discipline: aero
    cluster: COMP
    dirs: [ctrl, elec, mat]
    requires: [s9.eng.massively_parallel_hpc]
    hazard: none
    mil_value: none
    effects:
      - {kind: research_speed, dirs: [mat, chem, ctrl, elec], bonus: 0.03}
    anchor:
      year: 2007
      source: "R575-13"
      estimated: false
  - id: s9.prn.deep_learning_science
    number: 113
    type: PRN
    gen: 6
    discipline: aero
    cluster: COMP
    dirs: [ctrl, chem, mat]
    requires: [s9.eng.gpu_accelerated_computing]
    hazard: hz
    mil_value: none
    effects:
      - {kind: research_speed, dirs: [chem, mat, ctrl], bonus: 0.03}
    anchor:
      year: 2020
      source: "R575-14"
      estimated: false
  - id: s9.fnd.semiconductor_physics
    number: 114
    type: FND
    gen: 0
    discipline: chem
    cluster: COMP
    dirs: [elec, mat]
    requires: []
    hazard: none
    mil_value: base
    anchor:
      year: 1945
      before: true
      source: "R575-15"
      estimated: false
  - id: s9.prn.point_contact_transistor
    number: 115
    type: PRN
    gen: 1
    discipline: chem
    cluster: COMP
    dirs: [elec, mat]
    requires: [s9.fnd.semiconductor_physics]
    hazard: none
    mil_value: none
    anchor:
      year: 1947
      source: "R575-16"
      estimated: false
  - id: s5.prc.zone_refining_semiconductor
    number: 116
    type: PRC
    gen: 2
    discipline: chem
    cluster: COMP
    dirs: [chem, met, elec]
    requires: [s5.fnd.solidification_science]
    hazard: none
    mil_value: prep
    informs:
      - {to: s5.prn.single_crystal_growth, weight: 0.3}
      - {to: s5.prn.gaas_wafer_growth, weight: 0.3}
    anchor:
      year: 1952
      source: "R575-17"
      estimated: true
  - id: s9.cmp.silicon_transistor
    number: 117
    type: CMP
    gen: 2
    discipline: chem
    cluster: COMP
    dirs: [elec, mat]
    requires: [s9.prn.point_contact_transistor, s5.prc.zone_refining_semiconductor]
    hazard: none
    mil_value: none
    anchor:
      year: 1954
      source: "R575-18"
      estimated: false
  - id: s9.cmp.integrated_circuit
    number: 118
    type: CMP
    gen: 2
    discipline: chem
    cluster: COMP
    dirs: [elec, ctrl, mat]
    requires: [s9.cmp.silicon_transistor]
    hazard: none
    mil_value: base
    informs:
      - {to: s8.eng.integrated_avionics_architecture, weight: 0.3}
      - {to: s8.cmp.digital_flight_control_computer, weight: 0.3}
    anchor:
      year: 1958
      source: "R575-19"
      estimated: false
  - id: s9.prc.planar_monolithic_ic_manufacturing
    number: 119
    type: PRC
    gen: 2
    discipline: chem
    cluster: COMP
    dirs: [elec, mfg, chem]
    requires: [s9.cmp.integrated_circuit]
    hazard: none
    mil_value: prep
    informs:
      - {to: s5.cmp.gaas_mmic, weight: 0.3}
    anchor:
      year: 1959
      source: "R575-20"
      estimated: false
  - id: s9.prn.laser_principle
    number: 120
    type: PRN
    gen: 2
    discipline: chem
    cluster: COMP
    dirs: [elec, sens, ord]
    requires: [s9.fnd.semiconductor_physics]
    hazard: none
    mil_value: none
    anchor:
      year: 1960
      source: "R575-21"
      estimated: false
  - id: s9.prc.mos_lsi_manufacturing
    number: 121
    type: PRC
    gen: 3
    discipline: chem
    cluster: COMP
    dirs: [elec, mfg, chem]
    requires: [s9.prc.planar_monolithic_ic_manufacturing]
    hazard: none
    mil_value: prep
    anchor:
      year: 1968
      source: "R575-22"
      estimated: false
  - id: s9.prn.vlsi_design_methodology
    number: 122
    type: PRN
    gen: 4
    discipline: chem
    cluster: COMP
    dirs: [elec, ctrl]
    requires: [s9.prc.mos_lsi_manufacturing, s9.cmp.microprocessor]
    hazard: none
    mil_value: none
    effects:
      - {kind: research_speed, dirs: [elec, ctrl], bonus: 0.04}
    anchor:
      year: 1979
      source: "R575-23"
      estimated: false
  - id: s9.prc.euv_lithography
    number: 123
    type: PRC
    gen: 6
    discipline: mech
    cluster: COMP
    dirs: [mfg, elec, chem, mat]
    requires: [s9.prn.vlsi_design_methodology, s9.prn.laser_principle]
    hazard: key
    mil_value: prep
    anchor:
      year: 2019
      source: "R575-24"
      estimated: false
  - id: s8.eng.fire_control_radar_interceptor
    number: 124
    type: ENG
    gen: 2
    discipline: aero
    cluster: AERO
    dirs: [sens, ctrl, ord]
    requires: []
    hazard: none
    mil_value: direct
    informs:
      - {to: s8.app.jet_fighter_g3, weight: 0.6}
      - {to: s8.app.jet_fighter_g2, weight: 0.3}
      - {to: s8.prn.phased_array_radar_pesa, weight: 0.3}
    anchor:
      year: 1951
      source: "R575-25"
      estimated: true
  - id: s8.eng.infrared_air_to_air_missile
    number: 125
    type: ENG
    gen: 2
    discipline: aero
    cluster: AERO
    dirs: [ord, sens, elec]
    requires: []
    hazard: none
    mil_value: direct
    informs:
      - {to: s8.app.jet_fighter_g2, weight: 0.3}
    anchor:
      year: 1953
      source: "R575-26"
      estimated: false
  - id: s8.eng.inertial_navigation_system
    number: 126
    type: ENG
    gen: 2
    discipline: aero
    cluster: AERO
    dirs: [ctrl, aero, ord]
    requires: [s6.prc.precision_gear_bearing]
    hazard: none
    mil_value: base
    informs:
      - {to: s8.eng.integrated_avionics_architecture, weight: 0.3}
    anchor:
      year: 1953
      source: "R575-27"
      estimated: true
  - id: s8.prn.pulse_doppler_look_down_radar
    number: 127
    type: PRN
    gen: 3
    discipline: aero
    cluster: AERO
    dirs: [sens, elec, ctrl]
    requires: [s8.eng.fire_control_radar_interceptor, s9.cmp.integrated_circuit]
    hazard: hz
    mil_value: direct
    informs:
      - {to: s8.app.jet_fighter_g4, weight: 0.6}
      - {to: s8.prn.phased_array_radar_pesa, weight: 0.3}
      - {to: s8.eng.aesa_sensor_fusion, weight: 0.3}
    anchor:
      year: 1966
      source: "R575-28"
      estimated: true
  - id: s5.cmp.maraging_steel
    number: 128
    type: CMP
    gen: 2
    discipline: metal
    cluster: CHEM
    dirs: [met, struct, ord]
    requires: [s5.prc.vacuum_induction_melt]
    hazard: none
    mil_value: none
    anchor:
      year: 1959
      source: "R575-29"
      estimated: true
  - id: s5.cmp.ablative_heat_shield
    number: 129
    type: CMP
    gen: 2
    discipline: chem
    cluster: CHEM
    dirs: [mat, struct, chem]
    requires: [s7.fnd.blunt_body_reentry]
    hazard: none
    mil_value: none
    anchor:
      year: 1958
      source: "R575-30"
      estimated: true
  - id: s5.cmp.carbon_carbon_composite
    number: 130
    type: CMP
    gen: 2
    discipline: chem
    cluster: CHEM
    dirs: [mat, struct, prop]
    requires: [s5.cmp.ablative_heat_shield]
    hazard: none
    mil_value: none
    anchor:
      year: 1959
      source: "R575-31"
      estimated: true
  - id: s5.prc.hot_isostatic_pressing
    number: 131
    type: PRC
    gen: 2
    discipline: mech
    cluster: CHEM
    dirs: [mfg, met, mat]
    requires: [s5.prc.vacuum_induction_melt]
    hazard: none
    mil_value: prep
    informs:
      - {to: s5.prc.single_crystal_casting, weight: 0.3}
    anchor:
      year: 1955
      source: "R575-32"
      estimated: true
  - id: s5.cmp.thermal_barrier_coating
    number: 132
    type: CMP
    gen: 4
    discipline: chem
    cluster: CHEM
    dirs: [mat, prop, met]
    requires: [s5.cmp.heat_resistant_alloy, s8.cmp.turbine_blade_cooled]
    hazard: none
    mil_value: direct
    informs:
      - {to: s8.eng.turbofan_g5, weight: 0.3}
      - {to: s8.eng.thrust_vectoring_supercruise_engine, weight: 0.3}
    anchor:
      year: 1976
      source: "R575-33"
      estimated: true
  - id: s5.prc.additive_manufacturing_metal
    number: 133
    type: PRC
    gen: 6
    discipline: mech
    cluster: CHEM
    dirs: [mfg, mat, met]
    requires: [s9.prn.laser_principle, s5.prc.hot_isostatic_pressing]
    hazard: none
    mil_value: prep
    anchor:
      year: 2015
      source: "R575-34"
      estimated: true
  - id: s7.eng.liquid_rocket_engine_g1
    number: 134
    type: ENG
    gen: 1
    discipline: prop
    cluster: AERO
    dirs: [prop, ord, chem, ctrl]
    requires: []
    hazard: none
    mil_value: none
    anchor:
      year: 1942
      source: "R575-35"
      estimated: false
  - id: s7.prn.composite_solid_propellant
    number: 135
    type: PRN
    gen: 2
    discipline: chem
    cluster: AERO
    dirs: [chem, prop, ord]
    requires: []
    hazard: none
    mil_value: none
    anchor:
      year: 1954
      source: "R575-36"
      estimated: false
  - id: s7.fnd.blunt_body_reentry
    number: 136
    type: FND
    gen: 2
    discipline: aero
    cluster: AERO
    dirs: [aero, struct, mat]
    requires: [s8.fnd.transonic_aerodynamics]
    hazard: none
    mil_value: base
    anchor:
      year: 1951
      source: "R575-37"
      estimated: false
  - id: s7.eng.intercontinental_ballistic_missile
    number: 137
    type: ENG
    gen: 2
    discipline: prop
    cluster: AERO
    dirs: [prop, ord, ctrl, struct]
    requires: [s7.eng.liquid_rocket_engine_g1, s8.eng.inertial_navigation_system]
    hazard: key
    mil_value: none
    anchor:
      year: 1957
      source: "R575-38"
      estimated: false
  - id: s7.eng.cryogenic_hydrogen_engine
    number: 138
    type: ENG
    gen: 3
    discipline: prop
    cluster: AERO
    dirs: [prop, chem, mat]
    requires: [s7.eng.liquid_rocket_engine_g1]
    hazard: hz
    mil_value: none
    anchor:
      year: 1963
      source: "R575-39"
      estimated: false
  - id: s7.eng.staged_combustion_hydrogen_engine
    number: 139
    type: ENG
    gen: 4
    discipline: prop
    cluster: AERO
    dirs: [prop, met, mat, ctrl]
    requires: [s7.eng.cryogenic_hydrogen_engine, s5.cmp.heat_resistant_alloy]
    hazard: hz
    mil_value: none
    anchor:
      year: 1981
      source: "R575-40"
      estimated: false
  - id: s7.eng.reusable_booster_landing
    number: 140
    type: ENG
    gen: 6
    discipline: prop
    cluster: AERO
    dirs: [prop, ctrl, struct]
    requires: [s7.eng.staged_combustion_hydrogen_engine, s9.eng.massively_parallel_hpc]
    hazard: hz
    mil_value: none
    anchor:
      year: 2015
      source: "R575-41"
      estimated: false
  - id: s10.fnd.orbital_mechanics
    number: 141
    type: FND
    gen: 0
    discipline: aero
    cluster: SPACE
    dirs: [ctrl, prop]
    requires: []
    hazard: none
    mil_value: base
    anchor:
      year: 1945
      before: true
      source: "R575-42"
      estimated: false
  - id: s10.eng.sounding_rocket_science
    number: 142
    type: ENG
    gen: 1
    discipline: aero
    cluster: SPACE
    dirs: [sens, prop, ctrl]
    requires: [s7.eng.liquid_rocket_engine_g1]
    hazard: none
    mil_value: none
    anchor:
      year: 1946
      source: "R575-43"
      estimated: true
  - id: s10.eng.artificial_satellite
    number: 143
    type: ENG
    gen: 2
    discipline: prop
    cluster: SPACE
    dirs: [prop, ctrl, elec, sens]
    requires: [s7.eng.intercontinental_ballistic_missile, s10.fnd.orbital_mechanics]
    hazard: none
    mil_value: none
    anchor:
      year: 1957
      source: "R575-44"
      estimated: false
  - id: s10.eng.scientific_satellite
    number: 144
    type: ENG
    gen: 2
    discipline: aero
    cluster: SPACE
    dirs: [sens, elec, nuc]
    requires: [s10.eng.artificial_satellite, s9.cmp.silicon_transistor]
    hazard: none
    mil_value: none
    anchor:
      year: 1958
      source: "R575-45"
      estimated: false
  - id: s10.eng.reconnaissance_satellite
    number: 145
    type: ENG
    gen: 2
    discipline: aero
    cluster: SPACE
    dirs: [sens, ord, ctrl]
    requires: [s10.eng.artificial_satellite, s5.cmp.ablative_heat_shield]
    hazard: none
    mil_value: none
    anchor:
      year: 1960
      source: "R575-46"
      estimated: false
  - id: s10.eng.crewed_orbital_flight
    number: 146
    type: ENG
    gen: 3
    discipline: mech
    cluster: SPACE
    dirs: [struct, ctrl, mat, prop]
    requires: [s10.eng.artificial_satellite, s5.cmp.ablative_heat_shield]
    hazard: none
    mil_value: none
    anchor:
      year: 1961
      source: "R575-47"
      estimated: false
  - id: s10.eng.crewed_lunar_landing
    number: 147
    type: ENG
    gen: 3
    discipline: prop
    cluster: SPACE
    dirs: [prop, ctrl, struct, elec]
    requires: [s7.eng.cryogenic_hydrogen_engine, s10.eng.crewed_orbital_flight, s9.eng.mainframe_family_g3]
    hazard: key
    mil_value: none
    anchor:
      year: 1969
      source: "R575-48"
      estimated: false
  - id: s10.eng.satellite_navigation_gps
    number: 148
    type: ENG
    gen: 4
    discipline: aero
    cluster: SPACE
    dirs: [ctrl, sens, elec]
    requires: [s10.eng.scientific_satellite, s9.cmp.microprocessor]
    hazard: none
    mil_value: none
    anchor:
      year: 1978
      source: "R575-49"
      estimated: false
  - id: s10.eng.space_shuttle_orbiter
    number: 149
    type: ENG
    gen: 4
    discipline: prop
    cluster: SPACE
    dirs: [prop, struct, mat, ctrl]
    requires: [s7.eng.staged_combustion_hydrogen_engine, s5.cmp.carbon_carbon_composite, s10.eng.crewed_orbital_flight]
    hazard: hz
    mil_value: none
    anchor:
      year: 1981
      source: "R575-50"
      estimated: false
  - id: s10.eng.space_telescope
    number: 150
    type: ENG
    gen: 5
    discipline: aero
    cluster: SPACE
    dirs: [sens, ctrl, elec, mat]
    requires: [s10.eng.space_shuttle_orbiter, s10.eng.scientific_satellite]
    hazard: none
    mil_value: none
    effects:
      - {kind: research_speed, dirs: [sens], bonus: 0.02}
    anchor:
      year: 1990
      source: "R575-51"
      estimated: false
  - id: s11.fnd.nuclear_fission_chain_reaction
    number: 151
    type: FND
    gen: 0
    discipline: chem
    cluster: NUC
    dirs: [nuc, chem]
    requires: []
    hazard: none
    mil_value: base
    anchor:
      year: 1945
      before: true
      source: "R575-52"
      estimated: false
  - id: s11.prc.gaseous_diffusion_enrichment
    number: 152
    type: PRC
    gen: 1
    discipline: chem
    cluster: NUC
    dirs: [nuc, chem, mfg]
    requires: [s11.fnd.nuclear_fission_chain_reaction]
    hazard: hz
    mil_value: prep
    anchor:
      year: 1945
      source: "R575-53"
      estimated: false
  - id: s11.eng.fission_weapon_g1
    number: 153
    type: ENG
    gen: 1
    discipline: chem
    cluster: NUC
    dirs: [nuc, ord, chem]
    requires: [s11.fnd.nuclear_fission_chain_reaction]
    hazard: key
    mil_value: none
    anchor:
      year: 1945
      source: "R575-54"
      estimated: false
  - id: s11.prn.neutron_transport_monte_carlo
    number: 154
    type: PRN
    gen: 1
    discipline: chem
    cluster: NUC
    dirs: [nuc, ctrl]
    requires: [s9.eng.electronic_computer_g1, s11.fnd.nuclear_fission_chain_reaction]
    hazard: none
    mil_value: none
    effects:
      - {kind: research_speed, dirs: [nuc], bonus: 0.04}
    anchor:
      year: 1948
      source: "R575-55"
      estimated: false
  - id: s5.cmp.zirconium_nuclear_cladding
    number: 155
    type: CMP
    gen: 1
    discipline: metal
    cluster: NUC
    dirs: [met, nuc, chem]
    requires: [s5.prc.titanium_reduction_forging, s11.fnd.nuclear_fission_chain_reaction]
    hazard: none
    mil_value: none
    anchor:
      year: 1950
      source: "R575-56"
      estimated: true
  - id: s11.eng.naval_nuclear_propulsion
    number: 156
    type: ENG
    gen: 2
    discipline: chem
    cluster: NUC
    dirs: [nuc, prop, met, struct]
    requires: [s11.fnd.nuclear_fission_chain_reaction, s5.cmp.zirconium_nuclear_cladding]
    hazard: hz
    mil_value: none
    anchor:
      year: 1955
      source: "R575-57"
      estimated: false
  - id: s11.eng.light_water_power_reactor
    number: 157
    type: ENG
    gen: 2
    discipline: chem
    cluster: NUC
    dirs: [nuc, met, struct]
    requires: [s11.eng.naval_nuclear_propulsion]
    hazard: none
    mil_value: none
    anchor:
      year: 1957
      source: "R575-58"
      estimated: false
  - id: s11.prc.gas_centrifuge_enrichment
    number: 158
    type: PRC
    gen: 2
    discipline: chem
    cluster: NUC
    dirs: [nuc, mfg, met]
    requires: [s11.prc.gaseous_diffusion_enrichment, s5.cmp.maraging_steel, s6.prc.precision_gear_bearing]
    hazard: hz
    mil_value: prep
    anchor:
      year: 1960
      source: "R575-59"
      estimated: true
  - id: s11.prn.laser_inertial_confinement
    number: 159
    type: PRN
    gen: 6
    discipline: chem
    cluster: NUC
    dirs: [nuc, elec, mat]
    requires: [s9.prn.laser_principle, s9.eng.massively_parallel_hpc]
    hazard: key
    mil_value: none
    anchor:
      year: 2021
      source: "R575-60"
      estimated: false
```
