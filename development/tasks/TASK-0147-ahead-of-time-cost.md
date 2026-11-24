---
id: TASK-0147
title: "Ahead-of-time cost for knowledge nodes in Core (#458) and G5/G6 rev 3.1 data on the train-2 branch"
status: IN_PROGRESS
level: light
milestone: MS-M05
agent: developer
category: core
priority: medium
needs_owner: false
depends_on: []
related: [TASK-0128, TASK-0133, TASK-0134]
approved_by: orchestrator
approved_at: 2026-11-24
approval_ref: "owner decisions #457, #458 (chat 2026-11-24); light task inside approved decisions, approved by Orchestrator"
created: 2026-11-24
---

# TASK-0147 — Цена опережения (#458) и данные G5/G6 ред. 3.1 на ветке поезда 2

## Цель
Решения владельца #457 (промежуточные ступени) и #458 (мягкая цена опережения): открытия G5/G6 ближе к историческим годам, опережение возможно, но дорого.

## Описание
1. Данные: узлы 82–99 и правки `requires` по `docs/research/slice-g5-g6-content.md` ред. 3.1 в `mods/core/nodes.yaml` (и локализация RU/EN) на ветке поезда 2 (`worktree-agent-a94cedd4bf9d686b2`: TASK-0133 + TASK-0134). Валидатор принимает; узел 87 — лист (оставить).
2. Core: множитель цены узла `1 + k · max(0, anchor_year − t_years)^p` (или иной монотонный вид, `[Предл.]` simulation-engineer), параметры `AheadCostK`, `AheadCostPow`, `AheadCostMax`; `k = 0` — выключено (поведение как сейчас). Детерминизм L1/L2, без аллокаций на тике, таблица цен не пересчитывается каждый тик без нужды.
3. Lab: ключи манифеста для параметров; simulation-engineer — зонд 2×2 (цепочки × цена) с пре-регистрацией по `docs/reviews/slice-g5-g6-content-rev3-gate.md` (сдвиг G5 APP ≥ +3, АФАР ≥ +8 лет; годы первого открытия против целей §4.2 зонда), выбор k по правилу, показ владельцу.
4. Перезапись эталонов — одна, вместе с поездом 2 (§5 оркестрации).

## Критерии приёмки
- [ ] Unit-тесты формулы (монотонность, k=0 = без изменений, потолок); L1/L2 на 1020 мес.
- [ ] Зонд 2×2: VALIDITY OK, таблица годов; владелец утверждает точку.
- [ ] Ревьюер APPROVE; второе ревью.
