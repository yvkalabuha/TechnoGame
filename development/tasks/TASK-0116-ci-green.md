---
id: TASK-0116
title: "CI green on all jobs: Lab tests off Linux, llm_worker pilot contracts"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: test-engineer
category: tooling
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0106]
approved_by: orchestrator
approved_at: 2026-11-19
approval_ref: "project review 2026-11-19: CI red since 2026-11-14 (light)"
review_ref: "reviewer: APPROVE eac3f4f..c984f20; landed b4f8361, a6d9330; CI green on main twice (runs for a6d9330, 29ac1f1)"
created: 2026-11-19
tags: [task, platform, testing, done]
---

# TASK-0116 — CI green on all jobs: Lab tests off Linux, llm_worker pilot contracts

## Goal
CI on `main` has been red since 2026-11-14 (54 failed runs of the last 69) and nobody read it. Make every job green and keep it so.

## Description
Failures in run 44429444772 (`gh run view 44429444772 --log-failed`):
1. `dotnet (windows-latest)` and `dotnet (macos-latest)`: `BenchHarnessTests.Cli_Fref_TopK` expects exit 0, gets 1: there is no cpufreq off Linux.
2. `dotnet (windows-latest)`: `SliceModelTests.Matrix_2x2x2_every_run_has_all_14_tables`: "matrix exit 1".
3. `llm-worker`: `test_pilot_contracts_are_valid` and `test_pilot_b1_is_valid`: "context file changed since contract was written: tests/Directory.Build.props". Failing since 2026-11-14.

Rules: a test that needs Linux `/proc` or cpufreq is skipped off Linux with an explicit reason, not silently returned; a product defect found on Windows is fixed, not skipped.

## Acceptance criteria
- [ ] All CI jobs green on `main` for the landing commit (`gh run list --branch main --limit 1`).
- [ ] `python3 tools/development/verify.py --full` green locally.
