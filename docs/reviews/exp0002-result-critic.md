---
tags: [review, simulation-lab, done]
---
# EXP-0002 result — critic review

Gate review of `docs/simulation/exp0002-result.md` (commit db31dc8), 2026-11-17. Agent: critic (read-only; text transcribed by the Orchestrator).

## Summary
The outcome `refutes` is correctly derived from the pre-registered rule §7. No blockers. Deviations #264 and #265 do not change the outcome. P0 handling is correct. However, the P3 failure of `L_C` follows mechanically from the low `chg`, and the report's "0.37 → 0.052" explanation is not tested. The negative result therefore supports a narrower conclusion than the report presents.

## Findings
1. Recomputed against the spec [fact]:
   - P1: Wilson CI for 52/1000 = [0.040; 0.068], entirely outside [0.25; 0.45], so P1 fails.
   - P3 `L_K`: CI Δ [−0.071; −0.028] is inside (−0.10; 0.10), and the upper CI of `TVD_bc` is 0.061 < 0.15, so it fails.
   - P3 `L_C`: CI Δ [0.038; 0.065] and the upper TVD is 0.064, so it fails.
   - P3 `L_P`: lower bound of |Δ| is 0.844, so it passes.
   - Split-half p95 is 0.028 < 0.10, so TVD is informative.
   - In `exp0002.py:916-923` the `refutes` branch is checked before `inconclusive`. A P4 failure alone gives only `inconclusive` under §7, so P4 does not affect the outcome.
2. Major. Report §2 and «Вне пре-регистрации»: P3 for `L_C` is not informative because of a floor effect.
   - Δ = `chg_full − chg_off` ≤ `chg_full` = 0.052 < 0.10. A layer that *creates* leader changes cannot pass on Δ at this `chg`, by construction.
   - The leader TVD at tick 359 is also capped near 0.05, because about 95% of seeds have USA as leader.
   - The §7 power calculation assumed `chg_full` ≈ 0.35.
   - The §7 phrase «the outcome shows which layer is not needed» does not apply here: `L_C` creates almost all changes (0.052 against 0.001).
   - Proposal: state in the result that the `L_C` P3 failure (and partly the `L_K` one) follows from the P1 failure; it is not independent evidence. In effect, the refutation rests on one fact: few leader changes.
3. Major. «Краткий вывод» item 5 and «Вне пре-регистрации» item 2: these claims contradict the data.
   - "The `L_K` and `L_C` layers do not create the lead change" and "the layers lock in the USA lead" are wrong for `L_C`: without capability, `chg` = 0.001.
   - Proposal: correct the text to "knowledge and product suppress changes; capability creates them".
4. Major. «Вне пре-регистрации» item 1: the explanation "0.37 (#244) → 0.052 because of the #247 metric fix" is untested and cannot be attributed to one cause. Between print #244 and this run, four things changed:
   - the metric;
   - world dynamics: per #247, `fielded_L` is read by AI, `why_not`, `DefK` and the sale validator; golden were recomputed;
   - I-OC + A41 were enabled (TASK-0058 step 5);
   - seeds: 10001+ against 1–1000.

   D3 (0.011 of actor-months) only shows that the two metrics nearly agree on the *new* dynamics.
   - Proposal: a print outside pre-registration: `chg` by the r6 `fielded` rule on main, from the D3 `calib-print` dump. This separates the share of the change due to the metric from the share due to the dynamics. Until then, "model bug versus real refutation" is not resolved.
5. Major. Report §2 against §7: §7 requires the outcome to be stated with its parameter region (the main point and the grid). The report says only that there is no `knife_edge` flag.
   - Proposal: print the `chg` range over the 27 grid points, and state whether `chg` < 0.25 across the whole region.
6. Major. Interpretation: the refuted model uses the temporary culture rule C-B (RISK-0016; HYP «Поправка v2.4»: the outcome does not carry over to another rule).
   - The `C-B → R0` channel gives `chg` 0.265, at the edge of the band.
   - `START_PERT` gives 0.40 and `START_SYM` 0.78; the §6.5 flag is raised.
   - Proposal: in the HYP outcome, record that what is refuted is "three layers + C-B + the scripted 1945 start". The result does not reject the three-layer architecture as such.
7. Major. Deviation #265: a `git diff` between commits does not cover the 202 runs that recorded a dirty tree.
   - The binary is the same, but mods and `control_tables-EXP-0002.json` are read from the working tree at run time.
   - The report does not check `data_hash` (efcd0984) and the `run.layer.tables` label across all 21 080 `run.json`. Golden equality exists only for main seeds 1–20.
   - Proposal: check these fields in every run.json and list the dirty files. If mods/ and lab/manifests/ are not among them, the deviation is justified. [Assumption] Most likely no effect on the outcome.
8. Minor. Deviation #264 is justified.
   - The filter is by series, not by seed. The only readers of `whynot`/`prc_year` are main (lines 795–813, 964, 1101, 1129, 1386). `star_prc_gen` is a configuration constant.
   - The equivalence evidence lives in `lab/results`, which is not committed; only its sha is kept. Acceptable.
9. Minor. P0 is handled correctly. `DEFAULTS.p0_passed = None` produces the suffix "P0 not confirmed in config" (lines 924–930). The refutes and supports branches do not depend on it. P0 is confirmed in `meta.analysis.p0_passed` = true on the same code 8f7b3de.
   - Proposal: instead of calling it "a formality", say that §7 does not require P0 for `refutes`, and that `p0_source` confirms run validity.
10. Minor. P4 fails not only because capability is 0.799 > 0.75. Product (0.007) and economy (0.000) are also below 0.04. Add this.
11. Minor. The pre-registered print §6.1 «P3 `L_C`» (quality programmes, training series, licences: main against `L_C`) is listed as «не считались». Print it; it is a direct input to finding 2.
12. Minor. `L_P` → `chg` 0.918 may be a control artefact rather than a layer property. The same `T_design`/`ρ` for all actors gives a synchronous G and changes on the G/P field. Mark "product suppresses changes" as [assumption] and check how the `L_P` changes split by field.

## What the refutation supports [proposal]
- Supports: under C-B, the scripted start and `fielded_L`, USA almost never loses the lead (`chg` 0.05). All variance comes from the knowledge flow (`FIX_K` → 0). Capability is needed for the changes that do happen.
- Does not support: "layer X is not needed"; "the three-layer model is wrong"; any fitting to `chg`. A repeat under #43/#46 allows recalibration only. Replacing C-B, the start or the metric is a model change: it needs a new HYP and gate, and the choice is visible through `chg` on seeds 1–1000 (RISK-0011, RISK-0021).

## Open
- Owner question: record the negative result and revise the hypothesis (the `chg` band and the role of the start), or run the single allowed repeat EXP? Decide after findings 4 and 7.
