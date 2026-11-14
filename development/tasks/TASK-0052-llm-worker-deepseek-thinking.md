---
id: TASK-0052
title: "llm_worker: режим размышления DeepSeek для gate и review"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: developer
category: tools
priority: high
needs_owner: false
depends_on: []
related: [ADR-0015, TASK-0035, TASK-0037]
approved_by: owner
approved_at: 2026-11-14
approval_ref: "указание владельца 2026-11-14: «deepseek нужно переводить принудительно в режим размышления»; ADR-0015 §4–§5, #175 c"
created: 2026-11-14
---

# TASK-0052 — llm_worker: режим размышления DeepSeek для gate и review

## Цель
DeepSeek в роли третьего голоса gate и запасного второго ревьюера работает в режиме размышления. Сейчас оба профиля шлют `"thinking": {"type": "disabled"}` (`tools/llm_worker/config.py`).

## Описание
Документация DeepSeek, прочитана 2026-11-14 (api-docs.deepseek.com, guides/thinking_mode, guides/json_mode, quick_start/pricing):
- включение — `"thinking": {"type": "enabled"}`, глубина — `"reasoning_effort": "low" | "high" | "max"` (по умолчанию high);
- `response_format: {"type": "json_object"}` поддерживается обеими моделями, ограничений для thinking не указано; при невалидном JSON `content` может быть из одних пробелов с `finish_reason = "stop"` — это нужно ловить и повторять;
- в режиме thinking `temperature` не действует (ошибки нет);
- рассуждение приходит в `reasoning_content` рядом с `content`; без `tools` обратно его не отправлять;
- покрывает ли `max_tokens` рассуждение — в документации не сказано; максимум вывода 384K.

Сделать:
1. Профиль получает режим размышления: `thinking: enabled|disabled` и `reasoning_effort`. Для `gate` и `review` с DeepSeek по умолчанию `enabled` и `high`; флаг CLI `--thinking {on,off}` и `--effort {low,high,max}`. Исполнитель `run` по умолчанию без изменений (off), с тем же флагом.
2. `max_tokens` для gate и review в режиме thinking — достаточный запас на рассуждение и ответ, не меньше 32K. Значение вынести в config с комментарием. Резерв ledger считает худший случай по этому `max_tokens`. Если худший случай для pro не проходит лимит задачи $0,50, это нужно сказать явно, а не тихо урезать.
3. `reasoning_content` не идёт в разбор JSON. Его длина и `usage.completion_tokens_details.reasoning_tokens`, если поле есть, пишутся в запись run.
4. Пустой или пробельный `content` при `finish_reason = stop` считается сбоем модели (`schema_invalid`) и даёт один повтор в пределах лимитов #175 d.
5. Обновить комментарий в config, README (раздел gate/review) и тесты (`tools/llm_worker/tests`): тело запроса с thinking, отсутствие `temperature` при thinking, разбор ответа с `reasoning_content`, пробельный content.

## Критерии приёмки
- [ ] `python3 -m unittest discover tools/llm_worker/tests` зелёный.
- [ ] `gate --dry-run` показывает thinking и max_tokens; один реальный `gate` по RR-0013 с flash проходит и сохраняет `reasoning_tokens`.
- [ ] Ledger не превышает лимиты; траты записаны.

## REVIEW
- [ ] Code review (reviewer)
- [ ] Второе ревью (codex)

## Результат
**Сделано (developer, 2026-11-14), не закоммичено.**
- `config.py`: `THINKING_MAX_TOKENS=65536`, `DEFAULT_REASONING_EFFORT`, `REASONING_EFFORTS`; комментарий про thinking.
- `provider.py`: параметры `thinking`/`effort`; при thinking тело `thinking: enabled` + `reasoning_effort`, без `temperature`; `Completion` получил `finish_reason`, `reasoning_chars`, `reasoning_tokens`; `reasoning_content` в разбор не идёт.
- `review.py`: `max_tokens_for`; повтор при пустом `content` и `finish_reason=stop` (один), иначе `schema_invalid`; в запись `thinking{...}`; `--dry-run` показывает thinking и max_tokens.
- `__main__.py`: `--thinking on|off`, `--effort` у `gate`, `review` (по умолчанию on) и `run` (по умолчанию off).
- README (раздел «Режим размышления»), тесты `tests/test_thinking_0052.py` (12 новых). `unittest discover`: 289+ тестов, зелёно.

**Замер реального вызова (flash, thinking high, RR-0013):** при max_tokens=32768 весь лимит ушёл на рассуждение (reasoning_tokens=32768, `finish_reason=length`, content пуст) -> `schema_invalid`, стоимость $0,0808 (ledger `gate:RR-0013`). Вывод: `max_tokens` покрывает рассуждение; значение поднято до 65536. Повторный реальный вызов не делался (разрешён был один) — критерий «gate проходит и сохраняет reasoning_tokens» ждёт одного повторного вызова. reasoning_tokens сохраняется в `runs/gate-RR-0013-*-invalid.json`.

**Лимит $0,50/задача:** худший случай резерва для pro на документе ~570 KB ≈ $0,64 (вход bytes/2 токенов + 65536 выхода) — ledger откажет `task_cap_exceeded`; для flash ≈ $0,17. Лимит не урезан, решение владельца: поднять `max_usd_task` для gate или уменьшить вход. Ledger total 0,595 из 5,00; 1 unsettled (старый).
