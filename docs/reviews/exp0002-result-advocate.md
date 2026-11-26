---
tags: [review, simulation-lab, done]
---
# EXP-0002 result — advocate review

Gate review of `docs/simulation/exp0002-result.md` (commit db31dc8), 2026-11-17. Agent: advocate (read-only; text transcribed by the Orchestrator).

## Recommendation
Option (d): (a) plus an analysis. Accept `refutes`; do not use recalibration slot #43. First, analyse the mechanism on the existing data, labelled as post hoc. Then write a new hypothesis with a different "open race" metric in the next milestone.

## Findings
- (high) The three failures share one cause; they are not three independent pieces of evidence.
  - The P3 thresholds assume `chg` ≈ 0.35 (spec §7, "Power").
  - At `chg` = 0.052, the largest possible Δ for `L_C` is 0.052 < 0.10. The P3 failures of `L_K` and `L_C` are a floor effect of P1.
  - Only P4 (0.799) is independent of P1. The result report should say this.
- (high) For the game [assumption]: the slice has no open, legible catch-up race now.
  - The start sets the outcome: `START_SYM` 0.78.
  - All variance comes from the knowledge draw: `FIX_K` gives 0. AI decisions tend to damp variance: `FIX_A` gives `chg` 0.33.
  - D2: the actor that closes a link becomes leader in 1.7% of cases, against 0.1% otherwise. The promise "close the link and you catch up" is nearly empty in absolute numbers.
  - Useful negative findings:
    - the knowledge mechanism reproduces a schedule with fitted timings (the HYP-0001 lesson repeats);
    - capability causes almost all leader changes (without it `chg` is 0.001).
- Option (a), accept and close M0: **strong** as a base, but gives no next step.
  - HARKing risk zero; cost minimal; the infrastructure is ready.
  - MS-M0 item 5 is ambiguous ("tested" vs "confirmed"); the owner decides. The tick benchmark is not closed.
- Option (b1), move the P1 band or the layer roles: **weak**, pure HARKing. The 30–40% band is the owner's design target (#24), not an estimate.
- Option (b2), recalibration via #43/#46 on seeds 20001+: **weak**.
  - Calibration §8.2 uses micro-anchors without `chg`, and `L` is already 0. No legitimate knob moves `chg` from 0.05 to 0.3.
  - The only free knob is the unidentified ALPHA; fitting it would be p-hacking.
- Option (b3), split the joint hypothesis into small claims: **moderate**, only as part of (d) and on new seeds.
- Option (c), change the model now: **moderate**. The direction is right (start, `L_P`, `FORCE`/`DERIV` 0.6–1.0, `PURCHASE_CAP` 0.29), but the RISK-0011/0021 risk is high.
  - The value 0.918 with `L_P` off may be a control artefact (the `producible` preset by snapshot) or noise on a coarse per-generation metric, not real openness.
  - S3 and TVD are not analysed for that series. Do not change the model on that number alone.
- Option (d), recommended: **strong**.
  1. Record `refutes`; HYP-0002 becomes REJECTED in its current form.
  2. Explicitly decline slot #43, with the reason (owner decision).
  3. Analyse `lab/results/exp0002` only, labelled "after seeing the result", with no criteria. Questions:
     - who takes the lead in 1968–75, from whom, and by which field (G/N/P);
     - why `L_P` damps leader changes, and whether that is an artefact;
     - why `FIX_A` raises `chg`.
     Agents: simulation-engineer and economy-analyst.
  4. game-designer redefines "open race" before any runs. Binary "≥1 leader change in 30 years" over G1–G4 is a weak metric. Candidates: time to parity, share of contested years, the challenger–leader gap, D2 as the primary metric.
  5. Model changes go through a gate, justified by mechanism and history. [Assumption] Primacy in 1945–50 is historically disputed (British engines, Nene to MiG-15). That argues for revisiting the USA head start and the licence channel, not for fitting `chg`.
  6. New HYP and EXP on new seeds in the next milestone. The HARKing risk is controlled: each claim from the analysis is confirmed only by a new pre-registered EXP.

## Open
1. Owner: decline recalibration slot #43, with the reason recorded, or use it?
2. Owner: for MS-M0 item 5, is "tested" enough, or is "confirmed" required? This decides whether M0 can close with `refutes`.
