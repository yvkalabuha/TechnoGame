---
id: ADR-0003
title: "Численная политика"
status: PROPOSED
date: 2026-11-02
supersedes:
superseded_by:
related: [RR-0002, ADR-0002]
approved_by:
approved_at:
approval_ref:
---

# ADR-0003 — Численная политика

## Контекст
Float-вычисления могут давать разные результаты из-за FMA, порядка операций и платформенных математических функций. RR-0002.

## Решение (предложение)
- Деньги, запасы, население — `long` fixed-point (масштаб задаётся в коде одной константой на величину).
- Поведенческие модели — `double` без platform-specific функций.
- Не использовать `Math.Sin/Exp/Log/Pow` в состоянии мира: свои детерминированные реализации в `Singularity.Kernel`.
- Запрет `MathF`, `float` в состоянии мира.

## Открыто перед ACCEPTED
- Проверить, сжимает ли .NET JIT `a*b+c` в FMA на x64 и ARM64, и как это отключить. Без ответа решение не утверждается.

## Предложение: сужение блокера FMA (systems-architect, 2026-11-03)
Повод: срез «реактивная авиация» (`docs/simulation/slice-jet-aviation.md` §9.2, §9.4), LAYER_CONTRACT §13. Решения ещё нет, ждёт владельца.

Факты:
- [Факт] FMA меняет результат только у `a*b ± c` над `double`. `sqrt`, `×` и `/` по IEEE 754 корректно округляются, их результат не зависит от того, сжимает ли JIT выражения (IEEE 754-2008 §5.4).
- [Факт, код] `DetMath.Sqrt/PowDyadic/PowInt` — только `sqrt`, `×`, одно `/`. Для `PowFrac` это же требование задаёт ADR-0012. `Fx6.FromDouble` — одно `×` и `Math.Floor`. `Fx6.ToDouble` — одно `/`. `Fx6 *`, `/` считаются в `Int128`.
- [Факт, код] В Core `double` с `a*b+c` и `CounterRng.NextUnit` уже есть в `Singularity.Core.Science.ToyFunnel` (EXP-0001). Ещё `double` есть в `Singularity.Core.Simulation`: `ParamSet.GetDouble`, `MetricFormat.Real`.
- [Факт] Без анализа стека IL нельзя проверить правило «`double`-арифметика только внутри вызовов `DetMath`»: опкоды `add`/`mul` нетипизированы, а Release-компилятор передаёт `double` между вызовами через стек, без локальных переменных.

[Предл.] Правило «`double` только в Kernel»:
1. **R1.** В пространствах имён Core, кроме исключений из R4, тип `double` запрещён полностью: в полях, параметрах, возвратах, локальных переменных и сигнатурах вызываемых методов. Запрещены и опкоды `ldc.r8`, `conv.r8`, `conv.r.un`, `ldind.r8`, `stind.r8`, `ldelem.r8`, `stelem.r8`. Проверку делает тот же механизм `DeterminismRules`, что уже ловит `float`. Как следствие, в Core запрещены `CounterRng.NextUnit` и `ParamSet.GetDouble`.
2. **R2.** Kernel даёт API на `Fx6`, без `double` в сигнатуре: `DetMath.PowFracFx(Fx6 x, int q10) -> Fx6` (внутри `FromDouble(PowFrac(x.ToDouble(), q10))`) и целочисленную проверку вероятности по `NextU64`: `(NextU64 >> 11) < ⌊p_raw·2⁵³/10⁶⌋` в `Int128`.
3. **R3.** В Kernel `double` разрешён только в `DetMath`, `Fx6.FromDouble/ToDouble` и `CounterRng.NextUnit` (последний оставлен для исключений R4). Архитектурный тест сверяет фактический список методов с `double` в сигнатуре или IL с точным allowlist: новый метод роняет тест. Отсутствие `a*b±c` внутри этих методов IL-тест не проверит, его проверяют ревью и golden-векторы ADR-0012.
4. **R4.** Исключения перечислены в тесте явно: `Singularity.Core.Science.ToyFunnel` и `Singularity.Core.Simulation`. Код в них остаётся под открытым вопросом FMA. Новое исключение — только через ADR.

Проверка: `tests/Singularity.Architecture.Tests/DeterminismRules.cs` получает режим `forbidDouble`. Тест Core применяет его ко всем типам вне R4. Отрицательные фикстуры в `DeterminismRulesTests.cs`: класс с `double`-локалом, вызов `NextUnit`, вызов `ParamSet.GetDouble`. Отдельный тест — Kernel-allowlist из R3.

Что это даёт [Предл.]:
- **L1/L2 (ADR-0002).** Код по R1–R3 не содержит `a*b±c` над `double`, поэтому FMA и tiering JIT на него не влияют. Срез можно реализовывать до прогона TASK-0009 на ARM64. Условия: владелец принял правило, задачи среза APPROVED, тест R1–R3 зелёный.
- **Остаётся заблокированным:**
  - ACCEPTED самого ADR-0003, в том числе пункт «поведенческие модели — `double`»;
  - любой Core-код с `double`-арифметикой (исключения R4, будущие поведенческие модели);
  - заявления о L3. Для L3 нужны зелёные CI на macos ARM64: canary TASK-0009, golden-векторы `PowFrac` и хеш скомпилированных данных (ADR-0012). L3 отложен (ADR-0002), поэтому для M0 это не блокер.

Альтернативы: ждать ACCEPTED ADR-0003 (срез стоит); правило «`double` только внутри вызовов» с анализом стека IL (сложно, ~200 строк); явный `Math.FusedMultiplyAdd`/запрет сжатия флагом JIT (такого флага в .NET нет [Предп.]).

## Рассмотренные альтернативы
- Всё в fixed-point: надёжнее для L3, но сложнее писать модели.
- Всё в double: проще, но L3 практически недостижим.

## Проверка
Правило `csharp-core.md`; analyzer или тест, запрещающий `System.Math` в сборках ядра (кроме разрешённых методов).
