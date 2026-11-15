---
id: TASK-0055
title: "Session v4: параметры калибровки «К» и таблицы L_C/L_P в SliceRunConfig (блокер T12)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: developer
category: core
priority: high
needs_owner: false
depends_on: [TASK-0054]
related: [HYP-0002, EXP-0002, TASK-0034]
approved_by: orchestrator
approved_at: 2026-11-15
approval_ref: "ADR-0014 п.5 (Session отдаёт то, что нужно Lab); TASK-0054 «Блокирует T12»; спека среза §8.2, §6.1"
created: 2026-11-15
---

# TASK-0055 — Session v4: параметры калибровки и таблицы L_C/L_P

## Цель
Сделать точки калибровочной сетки T12 исполнимыми: Session принимает параметры класса «К» и таблицы контролей L_C/L_P через `SliceRunConfig`, не меняя поведения при значениях по умолчанию.

## Описание
По TASK-0054 («Блокирует T12»):
1. `SliceRunConfig` получает параметры «К» спеки среза §8.2: делитель `k` для `CostByType` (`c_n`-множитель, #177), `ThetaStart`, `HMax`, `KappaQ10`, `Alpha`, `XHalfByTier`, `DeltaH`, `CM` — по умолчанию текущие значения §8.1. Найти, где каждое значение сейчас задаётся (данные `mods/core` или константы Core), и передать через конфигурацию без дублирования логики.
2. Таблицы L_C/L_P: `ImmutableArray<CapabilityCurve>` (кривые `Y_k`/`U_k`), `T_design`, `ρ` — для `IndustryMode.CurveLc` и `ProductsMode.CanonicalLp` (уже есть в Core или нет — проверить; если режимов нет в Core, остановиться и описать).
3. `TestApiVersion` → 4, закреплённый `session-surface.v4.txt`; колонки `run.*` в Lab — только значения не по умолчанию.
4. Lab (`SliceParameters`, `ControlTables`): приём параметров сетки и `LAYER = LC/LP` из файла `CONTROL_TABLES` (формат v0.5 r8 §3.1).
5. Тесты: значения по умолчанию дают golden main без изменений (L1/L2); каждый параметр «К» меняет траекторию; L_C/L_P-прогон по таблицам `t11-calib` проходит; отказ при неверной таблице.
6. Закрыть `[Fact]`-строки «slice: control tables», «slice: design capacity», «slice: licence track», «slice: switches» в `tests/Singularity.Lab.Tests/SliceModelTests.cs` (DEBT-0034, часть Lab).

## Правила
- Golden main и поведение по умолчанию не меняются. Если меняются — остановиться и описать.
- Правила кода — `.claude/rules/csharp-core.md`; детерминизм — ADR-0002.

## Критерии приёмки
- [ ] Все точки черновика `lab/manifests/EXP-0002-calib.json` исполнимы (малый прогон по одной точке каждого этапа).
- [ ] Полный `dotnet test` зелёный, golden без изменений; L1/L2.
- [ ] Второе ревью Codex — accept.

## REVIEW
- [ ] Code review (reviewer)
- [ ] Второе ревью (codex)
- [ ] Детерминизм (test-engineer)

## Результат
