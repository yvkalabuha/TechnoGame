---
id: TASK-0076
title: "Localization fixes after Codex review of TASK-0074"
status: REVIEW         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0074, ADR-0018]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "light: Codex review of TASK-0074"
created: 2026-11-18
---


## Goal
Fix the Codex second-review findings on TASK-0074 (record tools/llm_worker/runs/review-TASK-0074-6514af88-codex.json; read it for detail).

1. **HIGH** Numbers.cs:151 — number and date formatting bypasses the English fallback for incomplete locales. NUMBER/DATE must use the same lang → en fallback chain as messages, for separators, month names and patterns.
2. **MEDIUM** LocaleCatalog.cs:82 — third-party locale validation and stale-translation warnings never run at runtime. Run the per-mod validation (L-checks applicable to mods) and staleness warnings at load, as warnings for non-core mods per ADR-0018.
3. **MEDIUM** AstUtil.cs:50 — L3 misses variables and functions used only in selectors. Walk selector expressions as well.
4. **MEDIUM** tests/.../FontCmap.cs:50 — the glyph coverage test can report missing glyphs as present. Fix the cmap parsing (format 4/12, segment end handling) and add a negative test with a known-missing glyph.

## Acceptance
- A test for each finding: it fails before the fix and passes after.
- The full `dotnet test` is green.

## Результат
Исправлены все 4 замечания Codex. Полный `dotnet test` зелёный (Localization 127, Core 1060, Session 353, Lab 20, Kernel 322, Architecture 101, Audio 54).

1. **HIGH** `Numbers.Msg` теперь идёт по цепочке язык → en → маркер. Связь «bundle → en-bundle» ставит `BundleFactory.Build` через `Numbers.SetFallback`.
2. **MEDIUM** `LocaleCatalog.Load(GameData, Action<string>? warn)` (и перегрузка со списком источников + `GameData`) пропускает сирот (L2) и шлёт предупреждения: L1, L2, L3 и устаревшие переводы. Синтаксис и грамматика по-прежнему бросают исключение. `TestKit.Catalog` теперь грузит без удаления сирот, чтобы тесты ядра видели их.
3. **MEDIUM** `AstUtil.CollectUsage` обходит селекторы. Переменная `$case` не учитывается: её задаёт NAME.
4. **MEDIUM** `FontCmap`: формат 4 считает `idDelta`/`idRangeOffset`/glyphIdArray, формат 12 учитывает `startGlyph`. Глиф 0 (.notdef) считается отсутствующим.

Файлы: `src/Singularity.Localization/{Numbers,BundleFactory,LocaleCatalog,LocaleChecks,AstUtil}.cs`, `tests/Singularity.Localization.Tests/{FontCmap,TestKit,CodexFixTests}.cs`.

Проверка: 5 регрессионных тестов в `CodexFixTests.cs`. Без правки Numbers/AstUtil/BundleFactory падают 3 теста (формат чисел, дата, L3). Без правки FontCmap падает тест .notdef. Тест runtime-Load не компилируется без нового API.

### Раунд 2 (Codex re-review, record review-TASK-0076-2e1127fa-codex.json)
Полный `dotnet test` зелёный (Localization 131). Без правки src падают 4 новых теста.
1. **HIGH** Запасной вариант даты по частям: `BundleFactory.Build` добавляет в bundle языка недостающие сообщения `fmt-*` и `date-*` из EN. Шаблон и название месяца (по падежам) откатываются независимо. CWT-механизм `Numbers.SetFallback` убран как лишний. Тест `Date_PatternAndMonthName_FallBackIndependently`.
2. **MEDIUM** `LocaleCatalog.Load(..., warn)` теперь запускает L4 и L6: `Loc` строится только для проверки (`LocaleChecks.Plurals`, `Budgets`). Тест `RuntimeLoad_RunsPluralAndLengthChecks`.
3. **MEDIUM** Предупреждающий `warn` обязателен, перегрузки `Load(GameData)` и необязательного `warn` нет. Тест `RuntimeLoad_HasNoSilentDefault`. Тесты без проверок используют `Load(ModLocaleSource.From(data))`.
4. **MEDIUM** `$case` исключается из L3 только в термах (`CollectUsage(..., inTerm)`). Тест `L3_CaseVariableOfAMessage_IsNotExempt`.
