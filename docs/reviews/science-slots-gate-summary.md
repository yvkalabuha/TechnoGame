# Gate SCIENCE_SLOTS.md (TASK-0186), 2026-11-25

Critic (agent ad7281524498a3cb0), advocate (aa7653e5956c3109d), DeepSeek third voice (tools/llm_worker/runs/gate-SCIENCE_SLOTS-39f7249d-third.json). Full texts in the session transcript; key findings:

## High
1. #488 conflict: document says «base speed without penalty» citing #488, but #488 (not superseded) says ≈10 % slower and experience grows in a foreign area; #493 removed the 10 % penalty — status of the experience part unclear. → owner question.
2. Money flow formula r = C/T_eff ignores institute and research_speed multipliers: speed would be free. → one rule r = C·M/T_eff (speed costs money per month, same total price), focus with its own convex surcharge (advocate 3, critic 2).
3. Science surplus → reserve is a loophole (reserve pays projects and licences): idle institutes convert science into general money. → surplus returns to the budget, not the reserve; property test «idle is never better than working» (critic 3).
4. Direction formula Lx «mean over node directions, missing = 0» nearly kills the bonus and depends on data: compare mean / weighted (main direction 0.5) / max over shared in the pilot (advocate 1).
5. Diffusion and theft lower only price, not time: catch-up countries lose their lever → diffusion also shortens time (advocate 4).

## Medium
Queue: first eligible element works, blocked ones stay; a free institute may take an unstarted node from another queue (critic 5, advocate 5). Drop the 6-month replace pause or count Unassign as replace (critic 4, advocate 6). Experience ∝ Δprogress·weight, not months (critic 6, advocate 7). AI: hazard node time limit; strict rule order; focus trigger by air power ratio (#565) (critic 7, advocate 8). Commands: BuildInstitute with 2–4 directions, cancel build, focus off, same-month conflicts order (critic 8, DeepSeek). Invariants S11 Shares≡LEGACY bytewise, S12 pauses, money incl. reserve (critic 9). Focus floor 0.5·T_eff dead or conflicting (critic 10, DeepSeek). militaryValue 0–5 has no rule (DeepSeek). Starting institutes founded «1945» without month vs t0 1945-10-01 (DeepSeek). Check MPI/FV_fleet exists in code (critic 12).

## Low
Staff reason has no model — remove (critic 13); free-slot badge (critic 14); O-088 should fix only the merge, numbers to TASK-0185 (critic 15); queue[0..5] vs active+5; citations #485/#494/#549/FLEET_RETIREMENT; Cmax double count and undefined symbols; Q7 closed by #569; leader bonus deferred by #570 — first-build cap 1.20 without leaders.
