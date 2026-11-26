---
title: "Platform: model limits from llmlimits (#605, #606)"
status: ACCEPTED
date: 2026-11-26
---

# Лимиты моделей в платформе (llmlimits)

## Краткий вывод
Сервис llmlimits проекта devLLMex (`127.0.0.1:8790`, ADR-yk-0007 в /var/www/devLLMex/decisions/) каждые 5–30 минут снимает квоты Claude, Codex, Gemini (agy) и баланс DeepSeek и даёт прогноз «хватит ли до обновления». Платформа gameDev читает его через свой тонкий клиент и **только предупреждает** (#605): пропускает исчерпанного провайдера в цепочках llm_worker, показывает лимиты в capacity.py и в начале сессии (#606). Без сервиса всё работает как раньше.

## Факты (2026-11-26 19:24 UTC, `/api/v1/status`)
- **[Факт]** Claude (план max): окно 5 ч — 5 %, окно 7 дней — 84 %, обновление 2026-11-29 04:00 UTC; прогноз исчерпания 2026-11-27 11:40 UTC (`enough_until_reset: false`). Совет `advice` при этом `proceed`: порог warn — запас ≤ 10 %, предупреждение по прогнозу выключено в настройке devLLMex. Поэтому клиент читает `/status` (запас, `forecast`), а не только `recommendation`.
- **[Факт]** Codex (plus): 7 дней — 100 % до 2026-11-28 08:04 UTC. Память лимита llm_worker (`limitstate.py`, по тексту ошибки) держала срок 19:09 сегодня — неверно; сервис знает настоящий срок.
- **[Факт]** agy: группы «Claude and GPT Models» (agy-sonnet, второе ревью) 7 дн. 59,65 %, 5 ч 26,92 %; «Gemini Models» (визуальные вердикты Gemini) 7 дн. 1,11 %. `advice?provider=gemini` сливает группы — карту окон делаем сами.
- **[Факт]** DeepSeek: `ledger_cap` считает только расходы devLLMex (`deepseek_ask`); для gameDev значим `balance` (4,31 USD).

## Механизм
1. **Клиент** `tools/development/llm_limits.py` (stdlib): GET `/api/v1/status`, только loopback, таймаут 1,5 с, кэш 60 с в `~/.cache/singularity/llm-limits.json`; `stale`, `ok=false`, нет сервиса — «нет данных» (поведение как сейчас). `SINGULARITY_LLM_LIMITS=off` и подмена источника — для тестов.
2. **Карта** `tools/development/llm_limits.yaml`: потребитель → провайдер и окна: `claude` (я и агенты) → claude 5h/7d; `codex` → codex 5h/7d; `agy-sonnet` → gemini claude-and-gpt-models:5h/7d; `gemini-ui` → gemini gemini-models:5h/7d; `deepseek` → deepseek balance (резерв в USD).
3. **Состояние потребителя**: `out` (запас ≤ 0), `low` (≤ 10 %), `short` (прогноз: кончится раньше обновления), `ok`, `unknown`; плюс ближайшее обновление.
4. **llm_worker**: перед вызовом провайдера цепочки — `out` → пропуск с причиной `<provider>_limit_service` и сроком обновления; для Codex срок пишется и в `limitstate`.
5. **capacity.py**: строка лимитов и поле `limits` в `--json` (автопилот читает). Слоты агентов не меняются (#605).
6. **Начало сессии** (#606): одна строка в `session_context.py` из кэша клиента — после TASK-0209 (тот же файл).

## Чего нет
- Блокировок и автоматической смены моделей агентов (#605: только предупреждать).
- Правок devLLMex и цепочек `advice?role=` (это роли devLLMex).
