---
id: TASK-0018
title: "Core.Products для среза: КБ, варианты, line_E, дефекты, когорты, upkeep, FV, заглушка L_P (T5)"
status: REVIEW         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: developer
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0015]
related: [HYP-0002]
approved_by: orchestrator
approved_at: 2026-11-03
approval_ref: DECISIONS_LOG #48
created: 2026-11-03
---

# TASK-0018 — Core.Products для среза: КБ, варианты, line_E, дефекты, когорты, upkeep, FV, заглушка L_P (T5)

## Цель
Слой изделий среза — задача T5 спецификации.

## Описание
Источники: спека `docs/simulation/slice-jet-aviation.md` (v0.7+) §1.4, §3.3–§3.5, §6.1 (`L_P`), §9; контракт v2.3 §4.4, §4.6, §5, §6; RR-0007; данные `docs/research/data/jet-slice/product_inputs.md` и `product_class.md` (перенести в мод `core` — YAML + схема); слои `Core.Knowledge`/`Core.Industry` (паттерны: месячная частота, входы как чистые функции снимка t−1, без RNG в изделии #28, без состояния вне Table, ImmutableArray для предвычисленного); решения #44 (tech_lead), #62–#65 не входят (второй срез).
КБ и два варианта («превосходящий»/«технологичный»), стадии П/О/С/В (#67), `line_E`, дефекты (`defects_hidden`, выявление от `fleet_hours`), когорты и списание, upkeep, `FV` при равном бюджете (#34), заглушка `L_P` с источником выпуска §6.1, `report_output` в слой способности.
Слить в `main` только после отчёта TASK-0017 (поэтапная сборка спецификации).

## Критерии приёмки
- Совпадение с эталоном для части изделий, если эталон её покрывает; иначе — эталонные значения добавляет simulation-engineer (D-тесты).
- Нет RNG и `double` в `Core.Products`; L1/L2; инварианты I1–I7 в части изделий.
- Ревью reviewer, проверка test-engineer.
