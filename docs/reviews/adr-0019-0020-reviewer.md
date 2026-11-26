---
tags: [review, core, done]
---
# Gate ADR-0019 / ADR-0020 — reviewer (code feasibility, 2026-11-18)

## ADR-0019
- **major, §5 p. 1, p. 18–19.** `SliceWorld.RegisterRunColumns:484` writes `run.scenario` = first 8 bytes of `Scenario.Hash`; adding `TickModel` to the slice scenario hash breaks `824f4ded…`. Proposal: SliceLegacy (default) adds nothing to hash or columns, like `run.layer`/`run.a41`; pin test.
- **major, Контекст "RNG key (seed, tick, …)".** True for Kernel, but Core keys by month index (`KnowledgeRng.cs:8`, #60); a daily Flow system gets the same draw 30 times. Define the Flow key (month, day) or "RNG only in Decision".
- **major, p. 3, p. 9.** Role exchange undefined: `IndustrySystem.CreateMailbox:194` throws on sender/receiver cadence mismatch; Policy/Products (Decision, Monthly) send to Industry (Flow, Daily). Commands are month-bound (`CommandState` stamp = month + 1, one-shot slots). Define: a day-15 command acts on Flow from D+1 or from the 1st.
- **major, p. 5.** `DaySlice` only slices linear flows; non-linear monthly rules (decay δ_H, culture/ramp λ, thresholds I1–I3 in `IndustrySystem`) need per-day parameters (δ_day = 1−(1−δ)^(1/D): Kernel pow, Fx6 error). Extend R3 with an inventory of non-linear rules.
- minor, p. 18: branching in 4 systems with existing `IndustryMode`/`ProductsMode`/Layer is heavy. Proposal: SliceLegacy = current classes frozen; Daily228 = new Flow/Decision classes. `Cadence` is hard-wired (`IndustrySystem:186`).
- minor, p. 15: "checked ⇒ order-independent sum" is false; determinism rests on fixed chunk order.
- minor, p. 6 `Apportion`: W = 0, negative F, weight-sum overflow (long or Int128) undefined.
- minor, p. 24 `OnRequest`: the frame flag is set from another thread (Volatile); a mid-month snapshot lacks `MonthStepView`.
- Missing tests: DaySlice at F = long.MinValue; leap February from `TickCalendar`; Apportion W = 0; save/load on day 15 → same hash; replay with a command not on day 1; unique Flow RNG keys per day; Decision→Flow mailbox; threshold event date (p. 8); `LastTickRan` always true in Daily228; SliceLegacy hash pin after adding `TickModel`.

## ADR-0020
- [Факт] items verified: only `LongColumn`/`IntColumn`; hash header (schema_version, n_rows); `Version` grows on swap and `Load`. ≈14 MB not checked.
- **major, p. 2 `ShortColumn`.** `SliceWorld.SchemaFingerprint:403` writes `c is IntColumn ? "i" : "l"`; short would print as "l". Exhaustive type switch and a test.
- **major, p. 1 multi-table world.** Wider than R2: `TickContext.Table` (one), `TickRunner._table`/`ChunkCount`/`_failures`, `Merge(ctx, chunkCount)`, system buffers (`ChunkCountFor(Table.RowCount)`, `SliceWorld:108`); `BeginTick`/`EndTick` for all tables. Changes the `ITickSystem` API of every Core system; estimate in R2.
- minor: "version vector" → `World.Version = Σ Table.Version` (monotone; `_hashVersion` stays long).
- minor, p. 8: ChunkTable = provinces with ChunkSize 1 → 5000 empty compute calls a day; ScenarioEventSystem in merge only or chunked by a small table.
- minor, p. 10: K9 check must not depend on `HashEveryTick`; separate invariant flag.
- Missing tests: golden byte vector for a 3-table hash; `ShortColumn` byte path == fast path; a provinces-only change resets the `LastHash` cache; `Load`/`Next` of another table inside a tick; two events on one day (date, event_id); joint polity controlling a mixed region (Apportion inside Apportion) exact sum; table with 0 rows.

## TASK-0080 (4f374c5) — approve
- minor, Column.cs:90: `InTick` resets in `EndTick` before Post, so `Load` in Post is allowed though ADR-0017 p. 12 says never inside a tick; no Post test.
- nit: `CheckOwner:79` message mentions the hash; `SliceSession.cs:211` parentheses; class doc line 13 outdated. `LastTickRan` public is acceptable; stays true after an exception in `RunTick`.
- The HashEveryTick test is tautological on the current slice (acknowledged); follow-up after #228.
