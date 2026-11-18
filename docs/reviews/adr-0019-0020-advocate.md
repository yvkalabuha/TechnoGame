# Gate ADR-0019 / ADR-0020 — advocate (2026-11-18)

## ADR-0019
1. **Strong — event effective date.** Question 3 wrongly says "from the morning of D" needs an ADR-0011 revision. Proposal: `ScenarioEventSystem` applies an event dated D in the merge of tick D−1 (this is ADR-0011 §3: ownership changes in merge, visible at t+1). Day-D flows then run under the new owner, closer to #328; day-1 decisions see events dated the 1st (otherwise #337, 1945-10-01, and many treaties dated the 1st skip a month of decisions). Change p. 11–13, test 4, question 3.
2. **Strong — push vs pull for transfers.** Default pull and p. 7 ("a transfer is one number x") conflict: source and receiver compute x separately; conservation rests on identical code in two places; `Apportion` under pull is O(n²). Proposal: inter-row transfers as a journal `(src, dst, x)` in chunk buffers; pull only for own-row changes.
3. **Medium — p. 15 rationale.** `checked` overflow depends on order ((+MAX, +1, −1)); determinism rests on fixed emission order, not commutativity. Fix wording, add a boundary property test.
4. **Medium — rationing on the "full day demand"** (p. 7, 16): other rows' demand is not visible in compute of day D, so rationing uses D−1 demand or needs a second phase. Name the one-day lag and choose.
5. **Medium-strong — day-1 decision spike.** At 1 month/s a day is ~33 ms; a day 1 of hundreds of ms is a stutter p95 per month hides. Add p99 of the daily tick to K4. Owner alternative: spread actor decisions over the month (`day = 1 + ordinal mod D`), deterministic, smooths load, removes AI synchrony.
6. **Medium — rate unit (question 2).** Per-month rate jumps ~10 % at Feb/Mar. Stronger: annual rate sliced by day of year (`DaySlice(F_year, dayOfYear, 365|366)`). Add as a third option.
7. **Weak — per-system mode.** Daily228 already mixes frequencies; reword the rationale (no monthly batch flows next to daily ones). Keep the decision.
8. **Weak — residual columns** lose; keep as is.
9. **Medium, owner — SliceLegacy as code branching until M2** is debt across Kernel, Core, Session. Is it enough that SliceLegacy guarantees Kernel invariants (hash, RNG, CoW) and the slice world is reproduced from a git tag?

## ADR-0020
10. **Strong, fact.** "Taiwan with JPN after surrender" and the example (owner JPN, controller ROC) contradict #338 (owner and controller ROC from 1945-10-25). Use #337 (owner DEU, controller POL/SUN east of Oder–Neisse) as the argument for controller. Keep controller.
11. **Strong — stored attribution vs function.** Recomputing 5000 shorts and 250 splits is microseconds; stored columns add derived state, invariant K9 and a hard link "only ScenarioEventSystem recomputes", which breaks in M2 (wars change controller). Proposal: attribution as a pure Core function of `provinces` and statics, memo keyed by `provinces.Version`, not state. If columns stay, a separate `AttributionSystem` recomputes on table version change.
12. **Medium — weights everywhere.** One path: `Apportion` by `w_p` for all regions (default 1 or area); removes the `mixed` special case, same result for aligned regions. Keep CI alignment for big partitions (Korea, Punjab).
13. **Medium — budget.** 100 ms reserve with < 5 ms expected is architecture for M2. Proposal: 10–20 ms in M1, revisit at M2 gate. Simplify: max speed = `max(1 s, 2 × T_p95)` automatically; 1500 ms (#331 floor) the only owner trigger; drop WARN as a separate process.
14. **Weak — one table** loses (×200, ADR-0013). Keep multiple tables and the single-table hash special case.

Open: owner — event timing; spread AI decisions; rate unit; SliceLegacy branching vs git tag. systems-architect — journal transfers, attribution as function, p99 daily tick.
