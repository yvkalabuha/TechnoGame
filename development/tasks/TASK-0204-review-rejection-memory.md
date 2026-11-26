---
id: TASK-0204
title: "Rejection memory: rejected review findings and their reasons are fed back to Codex, DeepSeek and agents so they do not repeat (#591)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: tooling                # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: owner
approved_at: 2026-11-26
approval_ref: "DECISIONS_LOG #591"
created: 2026-11-26
---

# TASK-0204 — Rejection memory: rejected review findings and their reasons are fed back to Codex, DeepSeek and agents so they do not repeat (#591)

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
#591: внешние ревьюеры (Codex, DeepSeek) и агенты не повторяют уже отклонённые находки.

## Объём (tools/llm_worker, tools/development)
1. Журнал отклонений `development/review-rejections.yaml`: при `llm_worker review-confirm --rejected` и при отклонении находки ворот (gate) — запись: провайдер, файл/область, суть находки, причина отклонения, ссылка на решение (ADR, #NNN), дата.
2. При новом ревью (review, gate, research) в промпт добавляется короткий блок «Ранее отклонено в этих файлах/темах и почему» — только записи, относящиеся к файлам диффа или теме, не больше N строк; gitleaks-проверка как сейчас.
3. Для агентов Claude: `tools/development/rejections.py for <paths|TASK>` печатает тот же блок; /dispatch и /recheck вставляют его в промпт.
4. Метрика: доля находок, совпадающих с отклонёнными (по ключу), до/после — в errors stats.
5. Тесты; verify.py --full.
