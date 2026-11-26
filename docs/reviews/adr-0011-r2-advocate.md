---
tags: [review, map, done]
---
# ADR-0011 rev 2 — advocate review (2026-11-18, commit bc1b007)

Transcribed by the Orchestrator. No rejected alternative wins outright. Refinements:

1. **(moderate) Voronoi for `subdivide`.** Use deterministic equal-area seeds clipped to the object; keep bisection as the fallback. This avoids slicing Siberia, the Sahara and northern Canada into strips.
2. **(strong) OpenHistoricalMap (CC0 objects only) as a point source for cuts.**
   - `source: ohm:<type>/<id>@v<version>`, with SHA-256 in `sources.lock.yaml`;
   - the validator accepts only CC0 and CC BY;
   - OHM is not the base layer.
3. **(weak) GDAL.** Native binaries make reproducibility depend on their version. mapshaper (MPL-2.0, Node) stays a fallback for shared arcs.
   - [Assumption] NTS coverage operations (`CoverageValidator`, `CoverageSimplifier`) must be verified in the pinned version in T2; this is the main risk of T2.
4. **(moderate) One rule for `geo/`:**
   - either always generate it at build time, with only `manifest.json` (SHA-256) in git;
   - or keep options (a) and (c) and drop (b).
5. **(weak) Vector tiles: reject.** They are designed for flat Mercator and streaming, and tiling breaks the shared-arc `owner(L)≠owner(R)` filter. Add a line to the alternatives.
6. **(weak) 1000 provinces: reject** (#195, #308). Refinement: uneven density (denser in Europe and East Asia, sparser in deserts); 2500–3500 is a build outcome, not a target.
7. **(strong) owner/controller refinements:**
   - (a) derive `occupied` from `controller≠owner`; the validator checks consistency;
   - (b) a condominium rule: a separate polity, or the main sovereign plus `status`; decide in the ADR;
   - (c) the default `owner` for disputed sovereignty until the owner decides.
8. **(strong) Treaty parallels as parameters.** The 38th, the 16th and 50°N as `cut: {parallel: …, within: […]}`. Write a digitising policy for the other class-A lines. [Assumption] The 1940s US Army Map Service maps are US federal works, hence PD; research-scientist to verify.

Open:
- The owner decides the condominium rule and the default owner under disputed sovereignty (7b, 7c).
- research-scientist checks AMS map licences and OHM object licences.
