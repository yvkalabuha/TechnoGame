---
paths:
  - "src/Singularity.Client.Spike/**"
  - "docs/ui/**"
---

# UI rules (owner decision #417)

- Every screen and control is judged for clarity and accessibility by `docs/ui/UI_DESIGN_SYSTEM.md` §12 (criteria C1–C8, A1–A12). A criterion marked blocking stops a PASS.
- Review starts from frames of a real 1920x1080 render at 100 % UI scale (#424, #425: the game is fullscreen, Full HD is the minimal screen; smaller windows are for tests and never give a PASS). A control that is legible only at 150 % does not pass.
- Any gauge-like control shows its number and unit next to it and has a plain-number fallback (C3, C7). Decoration never competes with data (C5).
- Colour is never the only carrier of meaning: add a shape, a hatch or a label, and take a greyscale frame (A5).
- Keyboard reach, a visible focus of at least 2 px and "Меньше движения" are part of every UI task (A6–A8).
- A UI task lists the criterion ids it must meet in its acceptance criteria. The plan of UX work is `docs/ui/UX_IMPROVEMENT_PLAN.md`.
- No per-frame allocations in `_Draw()`; own procedural drawing; assets only PD, CC0 or CC BY (ADR-0009).
- Frames taken from a fixture do not prove the live path (TASK-0111: the Science panel was empty in play while every fixture frame looked right). Each UI task has at least one frame and one run-time assertion on a real session state. The fixture and the live path share one pure function that picks what to show; that function has unit tests.
- A control never infers a simulation value from another view (TASK-0111: the alloy flask guessed its state from one why_not chain). If the Session view lacks the value, add the field (a task for the core) or leave the control out.
- Space is the pause key everywhere; focused buttons act on Enter. A state-changing control is reachable by keyboard and its focus ring passes 3:1 on every fill it can have (A6, A7).
