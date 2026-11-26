---
id: TASK-0043
title: "Пробный прогон DeepSeek на 4 шагах R0–R1 (ADR-0015, #182)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
- [x] Ревью Claude (Orchestrator, 2026-11-13): B1-ds (Fx6, 185 строк) и L1 (83 строки) — приняты и слиты (`b233e81`, `f6cfb0c`); A1, A2-clean — отчёты R0 без файлов. Низкие находки → DEBT-0027

## Результат

Прогон 2026-11-13, профиль `deepseek-flash`; `deepseek-v4-pro` не понадобился (все шаги приняты с 1-го раунда, без повторов). Слияний в main нет, итого $0,0113 из лимита $1,00 (на шаг ≤ $0,50). Ledger до: $0,0381 (только `gate:ADR-0015`); после: $0,0494, незакрытых резервов 0.

| Шаг | task_id | Исход | Модель | Раунды | $ | Риск (`classify --max R1`) | Ветка |
|---|---|---|---|---|---|---|---|
| A1 | PILOT-A1-product-class-inventory-ds | ACCEPTED | deepseek-flash | 1 | 0,0034 | R0 (без записи файлов) | — |
| A2-clean | PILOT-A2-jet-nodes-clean-ds | ACCEPTED | deepseek-flash | 1 | 0,0015 | R0 (без записи файлов) | — |
| B1 | PILOT-B1-fx6-tests-ds | ACCEPTED, нужно ревью | deepseek-flash | 1 (1 кандидат) | 0,0033 | R1 (`tests/Singularity.Kernel.Tests/Llm/Fx6LlmTests.cs`, +185) | `llm/PILOT-B1-fx6-tests-ds` |
| L1 | PILOT-L1-risk19-yield-tests | ACCEPTED, нужно ревью | deepseek-flash | 1 | 0,0031 | R1 (`lab/analysis/test_risk19_yield_channel.py`, +83) | `llm/PILOT-L1-risk19-yield-tests` |

Контракты (не закоммичены): A1, A2-clean, B1 — новые `task_id` с суффиксом `-ds`, `fill-hashes`, B1 `max_candidates` 2 → 1 (платный провайдер, ADR-0015 §6). L1 — новый `tools/llm_worker/contracts/L1-risk19-yield-tests.json` (v2, R1, `allowed_paths` = один тестовый файл, 9 мутантов на `risk19_yield_channel.py`, порог 0,8, тестов ≥ 8).

Сравнение с пилотом Qwen: A1 1 раунд против 2, B1 1 раунд против 2 (Qwen: 2 кандидата, один провален); расход времени сервера 0,1 мин против 9–20 мин.

Сбои инфраструктуры: код `tools/llm_worker` не менялся. Особенность L1: `b1_tests.py` разбирает вывод `dotnet test` (`Passed: N`/`Failed: N`), у `unittest` такого вывода нет. Обход в контракте: `test_cmd` — `python3 -B -c`, который запускает `unittest` и печатает эти строки; `build_cmd` — проверка `ast.parse`, `restore_cmd` — `python3 -B -c pass`. Предложение: поддержать вывод `unittest` в верификаторе отдельной задачей (OPEN).

Метрики отчёта (`python3 -m tools.llm_worker report`): отдельного раздела «Метрики» в отчёте нет (в `report.py` его не нашлось), есть «Экономика» и «Расход (ledger)». Токены flash: чтение 95 856, ответ 27 391 (накопительно по отчёту); время ответа сервера 63,9 мин (накопительно, включая Qwen; у `-ds` около 0,3 мин суммарно); эталон Claude по принятым 129 000 токенов. По ADR-0015 §7: раундов до accept Claude — не определено (ревью не проведено); $ на задачу 0,0015–0,0034; сбежавших high — 0.

Ревью Claude ветвей B1-ds и L1 не проведено (см. REVIEW).
