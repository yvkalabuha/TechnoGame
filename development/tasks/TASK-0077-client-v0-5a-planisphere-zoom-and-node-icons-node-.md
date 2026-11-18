---
id: TASK-0077
title: "Client v0.5a: planisphere zoom and node icons, node card with schematic and effects, map click fix, starry sky"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: tooling
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0071, ADR-0018]
approved_by: owner
approved_at: 2026-11-18
approval_ref: "DECISIONS_LOG #318"
created: 2026-11-18
---


## Goal
Owner feedback #318 for the client prototype (src/Singularity.Client.Spike).

## Scope
1. **Planisphere zoom and pan.**
   - Wheel zoom under the cursor, drag to pan, a reset button.
   - Level of detail per docs/ui/prototypes/tech-tree-scale.html: aggregated at overview, individual nodes when zoomed in.
   - When zoomed in, nodes become icons per node type and discipline, or at least colour-coded by discipline (palette C tokens from docs/ui/UI_DESIGN_SYSTEM.md §17).
2. **Node card on click:**
   - a blueprint-style schematic picture: SVGs from docs/ui/assets/node-schematics/, made by ui-art-director in parallel. Until they exist, use a placeholder frame with the node type label;
   - **what it affects** (layer and system);
   - **which parameter it raises**, from the Session/Core data if available; otherwise mark it «макет» and list the missing API;
   - **what it unlocks next** (its dependants);
   - each block in a distinct colour per palette C.
3. **Map bug:** clicking a country centres it and changes zoom incorrectly. Find the cause (camera target, latitude/longitude conversion, zoom clamp) and fix it. Clicking should centre smoothly and keep the zoom, or use a sensible per-country zoom.
4. **Background:** a dark starry sky around the globe. A procedural star field, subtle and deterministic, that does not compete with the globe; the paper tint stays only on panels.

## Rules
- Do not change Core, Session or Kernel. List missing APIs in «Результат».
- Build check: run `dotnet build src/Singularity.Client.Spike` and the Godot `--build-solutions` step via `./run-globe-spike.sh --shots`.
- Screenshots go to docs/ui/prototypes/client-v05a/.

## Acceptance
- Zoom and pan are smooth; the node card shows all four blocks; the country click works for at least the USA, the USSR and France, before and after rotation; the sky is visible.
- The main solution's full `dotnet test` is green.
