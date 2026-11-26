---
id: TASK-0211
title: "Model limits from llmlimits: client, consumer map, llm_worker skips exhausted providers, capacity.py shows limits; session line after TASK-0209 (#605, #606)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: tooling          # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: high         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: owner
approved_at: 2026-11-26
approval_ref: "DECISIONS_LOG #605, #606"
created: 2026-11-26
tags: [task, external-llm, platform, active]
---

# TASK-0211 — Model limits from llmlimits: client, consumer map, llm_worker skips exhausted providers, capacity.py shows limits; session line after TASK-0209 (#605, #606)

## Цель

## Описание

## Критерии приёмки
- [ ]

## Тесты
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->

## Цель
#605, #606: платформа знает лимиты моделей из llmlimits и не тратит время на исчерпанных провайдеров. Дизайн: docs/architecture/platform-llmlimits.md.

## Объём
Часть 1 (сейчас, параллельно с TASK-0209; не трогать land.py, .claude/, kb_*):
1. tools/development/llm_limits.py — клиент /api/v1/status (loopback, 1,5 с, кэш 60 с, fail-open), CLI: сводка, --json, `check <consumer>`.
2. tools/development/llm_limits.yaml — карта потребителей (см. дизайн §Механизм 2).
3. Состояния out/low/short/ok/unknown по запасу и forecast.enough_until_reset.
4. tools/llm_worker: пропуск провайдера в состоянии out (codex, agy-sonnet, gemini-ui, deepseek) с причиной и сроком; для Codex — запись срока в limitstate.
5. capacity.py: строка лимитов, поле limits в --json; слоты не менять.
6. Тесты с подменой источника; без сервиса и в CI — прежнее поведение.
Часть 2 (после посадки TASK-0209): строка в .claude/hooks/session_context.py по #606.

## Критерии приёмки
- Без сервиса (SINGULARITY_LLM_LIMITS=off или порт закрыт) все тесты и поведение как до задачи.
- С подменой «codex out до T» второе ревью сразу идёт на следующего провайдера, в записи причина и T.
- capacity.py печатает строку лимитов не дольше чем за 2 с при недоступном сервисе.

## Результат части 1 (2026-11-26)
- 44a6b729, 240a35f0: llm_limits.py/.yaml, capacity.py (строка limits, поле limits в --json), llm_worker review пропускает провайдера в состоянии out; тесты llm_worker 625 OK.
- reviewer: CHANGES (44a6b729: bad payload medium, 2 low) → APPROVE (240a35f0). Второе ревью: agy-sonnet (claude-sonnet-5-5-medium) accept, 2 findings, record review-TASK-0211-240a35f0-agy-sonnet.json.
- Влито 75c8f9f. Часть 2 (строка сессии, #606) — после TASK-0209.

## Результат части 2 (2026-11-26)
- .claude/hooks/session_context.py: одна строка «Лимиты: …» (#606) из `llm_limits.py --session` (подпроцесс, таймаут 1,8 с, без sys.path); только потребители out/low/short с обновлением в UTC, иначе «всё в норме»; без строки, если все unknown.
- DEBT-0126: llm_limits.status запоминает неудачный запрос на 60 с (FAIL_TTL_S); строка журнала review.py упрощена.
- Тесты: test_llm_limits (SessionLineTest, test_failed_fetch_is_remembered_for_a_minute), test_hooks SessionContextTest (2 новых); без изменений падают (проверено).
