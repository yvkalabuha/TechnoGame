---
tags: [review, performance, done]
---
# ADR-0016 tick budget — critic review

Gate review of `docs/decisions/ADR-0016-tick-budget.md` (commit 50685b6), 2026-11-17. Agent: critic (read-only; text transcribed by the Orchestrator).

Summary: the ADR follows #269. The weak part is the PASS/WARN/FAIL reading: one blocker and several major ambiguities that would let the outcome be chosen after the run.

1. **Blocker.** FAIL row (ADR §4, note §5).
   - (a) There is no outcome for `S > 300` when the profile blames allocations or an algorithm.
   - (b) "One optimisation round" is undefined.
   - (c) "Compute vs algorithm" is subjective.

   Fix:
   - `S > 300` before optimisation means FAIL-pending.
   - One round is one TASK on the top-N hotspots, timeboxed, then re-measured with the same protocol.
   - If S is still > 300, the outcome is FAIL whatever the cause. The cause only picks the route: compute leads to the RR-0001 trigger; allocations or an algorithm lead to a redesign ADR.
   - Pre-register an objective cause test, for example a log-log slope and the GC share of time.
2. **Major.**
   - `K > 150` is not in the table, so the outcome is undefined. Add a row: a Kernel ADR under #228, and M0 still recorded.
   - The caveat about S(t1) vs S(t8) is an escape hatch. Replace it with a pre-registered speedup test: if S(t1)/S(t8) < 4, the cause is parallelism (ADR-0006), and the language is then judged by S(t1) against 8×100 ms.
3. **Major.** K binding mode. The binding K is the #228 daily mode (30 ticks per month); record the monthly mode for reference only. Likewise, the full ViewMode counts; Calibration is reference only.
4. **Major.** How S is obtained.
   - Pre-register S as the median of the direct measurement at (200, 2000, t8); slopes are diagnostic only.
   - S is measured only in the monthly mode. State it as a lower bound, or add a synthetic Compute×30 at 1/30 of the work.
5. **Major.** The split decides the language trigger.
   - The 20% slice share is argued only in words, and the note's own prior (170–340 ms) puts the slice into WARN/FAIL. S=300 + K=50 still fits within 500.
   - Show the owner that the trigger is "3× the slice share". Before a language FAIL, review the split by a new ADR with cost estimates for the reserve systems.
6. **Major.** One product class in S. Add a product-class axis (1/5/20), or set the reserve for it explicitly.
7. **Major.** Determinism (ADR-0002) is missing from the method. A run is valid only if StateHash is identical across threads 1/4/8/16 and across ChunkSize values for each seed. Otherwise the result is INVALID.
8. **Major.** The outcome table vs #267. M0 closes on PASS, WARN or FAIL; only INCONCLUSIVE or INVALID block it.
9. **Medium.** "0 allocations in Compute/Merge" is premature and not testable as written. `GC.GetTotalAllocatedBytes(true)` also counts Parallel.For and Task allocations.
   - In the ADR: a bytes-per-tick budget, and 0 gen2 collections over 120 months.
   - Per system: 0 B in Compute as a BDN MemoryDiagnoser target, single-thread.
   - The rule itself goes into csharp-core.md.
10. **Medium.** SMT threads are not a separate resource for the client. Keep t8, and add one run with a synthetic background load on 2 threads.
11. **Minor.** The claim "weaker hardware is covered by the second half" is not from #269. Drop it or mark it [Предп.]; record t4 as the weak-machine point.
12. **Minor.** Noise statistic. Spread = (p90−p10)/median over 15 process medians, threshold ≤ 10%; repeat on throttling. The reading is the median of medians.
13. **Minor.** Godot runs on net8.0. Add one net8.0 point, or mark the .NET 10 numbers as optimistic.
14. **Minor.** Nightly CI: compare against a base taken on the same runner; check the absolute budget only on the i7.
15. **Minor.** Frontmatter `related`: add ADR-0002 and ADR-0010.

Open: the owner confirms that the RR-0001 trigger is read against the slice share (300 ms), not against the whole 500 ms.
