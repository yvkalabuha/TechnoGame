---
id: TASK-0035
title: "llm_worker: провайдер DeepSeek и учёт трат (ADR-0015 1a)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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
- [x] `tests/test_cost_invariants.py` проходит без `expectedFailure`.
- [x] Тесты: два процесса пишут ledger одновременно; нет маркера — отказ; фактический `usage` больше резерва — перерасход не больше одного вызова; 402 останавливает прогон; повтор провайдера резервируется до запроса.
- [x] Профиль llama.cpp работает как раньше (старые тесты зелёные).
- [x] `python3 -m unittest discover tools/llm_worker/tests` — всё зелёное, кроме `test_pilot_contracts_are_valid` (DEBT-0024 закрывает TASK-0037).

## Тесты
`python3 -m unittest discover tools/llm_worker/tests` — поддельный провайдер, без сети.

## Связанные документы
ADR-0015; DECISIONS_LOG #174–#176; DEBT-0022; отчёты ревью TASK-0019.

## REVIEW
- [x] Code review (reviewer, 2026-11-13) — changes-requested: 1 high (init пересоздаёт ledger), medium/low; всё исправлено в доработке, high проверен Orchestrator по коду и тестам
- [x] Тесты (test-engineer, 2026-11-13) — добавлен `test_ledger_invariants.py` (property-тест лимита); 4 пробела (усечение ledger, нулевой/bool/отрицательный usage) исправлены, `expectedFailure` сняты; 122 теста, красный только DEBT-0024. Остаток — DEBT-0025

## Результат
### Что сделано
- `config.py`: `Profile` и `PROFILES` (`llamacpp` как раньше; `deepseek-flash`, `deepseek-v4-pro`: `json_object`, `thinking: disabled`, без `seed`). Сверка с api-docs.deepseek.com (pricing, json_mode, thinking_mode, error_codes) **2026-11-13**, дата записана в комментарии. Лимиты: `MODEL_FAILURE_LIMIT=2`, `MAX_INFRA_FAILURES=3`.
- `prices.json` (R3, флаг `paid_providers_ready: false`, лимиты $5 и $0.50, пиковые цены без кэша: flash 0.30/1.20, pro 1.32/3.96 за 1M).
- `ledger.py`: `Ledger` (flock, маркер `ledger.init`, дозапись, нет маркера или битая строка — `SpendRefused`), `Guard` (резерв worst case: 1 токен на 2 байта запроса + `max_tokens`; сверка с `usage`; нет `usage` — резерв остаётся).
- `provider.py`: схема в промпте для `json_object`; резерв перед каждой попыткой, включая повторы; 4xx/429 освобождают резерв, 5xx и тайм-аут оставляют; HTTP 402 — `InsufficientFunds`; `on_raw` отдаёт сырой ответ до разбора; `parse_response`.
- `loop.py`: сырой ответ (`*_raw.bin`) и `*_response.json` сохраняются до разбора, возобновление читает их без нового запроса; счётчик неудач по (шаг, модель), у платного предел 2; `provider` в `loop.json`.
- `runner.py`: платный провайдер — 1 кандидат; guard привязывается к задаче; 402 и `SpendRefused` останавливают прогон, задача в QUEUED; сбои инфраструктуры считаются в `runs/<task>/infra.json`, на 3-м — ESCALATED (только платный).
- `report.py`: «Токены (<провайдер>)» вместо «Токены Qwen»; раздел «Расход (ledger)».
- `__main__.py`: `run --profile`, `ledger init|show`; платный профиль без флага или ledger — отказ (код 4).
- Тесты: новый `tests/test_ledger.py` (30 тестов); `expectedFailure` снят; `helpers.FakeProvider` принимает `on_raw`.

### Проверка
`python3 -m unittest discover tools/llm_worker/tests`: 94 тестов, единственный провал — `test_pilot_contracts_are_valid` (DEBT-0024, TASK-0037). Сеть и ключ не использовались; CLI-проверка: `run --profile deepseek-flash` → `refused: provider_not_ready`.

### Открыто
- Резерв при сбое между чтением сырого тела и сверкой (возобновление из `*_raw.bin`) остаётся худшим случаем: консервативно, не ноль.
- Цепочка flash → pro → Claude реализована только как профили и счётчик пары (шаг, модель); переключение модели при пределе — в задаче оркестрации (1b/1c).
- `ledger init` не запускался (создаёт каталог в `~/.local/share`); флаг `paid_providers_ready` снимает Orchestrator.

### Доработка после ревью (reviewer, test-engineer)
- Ledger: рядом с файлом лежит `ledger.head` (число записей и хеш-цепочка sha256), обновляется под тем же flock при каждой записи. Усечение, правка или мусор — `ledger_corrupt`, отказ. Сбой между записью строки и head даёт отказ (не недосчёт). `init` при наличии части файлов отказывает (`ledger_missing`), при полном ledger — no-op с предупреждением. Отрицательная сумма — отказ.
- `usage`: строгий `type is int`; `{0,0}`, нулевой вход, bool, отрицательные — как отсутствие `usage` (резерв остаётся). HTTP 200 без `choices`, но с `usage`: списывается по `usage`, затем ошибка.
- Резерв освобождается только при ConnectionRefused и `gaierror` (запрос не уходил) и при 4xx/429 (ответ-ошибка не тарифицируется); тайм-аут, сброс, 5xx оставляют резерв. `ledger reconcile <id> <usd> --source <ref>` закрывает только незакрытые резервы под flock; `ledger show` выводит их id.
- `run` — один процесс (flock на `queue/.run.lock`); `guard.task` сбрасывается в `finally`; бесплатный профиль принимает только loopback/частный адрес (10.8.0.1 для Qwen проходит); `__main__` не перечитывает prices и ledger; в промпте json-режима строчное «json» и пример объекта из схемы.
- Идентификаторы моделей сверены на api-docs.deepseek.com (2026-11-13): `deepseek-flash`, `deepseek-v4-pro`. Ключи профилей, `prices.json` и текст теперь совпадают (профиль `deepseek-pro` переименован в `deepseek-v4-pro`).
- Тесты: у test-engineer `expectedFailure` сняты без ослабления; добавлены тесты на init, head, reconcile (в том числе CLI), usage, освобождение резерва, ключ не попадает в `runs/` и лог, блокировку run. Итог: 122 теста, красный только `test_pilot_contracts_are_valid` (DEBT-0024).
- Переключение flash → pro → Claude не делалось (TASK-0037).
