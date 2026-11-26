---
tags: [review, economy, external-llm, done]
---
# Gate review: docs/game-design/notes/production-mastery.md — DeepSeek third voice

> 2026-11-24. Record: `tools/llm_worker/runs/gate-production-mastery-32f963ce-third.json`. Note: finding "horizon mismatch 1945–1975" is stale — the M-play horizon is 1945–2030 (#422, #423).

## FINDINGS
- high | Horizon mismatch: 2029–2033/2030 dates and 1020-month criteria vs M-play 1945–1975 | State which horizon is authoritative. Split M-play (348 months) from the long-horizon probe. In M-play UI use only dates ≤1975 or gate the 2100 continuation explicitly. Recompute or relabel P2–P8 as long-horizon criteria and keep P9 as the M-play playtest.
- high | Mastered requires proc_v ≥ v_min for 6 months, but the programme creates no output and the series starts after Mastered | Define Mastered before series from H/yield/programme progress, with Handover before Mastered, or explicitly allow series start in Pilot at y_start and count Mastered only after 6 months of actual v ≥ v_min. Remove or redefine T_hand so it does not sit after a Mastered state that already requires series output.
- high | Programme cannot guarantee E_useful/lead; V_OC alone does not grow culture | Choose one: guarantee the programme process becomes lead and measure displacement of the serial process, give programmes an explicit E/E_useful exception, or do not rely on D/lead for the culture input. Add a test where the programme process is not the cell leader and assert either expected growth or a visible why_not refusal.
- high | T_max=120 months contradicts P5 ETA ≤25 years and refusal ETA >40 years | Align T_max with the ETA criterion: either T_max must be at least the ETA threshold, or P5/refusal must use months bounded by T_max. State whether ETA is programme duration or time from project start to mastery.
- high | #127 conflict is unresolved: V_OC is nominal experience production | Obtain an explicit owner decision that #439 overrides #127 for this mechanic. If it does not, remove the programme and state that foresight plus licence/catch-up does not cure deadlock B for a leader with no donor.
- high | P1 does not test the claimed invariant that AI never starts a series it cannot master | Add P1b: false-positive rate of Masterable, actual mastery by predicted ETA, and handling of failed programme launches. Reformulate P1 as 'no launch without Masterable or an active programme', and add a separate forecast-calibration criterion.
- medium | Measurement manifest diverges from fleet-without-replacement §7 | Align seeds, threads, and the G=0 metric with fleet-without-replacement §7, or explicitly document why TASK-0133 is a different manifest. Add a crosswalk between P4 and fleet measure 1.
- medium | Programme cost from D is undefined and may double-count or be free | Specify the monthly programme cost formula, whether it is proc_E or a separate D sub-account, its cap per process and per country, the interaction with E_useful and 2 slots, and the residual to tooling. Keep the invariant ΣE + remainder = D and use the same formula in P5.
- medium | M_PLAY_LOOP is not integrated: StartProgramme is absent and player purchase of old generation contradicts the role | Add StartProgramme to the M_PLAY_LOOP command set and D1, or mark the programme as AI-only. Replace 'player buys old generation' with the existing budget-share/AI procurement mechanism or remove it from the player choice set.
- medium | P2 and P7 do not cover the recommended FRA/GBR play and #24 small-country asymmetry | Add P2/P7 measurements for FRA, GBR, and a small country such as SWE, with a separate small-country-vs-leader test. Justify any exclusion of FRA from the criteria or remove it.
- medium | T_hand=24 months after Mastered can expire before the series starts | Hold V_OC until the expected series start rather than a fixed 24 months after Mastered, or incorporate design lead time into T_hand. Add a test for 'Mastered before project is ready to start series'.
- low | P6 ablation does not isolate programme without foresight | Add a 'programme without foresight' branch, or explicitly state why it is impossible and what alternative comparison answers the §7 decision question.
- low | Hcap monotonic assumption is asserted without support | Prove monotonicity from #438 or implement and test the Hcap-drop failure path.
## OPEN
- Which horizon is authoritative for this mechanic: M-play 1945–1975 or the extended 2030/2100 probe? If M-play, what replaces the 2031/2033 examples and the 2030 criteria?
- Does #439 explicitly override #127, and is nominal V_OC 'опытное производство'?
- Can GrowthNominal be driven by a programme slot without changing C-B contract v2.4, hash, and golden, and how does it coexist with A41 training series?
- If the programme process cannot become the cell leader, how does it get E and grow culture? Should R2-4 be relaxed for programmes?
- What is the monthly cost formula and D reservation for up to two programmes, and how does it interact with E_useful and the 25% B_prog?
- What exactly is Mastered and in what order: before the series, or after 6 months of series output, and when does T_hand apply?
- How will Masterable forecast calibration and false positives be measured before claiming AI never launches a series it cannot master?
- Should P2/P7 include FRA and SWE and a separate small-country test under #24?
- Which experiment manifest governs: seeds 1–10/threads 1 and 4, or seeds 1–20/threads 1 and 2 as in fleet-without-replacement §7?
## STATUS
changes_requested
## SUMMARY
The document proposes a plausible programme input to C-B and identifies real interactions with Hcap, lead, and fleet aging, but it has several high-severity contradictions. It is framed for M-play 1945–1975 yet uses 2030/2031/2033 dates and 1020-month criteria; Mastered requires series output while the programme creates no output and the series is supposed to start only after Mastered; programme growth needs E, but CbCultureRule can return E_useful=0 when the process does not lead its cell; T_max=120 months contradicts P5 ETA of 25/40 years; and the #127 owner conflict is unresolved. The measurement manifest also diverges from fleet-without-replacement. Revision is required before approval.