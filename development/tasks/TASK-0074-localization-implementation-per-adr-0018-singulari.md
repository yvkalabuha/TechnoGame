---
id: TASK-0074
title: "Localization implementation per ADR-0018 (Singularity.Localization, ftl, anchor move)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0018, TASK-0072]
approved_by: owner
approved_at: 2026-11-18
approval_ref: "DECISIONS_LOG #297, #300, #304"
created: 2026-11-18
---


## Goal
Implement ADR-0018 (ACCEPTED; S1 passed, erratum #304) in the main solution. The client migration from CSV comes in a later task, so do not touch src/Singularity.Client.Spike.

## Scope (ADR-0018 §7, step 1)
- A new assembly src/Singularity.Localization: a facade over Linguini.Bundle 0.11.0 with no Godot dependency. NUMBER/DATE per the S1 erratum: a FluentNumber subclass, no FormatterFunc. Includes the DataRef case/gender functions, the fallback chain and the missing-key marker.
- Loading `mods/<mod>/locale/<lang>/*.ftl`:
  - the id mapping from data ids to Fluent ids per the ADR;
  - the mod override and conflict rules;
  - the stale-translation sidecar (`_sources.tsv`).
- tests/Singularity.Localization.Tests, all from ADR §7:
  - L1–L6;
  - id mapping in both directions, including dotted ids;
  - mod conflicts;
  - CLDR RU/EN plurals including «1,0»;
  - NUMBER and DATE;
  - staleness;
  - glyph coverage against the client fonts (#294);
  - the `xx` pseudo-locale;
  - "deleting locale/ changes neither DataHash nor StateHash".
- mods/core/locale/en and ru: .ftl for the 61 nodes (name, short, anchor), actors and countries for 1945 (SUN = СССР/USSR), enums, months. Russian names and anchors exist in the data and CSVs today. English: use the client's data.csv where present; otherwise translate and keep a list for research-scientist review.
- The anchor move (#297):
  - remove `anchor.event` from Core data and NodeTable;
  - update Core, Lab (SynthWorld.cs:380, SynthChecks.cs:72) and tests (CoreNodesTests.cs:55 moves to Localization.Tests);
  - DataHash changes once.
  - Run the EXP-0002-det matrix: `golden_set_hash` must be unchanged, which proves the dynamics are unchanged.
  - Report the old and new DataHash; the Orchestrator records them.
- Add the Unicode V3 notice to the licence files.
- A ModLoader rule that skips locale/ explicitly.

## Acceptance
- Full `dotnet test` green; Lab selftest green.
- The EXP-0002-det golden_set_hash is unchanged.
- No text in Core state.
