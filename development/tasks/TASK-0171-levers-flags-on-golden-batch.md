---
id: TASK-0171
title: "Golden batch: turn on Reserve, Institutes, LicenceMarket, Adviser, Intelligence by default; re-record goldens once with explanation"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: core           # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: high           # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0150, TASK-0151, TASK-0152, TASK-0153, TASK-0160, TASK-0173, TASK-0174, TASK-0175]
related: []
approved_by: orchestrator # owner | orchestrator (orchestrator — только level: light)
approved_at: 2026-11-25
approval_ref: "#474 one build for the owner; handoff 2026-11-25-2 «Не забыть»"
created: 2026-11-25
---

# TASK-0171 — Golden batch: turn on Reserve, Institutes, LicenceMarket, Adviser, Intelligence by default; re-record goldens once with explanation

## Цель
Одна сборка для партии владельца (#474): механики волны рычагов 1 работают по умолчанию.

## Описание
Включить по умолчанию флаги Reserve, Institutes, LicenceMarket, Adviser (для страны игрока), Intelligence в `SliceRunConfig` по умолчанию для клиента и среза. Один пакет изменений, меняющих эталоны (orchestration §5): перезаписать эталоны один раз и объяснить каждую перезапись. Проверить Lab-замер TASK-0148 на включённых флагах (калибровка годов 1945–1977, допуски) — если допуски не проходят, остановиться и доложить.

## Критерии приёмки
- [ ] Флаги включены по умолчанию; режим «всё выключено» остаётся доступным для тестов
- [ ] Эталоны перезаписаны один раз, у каждой перезаписи объяснение
- [ ] Калибровка годов в допусках (или остановка с отчётом)
- [ ] reviewer APPROVE, второе ревью

## Тесты
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
