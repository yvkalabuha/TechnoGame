---
tags: [spec, visual, active]
---
# Brief: modular parts kit for the G4 fighter (owner request 2026-11-25)

Purpose: in the game an aircraft design is assembled from slots — engine (eng_g4), airframe (af_mach2), afterburner (ab_on) (mods/core/product_classes.yaml). The 3D model must be assembled from parts the same way, so later kits (G1–G6) can be mixed: e.g. the G4 engine on a G3 airframe.

Start from your own G4 model in scene "Gemini_G4" (objects "G_*"). Do NOT modify scene Gemini_G4 or Claude_G4 or any existing object. Work in a NEW scene "Gemini_G4_kit". Copy (duplicate with data copies) what you need.

Parts (one collection each, names exactly):
- KIT_G4_fuselage — fuselage, radome, cockpit, canopy, spine, intakes (intakes belong to the airframe).
- KIT_G4_wing — both wings, LERX, fairings, wingtip rails.
- KIT_G4_tail — both vertical tails and both stabilators.
- KIT_G4_engine — the two engine aft bodies and plain nozzles (no afterburner petals/glow).
- KIT_G4_afterburner — afterburner nozzle petals and glow parts that replace/extend the plain nozzles.
Each part's objects are parented to one root Empty named like the collection with suffix "_root" (e.g. KIT_G4_wing_root).

Attach points: in KIT_G4_fuselage add Empties named ATTACH_wing, ATTACH_tail, ATTACH_engine_L, ATTACH_engine_R at the places where those parts mount. Each other part's root Empty origin is its mounting point, so placing the part root at the matching ATTACH empty (same location and rotation) reassembles the aircraft exactly as the original Gemini_G4 model (engine root goes to ATTACH_engine_L/R — if the engine is one part for both engines, use a single ATTACH_engine and say so). Afterburner root mounts on the engine root (add ATTACH_afterburner inside KIT_G4_engine).

In the assembled state (all roots at their attach points) the kit must look identical to Gemini_G4. Keep the materials. Units metres, nose +X, up +Z, bottom at z=0.

Rules: only execute_blender_code and look; no generate_3d / import_asset / search_assets; no shell commands; at most 60 execute_blender_code calls. When done, make Gemini_G4_kit active, leave it assembled, and print: number of calls, each collection with its object count, the list of ATTACH empties with locations.
