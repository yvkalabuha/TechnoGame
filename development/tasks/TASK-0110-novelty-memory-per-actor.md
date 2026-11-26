---
id: TASK-0110
title: "Novelty memory per actor: tried alternatives survive slot reuse (DEBT-0054)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M1
agent: developer         # ответственный агент
category: core
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0099, ADR-0005]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "DEBT-0054, spec C10 (light)"
created: 2026-11-18
tags: [task, core, gameplay, done]
---

# TASK-0110 — Novelty memory per actor: tried alternatives survive slot reuse (DEBT-0054)

## Goal
Fix DEBT-0054. After slot reuse (TASK-0099), `ProductsSystem.Create` builds `used` by walking the slots, so the alternatives of a project withdrawn in stage О become "new" again. Spec C10 defines novelty through the actor's knowledge, not the slot. Design note: `docs/game-design/notes/slot-reuse-novelty-memory.md`.

## Description
- A monotone per-actor column "alternative a reached О". It is set when a project enters О and for the starting fleet.
- `Create` takes `used` from the column.
- SchemaVersion +1. The golden hashes change: explain them; the trajectory must stay identical until the first slot reuse (~month 265).

## Acceptance criteria
- [x] Tests:
  - (a) withdrawn in О, then reuse: novelty unchanged;
  - (b) withdrawn in П: the alternatives stay new;
  - (c) the starting fleet;
  - (d) a different slot-freeing order gives the same novelty;
  - (e) L1/L2 and column save/load.
- [x] `dotnet test`, `lab selftest` pass; reviewer APPROVE.

## Решение Orchestrator (2026-11-19)
Live projects in П still count as "used" for novelty (`InLiveProject`), as before TASK-0110. Reason: the old behaviour; two parallel projects on the same alternatives should not both count as new; a live slot cannot be reused, so the result does not depend on slot history. The pure "reached О" reading of C10 would shift the trajectory before the first reuse; not adopted. Withdrawn-in-П projects are not counted (§4.4e).
## Результат
**Сделано.** Колонка `prd.altReached.{alt}` (int на актора и альтернативу, монотонная): 1 при входе проекта в О и для стартового парка (все альтернативы сценарных проектов, кроме снятых). `Create` и `CandidateValue` считают novelty через `FreshShare` по колонке, без обхода слотов и без аллокаций (List заменён счётчиком). Публичные `ProductsSystem.Novelty(actor, alts)` и `AltReached(actor, alt)`. Мёртвый `DesignRules.Novelty` и его тест удалены. SchemaVersion 9 → 10, отпечаток схемы перезаписан.
**Отклонение от формулировки задачи (на проверку).** Старый код считал «использованными» и альтернативы живого проекта в стадии П (не только О). Чистая колонка «дошло до О» сдвигала траекторию до реюза слотов (проекции T6dOff, NoRetire упали на месяце 383 без реюза). Чтобы траектория осталась идентичной, `FreshShare` дополнительно учитывает живые проекты в П (`InLiveProject`): их слот нельзя переиспользовать, ответ не зависит от истории слотов; снятый в П проект не учитывается (§4.4e). Это не по букве спеки C10 (только О); решение владельца: оставить так или перейти на чистое «дошло до О» с пересчётом траектории.
**Golden.** Перезаписаны с комментарием «Re-recorded in TASK-0110»: Main seed1 m149 (Calibration, SeriesSwitch, ViewTests, SliceChecks), F17b Admit/YProd ×5, LinkedRun, ProductAiSession, SchemaTests (v10). Траектория до первого реюза (~месяц 265) не меняется: проекции на старые колонки (`ColumnProjection.IsSlotGenerationColumn` теперь исключает и `prd.altReached.*`) проходят: T6dOff, FleetAblation NoRetire, ToolingUnmet; SlotReuseL2Tests подтверждает, что реюза до месяцев golden нет.
**Тесты.** `NoveltyMemoryTests` (8): (a) снят в О, реюз; (b) снят в П, реюз; (c) стартовый парк; (d) порядок освобождения слотов; (e) L1/L2 (threads 2/4/8, chunk 1..3) и save/load колонки, подмена колонки меняет хеш.
**Проверка.** `dotnet build -c Release` 0 предупреждений; `dotnet test -c Release` зелёный (Core 1012, Session 392, Lab 202, Kernel, Architecture, Localization, Audio); `lab selftest` 58/58; `TickPathAllocationTests` проходит.
- Review CHANGES ×2 (LaunchCanonical path; test (d) without retake; L_P test vacuous) fixed in 9dbb7d4, 5a9ec5b (mutation check: the L_P test fails without the fix). Merge 08a9d0f; full suite green on merged main (Core 1013), `lab selftest` 58/58.
