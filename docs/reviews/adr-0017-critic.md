---
tags: [review, determinism, core, done]
---
# ADR-0017 — critic review

Gate review of ADR-0017 (commit 3340b5f), 2026-11-17. Agent: critic (read-only; text transcribed by the Orchestrator). No blockers.

1. **Major. Lazy `LastHash` invalidation.** `Current` changes outside ticks: public `Column.Load` (Column.cs:49), save load and commands.
   - Fix: invalidate by a table version counter that increments on every write to `Current` (`Swap`, `Load`, save load).
   - Test: `Load` between ticks, then `LastHash` equals a fresh `StateHash.Compute`.
2. **Major. No thread rule for the lazy computation.** A client reading `LastHash` during `Swap`/`Load` would mix two states.
   - Fix: compute only on the simulation thread (ADR-0014 item 4) and throw on another thread. `SliceView` exposes only an already computed value or null.
3. **Major. The `Next` guard checks the union of `Writes` of the active systems, not per system.** Also, two writers of one column in parallel compute is a race (ADR-0002).
   - Fix: a per-system check via a "current system" handle.
   - Add an architecture test: one writer per column, or the column is marked as a merge mailbox.
4. **Major. Worst case of daily commands.** A command every day means ~30 × 9 ms ≈ 0.27 s/month.
   - Add a "command every day" variant to R4 and EXP-0007.
   - If it exceeds 50 ms, the owner decides (e.g. merge one day's commands into one hash point).
5. Minor. Verification item 1 cannot catch a stale cache. Read on irregular days, interleave with `Load` and commands, and compare against an uncached recompute.
6. Minor. ADR-0006 (post = hash on every non-empty tick) is also changed, as are the ADR-0016 §2 formula `k`, `M + 29·k` and the no-op command fallback. List all of them.
7. Minor. State explicitly that `Next` is not saved, and that a stale `Next` after load is safe.
8. Minor. Measure the per-access bool check on `Next` in R2. Take the span once per chunk.
9. Minor. Add a mandatory hash of the initial state (tick 0).
10. Minor. R2 and R4 need decision thresholds: R4 PASS at `K_d ≤ 50` ms, otherwise the owner decides; R2 (c) needs a minimum gain that justifies `Writes`.
