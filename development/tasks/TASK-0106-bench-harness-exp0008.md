---
id: TASK-0106
title: "Lab bench harness for EXP-0008: foreign-load gate, top-k frequency, repeat cap; alloc JSON provenance"
status: REVIEW         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: simulation-engineer         # ответственный агент
category: lab
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [EXP-0008, TASK-0102, TASK-0103]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "TASK-0102, EXP-0008 П10 (light; rules may change at the EXP-0008 gate)"
created: 2026-11-18
---

# TASK-0106 — Lab bench harness for EXP-0008: foreign-load gate, top-k frequency, repeat cap; alloc JSON provenance

## Goal
Build the `lab bench` harness that EXP-0008 needs (П10 of `development/experiments/EXP-0008-tick-benchmark-m0-rereg.md`). Close the TASK-0103 step 1 review findings for `--mode alloc`.

## Description
1. EXP-0008 П10:
   - foreign-CPU sampler from `/proc/stat` and `/proc/self/stat`;
   - top-5 foreign processes;
   - per-CPU and top-k frequency;
   - `fref --stat topk --k`;
   - series driver with the repeat gate, repeat cap, exclusion record, wait-for-quiet and the pre-start stop;
   - manifest `lab/manifests/EXP-0008-bench.json`;
   - analysis `lab/analysis/exp0008.py`;
   - selftest fixtures and a smoke run.

   Thresholds are parameters, not constants: the EXP-0008 gate may change them.
2. TASK-0103 review findings:
   - the alloc JSON gets `BenchEnv.Write` provenance/conditions, `WriteParams` and `WriteWorld`;
   - a real re-hash after the probes;
   - the lab-cli.md row lists all 6 probes;
   - a smoke test for `--mode alloc`.

## Acceptance criteria
- [ ] `lab selftest` covers the new modes; a smoke series on a small world runs end to end and produces the EXP-0008 records (loadavg, foreign_cpu, top-5, top-k frequency, repeats/exclusions).
- [ ] Unit tests in Singularity.Lab.Tests and lab/analysis; `dotnet test`, the analysis tests and `lab selftest` pass. Golden hashes are unchanged (harness only).

## Implementation notes (simulation-engineer, 2026-11-18)
- Code: `src/Singularity.Lab/Bench/` — `ProcParse.cs`, `LoadMath.cs` (pure), `LoadMeter.cs` (`procRoot` parameter, `available:false` off Linux), `FreqSampler.cs` (per-CPU values, `TopKMedianIn`, no `throttled`), `GcEventListener.cs` (LOH ticks, `AllocLarge`), `BenchGate.cs` (manifest `gate`, `reference`), `SeriesGate.cs` (pure decisions), `SeriesDriver.cs` (`--mode series`), `HarnessChecks.cs` (selftest), changes in `ProcessBench`, `BenchCommand`, `BenchJson`, `BenchManifest`, `AllocProfile`, `CopyBench` (R2(г) `ratio_cow_over_full_all`).
- ADR-0023 gate addition: `machine` block records run-queue wait (`/proc/self/task/*/schedstat` field 2, per window ÷ window × k), `nonvoluntary_ctxt_switches`, own CPU user+sys per window; `gate.exclusion_rule` = `any-flag` | `independent-evidence` (default).
- Manifests: `lab/manifests/EXP-0008-bench.json` (manifest_hash after rev 2 `2548543eaf6ade0d2c4b2ce530a768c517e63328fa4eb576b0f2df9401904853`, plan identical to EXP-0007; changes again after the п. 1.10 calibration), smoke `EXP-0008-bench-smoke.json`.
- Analysis: `lab/analysis/exp0008.py`, tests `lab/analysis/test_exp0008.py`.
- Format: `docs/simulation/lab-cli.md` «lab bench», «Блок machine», «lab bench --mode series». Disagreements H1–H5: EXP-0008 «Разногласия (TASK-0106)».
- Smoke: `lab bench --mode series --manifest lab/manifests/EXP-0008-bench-smoke.json --out lab/results/TASK-0106/series-smoke --inject-flag 3:3` — exit 0 in ~2 min; process 3 (B) excluded after 3 injected attempts, median-flag repeats in A resolved clean, hc variant and r1 excluded by real foreign load (machine busy with parallel work), profile ran with `dotnet-trace`.
- Split of `alloc` into tick path / snapshot path is a model (month − snapshots × probe bytes − listener bytes), not a direct count: Session builds the snapshot inside `Step`. A direct split needs a Session hook (outside the zone).

