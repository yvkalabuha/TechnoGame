---
id: TASK-0078
title: "ADR-0017 implementation: StateHash schedule, lazy LastHash, column CoW"
status: REVIEW         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
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

Реализовано по ADR-0017 (ветка `worktree-agent-a020198317cc94409`).

**Kernel.** `Column<T>.Next` открывается при первом касании в тике (volatile-чтение, `lock` + двойная проверка + `CopyTo`); `Table.Swap` меняет только открытые колонки в порядке регистрации и поднимает `Table.Version`; `Column.Load` тоже поднимает версию. `TickRunner` ставит флаг «в тике» (`BeginTick`/`EndTick`, закрытие флагов при тике, прерванном исключением); `Next` вне тика бросает `InvalidOperationException`. Режим полной копии оставлен как `Table.FullCopyMode` (тестовый переключатель, по таблице, а не статический).

**Session.** `SliceRunConfig.HashCheckpoints` (по умолчанию `true`) и `HashEveryTick` (`false`); `LastHash` ленивый, кэш `(version, hash)`, проверка потока-владельца; `HashComputations` считает только реальные вычисления; `SliceView.StateHash` — `string?` (только посчитанный хеш тика); конец `Replay` хеширует; `TestApiVersion` 8 → 9 (`session-surface.v9.txt`, `JetSliceModel.SurfaceVersion` = 9). Клиент (`SimHost`) ставит `HashCheckpoints = false`. `SliceObserver` требует хеш на годовых точках.

**Тесты.** `tests/Shared/HashAssert.cs` (подключён через `tests/Directory.Build.props`), все 98 сравнений хешей переведены на него; `HashAssertUsageTests` (Architecture) сканирует исходники тестов; `CopyOnWriteTests` (Kernel: гонка t16/chunk 1 × 20, CoW = полная копия, версия, прерванный тик), `HashScheduleTests` (Session: проверки 1–3, 5, 6, 8, 10), правки `ColumnBufferTests`, `DailyTickProbeTests`, `ViewTests`, `ApiSurfaceTests`. Итог: Kernel 330 (+1 skip), Session 362 (+2 skip), Core 1060, Lab 20, Architecture 103, Localization 131, Client.Audio 69 — все зелёные. `lab selftest`: 48/48, golden без изменения констант; `dotnet build src/Singularity.Client.Spike` собирается. Формат сохранения и DataHash не менялись.

**Отступления от ADR.**
1. Код сборки мира, писавший `.Next` вне тика, не найден: все обращения к `.Next` в Core/Lab — внутри compute/merge, мир строится через `Load`; переносить было нечего (тесты на старом коде прошли после включения проверки).
2. Save и load в Session отсутствуют (нет save-пути), поэтому обязательные точки «save/load» не реализованы; проверка п. 10 покрыта через `Replay` и хеш заголовка не затронут.
3. Span один раз на чанк (п. 14) в горячих циклах Core не сделан: массивы колонок (`_y[r].Next[s]` и т. п.) требуют массового переписывания; по ADR это второй шаг после замера R2(г) в EXP-0007.
4. Проверки (24, 180) выполнены на мире `jet_slice_1945` по умолчанию и на синтетике Lab selftest (24, 180), без отдельного (24, 180) в новых тестах.
