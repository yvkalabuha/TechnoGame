---
id: TASK-0106
title: "Lab bench harness for EXP-0008: foreign-load gate, top-k frequency, repeat cap; alloc JSON provenance"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
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
