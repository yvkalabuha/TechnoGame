---
tags: [review, determinism, core, done]
---
# ADR-0017 — advocate review

Gate review of ADR-0017 (commit 3340b5f), 2026-11-17. Agent: advocate (read-only; text transcribed by the Orchestrator).

1. **(strong) Per-column copy-on-write instead of declared `Writes` (items 7–9).**
   - How it works: `Next` opens a column on first touch, with a lock, a double-check and a copy. `Swap` swaps only the opened columns.
   - Why it is better:
     - The copy is idempotent, so it is deterministic.
     - There are no declarations, so no runtime throw on a rare code branch in play.
     - The saving adapts to what each tick actually writes.
     - There is no "all columns" default debt.
   - Cost: one bool check per `Next` access, the same as item 9 of the ADR.
   - Requires an explicit exception in `csharp-core.md` for the idempotent open under lock.
   - Tests: a full copy and CoW give an equal hash every day; a race test at t16 with 20 repeats.
2. **(moderate) The mandatory monthly and command hashes in play have no named consumer.**
   - Make save, load and end of replay mandatory in play.
   - Put the monthly and command points under an execution-only `HashCheckpoints` flag: on in tests and Lab, off in play.
   - Name the consumer if ADR-0006 or a command journal needs these points.
3. **(weak) A slab layout (one memcpy).** It conflicts with per-column CoW. Keep it as a fallback in R2.
4. **(weak) Double-buffering without a copy:** the rejection stands.
5. **(weak) Parallel copy:** keep it as an addition.
6. **(weak) Dirty flags by row or chunk:** the rejection stands.

Open: adopt CoW instead of `Writes`? Who consumes the monthly and command hashes in play? Add a CoW measurement to R2.
