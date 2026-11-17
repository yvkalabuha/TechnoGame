---
id: TASK-0065
title: "Synthetic full-scale world overlay and benchmark pre-registration (ADR-0016 T2)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: simulation-engineer
category: lab
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0016, EXP-0007]
approved_by: orchestrator
approved_at: 2026-11-17
approval_ref: "ADR-0016 ACCEPTED (#276), light task inside approved plan"
created: 2026-11-17
---


## Goal (T2)
Synthetic world generator as a Lab-only overlay (ADR-0010):
- actors 6/24/96/200 × nodes 180/500/2000;
- clones plus a cross-edge variant.

Write the pre-registration into EXP-0007 before any timed run: protocol, statistics, outcome table and diagnostics, all per ADR-0016.

## Inputs
- docs/decisions/ADR-0016-tick-budget.md (ACCEPTED, rev 5)
- docs/architecture/notes/tick-benchmark-m0.md §2, §5
- DECISIONS_LOG #269–#272, #276

## Acceptance
- Matches the ADR-0016 rules; the full `dotnet test` is green; zone_check is clean.
## Результат (2026-11-17, simulation-engineer)
- Генератор: `src/Singularity.Lab/Bench/SynthWorld.cs` — `SynthWorldGenerator.Build(SynthWorldSpec, outDir)` пишет оверлей-мод `synth_bench` (ADR-0010) и загружает core + оверлей; `SynthWorldSpec.BenchmarkGrid()` — 24 точки (акторы 6/24/96/200 × узлы 180/500/2000 × Clones/CrossEdges). Core и Session не менялись. Точка входа для `lab bench` (TASK-0064): `Build(...)`, затем `SliceRunConfig { ScenarioId = SynthWorldGenerator.ScenarioId }`.
- Проверки: `src/Singularity.Lab/Bench/SynthChecks.cs` — 5 проверок `synth:` в `selftest`: детерминизм генератора; клоны совпадают с источником; оверлей 5×61 воспроизводит прогон core; StateHash одинаков при threads 1/4/8/16 и ChunkSize 1/8/32; старт полного масштаба.
- Пре-регистрация: `development/experiments/EXP-0007-tick-benchmark-m0.md`, раздел «Пре-регистрация» П1–П8.
- По просьбе Orchestrator `JetSliceModelFactory.SurfaceVersion` изменён с 7 на 8 (TASK-0063). `SliceData.FindMods` стал `internal`.
- [Факт, смоук] Точка (200, 2000) при t8 — около 40 с на месяц. Объём протокола требует решения до T3 (EXP-0007, «Ошибки и аномалии»).
