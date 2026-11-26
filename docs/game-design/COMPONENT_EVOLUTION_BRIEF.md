# Modular technology development and component evolution — owner brief v1.0

Source: owner's concept proposal of 2026-11-03, kept in substance (original in English), plus the owner's note in Russian at the end. Status: input for research and design; nothing here is approved.

## 1. Role and objective
Senior game systems architect, aerospace systems engineer, industrial production designer, tech-tree architect. Design a system for independent research, development, production, integration and evolution of components inside complex products. A complex product has no single tech level: an aircraft can combine an old airframe, a modern engine, new avionics, an old radar, modern navigation, older weapons integration, new materials in some structures; an engine can combine an old compressor architecture, new blade materials, better cooling, modern fuel control, an older combustor, a new manufacturing process. Progress happens at the level of components, technologies, engineering solutions and manufacturing capabilities, not only whole products. Research, challenge and improve the concept; no code before the design is reviewed and approved.

## 2. Core principle
A complex product is an integrated system of independently evolving components. Distinguish: (1) scientific knowledge; (2) technology (applicable engineering principles); (3) component architecture; (4) component design (concrete implementation); (5) prototype; (6) qualified component; (7) production capability (quality, cost, volume); (8) integrated product; (9) upgrade/modernization. Not one progress bar. A technology can be discovered but immature; a component developed but not manufacturable at scale; a new component may need redesign of the surrounding system.

## 3. Component hierarchy
Hierarchical structures, e.g. aircraft (airframe: fuselage, wings, materials, gear, control surfaces; propulsion: engine, compressor, combustor, turbine, fuel system, exhaust; avionics: instruments, navigation, radar, comms, fire control, computer; electrical; hydraulics; weapons integration; life support) and jet engine (compressor: architecture, stages, blade geometry, materials; combustor; turbine: stages, geometry, cooling, high-temperature materials; bearings; fuel control; lubrication; starter; control). Depth depends on gameplay importance; avoid micromanagement. A component is not a technology (axial compressor = architecture; variable stators = technical solution; single-crystal blades = material/manufacturing technology; blade cooling = engineering technology; a specific compressor = implementation) — no artificial duplication between tech nodes and physical components.

## 4. Independent development cycles
Each component can have its own lifecycle. An aircraft programme starts with an existing engine while a new engine is developed in parallel (research → engineering → prototype → bench test → flight test → qualification → industrial preparation → series); the aircraft is designed, tested, produced, fielded and upgraded without waiting; the new engine is integrated later if compatible. Parallel programmes (aircraft, engine, radar, weapons, avionics, manufacturing process) share technologies, personnel, facilities and industry — competition and synergy.

## 5. Maturity and engineering risk
Separate theoretical feasibility, experimental validation, prototype maturity, engineering reliability, production readiness, serial manufacturing maturity, operational reliability. A new blade technology can be excellent in the lab and still have defects, short life, cooling problems, high cost, supply limits, batch variation — an early engine can be less reliable than an established old one; reliability, yield, cost and maintenance improve with experience. Not a random penalty: failures tied to specific engineering, material, production and operational factors.

## 6. Compatibility and integration (most important)
Levels: direct; minor modification; after redesign; requires a new variant; incompatible. A new engine changes weight, CG, fuel burn, airflow/cooling, thrust, loads, vibration, maintenance, mounts, electrics, flight controls → structural calculations, reinforcement, controls, cooling, testing, certification. A new radar needs power, cooling, nose changes, displays, computers, software and weapons integration. Realistic but playable; the player does not calculate engineering parameters.

## 7. Variants and generations
Variants keep lineage (A1 original engine/old avionics → A2 modern avionics → A3 new engine → A4 new radar + reinforced airframe). The player can develop from scratch, derive a variant, modernize, replace selected components, keep proven ones, introduce experimental ones, run several variants. New products do not obsolete old ones: lower cost, established lines, easier maintenance, spares, training, adequate performance.

## 8. Trade-offs
No linear progression; newer is not always better. Performance dimensions per component, e.g. engine (thrust, weight, consumption, reliability, life, cost, materials, maintenance, production difficulty, development time), radar (range, resolution, tracking, power, weight, reliability, ECM resistance, cost), structural materials (strength, weight, fatigue, temperature, corrosion, cost, production difficulty, raw materials).

## 9. Technology graph and reuse
Connect to the existing large interconnected research graph: fundamental research → applied technologies → component implementations → products across industries; one technology serves many industries (semiconductors → radar, computers, comms, automation, guidance, spacecraft); a component may need technologies from several branches. Mechanisms: prerequisites, partial readiness, knowledge reuse, transfer between industries, dual use, licensing, foreign acquisition, reverse engineering, national specialization. No arbitrary node collections per component.

