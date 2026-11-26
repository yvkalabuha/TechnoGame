---
id: TASK-0191
title: "Tooling: llm_worker research — DeepSeek checks claims against fetched source pages (#577)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: developer
category: tooling
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: owner
approved_at: 2026-11-25
approval_ref: "#577"
review_ref: "reviewer: APPROVE (re-review on main after landing); codex 3 returns, waived by #432, DEBT-0114; merge c1b9100"
created: 2026-11-25
---

# TASK-0191 — Tooling: llm_worker research — DeepSeek checks claims against fetched source pages (#577)

## Цель
Дешёвая проверка и сбор фактов через DeepSeek (#577): у DeepSeek API нет доступа к сети, поэтому инструмент сам скачивает страницы, а DeepSeek читает их текст.

## Описание
Новая команда `python3 -m tools.llm_worker research`: вход — YAML с утверждениями (id, текст, год/число, URL-кандидаты) или с вопросами для сбора (поле, список URL). Инструмент скачивает страницы (curl, только http/https, лимит размера, кэш вне репо, без куки), чистит в текст, gitleaks на тексте, отправляет DeepSeek (профиль deepseek-flash, учёт трат в ledger, JSON-схема): по каждому утверждению — confirmed / contradicted / not_found, цитата ≤ 30 слов из страницы, исправленное значение. Проверка цитаты: инструмент ищет цитату в тексте страницы; не найдена — вердикт not_found. Отчёт в runs/ и Markdown-таблица для записки. Пример использования — 4 неподтверждённых узла TASK-0188 (31, 32, 33 и мартенситная сталь).

## Критерии приёмки
- [ ] Тесты с подставным провайдером и подставными страницами; проверка цитат
- [ ] Живой прогон на примере; reviewer APPROVE
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
Сделано: команда `python3 -m tools.llm_worker research --input FILE.yaml [--profile deepseek-flash] [--dry-run]`. Вход: `items` с `id`, `claim` или `question`, `urls` (до 4). Скачивание curl (только http/https, публичные адреса, лимит 3 МБ, без куки, кэш `~/.cache/singularity/research`), текст из HTML, gitleaks на тексте страницы, один вызов deepseek-flash на пункт через ledger, схема `schemas/research.schema.json`. Цитата ≤ 30 слов ищется в тексте указанной страницы; не найдена — вердикт not_found. Запись и таблица Markdown — в `runs/` (текст страниц не сохраняется).

Живой прогон (`examples/research-tree-575.yaml`, flash): мартенситная сталь — подтверждено «late 1950s» (Wikipedia); HIP — страница даёт только «1950s» (Battelle), год 1955 не подтверждён; вердикт contradicted лишь по расхождению точности, не по факту; углерод-углеродный композит и TBC — not_found на Wikipedia (годов нет); первичные источники (NTRS, патенты) нужны для узлов 31 и 33.

Файлы: tools/llm_worker/research.py, schemas/research.schema.json, __main__.py, tests/test_research.py, examples/research-tree-575.yaml. Проверка: 16 новых тестов проходят; полный unittest llm_worker см. отчёт.
