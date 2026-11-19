---
paths:
  - "src/Singularity.Client.Spike/**"
  - "docs/ui/**"
---

# UI rules (owner decision #417)

- Every screen and control is judged for clarity and accessibility by `docs/ui/UI_DESIGN_SYSTEM.md` §12 (criteria C1–C8, A1–A12). A criterion marked blocking stops a PASS.
- Review starts from frames at 100 % UI scale (1280x720 and 1920x1080). A control that is legible only at 150 % does not pass.
- Any gauge-like control shows its number and unit next to it and has a plain-number fallback (C3, C7). Decoration never competes with data (C5).
- Colour is never the only carrier of meaning: add a shape, a hatch or a label, and take a greyscale frame (A5).
- Keyboard reach, a visible focus of at least 2 px and "Меньше движения" are part of every UI task (A6–A8).
- A UI task lists the criterion ids it must meet in its acceptance criteria. The plan of UX work is `docs/ui/UX_IMPROVEMENT_PLAN.md`.
- No per-frame allocations in `_Draw()`; own procedural drawing; assets only PD, CC0 or CC BY (ADR-0009).
