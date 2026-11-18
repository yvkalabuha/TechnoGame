---
id: TASK-0104
title: "Event sounds: final variants (#407), gain fixes, listening-pack pruning"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M1
agent: developer         # ответственный агент
category: client
priority: low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0100]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "#407"
created: 2026-11-18
---

# TASK-0104 — Event sounds: final variants (#407), gain fixes, listening-pack pruning

## Goal
Apply owner decision #407: one sound variant per event for the whole game, chosen by sound-director. The final table is in `docs/audio/EVENT_SOUNDS_MAP.md` §1.

## Description
1. Swap 2 cues in `src/Singularity.Client.Spike/assets/audio/ui/`: node_discovered.ogg is replaced by E09_c, launch_space.ogg by E16_b. Update Cue.Seconds/MinGap.
2. Gain: order_done (E04_a) +8 dB and line_commissioned (E11_a) +6 dB, or re-normalise those two files to their r2 LUFS windows.
3. Update LICENSES.md attribution to map §5.
4. Prune `docs/audio/listening-pack/events/`: remove the 27 unchosen files listed in map §6 and the 8 CREDITS.yml entries used only by them. Update listening-pack/index.html, tools/audio/listening_pack_manifest.json and build_listening_pack.py to match. Keep E12/E14/E15.

## Acceptance criteria
- [ ] The client plays the final variants; audio tests pass; Godot import is clean.
- [ ] The listening pack holds only the chosen variants plus E12/E14/E15; index, manifest and credits are consistent.
- [ ] Licences are only CC0/CC BY.
