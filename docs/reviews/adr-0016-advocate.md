# ADR-0016 tick budget — advocate review

Gate review of `docs/decisions/ADR-0016-tick-budget.md` (commit 50685b6), 2026-11-17. Agent: advocate (read-only; text transcribed by the Orchestrator).

Summary: the ADR frame is sound but has three weak points. The FAIL trigger is tied to the language choice with no proof that Rust would help. The statistic the budget is read by (mean, median, p95) is not defined. The main decision rests on extrapolation, although the target point N=200/2000 can be measured directly.

1. (strong) FAIL leads to Rust.
   - `S > 300` is 3× the 100 ms share, which is only 60% of the full 500 ms.
   - "Slower than budget" does not mean "Rust is faster". [Assumption] On allocation-free compute loops the C#/Rust gap is typically ~1–1.5×.
   - Adopt: FAIL leads to a gate on algorithm and layout. Rust is considered only if a hot-core port (≤200 lines) shows a measured speedup of ≥2× on the same input.
2. (strong) The budget statistic is undefined.
   - At 1 month/s the player feels the worst months: yearly events, January, gen2 GC pauses. After #228 the worst daily tick matters too.
   - Adopt: month p95 ≤ 500 ms, median ≤ 400; under #228, daily-tick p99 ≤ ~25 ms. Read the S and K shares by p95.
3. (strong) Measure full scale directly instead of relying on the grid.
   - The T3 generator builds N=200 × 2000 nodes anyway.
   - Adopt: the primary reading is a direct measurement of (200, 2000) at t8 and t1. For slopes, 2–3 points per axis are enough.
   - Record a limitation: k disjoint subgraph copies understate prerequisite branching and cache misses. Add a variant with random cross edges.
4. (moderate) Budget at threads 4 rather than 8.
   - The Godot client competes for the same cores, and a laptop's all-core turbo is lower than its single-core turbo.
   - Adopt: dual condition S(t8) ≤ 100 and S(t4) ≤ 150.
   - Also clarify that "half" means a CPU share under pipelined work with the client, not sequential latency.
5. (moderate) Use only the Lab stopwatch in M0, with no BenchmarkDotNet project in `bench/`.
   - K at 200 rows is likely a fraction of a millisecond.
   - Adopt: `lab bench` measures K and S. Make T1 (BDN) conditional: run it only if the Stopwatch K exceeds 10% of its share.
6. (moderate) A nightly CI threshold of +20% is noisy on shared runners.
   - Adopt: gate CI on allocations per tick (a deterministic metric) and on a relative base/head comparison within one run. Measure absolute time periodically on the owner's machine.
7. (moderate, missing) Lab throughput is a second constraint.
   - Lab runs 16 single-thread jobs, so S(t1) matters for experiments. 4.6 min per campaign limits hypotheses more than the game budget does.
   - Adopt: record a target S(t1), or a rule that the default Lab scale is reduced.
8. (moderate) The 100/50/350 split is top-down, and WARN is almost guaranteed (cleaned pilot 170–340 ms, before ×11 nodes).
   - Adopt as an addition: keep 100/50/350, and record unit costs in the EXP (µs per actor-month per system, µs per node), so that future systems declare their unit cost.
9. (weak) Defer the measurement: rejected correctly, because #267 and RR-0001 need the number now. Mark the split as "preliminary until E1".
10. (weak, missing) Memory budget: record peak memory now; set a threshold in the provinces wave.

Open:
- Owner: p95 instead of mean, and the dual t8/t4 condition (items 2 and 4); both make PASS stricter.
- Item 1 changes the meaning of the RR-0001 trigger; agree the wording with research-scientist and systems-architect.
