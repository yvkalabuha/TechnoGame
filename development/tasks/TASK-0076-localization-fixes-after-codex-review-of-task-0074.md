---
id: TASK-0076
title: "Localization fixes after Codex review of TASK-0074"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0074, ADR-0018]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "light: Codex review of TASK-0074"
created: 2026-11-18
---


## Goal
Fix the Codex second-review findings on TASK-0074 (record tools/llm_worker/runs/review-TASK-0074-6514af88-codex.json; read it for detail).

1. **HIGH** Numbers.cs:151 — number and date formatting bypasses the English fallback for incomplete locales. NUMBER/DATE must use the same lang → en fallback chain as messages, for separators, month names and patterns.
2. **MEDIUM** LocaleCatalog.cs:82 — third-party locale validation and stale-translation warnings never run at runtime. Run the per-mod validation (L-checks applicable to mods) and staleness warnings at load, as warnings for non-core mods per ADR-0018.
3. **MEDIUM** AstUtil.cs:50 — L3 misses variables and functions used only in selectors. Walk selector expressions as well.
4. **MEDIUM** tests/.../FontCmap.cs:50 — the glyph coverage test can report missing glyphs as present. Fix the cmap parsing (format 4/12, segment end handling) and add a negative test with a known-missing glyph.

## Acceptance
- A test for each finding: it fails before the fix and passes after.
- The full `dotnet test` is green.
