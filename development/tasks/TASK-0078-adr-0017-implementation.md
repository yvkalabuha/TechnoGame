---
id: TASK-0078
title: "ADR-0017 implementation: StateHash schedule, lazy LastHash, column CoW"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0017, ADR-0016, ADR-0014, ADR-0013, ADR-0006, EXP-0007]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "ADR-0017 ACCEPTED (#317); light task inside approved ADR"
created: 2026-11-18
---

# TASK-0078 — ADR-0017 implementation: StateHash schedule, lazy LastHash, column CoW

## Цель
Реализовать ADR-0017 (ACCEPTED, #317) в Kernel и Session: хеш по расписанию, ленивый `LastHash`, CoW по колонкам.

## Описание
Вход: `docs/decisions/ADR-0017-statehash-schedule-and-table-copy-in-daily-mode.md` (раздел «Решение», п. 1–15), `docs/architecture/notes/daily-hash-and-view.md`, `.claude/rules/csharp-core.md`.

1. **Расписание хеша** (п. 2–5): обязательные точки (старт, save, load, конец `Replay`); execution-only флаги `SliceRunConfig.HashCheckpoints` (по умолчанию `true`) и `HashEveryTick` (по умолчанию `false`, включает checkpoints). Клиент явно ставит `HashCheckpoints = false` (`src/Singularity.Client.Spike`, одна строка конфигурации).
2. **`Table.Version` и ленивый `LastHash`** (п. 6–8): кэш `(version, hash)`; проверка потока-владельца; `SliceView.StateHash` становится `string?`; `TestApiVersion` +1; `HashComputations` считает только реальные вычисления.
3. **CoW по колонкам** (п. 10–13): открытие `Next` при первом касании (volatile + lock + двойная проверка), `Swap` только открытых колонок в порядке регистрации, флаг «в тике» в `TickRunner`, `Next` вне тика бросает исключение. Код сборки мира, который пишет `Next`, перевести на `Load` (14 файлов обращаются к `.Next`). Режим полной копии остаётся тестовым переключателем Kernel.
4. **Горячие циклы**: span берётся один раз на чанк (п. 14).
5. **`HashAssert.Equal(string?, string?)`** и перевод всех сравнений хешей в тестах на него.

Правило `csharp-core.md` (п. 16) вносит Orchestrator после ревью.

## Критерии приёмки
- [ ] Проверки ADR-0017 «Проверка» п. 1–12 реализованы тестами и проходят.
- [ ] Golden-хеши и `lab selftest` проходят без изменения констант.
- [ ] `dotnet test Singularity.sln` зелёный; `dotnet build src/Singularity.Client.Spike` собирается.
- [ ] Формат сохранения и DataHash не меняются.

## Тесты
ADR-0017 «Проверка» п. 1–12; L2 (threads 1/4/8/16 × ChunkSize 1/8/32). Бюджет (п. 13, R1/R2/R4) замеряется в EXP-0007 (TASK-0066).

## Связанные документы
ADR-0017, ADR-0016, ADR-0014, ADR-0013, ADR-0006; `docs/simulation/tick-profile-m0.md`.

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2

## Результат
