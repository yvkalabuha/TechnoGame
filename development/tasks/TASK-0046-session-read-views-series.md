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
   - переключатели серий в `SliceRunConfig`: абляции `L_K`, `L_K0`, `L_C0` (§6.1), сетка P5 (ось `k_diff` по `r = 1 − k_diff`, #190), `EFFORT_SHARE`. **Вне задачи (решение Orchestrator по проекту SA §12):** `L_C`/`L_P` — нужен `t_known` в Core → TASK-0047; `COMP_V010` — спека §6.8 его пропускает.
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
developer, 2026-11-13 (записал Orchestrator).
- Session: views §12 (`Views/SliceViews.cs`, `Views/ViewBuilder.cs`), публикация после post непустого тика, `TestApiVersion = 1` + эталон `tests/Singularity.Session.Tests/Api/session-surface.v1.txt` (обновление: `SINGULARITY_UPDATE_API=1 dotnet test --filter ApiSurface`), `ViewMode.Calibration` (`WhyNot`/`CurrentRequirement` бросают; `Key`/`FleetValue` = null).
- Переключатели `SliceRunConfig`: `Layer` (`Lk`+таблицы, `Lk0`, `Lc0`), `Grid` (P5), `EffortShare` (×0/0,5/1/2/4; пол `Int128`, остаток округления в закупки — правило SA [Предл.], спека v0.24 r1 §6.8). Колонки `run.*` только при не-default. Core, Kernel, golden, `SchemaVersion = 7` не менялись.
- Тесты +48 (ViewTests, SeriesSwitchTests, ApiSurfaceTests). Полный `dotnet test`: Arch 80, Kernel 314, Core 1006, Session 290; 0 падений.
- Предположения: (1) `ucm` = `1/max(yield, eps)` (Profile одного процесса с единичными весами; с весами раскладки отличие до ~4%); (2) `BadLicence.Money = 4` есть, view отдаёт `Refusals [Self, Empty, Gen, Money]`.
- Р-12-2 снят: все 5 плеч `EffortShare` проходят `PolicyLiveness`/`ValidateStartUpkeep`; при ×4 мин. доля закупок 0,266.
- [high] 9 из 27 ячеек сетки P5 (`k_diff ×2` = 1,0) не исполнимы: `c_eff = 0`, деление на ноль в `KnowledgeSystem.Compute`; `ApplyGrid` отказывает с причиной. Нужно решение по оси.
- low: конструкторы view публичные (позиционные records); `DesignInputsView.GiveUp` = null без продуктового ИИ; тест `Lk0` с другой датой старта не написан (нет сценария); сьют Session ≈ 4 мин 49 с.
