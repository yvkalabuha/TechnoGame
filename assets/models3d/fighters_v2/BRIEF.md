# Brief: six generic jet fighters G1–G6 as slot kits (owner 2026-11-25, decisions #544, #557)

You build in the live Blender through the Blender MCP. Only execute_blender_code and look. No generate_3d, import_asset,
search_assets, no shell, no files. At most 120 execute_blender_code calls in total. Do not touch existing scenes or objects.

## Why
In the game a fighter design is assembled from slots: engine (eng_g1..eng_g6), airframe, afterburner
(mods/core/product_classes.yaml). Each generation is a kit of parts that can be mixed later. The pictures are rendered
later as flat ink drawings seen mainly FROM THE TOP, so the TOP-VIEW SILHOUETTE (planform) of each generation must be
clearly different. Small details do not matter; outline shapes matter most. Keep it generic: no replica of any real
aircraft, no markings, no text, no numbers, no camouflage.

## Scene layout
- One new scene per aircraft, named exactly G1, G2, G3, G4, G5, G6. Each aircraft sits at the world origin.
- Units metres. Nose points +X, up is +Z, the lowest point of the aircraft at z = 0 (no landing gear, gear up).
  Symmetric about the XZ plane (y = 0).
- Exactly two materials, shared by all scenes: "M_body" (all skin, plain light grey) and "M_dark"
  (canopy glass, intake openings, nozzle exits). Nothing else.
- Smooth shading is allowed; keep each aircraft under 20 000 triangles. Meshes must be closed and with consistent
  outward normals (no open holes in the top view).

## Parts (per scene Gn; one collection each, names exact)
- KIT_Gn_fuselage — fuselage, nose/radome, cockpit and canopy, spine, intakes (intakes belong to the airframe).
- KIT_Gn_wing — both wings with any root extensions (LERX), fairings, canards (G5).
- KIT_Gn_tail — vertical fin(s) and horizontal tail surfaces.
- KIT_Gn_engine — the engine(s): a simple whole engine body inside the fuselage (cylinder with a fan/compressor face)
  plus the plain dry nozzle that shows at the rear.
- KIT_Gn_afterburner — afterburner section and its nozzle petals that extend the engine at the rear. G1 and G2 have
  no afterburner (slot ab_none): create the collection with only its root empty.
Every part's objects are parented to one root Empty named like the collection plus "_root" (e.g. KIT_G4_wing_root).
In KIT_Gn_fuselage add Empties ATTACH_wing, ATTACH_tail, ATTACH_engine (one, even for two engines); in KIT_Gn_engine add
ATTACH_afterburner. Each part root sits exactly on its ATTACH empty (same location, rotation 0), so the aircraft is
assembled. Leave it assembled.

## The six aircraft (planform first; numbers are guides, ±10 %)
G1 (late 1940s): length 10.5, span 11.0. STRAIGHT wing (leading-edge sweep ≤ 5°), taper 0.5, rounded tips, low on the
  fuselage at mid length. CLOSED, ROUNDED, bullet-shaped nose (no open front intake, no flat cut). Small intakes at the
  wing roots. Cigar fuselage, max width 1.3. Straight tailplane (span 4.0), one fin. One engine, round nozzle.
G2 (1950s): length 11.5, span 9.5. Wing SWEPT 40°, taper 0.45. NOSE INTAKE that is CLOSED by a pointed conical centre
  body (a shock cone): seen from the top the nose ends in the cone tip, never a flat cut; the dark intake ring shows
  only as a thin ring around the cone. Swept tailplane (span 3.8, 40°) and swept fin. One engine, round nozzle.
G3 (1960s): length 15.0, span 7.5. SLIM pure DELTA wing, leading-edge sweep 60°, tips pointed, root chord about 7.5
  starting at x≈5 from the tail end. No horizontal tail. Pointed needle nose with a pitot probe. Half-cone side intakes.
  Area-ruled (waisted) fuselage. One tall swept fin. One engine with afterburner.
G4 (1970s–80s): length 18.5, span 12.5. TRAPEZOID wing: leading edge 40°, trailing edge straight across (0°),
  clipped tips (tip chord 1.4), mid-length. Narrow LERX strakes along the fuselage. SEPARATE all-moving tailplanes
  behind the wing with a visible gap (span 8.5). TWIN vertical fins, upright. Rectangular side intakes. Two engines,
  two ROUND nozzles side by side. Rounded radome.
G5 (1990s): length 15.0, span 10.0. Tailless DELTA wing (leading edge 52°, trailing edge straight) set aft, plus
  CANARDS (span 5.5) just behind the cockpit, clearly separate from the wing. One fin. One engine, round nozzle.
  Smooth blended fuselage, ventral or chin intake.
G6 (2030s): length 19.0, span 13.0. DIAMOND / LAMBDA planform, not a trapezoid: leading edge 45°, the wing trailing
  edge sweeps FORWARD (about -40°) so the outer wing narrows to small tips well forward of the tail; inboard the
  trailing edge ends in a CHEVRON/sawtooth (a 'W' notch) blended into a wide flat aft body. NO horizontal tail; only two
  small outward-CANTED fins (about 50° from vertical) or none. FACETED chined nose (flat planes, sharp chine edge all
  around, diamond cross-section), one-piece canopy. Caret/diverterless intakes under the chines. Two engines buried,
  ending in ONE wide FLAT rectangular nozzle (or two flat ones) whose trailing edge is cut at an angle (sawtooth edge),
  shielded from below by the aft deck.
  G4 and G6 MUST differ in the top view by at least 20 % of their area: G4 = trapezoid wing + separate tailplanes +
  round nozzles; G6 = one diamond/chevron lifting body, no tailplanes, flat angled nozzle.

## Check yourself with look (top view, orthographic, from +Z)
For each scene: look at the top view and the side view. Verify: G1/G2 nose closed and rounded/pointed (no flat cut);
G4 vs G6 planforms obviously different; no holes; parts on their ATTACH empties.

## When done
Print: number of execute_blender_code calls; per scene: collections with object counts and triangle counts, ATTACH
empties with locations, overall length, span and height.