## 10. Production and industrial maturity
Developed ≠ manufacturable efficiently: factories, specialized facilities, machine tools, materials, precision, skilled workforce, methods, quality control, suppliers, imports, volume, yield, cost. Strategic choices: import, license, build domestic manufacturing, special factories, accept lower quality, keep older designs, delay production. The player distinguishes technological possession from industrial capability.

## 11. Retrofit and modernization
A programme defines target product/variant, components to replace, modifications, compatibility, technologies, production and maintenance implications, expected performance, development/test cost, downtime/conversion time. Strategies: replacement; improvement without replacement; structural modification; subsystem modernization; major redesign on an existing platform; conversion to a specialized variant. Example: 500 aircraft — fleet-wide avionics; engines for some; mission-specific upgrades; a new production variant with the old fleet kept; old aircraft to training/reserve. Accounts for compatibility, conversion capacity, cost, production limits, time.

## 12. Military and civilian
One framework for aircraft, helicopters, ships, submarines, land vehicles, missiles, satellites, launchers, reactors, machinery, power plants, comms, radar stations, computers, civil transport; configurable detail (detailed aero engine, abstract truck).

## 13. Historical and alternative development
Support history and alternatives (advanced avionics with an old engine; late axial compressor but early turbine materials; foreign engine with a domestic airframe; advanced aircraft without enough engines; old platform kept because modernization is cheaper; experimental architecture hard to manufacture). No hardcoded outcomes; history defines start technologies, designs, capabilities and projects.

## 14. Player experience
Manage research priorities, programmes, component selection, configurations, production readiness, modernization, industrial investment, procurement and imports, strategic direction — not bolts and wires. Abstraction levels: strategic, programme, engineering, industrial; decide what is automated, delegated to institutions or handled by the player.

## 15. Required outputs (before implementation)
A COMPONENT_EVOLUTION_SYSTEM; B COMPONENT_DATA_MODEL (technology, component architecture, design, version, prototype, qualification, production capability, product, variant, modernization programme, compatibility relation); C ENGINEERING_LIFECYCLE; D COMPONENT_COMPATIBILITY; E TECHNOLOGY_GRAPH_INTEGRATION; F INDUSTRIAL_PRODUCTION_INTEGRATION; G GAMEPLAY_EXAMPLES (≥5: aircraft in service with an old engine while a new one develops; new avionics without engine change; new blade technology in an existing engine architecture; re-engining with changes and tests; advanced technology but insufficient capacity — with timelines, decisions, costs, risks, dependencies, consequences); H MVP_SCOPE (small vertical slice).

## 16. Critical questions (each: recommendation, alternatives, pros/cons, complexity, gameplay consequences)
1 independent component tech levels vs discrete designs/versions; 2 maturity representation; 3 needed detail; 4 compatibility calculation; 5 rules vs engineering parameters vs hybrid; 6 new component vs modified version; 7 component vs product development; 8 knowledge reuse across programmes; 9 production maturity from experience; 10 reverse engineering and transfer; 11 value of older equipment; 12 managing hundreds of products and thousands of components; 13 scaling to thousands of graph nodes; 14 deterministic vs probabilistic parts.

## 17. Constraints
C#, Godot 4.x .NET, global strategy simulation. No coupling of domain logic to Godot scenes/UI. Explicit domain models, data-driven definitions, versioned formats, reusable services, separation of research/engineering/production/operations, testable compatibility rules, determinism where practical. No monolithic component manager, no giant base class with optional fields; composition, typed categories, capabilities, schemas; no abstraction for elegance.

## 18. Final principle
Development as continuous engineering and industrial evolution, not "Jet Fighter Level 3": improve aerodynamics knowledge, develop materials, new compressor architecture, better blade manufacturing, new avionics, build and test an engine, integrate it into an existing aircraft, discover the airframe needs changes, produce a new variant, modernize the fleet, improve quality through experience. Superiority emerges from knowledge, engineering, integration, production, logistics and experience. Realism ≠ complexity; prioritize meaningful decisions, cross-system interaction, long-term depth.

## Owner note (Russian, verbatim)
Я бы особенно внимательно проверил одну вещь: не стоит делать «уровень технологии компонента» единственной характеристикой. Лучше разделить три сущности:
- Технология: что страна знает и умеет применять.
- Конструкция: конкретный вариант компонента с определёнными характеристиками.
- Производственная зрелость: насколько хорошо страна умеет выпускать эту конструкцию.
Например, страна может знать технологию монокристаллических лопаток, иметь проект двигателя с такими лопатками, но пока выпускать их с низким выходом годных изделий. И это совсем другая ситуация, чем отсутствие самой технологии.

Ещё важнее — сохранить историю поколений и преемственность конструкций. Новый двигатель не должен стирать старый. Он должен быть отдельным проектом, иметь собственную производственную линию, а затем становиться возможной модернизацией уже существующего самолёта.

Так мы получим не условные «самолёты третьего поколения», а реальную технологическую эволюцию, в которой разные страны могут прийти к совершенно разным сочетаниям технологий.
