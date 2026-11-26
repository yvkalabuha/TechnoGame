---
tags: [review, gameplay, simulation-lab, done]
---
# Gate MS-M1 draft — advocate (2026-11-18)

Artifact: `docs/game-design/MS-M1-draft.md` (commit ffc2c24). Read: DECISIONS_LOG #24/#44/#171/#323–#328, exp0002-result.md.

1. **Strong — P1 and P2 are dependent.** The start leader L(t0) is the same in all seeds, so p_L0 = 1 − chg30 and Neff ≤ 1/(1−chg30)². At chg30 = 0.20, Neff ≤ 1.5625 < 1.6: passing P1 at its lower edge fails P2. At chg30 ≤ 0.12, Neff ≤ 1.29 < 1.3: failing P1 drags P2 down, so "two independent failures" is false [Факт: algebra]. Proposal: P2 over leaders at t1 among seeds with a leader change, or top-5 rank mobility (Kendall τ, t0 vs t1). Recompute P2 bands at the HYP gate (3.1) within #323.
2. **Strong — where between-seed variance comes from.** Events fire on fixed dates (#328), so the seed only drives science stochastics. In EXP-0002 the start drove variance: START_PERT → chg 0.40, START_SYM → 0.78, baseline → 0.052 [Факт: exp0002-result]. Proposal: perturb the 1945 start data per seed within documented source uncertainty. Owner question.
3. **Strong — leader hysteresis.** Axis B (#44) is lexicographic and discrete; many rank ties. Monthly L(t) on a Borda sum will flicker and P3 counts noise. Proposal: a change counts only if the new leader holds ≥ 12 months (or a 12-month moving window); ties within an axis by mean rank.
4. **Strong — floor diagnostic (3.2) contradicts itself.** "≥ 90 % of seeds with one leader at t1" prints 1 − chg30, i.e. peeking (RISK-0021). Proposal: call it a pilot on calibration seeds 22001–22100, excluded from the main series, recorded in the manifest; run it early, right after the first working E1a.
5. **Strong — intermediate gate after E1a** is only in the risk table. Make it a work item (e.g. 2.2g) with criteria: K3 invariants 100 %, tick measurement, floor pilot from item 4.
6. **Strong — cheaper tick budget.** In M1 ownership changes only by dated events, so province columns are static between events. Recompute province → region aggregation only on event days (dirty flag); hash cost is covered by CoW. Provinces then cost almost nothing per tick and #327 may never be triggered. Put this into ADR 0.3 as the first measure.
7. **Moderate — S4 contradicts itself** (non-deciding in the hypothesis, "mandatory with threshold" in risks). A world without events is unhistorical (united Germany is another contender). Keep S4 diagnostic without threshold; add "share of leader changes within 12 months after an event touching the leader or a contender" and ablation by event groups.
8. **Moderate — S1 as chg30_full/chg30_off** divides by ~0. Use a paired design (common random numbers): ablations on the same seeds, McNemar on disagreements, effect as log-odds difference.
9. **Moderate — N seeds and compute.** For band [0.20; 0.50] and fail bounds [0.15; 0.55], ~400 seeds give CI half-width ≈ 0.045 at p = 0.3; 1000 seeds × ~12 ablation series is excessive. Line 118 arithmetic is wrong: 1000 × 3.5 min ≈ 58 h sequential, ≈ 4 h at 16 jobs, not 50–70 h; and 0.5–0.6 s/month (ADR-0016) was measured at threads = 8. Proposal: sequential design (300, extend to 600 by a pre-written rule) and a real measurement in 2.8.
10. **Moderate — critical path.** 3.2 depends on 1.6 (broad #328 research). Proposal: pilot and calibration on a frozen event core (Germany, Korea, condominiums, Taiwan); the rest loads before the 3.3 manifest freeze; entries without a source are deferred.
11. **Weak, keep as is — ADRs first.** A vertical slice without provinces saves little (K2 and hash depend on ADR 0.3). Concession: an E1a prototype in Lab may proceed before ADR 0.2 is ACCEPTED; only Core code waits. Do not replace Borda with share sum (country size would decide again); add item 3 only.
12. **Weak — S3 `dom` undefined** (what wins, for which actor). Specify at the HYP gate.

Open: owner question on start perturbation (item 2). Item 1 to HYP gate 3.1; items 4, 7, 9 to the draft; item 6 to ADR 0.3 (systems-architect).
