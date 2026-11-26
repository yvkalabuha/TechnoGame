# Godot 4 + C# — automated game testing infrastructure (owner brief v1.0)

Source: owner's specification of 2026-11-03 (sent in two parts), kept in substance.

## 0. Context and baseline
Stack: Godot 4.x, C#, .NET compatible with the Godot .NET release, Claude Code with agents, desktop first. Design specifically for Godot 4 + C#; verify engine APIs, CLI options, C# compatibility and dependencies against real versions. Before implementation record: exact Godot version; .NET-enabled editor or not; exact .NET SDK and runtime; target OSes; project structure; existing test frameworks; save architecture; simulation architecture. Do not upgrade engine or .NET only for a test library without evaluating consequences.

## 1. Mission
Permanent infrastructure that lets Claude: build; launch; connect to the running game; create/select a scenario; configure the world; save/load; verify initial conditions; operate the real UI; advance time; inspect state; compare actual vs expected; collect logs, screenshots, performance data; report failures reproducibly; repeat after changes. Both software testing and realistic gameplay testing. Testable from the earliest stages.

## 2. Architecture
Claude Code → Test Orchestrator → (Game Test MCP Server | Test Runner/CLI) → Automation Bridge → (Game Test API | OS UI Automation) → running Godot game / real window → Simulation Control, Scenario Manager, Save Manager → Observability (logs, events, screenshots, performance, reports). Clear separation: simulation, presentation/UI, test functionality, external automation, MCP, reporting. Not everything inside the game executable; a controlled, documented interface, not the whole internal architecture.

## 3. Component A — Godot test harness (`Game.Testing`)
Files (adapt): TestBootstrap, GameTestHarness, GameTestServer, GameTestCommands, GameTestStateProvider, SimulationTestController, ScenarioManager, SaveTestManager, TestEventRecorder, TestAssertions, TestEnvironmentGuard.
- 3.1 Activation only on explicit request (CLI flag, test build config, debug config, test config file), e.g. `--test-mode --test-port 0 --test-scenario … --test-seed …`. Release must not expose test functions. Bootstrap: detect test mode, init services, connect to simulation, open endpoint, register capabilities, report readiness, expose build and test API version; never before essential systems are ready.
- 3.2 Typed C# API; read-only separated from mutations. Read: version, date, simulation state and speed, active country, country info, population, workforce, budget, resources, industrial capacity, production queues, research, technologies, politics, diplomacy, military, logistics, national projects, intelligence, space, events, warnings/errors — structured data. Mutations: pause, resume, speed, advance by duration, load scenario, reset world, create/load test save, approved test commands, supported test events, selected scenario parameters. No "execute arbitrary code"; all mutations through validated domain operations.
- 3.3 Guard: refuse when test mode off; validate everything; restrict to active session; log every mutation; never touch player data or ordinary saves; no credentials; test mode visibly marked.

## 4. Component B — simulation control
- 4.1 Clock: pause, resume, single step, advance by hours/days/ticks, accelerated, read date and tick, wait for checkpoint. The runner must know when advancing completed; never real-time sleeps instead of synchronization.
- 4.2 Determinism: explicit seed, controlled centralized RNG with independent streams (politics, economy, combat, AI, research, world events), stable update order, reproducible init, checkpoints, recordable event sequences. Document limits across platforms, CPUs, runtimes; do not claim cross-platform determinism without proof.
- 4.3 Distinguish simulation, real, rendering and test time; headless runs when rendering is not needed; windowed when it is.

## 5. Component C — automation bridge (`tools/GameTest.AutomationBridge/`)
Separate .NET service: start/monitor the game process, detect readiness, connect to the Test API, send approved commands, receive structured responses, monitor disconnects, collect screenshots/evidence, manage session, report crashes/timeouts.
- 5.1 Evaluate local TCP, WebSocket, named pipes, other IPC. Start with a local authenticated connection and versioned JSON protocol; loopback only; session token; no predictable fixed ports; restricted commands; message size/structure validation; timeouts; clean shutdown; version negotiation. Never public network.
- 5.2 Versioned envelope `{protocolVersion, requestId, command, parameters}` → `{protocolVersion, requestId, success, result, error}`; documented message types; correlation, structured errors, validation, compatibility checks, health checks, timeouts. An accepted command is not proof of a state change: assert state after important mutations.

## 6. Component D — MCP server (`tools/GameTest.Mcp/`)
Official MCP SDK compatible with the .NET version (verify docs). Thin layer over the bridge; no game logic.
- 6.1 Local STDIO transport preferred; start locally, connect to the bridge, documented tools, structured results, clear "no game instance", clean close; not tied to a Claude model.
- 6.2 Tool groups (proposed, keep the final set small and typed): lifecycle (build, launch, close, status, connect, wait ready); UI (screenshot, window state, resolution, click, key, text, scroll, drag, inspect UI); simulation (state, pause, resume, speed, advance, date, country/economy/research/production/military state); scenarios (list, create, validate, load, reset, state); saves (create, load, validate, list, compare); evidence (run test/suite, result, logs, errors, performance, screenshots, compare runs).
- 6.3 Destructive or state-changing operations explicit; safe defaults; never overwrite player saves; no unrestricted filesystem or shell.

