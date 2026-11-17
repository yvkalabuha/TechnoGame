---
id: TASK-0069
title: "Client prototype v0.2: design controls, Session wiring, tech tree A/B"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: tooling
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0067, ADR-0014]
approved_by: owner
approved_at: 2026-11-17
approval_ref: "DECISIONS_LOG #292"
created: 2026-11-17
---


## Goal
Continue the Godot globe spike (`src/Singularity.Client.Spike/`, TASK-0067) into a playable client mock-up, following the design documents. Wherever the Session API exists, wire it for real; elsewhere use visibly marked mock data. Owner decision #292.

## Design inputs
- docs/ui/UI_ART_DIRECTION.md, UI_DESIGN_SYSTEM.md (controls: segments, signal field, panels, typography), GLOBE_DIRECTION.md (approved, #288).
- docs/ui/TECH_TREE_SCREEN.md and prototypes/tech-tree.html + tech-tree-scale.html: A «Планисфера» main view, B «Ведомость цели» second view (#286).
- Tempo #269: pause; slow 1 day/s, medium ~1 week/s, max 1 month/s.

## Scope, in order
1. **Session wiring.** Reference Singularity.Session/Core/Kernel from the Godot project. Godot 4.7.2 targets net8.0 and the core targets net10.0: find a working setup (Godot project on net10.0 with rollForward, or another way). Record it. If impossible, stop and report.
   - Load the slice scenario (`SliceSession.Create` with mods/core; see how Lab or tests build `GameData` and `SliceRunConfig`).
   - Run the simulation on a background thread per ADR-0014 item 4. The client reads `View` only.
2. **Top signal field** per the design system: date (`Date`), speed segment (pause / 3 speeds), power selector (the 6 slice actors), signal slots.
3. **Globe ↔ simulation.** Map the 6 slice actors to their countries on the globe. Colour fills per the direction. Click on a slice country opens the actor panel.
4. **Actor panel (inset).** Real data from `ActorView`:
   - budget shares with sliders → `Submit(SetBudgetShares)`, showing the verdict;
   - designs and lines (`DesignView`);
   - licences.
   - «Почему не» via `WhyNot`.
5. **Tech tree screen** as a full-screen inset:
   - A «Планисфера» from real `NodeView` (61 nodes) with states known / frontier / unavailable, the goal route, and the why_not root;
   - B «Ведомость цели» as the second view (switch).
   - Priorities (`SetEffortFocus` if it maps; otherwise mock with a label).
6. Everything without an API is mock, labelled «макет» in the UI.

## Rules
- The prototype stays outside Singularity.sln and must not change Core, Session or Kernel. If an API is missing, list it in «Результат» as a proposal. Do not add it.
- Licences per ADR-0009 (CC BY allowed with the registry, #288).
- Keep `./run-globe-spike.sh` working.

## Acceptance
- Runs on the owner's machine.
- Speed controls drive real ticks; the date advances; a budget slider change is accepted by Session and visible in the next view.
- Screenshots of: globe with the panel, tech tree A, tech tree B, signal field.
- The main solution's full `dotnet test` is green.
- «Результат» lists real vs mock per control and the missing APIs.
