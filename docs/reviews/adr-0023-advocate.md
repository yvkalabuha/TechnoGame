---
tags: [review, performance, testing, done]
---
# ADR-0023 — advocate review (2026-11-18)

Verdict: structure holds (no frequency gate, foreign-load gate, split P8). One strong alternative to adopt, two moderate.

1. (strong) Median flag repeats only; exclusion only with independent evidence of foreign load (foreign_cpu or run-delay) on every attempt. Otherwise exclusion on `T > 1.10·med_point` clips the upper tail before p90 and makes noise ≤ 10 % near-certain, pulling p95 toward the limit at point A (p95 490 of 500 ms).
2. (moderate) Sensitivity binding: if the row with exclusions differs from the row without, the outcome at that point is at most INCONCLUSIVE (owner question).
3. (moderate, adopt) Per-process run-queue wait from `/proc/<pid>/task/*/schedstat` (field 2, delta per window / threads) plus `nonvoluntary_ctxt_switches`, recorded always; threshold as an EXP-level number.
4. (moderate as diagnostic) Own-thread CPU time (user+sys) recorded; wall·threads/CPU separates run-queue wait from slow execution. Not the outcome (#269 budget is wall time).
5. (weak) Instructions-retired (perf) as outcome: blind to GC, stalls, turbo; needs perf rights. Maybe later as a CI regression metric.
6. (weak) MAD / trimmed means: would reopen #271; record MAD only.
7. (weak) Single total allocation limit: hides tick-path regressions; keep the split; 0 LOH and no per-node reference objects carry most value.
8. (weak) Snapshot A2 or B instead of the limit: A2 does not reduce Lab allocations; B breaks ADR-0014 p.5; defer to the #228 ADR.
9. (weak) Short fixed quiet window: rejected by #408; p.1.8 needs a deadline/default action so an autonomous series does not hang.
10. (minor) X = 10 MiB has no margin over the upper estimate; state that it is set before measurement; probe confirmation covers it.

Sources: kernel.org Scheduler Statistics v6.8 (accessed 2026-11-18).
