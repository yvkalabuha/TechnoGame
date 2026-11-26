---
tags: [review, map, gameplay, done]
---
# ADR-0022 — critic (gate 2026-11-18)

1. HIGH (ADR-0022:36, :60, :66): SUN has 31 `owner: SUN, status: core` regions (owners.yaml), so the metropole-only quota still warns (Validator.cs:235); "если их метрополии ≤ 15" hides it. Owner question needed (separate cap, bound 5–35, or permanent warning); rewrite :60/:66.
2. MED (:35): "~27 regions of headroom" uncounted. Known additions ≥ +29: 16 `not_drawn` lines (TASK-0091:42), 9 mixed regions to cut (TECH_DEBT DEBT-0048), Vienna 1 → 5 for strict #350 (ADR-0020:54); Berlin already 2. Add a min/max table per source; choose e.g. 450 or make 401+ a warning that goes to the owner.
3. MED (:21, :36): `status: core` ≠ metropole in the data: Crown Dependencies under GBR (owners.yaml:30,34,37), Puerto Rico, USVI, Alaska, Hawaii under USA, Taiwan `core` under JPN (:344, against ADR-0020:59), Algeria `core` under FRA. Define `core` as legally integral territory, list edge cases, re-check `status`; SWE is also an actor while the quota speaks of great powers.
4. MED (:39): floor 5 "only for the whole world" — `area.World` already true (Validator.cs:133); with strict `core` GBR has 2, USA 3. State floor behaviour and numbers per actor.
5. MED (:43): remedy "simplify or group small countries" on budget overrun conflicts with #327/#331 (speed first, down to the 1 month / 3 s floor) and #384. Apply #327/#331 first, then the owner.
6. MED (:43, :67): no pass/fail for the 400-region point. Use ADR-0016:45 (p95 ≤ 500 ms, median ≤ 400 ms), ADR-0016:103, `K_d > 150` ms flag (ADR-0016:121); name the EXP.
7. MED (:61): hidden costs missing — save size vs ≤ 1 MB/month (ADR-0016:189), AI cost (actors × regions), UI labels and small regions. Give [Предп.] estimates or a separate check.
8. LOW (:38): validator does not warn at < 150 now (Validator.cs:135 checks only > 250); Info string (:132) and comments (:129, :230, :234) need updating; list every line.
9. LOW (:47–49): ADR-0011/0016/0020 are "read as amended" without a pointer to ADR-0022; add an "amended by" pointer (ARCHITECTURE.md or DECISIONS_LOG).
10. LOW: quota scope — write "only the t0 map validator", not runtime after decolonisation.
#350 not violated by ADR-0022 itself.
