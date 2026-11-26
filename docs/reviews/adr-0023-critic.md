# ADR-0023 — critic review (2026-11-18)

HIGH
1. p.1.3 + p.1.5: the median flag (`T > 1.10·med_point`) selects on the outcome variable; slow processes with clean foreign_cpu are repeated, then excluded; real thermal throttling, slow machine phases and GC variance leave the statistics. Fix: median flag = repeat only; exclusion only with foreign_cpu above the threshold on all attempts; median-only flag keeps the first attempt.
2. p.1.6 + p.1.9: sensitivity only annotates; 20 % fitted to EXP-0007 (3 of 15 noisy). Fix: check the budget (median ≤ 400, p95 ≤ 500) twice — accepted processes and first attempts of all processes; on disagreement INCONCLUSIVE or the stricter row.

MEDIUM
3. p.1.6: sensitivity uses first attempts of all processes, not only of excluded ones.
4. p.1.1 + p.1.5: a #355 violation (other agents, dotnet builds) becomes exclusion, not INVALID. Fix: driver scans top-5/process snapshot for dotnet/MSBuild/VBCSCompiler/agent processes; found = INVALID (б).
5. p.1.3 (5 %): 5 % of 16 logical CPUs = 0.8 thread; bdsecd ≈ 2.3 % passes; HT sibling interference. Fix: calibration run of foreign_cpu before freezing; threshold in absolute CPUs plus share of seconds with foreign load > 1 CPU.
6. p.1.9: EXP-level numbers can be relaxed by a new pre-registration (garden of forking paths). Fix: each re-registration cites previous outcomes; relaxing a gate after a run needs owner approval (DECISIONS_LOG).
7. p.2.3: X confirmed by probing the own implementation, with a path to a larger X = moved goalpost. Fix: fix X = 10 MiB before the probe; exceeding it is a P8 violation and a task, not a revision; call X a regression guard.
8. p.2.4: GC pause statistic left to EXP. Fix: define in ADR, e.g. median of pause shares over all point-A processes including excluded.
9. p.1.2: f_ref once per 5–10 h series hides thermal throttling. Fix: record thermal_throttle core/package counts (delta per window), package temperature, EPP, no_turbo; if Δthrottle > 0 in > 20 % of point-A processes, PASS gets a "possible throttling" mark.

LOW
10. LOH count from sampled AllocLarge events can miss single ~85 000 B arrays; the architecture test on array sizes decides, the counter is reference.
11. MB → MiB relaxes the limit by 4.9 %; state it.
12. EXP-0008 vs ADR: П8 still "≤ 1 MB/month"; П0 allows DEBT-0051 as alternative; П5 refers to a missing OPEN; letter X used for two thresholds. Fix before freezing.
13. Repeats at the end of the queue mix machine cleanliness with time/thermal state; worst case 9–10 h vs the 6 h window of #355. Fix: record time and temperature per attempt; agree a window extension with the owner.

Justified: 2 repeats and the replica-majority rule; ADR-0023 and EXP-0008 numbers agree (≤ 2 of 5, > 22 of 114, > 3 of 15).
