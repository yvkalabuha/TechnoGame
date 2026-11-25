# tools/models3d — parametric fighter generator (3D pilot)

Pilot of `docs/ui/specs/3d-models-plan.md` variant (a), decision O-075. `fighter.py` builds a generic jet fighter
mesh from the design slots of `mods/core/product_classes.yaml` and renders orthographic blueprint views
(paper `#F2EFE6`, ink `#1D1E20` Freestyle lines, flat fills). Output is committed PNG; nothing 3D goes into the client.

## Run (Blender 5.3, headless)

```sh
# six pilot designs G1–G6: 3 views each at 1024x1024 + contact sheet 1920x1080 into assets/models3d/fighters/
nice -n 10 blender -b --factory-startup -P tools/models3d/fighter.py
# any slot combination
nice -n 10 blender -b --factory-startup -P tools/models3d/fighter.py -- \
    --design eng_g4,af_mach2,ab_on --engines 2 --name test --out /tmp/fighters
# determinism test: same design rendered twice -> identical bytes; a slot change -> a different picture
nice -n 10 blender -b --factory-startup --python-exit-code 1 -P tools/models3d/test_determinism.py
```

`blender` is `~/.local/bin/blender` here. Full set: about 60 s on 4 Cycles CPU threads; the test about 10 s.

## Determinism

No random numbers. Cycles on CPU, 24 samples, fixed seed, no adaptive sampling, no denoising, no dither, 4 fixed
threads, emission-only materials (no lighting). Cycles writes render times into PNG text chunks, so every render is
re-encoded by `_png_write` (plain RGB, no metadata, zlib 9). Two full runs on this machine gave identical SHA-256 for
all 19 files. Bytes on another machine or another Blender build are not guaranteed: pin Blender 5.3 and re-render.

## Slots -> geometry

The tables live at the top of `fighter.py` (`ENGINE`, `AIRFRAME`, `AFTERBURNER`); `check_slot_ids()` fails if
a slot id is not in `product_classes.yaml`. `design_params()` is the single pure function slots -> geometry.

| Slot | Drives |
|---|---|
| `engine` eng_g1…eng_g6_adaptive | size (length 10.5–19.5 m), fuselage radius, intake layout (G1–G2 nose, G3 side round, G4–G5 side rectangular raked, G6 caret), nozzle (round; flat at G6), default engine count |
| `airframe` af_straight | straight tapered wing, tip tanks, conventional tailplane |
| af_swept | 40° swept wing and tail |
| af_milled | 58° delta, area-ruled waist, no tailplane |
| af_titanium | cropped 52° delta, thin, tailplane |
| af_mach2 | 40° trapezoid wing, leading-edge root extension, all-moving tail, boxy section |
| af_fbw | trapezoid wing, large root extension, ventral intake |
| af_composite | 52° delta with canards, blended section |
| af_lo_composite | faceted chined fuselage, diamond-profile wing, canted twin fins, caret intakes |
| `afterburner` ab_none / ab_on | short convergent nozzle / long afterburner pipe with a petal step and flare |
| engine count (not a slot yet) | by generation archetype (O-075): G1–G3 and G5 one, G4 and G6 two; `--engines` overrides. Two engines + airframe class ≥ 4 give twin fins |

Pilot designs (`PILOT`): G1 eng_g1+af_straight+ab_none, G2 eng_g2+af_swept+ab_none, G3 eng_g3+af_milled+ab_on,
G4 eng_g4+af_mach2+ab_on, G5 eng_g5+af_composite+ab_on, G6 eng_g6+af_lo_composite+ab_on.

## Output (`assets/models3d/fighters/`)

`gN_top.png`, `gN_side.png`, `gN_front.png` (1024x1024; nose up in top view, nose left in side view; the three
views of one aircraft share one scale, so the front view is small) and `sheet_1920x1080.png` (3x2 cells G1–G6 left to
right, top to bottom; the generation is shown as a count of ink squares, no text — labels are drawn by the client).

## Licence

Own work: procedural geometry and code of this project, no third-party models, textures or references (ADR-0009).
Blender (GPL) is used as a tool only; its output is not GPL. Shapes are generic and copy no real aircraft.