## 7. Component E — real UI automation
- 7.1 Internal Godot UI tests (scene creation, buttons, signals, control state, focus, navigation, layout, UI ↔ state); evaluate GdUnit4 / GdUnit4Net for the actual Godot version.
- 7.2 External OS-level adapter: window discovery, focus, mouse click/move, keyboard, scroll, drag, screenshots, resolution, window state; maintained tech per OS; not Windows-only if Linux/macOS are targets; Playwright does not drive native Godot.
- 7.3 Two independent paths — A internal (harness → UI/app interfaces), B real (Claude → OS automation → window); combine for end-to-end (load scenario → real click → API checks order → advance → API checks output → screenshot → visual + state assertions).

## 8. Component F — scenario manager (`tools/GameTest.Scenarios/`, `Game.Testing.Scenarios`)
- 8.1 Declarative JSON or YAML, one canonical format: id, name, description, version, required game version, start date, seed, active country, countries, politics, economy, resources, workforce, education, technologies, research, industry, production chains, trade, diplomacy, military, logistics, national projects, events; sections optional; extend a standard world by changing selected values.
- 8.2 Reusable templates (e.g. `1945_baseline` → cold war, industrial crisis, nuclear race, space programme); inheritance/composition only if clear and safe; no complex scenario language.
- 8.3 Validate: unknown countries, invalid technologies, impossible values, invalid dates, duplicate ids, missing entities, inconsistent diplomacy, invalid production dependencies, invalid military, unsupported versions, contradictions; errors name the exact field; never silently ignore properties.
- 8.4 Init: load base world, apply changes through domain services, validate, init systems, set seed, set date, init events/history, confirm readiness, return structured summary; result equals the requested configuration.

## 9. Component G — test save manager
Test saves are versioned fixtures, separate from player saves.
- 9.1 Create, load, validate, copy into a run, compare with expected, migrate, preserve baselines. No serialization of scene trees or runtime objects; use the domain-state serialization.
- 9.2 Metadata: save id, scenario id, created, game version, save format version, test API version, seed, simulation date, description, expected state summary, test suite.
- 9.3 After load verify date, active country, resources, technologies, production orders, politics, military, projects, RNG state where supported — compare with expected, not just "file opens".
- 9.4 Explicit versioning; test current, older migration, missing/deprecated fields, invalid values, corrupted and incomplete saves, recovery; never overwrite the original fixture; migrated copies stored separately.

## 10. Component H — test runner (`tools/GameTest.Runner/`)
CLI usable by Claude: run a test or suite, select scenarios, build, launch, load saves, connect, act, assert, collect evidence, report, meaningful exit codes (illustrative: `gametest run ECON-PROD-001`, `run-suite economy`, `scenario validate|load …`, `save validate …`, `report latest`).
- 10.1 Structured test definitions: id, name, purpose, system, required build, scenario or save, preconditions, actions, expected results, assertions, evidence, cleanup (YAML example in the original). Results: PASSED, FAILED, BLOCKED, SKIPPED, INCONCLUSIVE; automation completion is not success; only verified assertions give PASSED.

## 11. Component I — observability and evidence (`tools/GameTest.Reporting/`)
Unique run id; collect build id, test/scenario/save ids, seed, timestamps, simulation date before/after, actions, state snapshots, errors, warnings, logs, screenshots, performance, result.
- 11.1 Structured logs: timestamp, severity, subsystem, event id, run id, scenario id, entity ids, error details.
- 11.2 Harness records simulation events (production, consumption, trade, research completion, production start/end, political transition, agreement, engagement, supply disruption).
- 11.3 Screenshots: full window, regions, before/after, named by test and step, visual regression references, tied to the run.
- 11.4 Reports: Markdown, JSON, JUnit XML where useful, optional HTML; totals by result, time, critical failures, reproducible defects, evidence locations, next actions.

## 12. Component J — visual regression and UI quality
UI Art Director takes part in visual test design. Detect overlaps, clipped text, missing icons, wrong colours/layout/scaling, broken charts, missing data, wrong active states, broken navigation, localization overflow, unreadable type, artifacts. Approved reference screenshots; tolerant comparison (antialiasing); not every pixel diff is a defect; combine with structured UI and state assertions (UI shows 3 orders = state has 3 orders, same ids and statuses). Baselines approved by the UI Art Director; never auto-replaced; changes reviewed.

## 13. Component K — performance and stress
Profiles Small / Medium / Large / Extreme. Measure ticks/s, time per simulated day, CPU, memory and growth, rendering, UI response, save/load duration, tech graph and map rendering, AI decision time, per-subsystem time (economy, research, production, logistics, diplomacy, military, politics, AI). Separate rendering from simulation bottlenecks; headless for simulation, windowed for rendering/UI. Budgets only after a measured representative baseline.

