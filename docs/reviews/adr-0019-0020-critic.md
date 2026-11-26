---
tags: [review, core, performance, done]
---
# Gate ADR-0019 / ADR-0020 — critic (2026-11-18)

## ADR-0019
- **high, §3 p. 11/13, plan 1.6b/2.9.** `ScenarioEventSystem` writes derived columns and (per plan) moves people and capacity in merge, while Flow systems write Next of the same E1 columns in the same merge; `ITickSystem` allows one writer per column. Transfer is overwritten or breaks the contract; K3 fails on event days. Fix: the event writes only owner/controller/attribution (and actor activation); E1 consequences are computed by the column-owner system on D+1 via a change marker; add an architecture test "one writer per column".
- **high, §1 p. 2, §2 p. 5.** Day-1 Decision acts only from D+1, so day 1 of month m+1 is sliced with the old F, days 2..D with the new; the calendar-month sum equals no F. Fix: run Decision on the last day of the month (or in Session before RunTick of day 1, like `ApplyDeals`); test Σ month = F of that month's decision.
- **medium, §2 p. 5.** Unclear whether amounts or rates are sliced; dividing a rate by D in Fx6 zeroes small rates. Fx6 `*`/`/` floor (`FloorDiv`) but DaySlice/Apportion truncate toward zero: two conventions. Fix: slice only amounts; one rounding convention with a property test.
- **medium, §2 p. 7.** "One number, both sides in one merge" conflicts with one writer when source and receiver columns belong to different systems. Fix: the owner of both columns writes the transfer, or the receiver pulls with a post check.
- **medium, §6 p. 22.** "Hash bytes unchanged" and "ADR-0017 unchanged" are false for Daily228: ADR-0020 hashes three tables with a new header and version vector. Fix: reference ADR-0020 §1 p. 5; list ADR-0017 under «Частично заменяет» in one ADR.
- **medium, §5 p. 18–19.** `TickModel` in `scenario_hash` has no default for the frozen EXP-0002 manifest; editing the scenario file changes data_hash. "RNG keys of the monthly tick" misleads (tick = day index in M0). Fix: missing field → SliceLegacy; do not touch slice files; test the default.
- **medium, Проверка.** Missing: mid-month save/load in Daily228 → same hash; Apportion with W = 0 and negative F (rule: all stays at source); conservation with changing F; K4 measured without HashEveryTick and with game ViewPublish.
- **low, §6 p. 24.** `OnRequest` view exceeds #228 and is unmeasured; add snapshot ×30 to R2 or separate it.
- **low, Research.** Make "EXP-0007 lifts the ADR-0016 flag K_d > 150 ms" an explicit ACCEPTED condition in R1.

## ADR-0020
- **high, §4 p. 17, test 5.** "share × output" in Fx6 rounds down, Σ budgets ≠ output; test 5 fails by construction. Fix: budget = `Apportion(output_r, attr_share[])` in pair order.
- **high, §1 p. 4, §3 p. 12.** Vienna has 4 sectors plus a joint centre = 5 controllers; "≤ 4" fails at t0. Fix: raise to 6 or a separate region for the centre.
- **high, hidden assumption.** New actors (India, Pakistan, Indonesia, FRG/GDR) change `actors` row count, which drives n_rows, chunks and L2. Fix: rows preallocated from t0, `active` column, activation in the ScenarioEventSystem merge; test L2 and hash on such an event.
- **medium, §1 p. 1.** No rule for writing into other tables. Fix: compute writes only own-chunk rows; other tables only via chunk buffers in merge.
- **medium, §5 p. 19.** (a) Stepped speed 2·p95 differs from approved MS-M1 K4 (#333: WARN → #331 floor) — record under «Влияние»; (b) p95 ≤ 500 with median > 400 (#271) unclassified; (c) speed is a build constant from the K4 measurement, not adaptive. Fix: WARN at p95 > 500 or median > 400; speed = max(1 s, 2·p95, 2.5·median) [Предл.].
- **medium, §5 p. 18.** 100 ms for provinces with < 5 ms expected reserves M2 budget at E1's expense (E1 is ×30 under Daily228). Fix: ≤ 10 ms in M1.
- **low, §1 p. 5.** Single- and multi-table headers are indistinguishable if `schema_version == table_count`. Fix: marker int32 −1.
- **low, Контекст/§4 p. 15.** #337 and #338 not reflected; add tests "owner-only change does not change attribution" and joint-polity dissolution (#312).

Open: K4 "floor at once" vs "2·p95" — owner (cite #333). Which ADR formally updates ADR-0017.
