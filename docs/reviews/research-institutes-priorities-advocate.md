---
tags: [review, science, institutions, done]
---
# Gate: research-institutes-priorities.md — advocate

STATUS: done
SUMMARY: The design follows owner decisions #484–#491. As advocate I see two strong alternatives the doc should take: fix the competence reference at a constant level, and make a new institute either give real capacity or carry a running cost. One technical claim is wrong: "bit-for-bit at κ=1". I wrote no files.
FILES: none (review only)

FINDINGS:
1. (high) §3, the competence reference `c_ref` = "best institute in the area". With that reference, the best institute gets 0 and everyone else goes negative. So the "institute bonus" the owner asked for in #487 almost never shows as a plus. The doc's own preview says "ЦИАМ (prop 4) +5 %", but by its formula a prop 4 institute that is the best in its area gets 0 %. Alternative (strong): a fixed reference of 2, so levels 0/1/2/3/4 give −10/−5/0/+5/+10 %. This fits #488 (level 0 means −10 %) and #487 (the bonus can be seen). Calibration neutrality comes from rule (3) "no priorities means the same as today", not from `c_ref`, because AI nodes outside priority have m=1. Fix: §3 `c_ref ≡ 2`, then recheck the preview.
2. (high) §5 property (2), "at κ=1 and m=1 the result matches today bit for bit", is not true in Fx6. The code computes `alloc = s * (w / wsum)` (`KnowledgeSystem.cs:363–364`). The doc computes `A_k·w_r/W_k` with `A_k = S·W_k/wsum`, which rounds in a different order. Fix: when there are no priorities (K=0), use the current code path, which keeps property (3). Restate (2) as "within N units of rounding", or test the money sum only (P-2).
3. (medium-high) Dominant strategy "build every institute early". A slot only moves money around and adds no science, while the build is paid from the same `S` (up to 24 % of a year's science budget for ONERA). Its value is permanent and its cost is paid once, so building early always wins. The "8–12 years to pay back" figure has nothing behind it. Also, the `capacity` field "(разд. 5)" from §1.3 is not used anywhere in §5. Alternative (strong): a small running cost per institute (≈0.3–0.5 % of `S` per year). That gives a trade-off every year, gives the reserve a steady use (#475), and limits "greedy" play without a hard cap. Or else give `capacity` a meaning, since #192 says "capacity grows in steps". Fix: pick one of the two and delete the unused field.
4. (medium) "Always assign to the best institute" is nearly forced: the only alternative choice is −5/−10 % today for experience later. Combined with growth from non-profile assignments, every institute drifts towards level 4 everywhere and specialisation disappears. Alternative (moderate): experience grows faster in an institute's adjacent areas, plus a cap of "at most 2 areas at level ≥3". Or keep the doc's rule but raise the 0→1 threshold to ≈5 nodes. Verify with a new check P-8: the spread of levels after 40 years.
5. (medium) Asymmetry with the AI (§7): AI κ=1 while the player has κ=1.5. AI slots then mean nothing, P-4 will show an advantage built into the design, and the "smart AI" is half-empty. This is not a reason to hold the gate, but in the gate decision state openly that the player gets an edge over the AI in v0. The AI's κ moves to the recalibration package after #457/#458. Do not leave it as an implicit "second version".
6. (medium, minimal scope) Two growth paths plus "forgetting after 20 years" is extra in v0. Forgetting almost never triggers but adds a counter per (institute, area). Fix: drop forgetting from v0 and keep experience plus the "deepen area" project (#192/#489).
7. (low-medium) §3, path nodes take competence from their own area. This makes forecasts harder to read: a −10 % on a path node is invisible in the preview. Alternative (moderate): m=1 on the path, m_inst only on the target node. Simpler, and the player sees one number.
8. (low) §4, 12-month pause on reassignment. This is a hidden penalty, and #484 says priority has no penalty. Alternative: no pause, but the 12-month limit counts only for slot changes that bring a gain, i.e. an anti-micromanagement rule that is shown on the greyed-out button. Or keep it and call it a limit in the text, not "no penalty".
9. (low) OPEN 1 is already answered: #487 says "plus the institute bonus... on top". The cap of ±15 % (#462) applies to `m`; the ×1.5 money goes separately. Close it with a reference to #487 instead of sending it to the Orchestrator.
10. (low) §5: "the remainder burns" cannot happen with κ>1. When every frontier node is in a group, Σdem = κ·S > S. The sentence is harmless but misleading; delete it or mark it as κ=1 only.

Checked by grep: the 5 disciplines in `mods/core/nodes.yaml` (99 nodes); the weight `Priority·J` and the allocation in `KnowledgeSystem.cs:234–250, 345–364`; the texts of #192, #458, #461, #462, #465, #475, #484–#491; and the reserve link in `PLAYER_LEVERS_WAVE1.md:80`.
Not checked: IP2 6.1 (how the AI trajectory is normalised); the cost and payback numbers (they are the economy-analyst's job); the sources in §1.1.

OPEN:
- Orchestrator: choose for finding 3, running cost or `capacity`. This is D2 if it is the cost, D1 if it touches #192.
- Owner: does the edge for the player over the AI (AI κ=1) in v0 need to be shown openly (finding 5)?