## Tests to add (for test-engineer, `tests/Singularity.Lab.Tests`)
`SeriesGate`, `BenchGate`, `ProcParse`, `LoadMath`, `LoadMeter`, `FreqSampler.MedianOf/CpuinfoMhz` are public; `SeriesDriver` and `AllocProfile` are internal — drive them through `Program.Main` as `BenchTests.Cli` does.
1. `Gate_FrequencyBelowRef_NoForeignLoad_NoRepeatNotInconclusive`: 15 A processes, attempts with `ForeignCpu` 0.01, medians equal, (frequency is not an input of `GateAttempt`) → every `Decide` is `Accepted` at attempt 0; `Verdict.Inconclusive == false`.
2. `Gate_ThreeFlaggedAttempts_Excluded_NoFourth`: attempts foreign 0.2/0.3/0.1 → after 2 `NeedsRepeat`, after 3 `Excluded` (both rules); passing 4 attempts throws `ArgumentException`.
3. `Verdict_A_FourOf15Excluded_Inconclusive_ThreeNot` (one excluded per seed for 3).
4. `Verdict_SeedTwoOfFiveAccepted_Inconclusive` (3 excluded, all seed 2) and `OutcomeSeeds[2] == (2, 5)`.
5. `Verdict_Series23Of114_Inconclusive_22Not`.
6. `MedPoint_FixedAcrossRepeats`: `MedPoints` of first attempts {100, 200, 900} = 200; `Decide([900, 150], 200)` → Accepted attempt 1.
7. `IndependentEvidence_MedianOnly_AcceptedNoEvidence` and `AnyFlag_MedianOnly_Excluded`; `IndependentEvidence_MedianPlusRunDelayEveryAttempt_Excluded`; `RunDelayAlone_NoRepeat`; `MedianBelowGateMin_NoFlag` (med_point 20 ms, T 40 ms).
8. `Manifest_Exp0008_GateEqualsP5` (all numbers of П5 and `new BenchGate()`), `Manifest_Exp0008_PlanEqualsExp0007`, `Manifest_UnknownGateKey_Rejected`, `Manifest_BadExclusionRule_Rejected`, `Manifest_ReferenceForbiddenArg_Rejected` (`"out"` in `args`), `Manifest_Exp0007_HashUnchanged` (= `6112abb4…21ac`).
9. `ProcParse_*` and `LoadMath_*`: the fixtures of `HarnessChecks` (cpu line, comm with spaces and ')', 4-field cpu line, loadavg, schedstat, status, foreign 100/1600 = 0.0625, top-k [1000,4000,3000,800] k2 = 3500, top-foreign with pid reuse → "2:60,5:60,4:40", run delay 2000 ns / (1000 × 2) = 1.0).
10. `LoadMeter_FixtureProc_MachineBlock` (temp /proc tree as in `HarnessChecks.MeterFixtures`: foreign = 100/450, `run_delay_ns` 3000, top comm "ide" with 50 ticks) and `LoadMeter_NoProc_AvailableFalse_NoThrow`.
11. `FreqSampler_TopK_PowersavePicture`: 14 CPUs 800 MHz + 2 at 4500 → top-2 median 4500, all-CPU mean 1262.5.
12. `Cli_Alloc_Smoke`: `bench --mode alloc --actors 6 --nodes 180 --months 3 --warmup 1 --allow-debug true --out X` → code 0; JSON has `provenance`, `conditions`, `params`, `world`, `machine`, 9 probes, `probes_kept_hash == true`, `state_hash_before_probes == state_hash_after_probes`, `p8_split.snapshot_without_nodes_bytes > 0` and `< snapshot_no_step_fresh_bytes`, every month has `snapshots_no_step + snapshots_stepped ≥ 1`.
13. `Cli_Process_Smoke_FrequencyAndMachine`: adhoc `process --actors 6 --nodes 180 --threads 2 --warmup 1 --measure 2 --fref 3000` → `frequency.k == 2`, no `throttled` key, `possible_thermal_throttle` present, `machine.windows.Length == 2`, `runs[*].allocated_bytes_net` present.
14. `Cli_Series_Smoke`: `--mode series` on a temp manifest (smoke manifest with blocks of 2–3 processes, `fref_seconds 2`, `pre_series_s 1`, `quiet_* 1/1/0`, no reference) with `--inject-flag K:3` → exit 0, `series.json` `repeat_plan` round 1 and 2 contain K, process K `Excluded` with 3 attempts; second run into the same directory → exit 2 (no manual repeat/resume). With `pre_series_foreign_max 0` the series stops → exit 3 (skip off Linux). Two injected A processes out of 4 → exit 4.
15. `Cli_Fref_TopK`: `fref --seconds 2 --tail 1 --threads 1 --k 1` → `stat == "topk"`, `k == 1`, `machine` present.
16. `R2d_WritesPairwiseRatios`: `r2 --variants d --actors 6 --nodes 180 --month-warmup 1 --months 3 --threads-list 1` → `ratio_cow_over_full_all.Length == 3`.

