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

### Часть А (черновик, developer)
- **DEBT-0030 закрыт:** `review.round_lock` (flock `runs/.review-<task>.lock`) охватывает выбор `round` и запись записи; старые записи без `round`/`created_at` в `report._review_rounds` упорядочиваются по mtime, затем по имени.
- **DEBT-0035 закрыт:** `review._deepseek_call` суммирует usage и статистику рассуждения всех попыток; сырое тело пустой первой попытки — `thinking.first_attempt_raw`; `run --thinking on` пишет `thinking` в `round_N/*_response.json` (`loop.py`); тест лимита gate (#226); README обновлён (лимит review $0,50, flock, mtime, поля).
- **DEBT-0034, тесты:** `ToyFunnelParityTests.TableLayout_MatchesPrototype` (A/B/C), `LkTablesTests` (отказ `BuildLk` при узле > max; L_K на 2 актора, узел 1000, разные δ). [Fact] «licence track» и «switches» в `SliceModelTests` уже были (строки 47, 51), пункт долга устарел.
- **Остаётся (часть Б):** `SliceWorld.TablesDigest` (ActorCount/MaxNodeNumber), `JetSliceModel.ForcedStarts`; DEBT-0036.
- Файлы: `tools/llm_worker/{review,report,loop}.py`, `README.md`, `tests/test_debt_0061.py` (новый), `tests/test_thinking_0052.py` (ожидание суммы попыток: 10 вместо 5); `tests/Singularity.Core.Tests/{Knowledge/LkTablesTests.cs (новый),ToyFunnel/ToyFunnelParityTests.cs}`.
- Проверка: Python 299 тестов OK; `dotnet test Singularity.Core.Tests` 1058 OK. src/ и golden не менялись.

