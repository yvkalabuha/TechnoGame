---
id: TASK-0035
title: "llm_worker: провайдер DeepSeek и учёт трат (ADR-0015 1a)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: developer
category: tooling
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0019]
related: [ADR-0015]
approved_by: owner
approved_at: 2026-11-13
approval_ref: "DECISIONS_LOG #176 (ADR-0015, план 1a)"
created: 2026-11-13
---

# TASK-0035 — llm_worker: провайдер DeepSeek и учёт трат (ADR-0015 1a)

## Цель
Сделать `tools/llm_worker` пригодным для платного провайдера DeepSeek: профиль провайдера и учёт трат с лимитом $5, который отказывает при любом сомнении. Это задача 1a ADR-0015.

## Описание
Источник: `docs/decisions/ADR-0015-external-executors-providers.md` §6 (учёт и лимиты), §9 (готовность), §2 (цепочка flash → pro → Claude, #175 d). Долг: DEBT-0022.
- Профиль провайдера: поля запроса и способ JSON-режима настраиваются. Для DeepSeek — `json_object`, схема в промпте, локальная проверка (имена моделей, цены и режим — сверить с api-docs.deepseek.com, записать дату). Профиль llama.cpp сохраняется.
- Ledger вне репозитория: `flock`, маркер при init (нет маркера — отказ), резерв худшего случая (вход 1 токен на 2 байта, выход `max_tokens`) через callback перед каждой попыткой провайдера, включая повторы; сверка с `usage` после ответа; нет `usage` — отказ или консервативная оценка, не 0.
- Сохранение сырого ответа до `json.loads`; возобновление берёт `*_response.json` и не оплачивает запрос второй раз.
- HTTP 402 останавливает прогон, задача возвращается в QUEUED.
- Неудачи считаются по паре (шаг, модель); `max_candidates` = 1 для платного провайдера; предел обрывов на задачу.
- Флаг §9: платный провайдер отказывает (`provider_not_ready`), пока флаг не снят; флаг снимает Orchestrator после 1a–1c.
- Раздел метрик в отчёте ledger; строка «Токены Qwen» заменяется на имя провайдера.
- Сеть в тестах не используется; ключ DeepSeek агенту не нужен и не читается.

## Критерии приёмки
- [ ] `tests/test_cost_invariants.py` проходит без `expectedFailure`.
- [ ] Тесты: два процесса пишут ledger одновременно; нет маркера — отказ; фактический `usage` больше резерва — перерасход не больше одного вызова; 402 останавливает прогон; повтор провайдера резервируется до запроса.
- [ ] Профиль llama.cpp работает как раньше (старые тесты зелёные).
- [ ] `python3 -m unittest discover tools/llm_worker/tests` — всё зелёное, кроме `test_pilot_contracts_are_valid` (DEBT-0024 закрывает TASK-0037).

## Тесты
`python3 -m unittest discover tools/llm_worker/tests` — поддельный провайдер, без сети.

## Связанные документы
ADR-0015; DECISIONS_LOG #174–#176; DEBT-0022; отчёты ревью TASK-0019.

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно

## Результат
