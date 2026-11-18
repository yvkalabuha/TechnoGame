---
id: ADR-0003
title: "Численная политика"
status: PROPOSED
date: 2026-11-02
supersedes:
superseded_by:
related: [RR-0002, ADR-0002, ADR-0012, ADR-0017, TASK-0009]
approved_by:
approved_at:
approval_ref:
---

# ADR-0003 — Численная политика

> После ACCEPTED текст не меняется. Изменение решения — новый ADR с `supersedes`.
> Ревизия 2 (systems-architect, 2026-11-18): текст приведён к коду и к решению #49. Ревизия 1 (2026-11-02, «поведенческие модели — `double`») и предложение R1–R4 от 2026-11-03 слиты в раздел «Решение». Готов к gate (пункт 0.1 плана MS-M1).

## Краткий вывод
- Состояние мира хранится только в целых числах: `Fx6` (`long`, масштаб 10⁶, округление floor, переполнение — исключение) и `int`/`long`.
- `double` в Core и Session запрещён полностью. В Kernel он разрешён в точном allowlist из 13 членов (`DetMath`, `Fx6.From/ToDouble`, `CounterRng.NextUnit`). Это правило R1–R3, принятое владельцем (#49).
- Вопрос FMA закрыт фактом: JIT не сжимает `a*b+c` на x64 и на ARM64 (TASK-0009, CI 2026-11-03). Код по R1–R3 от этого факта и не зависит.
- Для ACCEPTED осталось два решения владельца: единицы величин в `Fx6` и судьба исключений R4.

## Контекст
- [Факт] Float-вычисления могут различаться между платформами из-за FMA, порядка операций и платформенных `Math.Pow/Exp/Log`. RR-0002.
- [Факт] FMA меняет результат только у `a*b ± c` над `double`. `+`, `−`, `×`, `/`, `sqrt` по IEEE 754 корректно округляются (IEEE 754-2008 §5.4).
- [Факт, TASK-0009] `tests/Singularity.Kernel.Tests/FmaContractionCanaryTests.cs`: 6 тестов (таблица из 8 случаев, `NoInlining`, горячий цикл с OSR/tier-1, `static readonly`-константы, 4096 случаев из `CounterRng`). Зелёные на ubuntu x64, windows x64 и macos-latest ARM64 (CI b0b95cf, 2026-11-03). Тест входит в обычный `dotnet test` всех трёх ОС (`.github/workflows/ci.yml`), поэтому регрессия JIT будет поймана.
- [Факт, код] `Fx6` (`src/Singularity.Kernel/Fx6.cs`): `long Raw`, `Scale = 10⁶`, `+`/`−` с `checked`, `*` и `/` через `Int128` с floor-делением, `FromDouble = floor(fl(x·10⁶))` с отказом на NaN/∞. Диапазон ±9,22·10¹² единиц величины.
- [Факт, код] `DetMath` (`src/Singularity.Kernel/DetMath.cs`): `Sqrt`, `PowDyadic`, `PowFrac` (ADR-0012), Fx-обёртки для Core `PowFracFx`, `SmoothStep01`, `MulCeil`. `CounterRng.Chance(u, Fx6 p)`, `UniformInt`, `Index` — целочисленные.
- [Факт, код] Архитектурные тесты (`tests/Singularity.Architecture.Tests`):
  - `DeterminismRules.Check` для Kernel, Core, Session: нет `float`, `Half`, `MathF`, r4-опкодов; `System.Math` только `Sqrt, Floor, Abs, Min, Max, BigMul`; `double.*` только классификация, `Parse` с `IFormatProvider` и разрешённая математика; generic math (`IPowerFunctions<>` и др.) запрещена;
  - `CoreArchitectureTests.Core_OutsideR4Exceptions_HasNoDouble` и `SessionArchitectureTests.Session_FollowsDeterminismRules_AndHasNoDouble` — режим `forbidDouble` (поля, сигнатуры, локалы, вызываемые методы, r8-опкоды);
  - `KernelArchitectureTests.Kernel_UsesDoubleOnlyInR3AllowList` — точный список из 13 членов;
  - `R4Exceptions_AreNotStale` — исключение должно указывать на существующий код.
- [Факт, код] `double` в Core остался только в исключениях R4: `Singularity.Core.Science.ToyFunnel` (модель EXP-0001) и `Singularity.Core.Simulation` (`ParamSet.GetDouble`, `MetricFormat`). Хеши ToyFunnel закреплены в `ToyFunnelParityTests` и проходят на трёх ОС CI [Предп.: по последнему зелёному CI; отдельно не перепроверял].
- [Факт] Решение #49 (2026-11-03) приняло R1–R3 и R4 (ToyFunnel), но оставило ADR целиком в PROPOSED «до прогона ARM64 (TASK-0009)». TASK-0009 DONE в тот же день. Строка 1 `development/OPEN_QUESTIONS.md` («ARM64 — не проверено») устарела.

## Решение
1. **Состояние мира — только целые типы.** Запасы и потоки (деньги, энергия, население, ресурсы, выпуск) — `Fx6`. Счётчики, индексы, стадии, даты — `int`/`long`. Колонки таблиц — только `LongColumn`/`IntColumn` (ADR-0005); `Fx6` хранится как `Raw`.
2. **Арифметика `Fx6`.** Округление везде floor (к −∞). Переполнение — `OverflowException`, не тихий wrap. Произведение и частное считаются в `Int128`, переполниться может только результат. Где нужен потолок — явный `DetMath.MulCeil`.
3. **Единицы.** Один масштаб 10⁶ для всех величин; разную величину различает **единица**, а не масштаб. Единица каждой величины задаётся в спецификации слоя и в схеме данных так, чтобы сумма по миру за горизонт 1910–2100 укладывалась в 9,22·10¹² единиц с запасом ≥ 10×. [Предл.] Деньги — млн долларов 1990 г. (потолок ~9·10¹⁸ $, шаг 1 $), энергия — ТВт·ч, население — человек (потолок 9·10¹² чел.). Окончательный выбор — вопрос 1 ниже.
4. **R1. `double` в Core и Session запрещён полностью**: в полях, параметрах, возвратах, локалах, сигнатурах вызываемых методов, опкодах `ldc.r8`, `conv.r8`, `conv.r.un`, `ldind.r8`, `stind.r8`, `ldelem.r8`, `stelem.r8`. Следствие: в Core нельзя вызывать `CounterRng.NextUnit` и `ParamSet.GetDouble`.
5. **R2. Kernel даёт Core API на `Fx6`**: `DetMath.PowFracFx`, `SmoothStep01`, `MulCeil`, `CounterRng.Chance(u, Fx6)`. Новая функция для Core добавляется в Kernel с Fx6-сигнатурой.
6. **R3. `double` в Kernel — только в точном allowlist** (`KernelArchitectureTests.DoubleAllowList`). Внутри этих методов только корректно округляемые операции (`+ − × / sqrt`, `Math.Floor`), без `a*b ± c` над `double`. Новый член списка проходит ревью по этому правилу.
7. **R4. Исключения** — только пространства имён, перечисленные в `CoreArchitectureTests.DoubleExceptions` (сейчас `Singularity.Core.Science.ToyFunnel`, `Singularity.Core.Simulation`), без прав у вложенных пространств. Новое исключение — только через ADR.
8. **Запрещено в состоянии мира везде**: `float`, `Half`, `MathF`, `System.Random`, `Math.*` вне `Sqrt, Floor, Abs, Min, Max, BigMul`, generic math с некорректно округляемыми членами. Степени — только через ADR-0012 (`PowFrac`, показатель `q10` из данных).
9. **FMA.** Закреплённый факт: JIT .NET 10 не сжимает `a*b+c` на x64 и ARM64 (TASK-0009). Политика на него **не опирается**: в коде по R1–R3 нет `a*b ± c` над `double`. Канарейка остаётся в CI как сторож: если она упадёт после обновления runtime, это блокер релиза, а не повод ослабить тест.
10. **L3** (ADR-0002) этим ADR не заявляется. Входы для L3: зелёные CI на трёх ОС, golden-векторы `PowFrac`, закреплённый `DataHash` и закреплённые golden мира в тестах Session.

## Рассмотренные альтернативы
| # | Вариант | Плюсы | Минусы |
|---|---|---|---|
| A | Ревизия 1: поведенческие модели на `double`, запасы на fixed-point | модели писать проще | зависимость от FMA и от `Math.*`; правило «`double` только внутри вызовов» без анализа стека IL не проверить |
| **B** | **Всё состояние и модели Core на `Fx6`, `double` только в allowlist Kernel (выбран, #49)** | проверяется IL-тестом; независим от FMA; L3 достижим | Fx-обёртки, квантование 10⁻⁶, двойной круг `Fx6↔double` в `PowFracFx` (−1 raw) |
| C | Всё в `double` | проще всего | L3 практически недостижим, L2 зависит от JIT |
| D | Отдельный масштаб на каждую величину (`Fx2`, `Fx9`…) | точнее подогнать диапазон | больше типов и конверсий; та же цель достигается выбором единицы (п. 3) |
| E | Правило «`double` только внутри вызовов `DetMath`» с анализом стека IL | меньше обёрток | ~200 строк анализатора; опкоды `add`/`mul` нетипизированы |

## Последствия
- Плюсы: результат Core не зависит от FMA и платформенной математики; правило проверяется автоматически; срез M0 уже живёт по нему.
- Минусы: ошибка квантования 10⁻⁶ единицы на операцию (floor смещает вниз); `PowFracFx(x, 1024)` может вернуть `x − 1 raw`; запасы нельзя гонять через `double` туда-обратно (дрейф).
- Обязательно: единица величины записана в спецификации слоя до первого кода слоя; в модели E1 и провинций — проверка суммы по миру на запас диапазона (тест на худший сценарий).

## Открыто перед ACCEPTED (вопросы владельцу)
1. Единицы `Fx6` для денег, энергии и населения (п. 3) — принять предложение или задать свои.
2. Исключения R4: [Предл.] `ToyFunnel` перенести из Core в `Singularity.Lab` (модель EXP-0001 закрыта и в мир не входит), `Singularity.Core.Simulation` оставить исключением до замены `ParamSet.GetDouble` на `GetFx6Raw` у всех моделей. Альтернатива — оставить оба исключения как есть.

## Проверка
- `tests/Singularity.Architecture.Tests`: `Kernel_FollowsDeterminismRules`, `Kernel_UsesDoubleOnlyInR3AllowList`, `Core_FollowsDeterminismRules`, `Core_OutsideR4Exceptions_HasNoDouble`, `R4Exceptions_AreNotStale`, `Session_FollowsDeterminismRules_AndHasNoDouble`; отрицательные фикстуры — `DeterminismRulesTests.cs`.
- `tests/Singularity.Kernel.Tests/FmaContractionCanaryTests.cs` — на ubuntu x64, windows x64, macos ARM64 (CI).
- Golden-векторы `DetMathPowFracTests`, закреплённые `DataHashTests.CoreDataHash` и golden мира в `tests/Singularity.Session.Tests` — на трёх ОС CI.
- [Предл.] Тест диапазона: сценарий-максимум E1 (все провинции, 2100 г.) не ближе 10× к пределу `Fx6` ни в одной колонке — добавляется вместе с кодом E1.
