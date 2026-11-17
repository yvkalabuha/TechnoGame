---
id: TASK-0061
title: "Долги перед заморозкой EXP-0002: DEBT-0030, 0034, 0035, 0036"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: tooling         # core | lab | data | tooling | docs | design
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [EXP-0002, TASK-0056]
approved_by: owner
approved_at: 2026-11-17
approval_ref: "AskUserQuestion 2026-11-17: 4 долга до заморозки"
created: 2026-11-17
---

# TASK-0061 — Долги перед заморозкой EXP-0002: DEBT-0030, 0034, 0035, 0036

## Цель
Закрыть долги DEBT-0030, DEBT-0034, DEBT-0035, DEBT-0036 (`development/TECH_DEBT.md`) до заморозки EXP-0002, чтобы заморозка фиксировала чистую сборку. Динамика модели и golden не меняются.

## Описание
- **Часть А (сейчас, параллельно с TASK-0056):**
  - DEBT-0030, DEBT-0035 — `tools/llm_worker`: блокировка при выборе `round`, mtime для старых записей; usage и статистика рассуждения всех попыток в записи; статистика рассуждения в `run --thinking on`; тест лимита gate (#226); сохранение сырого ответа пустой первой попытки; README (лимит review $0,50).
  - DEBT-0034, только тесты: ToyFunnel parity раскладки Table; отказ `BuildLk` при узле > max и L_K на 2 актора с узлом 1000 и разными δ; [Fact] для «licence track» и «switches» в `SliceModelTests`.
- **Часть Б (после того как simulation-engineer закончит TASK-0056, до заморозки):**
  - DEBT-0034, код: `SliceWorld.TablesDigest` с ActorCount/MaxNodeNumber; `JetSliceModel.ForcedStarts` — только новые записи лога.
  - DEBT-0036: проверки диапазонов `StandPoint` — через публичный helper Session или проверки в стенде; `slice-csv-format.md` §3/§3.1 (CONTROL_TABLES при LK/LC/LP; `K_DIV = 1` без `run.*`).

## Критерии приёмки
- [ ] Пункты долгов закрыты или явно записаны остатком.
- [ ] Golden и `golden_set_hash` калибровки (`0fe2bbe5…`) не меняются; если `TablesDigest` меняет хеш — остановиться и сообщить.
- [ ] `dotnet test` и Python-тесты llm_worker зелёные.

## Тесты
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
