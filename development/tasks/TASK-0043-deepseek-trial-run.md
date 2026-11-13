---
id: TASK-0043
title: "Пробный прогон DeepSeek на 4 шагах R0–R1 (ADR-0015, #182)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: developer
category: tooling
priority: medium
needs_owner: false
depends_on: [TASK-0039]
related: [ADR-0015]
approved_by: owner
approved_at: 2026-11-13
approval_ref: "DECISIONS_LOG #182; ADR-0015, план задач, п. 4 (пробный прогон)"
created: 2026-11-13
---

# TASK-0043 — Пробный прогон DeepSeek на 4 шагах R0–R1

## Цель
Первый реальный прогон исполнения DeepSeek (`tools/llm_worker`, узкий контракт) на 4 шагах R0–R1. Цель — проверить цепочку целиком: контракт → очередь → `deepseek-flash` → `pro` → проверки в bwrap → ветка `llm/<id>` → классификатор → ревью Claude. Кроме того, собрать метрики ADR-0015 §7.

## Шаги
1. **A1** — `contracts/A1-product-class-inventory.json` (R0, docs). Повтор пилота Qwen на DeepSeek.
2. **A2-clean** — `contracts/A2-jet-nodes-clean.json` (R0).
3. **B1** — `contracts/B1-fx6-tests.json` (R1, `tests/Singularity.Kernel.Tests/Llm/*.cs`, #179).
4. **L1** — новый контракт: unit-тесты stdlib `lab/analysis/test_risk19_yield_channel.py` для `lab/analysis/risk19_yield_channel.py` (R1). Тесты проверяют воспроизведение ключевых чисел отчёта `docs/simulation/risk19-yield-channel.md` §2: 68%/64% и сценарий G.

## Правила
- Хеши контрактов актуализировать (`fill-hashes`), при необходимости завести новый `task_id`.
- Лимит: ≤ $0,50 на задачу (`max_usd_task`), всего ≤ $1,00 на прогон. Ledger до и после.
- Слияние в main не выполняется. Ветки `llm/*` остаются для ревью Orchestrator; `risk classify --max R1` на каждой.
- Код `tools/llm_worker` меняется только при сбое инфраструктуры. Такие правки описываются в «Результат».

## Критерии приёмки
- [ ] 4 шага прогнаны; по каждому записаны исход (ACCEPTED/REJECTED/ESCALATED), модель, раунды, $ и класс риска диффа.
- [ ] Раздел метрик `python3 -m tools.llm_worker report` приложен.
- [ ] Сбои инфраструктуры (если были) описаны.

## REVIEW
- [ ] Ревью Claude каждого ACCEPTED диффа (Orchestrator / reviewer)

## Результат
