---
id: TASK-0143
title: "Gemini sketch runs: detect subagent quota errors, one image per subagent, no extra refinements"
status: APPROVED
level: light
milestone: MS-M05
agent: developer
category: tools
priority: medium
needs_owner: false
depends_on: []
related: [TASK-0141]
approved_by: orchestrator
approved_at: 2026-11-24
approval_ref: "owner asked in chat 2026-11-24 to investigate fewer images; light task approved by Orchestrator"
created: 2026-11-24
---

# TASK-0143 — Почему Gemini рисует меньше эскизов, чем просили

## Цель
Владелец попросил выяснить, почему агент Gemini иногда рисует меньше картинок. Причина найдена (ниже); правка в `tools/llm_worker/sketch.py`.

## Причина [Факт: transcript.jsonl прогонов c84d4536, 5802b6fe, ae58bd5d]
- agy на `--count N` запускает N подагентов (`invoke_subagent`) одновременно; каждый вызывает `generate_image`.
- Квота генератора картинок подписки мала: в одном окне прошло около 13 картинок (10:44–11:05), дальше `ResourceExhausted, HTTP 429 … quota will reset after 4h40m`. Окно около 5 ч.
- Подагенты получают 429, сообщают родителю и не повторяют; родитель отдаёт то, что есть. Инструмент видит 429 только в потоке главного агента, поэтому запись — `SUCCESS`, exit 0 (или partial), а не `agy_quota`.
- Квота тратится впустую: подагенты делают «уточнённые» версии (`_v2`, `_refined`, `_final`) без запроса — прогон с `--count 2` дал 6 картинок.

## Описание
1. Искать 429/`RESOURCE_EXHAUSTED` в transcript всех подагентов; при нехватке картинок из-за квоты — статус `agy_quota` (код 4), время сброса в записи и в выводе.
2. Промпт: ровно один вызов `generate_image` на подагента, без уточнений и повторов; брать по одной картинке на подагента.
3. По умолчанию подагенты по очереди (флаг `--parallel` для параллели).
4. Вывод: «получено K из N, причина: квота, сброс через …».

## Результат
Пункты 1-4 сделаны в `tools/llm_worker/sketch.py`, `__main__.py` (флаг `--parallel`), README. 429 ищется в transcript.jsonl всех подагентов; при нехватке картинок запись получает `quota` (reset_seconds, reset_at) и `stop_reason: agy_quota`, вывод «got K of N, reason: quota, reset in ...», код 4; без картинок — отказ `agy_quota`. Промпт: один вызов generate_image на подагента, без уточнений, по очереди (по умолчанию). Тесты: `tools/llm_worker/tests/test_sketch.py`, фикстура `tools/llm_worker/fixtures/agy_subagent_quota_transcript.jsonl` (по форме реальных transcript 29c55f30 / 0412cc81, обезличена).

## Критерии приёмки
- [ ] Unit-тесты: разбор 429 в transcript подагента, одна картинка на подагента, время сброса.
- [ ] Ревьюер APPROVE; `verify.py --full`.
