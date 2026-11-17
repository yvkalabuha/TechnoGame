# ADR-0017 — DeepSeek third voice

Record: `tools/llm_worker/runs/gate-ADR-0017-statehash-schedule-and-table-copy-in-daily-mode-3340b5f1-third.json` (sha256 8211f4ec…). changes_requested: 1 high, 3 medium, 3 low.

High:
- The lazy hash conflicts with ADR-0014 item 4: `Step` returns `StateHash` synchronously. So either `Step` computes the hash on every non-empty day, or its contract must change.

Medium:
- The EXP-0007 `K_d` measurement with the probe (empty `Writes`, no daily hash) is only a lower bound. It does not test the real mechanism.
- Cache invalidation is not specified: the dirty flag must be set on every non-empty tick.
- `string?` StateHash allows vacuous `null == null` comparisons in tests.

Low:
- The "all columns" default `Writes` keeps the full copy and disables the guard during migration.
- `supersedes:` is empty although ADR-0016 §2 is partly replaced.
- The definition of the "monthly step" under #228 is ambiguous.
