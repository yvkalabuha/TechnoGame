---
tags: [spec, visual, active]
---
# Brief v2: the G4 engine part must be a whole engine (owner 2026-11-25: "двигатель он сделал - сопло только")

Scene "Gemini_G4_kit" (your kit from brief v1, BRIEF_v1.md). Problem: KIT_G4_engine holds only the two nozzles. In the game the engine slot (eng_g4: twin turbofans with afterburner) is the engine itself.

Do:
1. Rebuild KIT_G4_engine as two complete low-bypass turbofan engines (L and R), each about 5.0 m long without the afterburner, max diameter about 1.0 m (fits inside the fuselage): front fan face with visible blades and spinner, fan case, tapered core casing with a few rings/flanges, accessory gearbox under the casing, pipes/harness hints, turbine section, and the plain (dry) nozzle at the rear. Keep the existing nozzle meshes or replace them so the outside look of the assembled aircraft does not change.
2. Place the engines inside the fuselage on their real positions (fan face just behind the intake ducts, nozzles where they are now). In the assembled state the engines are hidden inside the fuselage except the nozzles — the outside must look as before. If the fuselage has no inner volume, that is fine (engines may intersect the hidden interior).
3. KIT_G4_afterburner stays a separate part: afterburner duct section plus petals and glow, mounting on ATTACH_afterburner at the rear of the engine part. If the afterburner duct makes the dry engine longer than the fuselage, shorten the dry engine so dry engine + afterburner end where the nozzles are now.
4. Keep ATTACH points and part roots as in v1 (update locations only if the engine mount moves; report them).
5. Do not touch scenes Gemini_G4 and Claude_G4. Only execute_blender_code and look; no generate_3d / import / shell. At most 40 execute_blender_code calls.
6. Check with look: assembled view unchanged from outside; an exploded view (engine root moved -6 m in X) shows full engines.
Print: calls used, objects per collection, ATTACH locations, engine length and diameter.
