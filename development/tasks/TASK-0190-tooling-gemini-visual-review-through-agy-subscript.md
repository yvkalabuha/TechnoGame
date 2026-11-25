---
id: TASK-0190
title: "Tooling: Gemini visual review through agy (subscription) instead of the AI Studio key"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: developer
category: tooling
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: owner
approved_at: 2026-11-25
approval_ref: "owner message 2026-11-25: «мы не по ключу же работаем»"
created: 2026-11-25
---

# TASK-0190 — Tooling: Gemini visual review through agy (subscription) instead of the AI Studio key

## Цель
Второе мнение Gemini по кадрам (#441, #450) идёт через подписку agy, как 3D (#544), а не через ключ бесплатного уровня AI Studio с самодельным счётчиком 100/день.

## Описание
`tools/llm_worker visual` получает провайдера agy: `agy -p` с моделью из подписки (по умолчанию как в #544, проверить список), кадры из shots-out/ доступны через --add-dir (только чтение), --sandbox, без инструментов кроме чтения файлов, без --dangerously-skip-permissions; ответ в той же JSON-схеме (--json-schema или --output-format json). Те же проверки: секреты (gitleaks на тексте промпта), не больше 8 кадров, только shots-out/. Счётчик: свой осторожный предел для agy (как AGY_RPD в sketch.py), неудачные попытки 503/перегрузка не считаются. Провайдер по умолчанию — agy; ключ AI Studio — запасной (--provider key). Сбросить сегодняшний счётчик ключа не нужно.

## Критерии приёмки
- [ ] Живой прогон на одном наборе кадров (shots-out/TASK-0161-r2) через agy, запись в runs/
- [ ] Тесты провайдера с подставным agy; reviewer APPROVE
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
