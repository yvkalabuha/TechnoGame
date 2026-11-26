---
tags: [review, gameplay, simulation-lab, done]
---
# Gate MS-M1 draft — critic (2026-11-18)

Artifact: `docs/game-design/MS-M1-draft.md` (commit ffc2c24).

**High**
1. P1 and P2 are mathematically linked if L(t0) is the same in all seeds: Neff ≤ 1/(1−chg30)²; at chg30 = 0.20, Neff ≤ 1.56 < 1.6. "Two independent failures" is one event; P1 lower edge contradicts P2. Fix: check in the HYP spec whether L(t0) is constant; if so compute P2 over seeds with a leader change, or replace it (e.g. TVD of the rank distribution at t1). Align the bands at the HYP gate.
2. Race object (axis A Borda) vs #27: #27 defines the leader by technology level; volume is a separate metric without a target. Fix: leader = tech_lead (#44), industrial share printed separately — or an explicit owner question on revising #27.
3. P3 flip: Borda flicker with monthly recomputation; a P3 failure alone gives `refutes`, so a metric artefact could refute the hypothesis. Fix: hysteresis set before runs (new leader holds ≥ 12 months) or yearly points; define ties on axis B.
4. S4 vs strict dates #328 — internal contradiction (diagnostic in the metric section, "mandatory with threshold" in risks, no threshold given). Fixed dates shift the distribution and may predetermine the leader. Fix: pre-write a rule — if the no-events run is out of band and the main run is in band, the outcome is "supports only with scripts", recorded separately.
5. Hidden assumption "region ↔ one owner". #328 events split E1 regions (Germany, Korea, Punjab, Bengal, Palestine). Fix in ADR 0.3: region borders must match all event borders (CI check), or a region splits by owner shares.

**Medium**
6. Floor diagnostic 3.2 is peeking at chg30 when L(t0) is constant (RISK-0021), and runs before 2.3/2.4/2.9 and calibration 3.3. Fix: calibration by micro-anchors first, then diagnostics only of start quantities; allowed start correction and its limit written into the HYP in advance.
7. Levels: 1.6 changes StateHash, L2 and in-day order → gate+research. 1.2 (world owners table, #314 check) at light is inconsistent with 1.6/2.1 → at least gate on facts.
8. Dependencies: 1.6 includes "moving people and capacity by E1" but depends only on 0.2, 1.3, while 2.9 depends on 1.6 — a cycle. Fix: 1.6a data and dates (research-scientist), 1.6b rules and log (after 2.2), then 2.9.
9. K5 "if the ADR accepts it": #228 is an owner decision; ADR 0.2 decides how, not whether. Remove the condition.
10. K4 and #327/#269: "or another share per ADR" makes the criterion unfalsifiable; a new max speed revises #269, an owner decision. Fix: owner answers the lower speed limit before ADR 0.3; K4 as PASS/WARN/FAIL like #272; record the #269 replacement in DECISIONS_LOG.
11. RISK-0001 measure "cut provinces" contradicts #327. Fix: order of cuts — N seeds and horizon first, then breadth of #328 only with owner consent, provinces untouched; the E1a intermediate gate is a checkpoint where the owner may split M1a/M1b.
12. Run cost: 0.5 s/month → 360 months ≈ 3 min; 1000 seeds at 16 jobs ≈ 3–4 h, not 50–70 h; but 0.5 s was at threads = 8, and ablations multiply ×8–10. Fix: full matrix series × N × single-thread daily-mode time in 3.2.
13. Participants G: on 1945-10-01 Germany has no government; states created by events (India, FRG, Indonesia) drop out of G though they may lead. Fix: G = actors existing at t1 with ≥ 5 regions at t1; entry rule written before runs.
14. #24 asymmetry: wide Neff could come from small-country wins. Fix: print the share of seeds where L(t1) is not a great power, with an error threshold.
15. P2 without CI: bootstrap CI over seeds; failure by the whole CI like P1.
16. K8/S2: if why_not always has a "main cause", 70 % holds by construction. Fix: counterfactual check on a sample (remove the cause, the change disappears).

**Low**
17. K7: carrying #272 ("any valid outcome closes the criterion") from the benchmark to the hypothesis needs owner consent; state the link to #43 (one repeat EXP).
18. S3 `dom` undefined — define in HYP or drop.
19. K6 `slice_legacy`: bit-exact dual semantics is a lasting cost; give it a lifetime or a TECH_DEBT row.

Open: owner — axis A vs #27; lower speed limit (#269 replacement) before ADR 0.3; #272 rule for HYP outcome (K7). systems-architect — multi-owner region in ADR 0.3.
