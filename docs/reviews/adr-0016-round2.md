# ADR-0016 rev 3 — gate round 2

2026-11-17, commit bb6973a. Agents: critic and advocate (read-only; text transcribed by the Orchestrator). DeepSeek: the run was refused by the local secret scan, a false positive from our own fence marker (TASK-0062); it will be rerun after the fix.

## Critic
Revision 3 closes blocker 1 and majors 2–8 of round 1. No blocker remains.

Major:
- **M1.** `T = M + 29·k` multiplies the error in `k` by 29, and `k` has no measurement protocol. Measure the daily mode directly, with every day a non-empty tick on the real session tables, and keep the formula as a check only. Otherwise use `k` = p95 over the same months.
- **M2.** The cause-test protocol is not fixed: threads, points, graph variant, and whether the slope is of S or of T.
- **M3.** The hot spot for the Rust port is chosen after the run. Pre-register it:
  - the top-1 by exclusive CPU inside S, after the optimisation round;
  - share of T ≥ 20%;
  - the same input, Release build, median of ≥ 10 runs;
  - compared against the optimised C# version.
- **M4.** The product-class reserve `19×P` has no consequence. Add a rule: if `S + K_d + 19·P > 500`, a split-review ADR is required before E1.

Minor:
- **m1.** Throttling needs an objective criterion: frequency median < 90% of base. Record both runs; the first valid run decides.
- **m2.** Optimisation round:
  - pick hot spots by exclusive CPU;
  - if the timebox runs out, measure what has been merged and reclassify with the same table.
- **m3.** Kernel goes first in the cause test when `K_d ≥ S`.
- **m4.** WARN-K is a subset of WARN; say so.
- **m5.** The WARN exit compares p95 and median separately.
- **m6.** The Rust port only for the compute route.

## Advocate
- Fair treatment of round 1. Restore t1 as a mandatory recorded point (not a reading).
- **(strong)** FAIL-pending contradicts #272: the optimisation round would delay M0. Remove FAIL-pending. FAIL on the first measurement closes M0, and the optimisation round becomes the first task of the next milestone.
- **(strong)** WARN-K is a flag, not an outcome. Use 5 outcomes: INVALID, INCONCLUSIVE, PASS, WARN, FAIL.
- **(moderate)** The cause chain is redundant. FAIL leads to a redesign ADR from the profile, with the diagnostics recorded. The language is protected by the #270 port test.
- **(moderate)** `T = M + 29k`: measure the daily mode directly if Session allows it.

## Orchestrator resolution
- FAIL-pending is removed (advocate; #272 binds). The optimisation round moves to the next milestone, where the #270 trigger is evaluated.
- Outcomes: INVALID, INCONCLUSIVE, PASS, WARN, FAIL. `K_d > 150` is an action flag (critic m4 and advocate agree).
- The cause chain is replaced by recorded diagnostics plus a redesign ADR (advocate). The language route depends only on the pre-registered Rust port test (critic M3). Critic M2, m3 and m6 are therefore moot. Disagreement recorded.
- Critic M1 accepted: measure the daily mode directly if Session allows it, otherwise use `k_p95`, with the formula as a check.
- Critic M4, m1, m2 and m5 accepted. Advocate: t1 is a mandatory recorded point.

## DeepSeek (rev 4, after TASK-0062)
Record: `tools/llm_worker/runs/gate-ADR-0016-tick-budget-4b40e840-third.json` (sha256 cc7ed444…). changes_requested: 2 high, 7 medium, 3 low.

High:
- **The language trigger differs from the RR-0001 §5 wording ("3×").** Resolution: #270 is the owner's decision and replaces that wording. The ADR must quote #270 and state that it supersedes RR-0001 §5; RR-0001 gets a pointer note.
- **PASS on a known lower-bound slice would "prove" ADR-0001.** Resolution: PASS means "budget not refuted at M0 scale (lower bound)". It does not prove the full-game budget, and the budget is re-measured at E1.

Medium:
- The fallback T underestimates the overhead of the empty daily system.
- The throttling criterion uses base frequency, which is weak: the i7 base is 2.3 GHz and max turbo 4.6 GHz. Also specify sampling and OS.
- The split mixes S p95 with K_d median; use p95 throughout for the split and the 19P rule.
- Rust port: no rule for a top-1 method over 200 lines.
- Rust port comparison: warm-up, outlier rule and statistic are missing; validate the input dump.
- INVALID «п. 5 violated» contradicts the throttling repeat rule.
- The CI allocation threshold at N=24 is undefined.

Low:
- 1945–2100 is 1872 months, not 1860.
- "5 processes" must say they run sequentially.
- `related` lacks RR-0008 and RR-0010.
