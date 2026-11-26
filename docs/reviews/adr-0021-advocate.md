---
tags: [review, core, determinism, done]
---
# Gate ADR-0021 — advocate (2026-11-18)

The base is right: the role only rolls dice, one writer per column, the #342 boundary holds. Partly adopt rejected alternatives in three places.

1. **strong — historical exposures as deterministic scripted events** (question 1, option (a) or hybrid (c)). #342 and #329 keep base history seed-independent; random Gouzenko-1945 or Fuchs-1950 make key Cold War dates float across seeds; option (b) turns hypothesis checks into ≥ 100-seed statistics for known events. Conflict with the stream is already solved by p. 3 and #341 (an earlier random exposure → the scripted event drops as `target_gone` or is skipped). Cost: a structural atom like `network_exists(o,t)` — a new atom in the #340 vocabulary, needs approval. Then question 3 simplifies: base scenario almost seed-independent, golden with `off`.
2. **moderate→strong — cooldown state to the domain owner.** p. 10 lets `StochasticEventSystem` write `stoch_last_tick_*` / `stoch_count_*` in other tables (Intelligence, Logistics): two writers per table. Alternative: the owner zeroes `hazard` for the cooldown when applying consequences; it already keeps the counter. Only `stoch_today` remains; `cooldown_days` leaves p. 5 and p. 8; why_not reads one column. Drop p. 10, describe cooldown as an owner duty in R3.
3. **moderate — rolls inside owner systems via a shared library.** The rejection ("keys diverge, P05 unverifiable, why_not in N places") is removed by a static helper `StochasticRoll.Roll(streamId, D, e, k)` reading the registry and mask; an architecture test checks Core calls `CounterRng` only from the helper and Flow/Decision. Pros: no extra system/table/pass. Cons: journal and merge order spread across owners, `force` harder. Keep the central system but rewrite the rejection reason honestly: the journal and `force` decide, not the keys.
4. **moderate — `link.failure` weekly, not daily.** At 0.1 %/year, a 7-day chance ≈ 19 raw vs 2.7 raw per day: error ~3 % instead of ~30 % (R1); cost ÷7; daily precision invisible to the player (day within the week via an `extra` roll). `period_days = 7` by default, 1 only with justification (later `war.weather`). Still add `ChanceRaw` in ppb to Kernel — cheap, closes R1.
5. weak — hazard as a function rejected correctly: the column serves why_not, `ExplainHazard`, ablation and UI; one-day lag acceptable. Keep.
6. **moderate — showing risk (question 4).** Own networks: a word scale in advance, the number after the event ("risk was ~4 %/year") in why_not and the journal [Предп.: players overestimate small probabilities; the number after the event builds trust in the RNG]. Foreign networks: only the intelligence estimate, never exact `hazard` (fog of war).
7. **moderate — key `e` = row number of the sparse network table.** Compaction or reuse shifts keys and schedule (p. 7) after save/load or deletion. Require a stable row id (no compaction); add to Проверка p. 9.

Open: owner — atom `network_exists` (extends #340). Orchestrator — "Decision RNG by month" vs ADR-0019 p. 15 (day key): the brief was wrong, ADR-0019 governs.
