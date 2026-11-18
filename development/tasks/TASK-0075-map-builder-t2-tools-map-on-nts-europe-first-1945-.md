---
id: TASK-0075
title: "Map builder T2: tools/map on NTS, Europe first, 1945 cuts, coverage simplification check"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: tooling
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0011, ADR-0009]
approved_by: owner
approved_at: 2026-11-18
approval_ref: "DECISIONS_LOG #315 (ADR-0011 ACCEPTED)"
created: 2026-11-18
---


## Goal
The first map builder step from ADR-0011 (ACCEPTED, revision 3): `tools/map/` §2. Run on Europe first. The main risk is the NTS coverage operations for shared-boundary simplification; verify them first and report early if they are missing.

## Scope
1. Inputs:
   - pin the Natural Earth 10m admin-1 and admin-0 map_units GeoJSON by URL and SHA-256 in `sources.lock.yaml`, with licence and date (ADR-0009);
   - download into a cache; do not commit raw NE files.
2. A C# console tool under tools/map on NetTopologySuite (BSD-3), with the LGPL modules excluded:
   - CI check of the package graph;
   - pinned SDK and NuGet.
3. Spec files per ADR §2 for Europe: ids with `bootstrap`, merges, cuts (Oder–Neisse, the German and Austrian zones as of 1945-10-01, Karelia, the Baltics in the USSR), subdivide by Voronoi.
4. Outputs per ADR §2.5:
   - provinces, regions, adjacency, straits, the owners/controllers table for Europe as of 1945-10-01, polities (#311–#313);
   - generated geo/ with a manifest binding hash (§4).
5. The validator per ADR «Проверка» for Europe; a determinism check (two runs give byte-identical output).
6. A debug render (PNG) of Europe with owner fills and controller hatching, into docs/ui/prototypes/map-1945/ for review.

## Out of scope
The rest of the world; the simulation ownership layer; client integration (later tasks).

## Acceptance
- Byte-identical output on two runs.
- The validator passes for Europe.
- The coverage simplification works or the gap is reported.
- The main solution's `dotnet test` is unaffected and green.
