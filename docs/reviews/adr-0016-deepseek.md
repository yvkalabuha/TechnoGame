---
tags: [review, performance, external-llm, done]
---
# ADR-0016 tick budget — DeepSeek third voice

Record: `tools/llm_worker/runs/gate-ADR-0016-tick-budget-50685b67-third.json` (sha256 9784bb7e…). STATUS changes_requested: 2 high, 14 medium, 3 low.

## High
- **WARN can consume the 350 ms reserve.** At the top of WARN, S + K = 450 ms. WARN only requires optimisation and has no exit rule.
- **The FAIL threshold `S > 300` conflicts with RR-0001 §5 ("3× the budget").** The trigger is ambiguous. Resolved by #270.

## Medium
- The status of MS-M0 under WARN, `K > 150` and INCONCLUSIVE is undefined. Resolved by #272.
- WARN has no exit criterion and no escalation.
- "Weaker hardware is covered by the second half of the month" double-counts that half.
- "Full scale" excludes provinces; state that explicitly.
- No outcome is defined for `S > 300` when the cause is allocations or the algorithm.
- INCONCLUSIVE is defined in the note but missing from the ADR.
- A nightly +20% check against a baseline from different hardware cannot work.
- `threads = 8` is attributed to #269, but it was the architect's proposal. Now #271.
- The K check covers neither the view snapshot nor commands.
- GC mode, .NET version, OS, ChunkSize and power settings are not fixed as measurement conditions.
- Core and client compete for the same physical cores; no client frame budget is set.
- No confidence interval on the thresholds when S is extrapolated.
- The post-phase invariants and metrics (ADR-0006) are not counted in the budget.
- There is no reading rule for the threads = 1 number.

## Low
- `related` lacks ADR-0010, ADR-0013 and ADR-0014.
- PASS `S ≤ 100` is unlikely by the note's own forecast; state this as an expected risk.
- "0 bytes per tick" has no definition of steady state.
