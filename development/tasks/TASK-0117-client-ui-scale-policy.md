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
Owner decisions #424 and #425: the game runs fullscreen or as a borderless full window; the minimal supported screen is Full HD 1920x1080; larger screens scale the interface up. Today the client is laid out for a 1600x900 window and draws smaller text in smaller windows (ui-art-director, TASK-0112 review: criterion A1 fails). Set the scale policy for the whole client and give the owner one command to start the game.

## Description
1. ui-art-director: write the policy into `docs/ui/UI_DESIGN_SYSTEM.md` (§4 and §12): the base layout is 1920x1080; what one du is there; integer or fractional scale for 2560x1440 and 3840x2160; the text floor in rendered pixels; the settings "Масштаб интерфейса" 100–150 % on top of it. Windows smaller than 1920x1080 are test-only and may scale down.
2. developer: start fullscreen or borderless by default (a setting: fullscreen / borderless window; a framed window only with a command-line flag for tests and `--shots`); implement the policy (`ContentScaleMode`, `ContentScaleFactor`, `ContentScaleSize`); fix the layouts that break at 1920x1080; `--shots` at a real 1920x1080 render.
3. developer: `./play.sh` in the repository root starts the game fullscreen with one command (build if needed, then run); a line in README or `docs/ui/` says how to quit.

## Acceptance criteria
- [ ] The game starts fullscreen or borderless with `./play.sh`; Esc or the menu quits.
- [ ] A1, A2, A9 pass on the key screens at 1920x1080 (frames are a real 1920x1080 render), UI scale 100 % and 150 %.
- [ ] Client tests pass; code review APPROVE; ui-art-director PASS.
- [ ] TASK-0112 (credits) and TASK-0111 (instruments) get their visual PASS for A1 on the new frames.
