---
id: TASK-0067
title: "Godot 4 .NET 3D globe spike: regions, relief, CC0 objects, FPS and memory"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: tooling
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0001, ADR-0009, ADR-0016]
approved_by: orchestrator
approved_at: 2026-11-17
approval_ref: "DECISIONS_LOG #284, #285 (owner asked for the spike)"
created: 2026-11-17
---


## Goal
A throwaway technical spike: show what a 3D-globe client costs on the owner's machine (i7-11800H, 31 GB). It is not production code and is not connected to the simulation (M2).

## Scope
1. Install Godot 4 .NET outside the repository, under `~/.local/opt/godot/`. Use the latest stable 4.x mono build for Linux from the official godotengine GitHub releases. Record the version and the sha256. Never commit binaries.
2. Create the Godot C# project in `src/Singularity.Client.Spike/`. Keep it out of the main solution, or make sure `dotnet build`/`dotnet test` of the solution are unaffected.
3. A globe:
   - sphere with an Earth texture or relief from a public-domain or CC0 source (for example NASA Blue Marble or Natural Earth raster; check the licence against ADR-0009);
   - region borders from Natural Earth admin-0 (and admin-1 if cheap), drawn as lines on the sphere;
   - camera orbit and zoom;
   - region pick on click, highlighting the region.
4. 3–5 CC0 3D objects placed on the globe (Kenney or Quaternius: a factory, an airfield, a plane). One simple "under construction → in service" animation, as in #263.
5. Measure FPS at idle, while rotating and zoomed in; VRAM and RAM; startup time; draw calls. Record the GPU model.
6. Data: download scripts or a README with the URLs. Put large data files in .gitignore, or commit only small, licence-checked files with a LICENSES note.

## Acceptance
- The project opens and runs headless-buildable (`godot --headless --build-solutions` or the equivalent) on this machine.
- A short report in `docs/ui/prototypes/globe-spike-report.md` with the numbers, screenshots (if a display is available; otherwise say so) and problems.
- The main solution's full `dotnet test` is still green.
