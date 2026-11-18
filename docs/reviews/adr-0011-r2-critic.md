# ADR-0011 rev 2 — critic review (2026-11-18, commit bc1b007)

Transcribed by the Orchestrator. No blockers; 7 major, 5 minor.

1. **Major: geo ↔ data binding.** `manifest.json` stores the hash of `provinces.yaml` and `adjacency.yaml`. The client fails loudly on a mismatch; add a test.
2. **Major: mods.** In v1, the `map/*` tables are sealed (override or delete is a load error). Mods change only `scenarios/*/owners.yaml`; a mod that changes the province set ships its own `geo/` bound by item 1.
3. **Major: builder determinism.**
   - Commit the canonical output; CI rebuilds and diffs it.
   - Pin NTS and the SDK (`packages.lock.json`, `global.json`).
   - Compute area with our own explicit formula. The validator warns for values within ε of a threshold.
4. **Major: `as_of` 1945-09-02 vs the controller table.** US forces landed in Korea on 8.09; GBR and ROC entered Indochina mid-September. Owner decides: `as_of` = the scenario start date, or controller = JPN on 1945-09-02.
5. **Major: the 5–150 thousand km² area band** fails microstates and small islands. Use a rule-based exception (admin-0 below the band, island), not a name list.
6. **Major: condominiums and multi-power areas** (Anglo-Egyptian Sudan, New Hebrides, central Vienna, Tangier). Either composite polities, or `status: condominium` with a primary owner as a stated simplification.
7. **Major: island connectivity in the region graph (E9).** The validator requires a connected graph or an explicit `isolated` mark; define the rule for d=∞.
8. Minor: CI check of the transitive package graph of `tools/map` (forbid `NetTopologySuite.IO.ShapeFile`, `ProjNet*`, `*CoordinateSystems*`); pin the SHA-256 of the NE GeoJSON files.
9. Minor: `polities.yaml` is part of `scenario_hash`; migrations map values by `polity_id`, not by index.
10. Minor: an NE version upgrade procedure (diff report, DataHash pair in DECISIONS_LOG, collision check); decide whether `ne_src` is a hashed field.
11. Minor: class-A lines store a map or treaty reference and a Hausdorff distance to OHM where available. Indochina is class A in the ADR and class C in the note; pick one.
12. Minor: memory threshold for the R16 raster (~128 MB with the CPU copy). `fwidth` border detection misses pixels at Z0–Z1; sample 4 neighbours. Prefer always-generate over committing geo.
