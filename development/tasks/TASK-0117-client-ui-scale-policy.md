---
id: TASK-0117
title: "Client UI scale policy: text never below the A1 floor at 1280x720"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M05
agent: developer         # ответственный агент
category: client
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0112, TASK-0111]
approved_by: orchestrator
approved_at: 2026-11-19
approval_ref: "#417 A1; ui-art-director review of TASK-0112 (light)"
created: 2026-11-19
---

# TASK-0117 — Client UI scale policy: text never below the A1 floor at 1280x720

## Goal
At a 1280x720 window the whole client is drawn about 0.8 times smaller than at 1600x900: text set at 14 du renders at about 11 px, and criterion A1 (#417) fails on every screen, not only on the credits screen (ui-art-director, TASK-0112 review). Fix the scale policy for the whole client.

## Description
1. ui-art-director: write the policy into `docs/ui/UI_DESIGN_SYSTEM.md` §12/§4: what one du is at 1280x720, 1600x900 and 1920x1080; the text floor in rendered pixels; what gives way when the window is small (layout reflows, panels narrow, lists scroll; text does not shrink).
2. developer: implement it in `Settings.cs` / window setup (`ContentScaleMode`, `ContentScaleFactor`, `ContentScaleSize`) and fix the layouts that break. `--shots` of the key screens at a real 1280x720, 1600x900 and 1920x1080 window, UI scale 100 % and 150 %.

## Acceptance criteria
- [ ] A1, A2, A9 pass on the key screens at a real 1280x720 window (frames checked by pixel size).
- [ ] Client tests pass; code review APPROVE; ui-art-director PASS.
- [ ] TASK-0112 (credits) gets its visual PASS on the new frames.
