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
