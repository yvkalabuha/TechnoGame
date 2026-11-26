# ADR-0011 rev 2 — DeepSeek third voice (2026-11-18, commit bc1b007)

Record: `tools/llm_worker/runs/gate-ADR-0011-map-representation-bc1b007d-third.json` (sha256 e3a2a0ba…). changes_requested: 3 high, 11 medium, 3 low.

## High
- **ADR-0016 contradiction.** ADR-0016 §1 excludes provinces from full scale; this ADR adds them now. Needs explicit handling: the ownership layer is in K_d, and province systems need a budget ADR.
- **ADR-0018 key contradiction.** `-country-<ADM0_A3>` vs `-country-<polity_id>`. The Orchestrator reading (#310) must be stated in the ADR, together with the polity code rule.
- **Stable `province_id` generation** for merge, cut, subdivide and assign is undefined, yet province_id is the save key.

## Medium
- "Geometry edits do not change DataHash" is too broad: area, centroid, coastal flag and adjacency come from geometry and are hashed.
- The equal-area projection for km² is not specified.
- The validator list is incomplete (as_of, precision, owner/controller rows).
- Mixed-ownership regions vs RR-0011 (H/M, K, N_r are region-level).
- Ownership changes "only in merge" vs ADR-0006/0014 command timing.
- `arcs.bin` lacks topology (arc ids, order, ring assembly).
- New `-country-*` and `-region-*` terms are not in the ADR-0018 L1 completeness check.
- No mod mechanism for binary geometry (ADR-0010).
- RR-0011 uses the `/` separator; record an erratum.
- `merge` mixes geometric merges with owner assignment (IND+PAK+BGD, colonies).
- The source lock lacks licence and date (ADR-0009); the GeoJSON vs shapefile path is unclear.

## Low
- New Session views require a `test_api_version` bump and view tests.
- `d_max` and the storage of the region graph are not specified.
- History verification cites the forbidden COW and CShapes and omits OHM.
