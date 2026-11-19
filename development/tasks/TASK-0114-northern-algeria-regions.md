---
id: TASK-0114
title: "Northern Algeria regions for the 1945 map (DEBT-0058)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M1
agent: research-scientist
category: data
priority: low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0109, ADR-0022]
approved_by: orchestrator
approved_at: 2026-11-19
approval_ref: "DEBT-0058 (light)"
created: 2026-11-19
---

# TASK-0114 — Northern Algeria regions for the 1945 map (DEBT-0058)

## Goal
Close DEBT-0058. The 1945 map has only the two Southern Territories regions of Algeria; the northern départements (Alger, Oran, Constantine), legally part of France, have no regions.

## Description
1. research-scientist: 1945 borders of the three départements and the Territoires du Sud; whether the pinned map sources (tools/map/sources.lock.yaml, ADR-0009 licences) allow the split; a proposed region spec.
2. developer: tools/map spec and mods owners (FRA, core); rebuild; MapBuilder tests and determinism.

## Acceptance criteria
- [ ] Northern Algeria has its own regions with status core (FRA); FRA core count recorded; region total within ADR-0022 limits or the warning explained.
