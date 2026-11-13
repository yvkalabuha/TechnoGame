---
id: TASK-0046
title: "Session: read views среза и переключатели серий для Lab (разблокировка T10)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: developer
category: core
priority: high
needs_owner: false
depends_on: []
related: [ADR-0014, TASK-0044]
approved_by: orchestrator
approved_at: 2026-11-13
approval_ref: "ADR-0014 п. 5–6 (read views — вместе с T10); спека среза v0.24 §11 T10"
created: 2026-11-13
---

# TASK-0046 — Session: read views среза и переключатели серий

## Цель
Снять блокировку T10 (TASK-0044). Lab должна читать состояние среза через публичные read views Session и запускать все серии манифеста EXP-0002 без отражения на приватные члены.

## Описание
Источник: TASK-0044 «Результат»; `docs/simulation/slice-csv-format.md` v0.3 §9 (нужные views) и §11 п. 3–4 (неисполнимые серии); ADR-0014 п. 5–6; `docs/architecture/SLICE_COMPOSITION.md`.
1. **systems-architect** — короткий проект поверхности в `SLICE_COMPOSITION.md`:
   - views: `ActorView`, `NodeView`, `DesignView`, `PolicyDecisionView`, `ToolingLedgerView`, `CapabilityView` и строки печати A40, F17b, флота и лицензий;
   - неизменяемые снимки после post непустого тика;
   - повышение `test_api_version`;
   - переключатели серий в `SliceRunConfig`: абляции `L_K`, `L_C`, `L_P` (§6.1), сетка P5, `EFFORT_SHARE`, `COMP_V010` — только те, что заданы спекой v0.24.
2. **developer** — реализация в `src/Singularity.Session`:
   - тест на каждый view;
   - тест версии поверхности;
   - переключатели с тестами «выключено = прежний хеш» (golden-хеши не меняются).
   Core меняется только через публичные методы. Если нужна правка Core — остановиться.

## Критерии приёмки
- [ ] Проект поверхности утверждён Orchestrator, systems-architect проверил реализацию.
- [ ] Views и переключатели реализованы; golden-хеши без изменений; `dotnet test` зелёный.
- [ ] L1/L2 не нарушены.

## REVIEW
- [ ] systems-architect (поверхность)
- [ ] Code review (reviewer)
- [ ] Второе ревью (codex) — R2, до чистого accept (#181)
- [ ] Тесты (test-engineer)

## Результат
