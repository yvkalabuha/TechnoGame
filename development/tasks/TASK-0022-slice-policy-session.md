---
id: TASK-0022
title: "T6a: система политики ИИ и сборка мира среза (Session)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0020, TASK-0021]
related: [HYP-0002]
approved_by: orchestrator
approved_at: 2026-11-03
approval_ref: "DECISIONS_LOG #123–#126; спека v0.12 §11"
created: 2026-11-03
---

# TASK-0022 — T6a: система политики ИИ и сборка мира среза (Session)

## Цель
Собрать мир среза «реактивная авиация» в одной точке (`Singularity.Session`, ADR-0014) и добавить систему политики ИИ без правил способности и изделия (T6a спеки v0.12). Закрыть часть DEBT-0016 (composition root, `Planned`/`PlannedVolume`, журнал команд, проверка повтора фокуса).

## Описание
- Сборка мира по `docs/architecture/SLICE_COMPOSITION.md` (решения [Предл.] приняты Orchestrator как локальные; П-1 — #126): сборка `Singularity.Session`, `SliceWorld`, `SliceSession` (`Submit`, `Step`, применение команд на границе тика, `commands.log`), порядок систем Knowledge → Industry → Products → Events → Policy.
- `PolicySystem` (A41): профили P1–P3 из потока `AI`, доли и `share_D`, `prio`/`j`, фаза инициализации A8; годовой ритм решений. Правила оснастки, учебной серии, программы качества, `Requirement`, запуска, лицензии и перебоя — **не** в этой задаче (T6b–T6d); на их месте нет заглушек, выдающих себя за правило.
- Потоки RNG §9.1, `FIX_*`, `START_PERT`/`START_SYM`, факторные ячейки §6.6, переключатели каналов §6.2.
- Журнал команд: колонки `cmd.*`, ключ `(apply_tick, source_class, source_id, local_n)`, проверка при `Submit` (повтор фокуса наладки, `force` не от Ai, сумма долей = 1). Решения ИИ — колонки `pol.*`, без поля `force`.
- `Planned`/`PlannedVolume` из `ProductsSystem`; убрать переходные значения по умолчанию в `IIndustryInputs`/`IKnowledgeInputs`.
- Golden 60 мес. перенести в `tests/Singularity.Session.Tests` (новый хеш, причина «composition root T6a» в сообщении коммита); удалить `ProductsLinkedRunTests` (V23).
- `IndustrySystem._lastRest` вне таблицы: проверить против ADR-0013 п.4; если нарушает — перенести в колонку.

## Критерии приёмки
- [ ] Всё из спеки v0.12 §11 «T6a» (ключи потоков, `FIX_ALL`, `FIX_A`, доли, `START_PERT`, `START_SYM`, номер ячейки `9u + 3g + s`, A8, переключатели §6.2).
- [ ] Общие пункты §11 T6a–T6d: `dotnet test` зелёный, ADR-0003 R1–R3 зелёный, нет `CounterRng` в `Core.Industry`/`Core.Products` и их адаптерах, L1 и L2 (threads 1/4/8, seeds 1…20) на сборке среза.
- [ ] Мир среза собирается только через Session (архитектурный тест); `SmokeT4s.LineStand` помечен как стенд одного слоя.
- [ ] Журнал команд пишется и читается одним кодеком; отказ `Submit` даёт причину, а не только счётчик.

## Тесты
Тесты по §11 T6a; архитектурные тесты сборок; golden-хеш Session; L1/L2.

## Связанные документы
`docs/simulation/slice-jet-aviation.md` v0.12 (§4, A8, A41, §6.2–§6.6, §9.1, §11); `docs/architecture/SLICE_COMPOSITION.md`; ADR-0004, ADR-0006, ADR-0013, ADR-0014; `.claude/rules/csharp-core.md`; DEBT-0016.

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
