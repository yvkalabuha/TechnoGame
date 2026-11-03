# Godot 4 + C# — automated game testing infrastructure (owner brief v1.0)

Source: owner's specification of 2026-11-03, kept in substance. **The text received ends inside section 10 ("10. CO…"); sections 10+ are missing** — to be added when the owner sends them.

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

## 10+ — not received
