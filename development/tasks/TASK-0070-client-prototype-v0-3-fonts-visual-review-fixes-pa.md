---
id: TASK-0070
title: "Client prototype v0.3: fonts, visual review fixes, pause pending, robustness"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: tooling
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0069]
approved_by: owner
approved_at: 2026-11-17
approval_ref: "DECISIONS_LOG #292, #294"
created: 2026-11-17
---


## Goal
Bring the client prototype (src/Singularity.Client.Spike, v0.2 from TASK-0069) to the design system. Fix the code-review findings. Core, Session and Kernel stay unchanged.

## Inputs
- Visual review: docs/ui/prototypes/client-v02-review.md (top-10 fixes; 1, 2, 4, 5 and 6 are blocking).
- Font page: docs/ui/prototypes/client-fonts.html.
- Design documents: docs/ui/UI_DESIGN_SYSTEM.md, UI_ART_DIRECTION.md, GLOBE_DIRECTION.md, TECH_TREE_SCREEN.md, prototypes/tech-tree.html.

## Scope
1. **Fonts (#294).**
   - Embed the TTF files of Golos Text, PT Mono, Jura and Literata, with their OFL.txt, in the client. Source: the official google/fonts GitHub repository, OFL directories.
   - Add LICENSES.md entries.
   - Apply the roles from the font page.
   - Minimum interface size 14 px.
2. **Visual review fixes 1–10, in priority order.**
   - Russian node names, with ids only in tooltips; debug text and API names behind F3.
   - Segment controls with no fill.
   - Colour rules.
   - Distinct country tints and Russian map labels.
   - Planisphere per the sketch:
     - channel edges;
     - the double circle;
     - dashed frontier nodes;
     - a continuous route with a callout;
     - the clock with its hand.
   - Labels without overlap.
   - The panel as an inset sized by content.
   - Tree B: a legend.
   - Controls separated from signals; dates in sync.
3. **Code review fixes:**
   - A command on pause is shown as «ожидает» and applies at the first tick after unpause, with no extra tick. Keep the slider values while the command is pending.
   - Catch errors per job and show a sim-thread failure permanently in the signal field.
   - `_ExitTree` disposes SimHost.
   - Rebuild panel and tree controls only when their data changed.
   - Map actors by `.Actor`.
   - A `LastRejected` flag instead of matching on text.
   - Use `ReferenceEquals` for views.
   - Fix the TechTree indentation and record the stubs.
   - README: .NET 10 runtime next to Godot.
4. **Screenshots** into docs/ui/prototypes/client-v03/, including an active-signal state, for the second visual review.

## Acceptance
- `./run-globe-spike.sh` and `--shots` work.
- The main solution's full `dotnet test` is green.
- «Результат» maps each review item to done or not done.
