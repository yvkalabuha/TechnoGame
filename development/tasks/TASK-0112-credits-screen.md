---
id: TASK-0112
title: "Credits screen with music and sound attribution (DEBT-0050)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M1
agent: developer         # ответственный агент
category: client
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0100, TASK-0104]
approved_by: orchestrator
approved_at: 2026-11-19
approval_ref: "DEBT-0050, ADR-0009 (CC BY needs credit) (light)"
review_ref: "reviewer: APPROVE ced692d (code read, no build; 4 Codex findings remain as DEBT-0065); codex accept 61f15a63; ui-art-director: PASS on 1920x1080 frames, docs/ui/reviews/task-0117-visual-2.md"
created: 2026-11-19
---

# TASK-0112 — Credits screen with music and sound attribution (DEBT-0050)

## Goal
Close DEBT-0050. The 16 music tracks (#392) and the CC BY event sounds (TASK-0104) need visible attribution in the client (ADR-0009).

## Description
A credits screen reachable from the main menu, with sections for music and sounds. Each entry shows the title, the author, the licence with a link, and the changes made. Data comes from the existing attribution sources (`docs/audio/MUSIC_SELECTION.md`, `docs/audio/EVENT_SOUNDS_MAP.md` §5, `src/Singularity.Client.Spike/LICENSES.md`): one machine-readable list, with no hand-copied duplicates. RU/EN locale.

## Acceptance criteria
- [ ] Every CC BY asset used by the client appears with author, licence and link; a test checks that every audio file in assets has a credits entry.
- [ ] Code review APPROVE; ui-art-director PASS on the screen.

## Scope notes (2026-11-19)
- "One machine-readable list" means `src/Singularity.Client.Spike/assets/credits.json` is the only source the client and `LICENSES.md` use. `docs/audio/MUSIC_SELECTION.md` and `docs/audio/EVENT_SOUNDS_MAP.md` are research records of how the files were chosen; they are not kept in sync by hand. A test compares the authors and licences of credits.json with EVENT_SOUNDS_MAP §5 if that is cheap; otherwise the docs get a line that credits.json is authoritative.
- The visual PASS for text size (criterion A1) comes with TASK-0117 (decision O-010).