## 14. Scenario library
Growing library (baseline 1945/1950; politics; economy; production; research; diplomacy; military; space; nuclear; integrated up to 2100) — concepts, not all now. Each scenario tests explicit hypotheses.

## 15. Three testing levels
1. Fast automated: formulas, accounting, prerequisites, production, transitions, data validation, domain logic — pure C# unit tests where Godot is not needed; GdUnit4Net for Godot-integrated tests.
2. Simulation/gameplay through the Test API: load world, advance, cross-system behaviour, save/load; headless when possible.
3. Real UI and visual: real window, OS input, navigation, layouts, screenshots, baselines, UI shows correct state; needs a graphical session.

## 16. Framework and dependency evaluation
GdUnit4/GdUnit4Net (exact compatibility with Godot, .NET TFM, C# version; never "latest" if it needs a newer Godot); xUnit/NUnit for pure C# — clear responsibilities, no duplication; native UI automation per OS; Godot CLI (import, build, headless, tests, export, logging, debug) verified for the exact version; maintained official MCP SDK for .NET, isolated from the game.

## 17. Project structure
Adapt to the existing repository; no duplicate structures. Suggested: `game/` (Godot project, `Testing/`), `tests/` (Domain, Godot, Integration, Scenarios, Fixtures, UI, Performance, Regression), `tools/GameTest.*`, `artifacts/` (gitignored), `docs/testing/*.md`. Transient artifacts not committed; selected fixtures and approved baselines versioned.

## 18. Agent responsibilities
Test Architect (strategy, standards, coordination); Test Infrastructure Developer; Functional QA; Simulation QA; UI Automation; Performance QA; Regression QA. Do not create separate agents while the project is too small. Shared test definitions, fixtures, evidence format.

## 19. Defect reproduction
Record build id, run id, scenario/save ids, seed, simulation date, actions, logs, screenshots, expected vs actual, reproducibility; create a regression test. After a fix: rebuild, reload the fixture, repeat exactly, verify, run neighbouring tests. Never weaken or remove a failing test to pass; a wrong expectation is documented and approved.

## 20. Security and isolation
Dedicated test profile, save directory, configuration; loopback only; session authentication; strict validation; no remote code execution; no unrestricted filesystem via MCP; no deletion or modification of player data; safe cleanup; explicit approval for destructive operations outside the test environment; test mode unavailable in release builds unless explicitly and safely enabled.

## 21. Phases
0 inspection and dependency verification → `TESTING_ARCHITECTURE_PROPOSAL.md` (stack, alternatives, compatibility, risks, complexity, required changes, plan, owner questions); no major implementation before review. 1 minimum test environment (test mode, build/launch, process detection, API connection, pause/resume, state, screenshots, structured logs, simple runner; acceptance: build, launch, connect, inspect, close safely). 2 deterministic control (central RNG, seed, pause, advance, checkpoints, acceleration, repeatable init). 3 scenarios and saves (format, validation, create, load, saves, metadata, versioning; acceptance: custom world saved, reloaded, conditions verified). 4 bridge + MCP (lifecycle, simulation, scenario and save tools; acceptance: full workflow through MCP). 5 real UI automation (acceptance: real input action verified). 6 integrated suites, regression, evidence, reports. 7 visual and performance.

## 22. First demonstration
Scenario `1945_industrial_baseline` (known date, one active country, defined resources, capacity, workforce, one production chain, fixed seed): build → launch in test mode → bridge connects → scenario loads → API verifies initial state → Claude opens the production UI and creates an order through real input → API verifies → advance → API verifies progress and consumption → screenshot → report → safe close → repeatable. Second: Claude creates a resource-shortage scenario, validates, saves, loads, confirms the shortage. No mock interface or simulated result instead of the real Godot application.

## 23. Acceptance of the complete system
Build and launch; connect; inspect; pause and advance; control real UI; create/validate scenarios; create/load saves; reproducible conditions; UI vs state comparison; automatic logs and screenshots; actionable failures; regression reproduction; performance data; player data protected; maintainable independently of Claude sessions; definitions, scenarios, fixtures, docs in the repository.

## 24. First task
Inspect the repository; do not generate every file, install every dependency, or write placeholder MCP tools that claim to work without a real game. Assessment: Godot and .NET versions; GdUnit4Net compatibility; .NET test framework; MCP SDK; bridge transport; native UI automation for the dev OS; harness integration points; determinism requirements; scenario and save architecture; security; initial structure; minimum viable implementation; risks and open decisions; trade-offs where several tools fit. Then propose the first milestone; wait for owner approval before major architectural changes; then implement incrementally with evidence.

## 25. Non-negotiable principles
Test the real game, not a mock; separate simulation testing from real UI testing; controllable and observable simulation from the start; reproducible scenarios and versioned saves; structured state assertions over visual guessing; real OS input for tests claiming real interaction; automation ≠ gameplay success; keep regression tests; protect player data; do not overengineer the first milestone; independent of Claude specifics; document actual behaviour, not plans.