## ADR-0023 rev 2 update (simulation-engineer, 2026-11-18)
Done:
- Signals (п. 1.3): (н1) `ForeignCpus` (mean, absolute CPUs) and `ForeignOverShare` (share of seconds > `foreign_second_cpus`); (н2) run delay; (м) median. Any signal is a flag and triggers a repeat (п. 1.4); exclusion only with (н1) or (н2) on every attempt, otherwise the first attempt without evidence (п. 1.5).
- Р1/Р2 (п. 1.6) in `exp0008.py`: Р1 accepted, Р2 first attempts of all processes; different rows → INCONCLUSIVE; noise for both.
- #355 (п. 1.1): `gate.forbidden_processes` against every attempt's top-5 and the pre/post windows → INVALID (б), exit 5.
- Thermal (п. 1.2): core/package throttle deltas, package temperature start/end, EPP, `no_turbo` per window and per attempt; start time per attempt; throttling mark on PASS in the analysis.
- Wait for quiet before the series with `pre_series_deadline_s` (default 1800) → exit 3 «not started» with top-5 in the log (п. 1.8).
- #355 window `window_s` (default 6 h): repeats after it are virtual (м)-flagged attempts (п. 1.9).
- Calibration (п. 1.10): `--mode series --calibration true` + `exp0008.py --calibrate`.
- `alloc` split probe and «Snapshot without nodes» (from step 1); X default 8 MiB (`--snapshot-x-mib`).
- GC pause share per process (`gc` block in the process JSON); the analysis takes the median over all processes of the point (п. 2.4).
Not done (left for a follow-up):
- Rust-port protocol (п. 1.12: replace an excluded process by the next seed, ≤ 3 per version, ≥ 10 accepted). Not used in M0 (ADR-0016 §4, #272).
Tests to add, changes against the list above: in 1, 2, 7 the foreign values are absolute CPUs (`GateAttempt(n, T, foreignCpus, overShare, runDelay)`); run delay alone now triggers a repeat (`RunDelayAlone_Repeat`, not `NoRepeat`); add `WindowEnd_VirtualRepeat_AcceptedWindowEnd` ([н1, н2, virtual] → Accepted attempt 0, `window-end`), `Mixed_FirstWithoutEvidenceAccepted` ([н1, м, н2] → attempt 1), `Forbidden355_PrefixCaseInsensitive_SkipsDriverPid` (`SeriesDriver.ForbiddenIn` is internal: test through a series CLI run with `forbidden_processes: ["dotnet"]` and an active `dotnet` neighbour, or make it public), `Cli_Series_Calibration_NoRepeats`, `Cli_Series_DeadlineNotStarted_Exit3` (`pre_series_foreign_cpus_max 0`, `pre_series_deadline_s 1`), and in analysis `test_r1_r2_differ_gives_inconclusive` (present in `test_exp0008.py`).

Point A `alloc` after the TASK-0103 merge (6cd45ec, t1, 5 months, bytes only, busy machine) [Факт: lab/results/TASK-0106/alloc-A.json]: month 9.9 MiB; fresh snapshot 6.86 MiB (≤ X = 8 MiB), «Snapshot without nodes» 0.73 MiB, nodes 6.13 MiB; snapshot path ≈ 7.59 MiB/month; tick path ≈ 2.26 MiB/month by the model — above the 1 MiB of ADR-0023 п. 2.1; LOH ticks 0, AllocLarge 0; gen2 1 in 3 measured months; GC pause share 0.36 %. The probe after the merge replaced the reflection on `ViewBuilder.Nodes` (blocks are now cached, so a direct call returned 0 B).
