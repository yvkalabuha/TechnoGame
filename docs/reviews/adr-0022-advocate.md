# ADR-0022 — advocate (gate 2026-11-18)

Verdict: ADR-0022 follows #384; two choices fail on the data. Fixes stay inside #384.

1. (high) Core-only quota: SUN has 31 `status: core` regions and no colonies (owners.yaml), so the warning stays on for good; the test "no warning for SUN" hides it; n. 3 checks every actor, #171 speaks of great powers. Proposal: quota only for an explicit `great_powers` list; owner question for SUN (named exception or count by union republic); fix the test.
2. (high) Hard cap 400 leaves 27 regions of headroom; #350 makes every ownership-event line a region border; 16 `not_drawn` lines, the 1910 scenario and later partitions remain. Proposal: 400 is provisional, final value from measurement, with review triggers (#384 says "about 400 … by measurement").
3. (medium) One measurement point: measure 250 / 400 / 600 regions to see the cost slope.
4. (medium) Two-level model (full vs light E1 regions) for micro-states (andorra, aland, faroe_islands, guernsey, gibraltar): not in this ADR; record E1 region size limit as an open question for the E1 gate; name it first among fallbacks.
5. (low) Population/GDP quota: not as a quota; the build report prints region population spread (min, median, max, 10 smallest).
6. (low) Merging micro-states: saves 5–10 regions, rejected by #384; keep as is.
7. (low) Quota depends on per-scenario `status` (Guernsey `core` is a Crown dependency): validator checks per scenario file; research-scientist checks `status` in T4.
