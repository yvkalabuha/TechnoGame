---
id: TASK-0160
title: "Intelligence v1 core: security budget share, targets and modes, theft channel in CEff, counterintelligence, disinformation, confidence of estimates (#515)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: gate
milestone: MS-M05
agent: developer         # ответственный агент
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0150]
related: []
approved_by: owner
approved_at: 2026-11-25
approval_ref: "#515, #259-#261, #364-#367, O-051"
created: 2026-11-25
---

# TASK-0160 — Intelligence v1 core: security budget share, targets and modes, theft channel in CEff, counterintelligence, disinformation, confidence of estimates (#515)

## Цель
Разведка v1 в ядре по `docs/game-design/INTELLIGENCE.md` ред. 3 (#515): седьмая доля бюджета «безопасность», ≤ 3 цели с режимом (наблюдать / красть / ложные следы), канал кражи только в `CEff` (O-051), рост сети и раскрытие (#260), контрразведка, проценты достоверности оценок, скрытые события через разведку (#508).

## Описание
За выключателем, по умолчанию выкл.; при выкл. — побитно как main. Седьмая доля меняет состав долей и формат — входит в пакет изменений, меняющих эталоны. ИИ по тем же правилам. Правила черновиков ADR М1: годовые нормы, без новых очередей `cmd.*`.

## Критерии приёмки
- [ ] Нейтральность при выключенном флаге (хеши main)
- [ ] Тесты: кража ≤ s_max, раскрытие детерминировано (RNG по ключу сети), L1/L2
- [ ] reviewer APPROVE, Codex

## Связанные документы
`docs/game-design/INTELLIGENCE.md`, `docs/reviews/intelligence-gate.md`, RR-0017

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
**Сделано** (за флагом `SliceRunConfig.Intelligence`, по умолчанию выкл.; при выкл. таблица, хеши и эталоны main — проверено прогоном с закреплёнными хешами main):
- Седьмая доля «безопасность»: `BudgetShares.Security` (0 при выкл.), работает с резервом и без него (остаток резерва = 1 − Σ семи долей). Доля ИИ — пресет «обычная» (0,02 бюджета), берётся сначала из статьи «качество» (в ядре не читается), остаток из закупок: сумма остаётся 1.
- ≤ 3 цели с режимом (наблюдать / красть / ложные следы) в `set_budget_shares` (поле `Targets`, колонки в семействе `cmd.shares.*`, новых `cmd.*`-семейств нет). План ИИ — те же правила, раз в 12 мес.: красть у тех, кто знает больше узлов (до трёх), иначе наблюдать за сильнейшим.
- `IntelSystem` (колонки `int.*`, один писатель): сеть `N`, зрелость `A`, след, потери; рост/спад годовыми нормами; раскрытие — поток `intel.expose` (RNG событий, ключ `(месяц, 7, o·2^16+t, 0)`), месячная вероятность выведена из годовой (потолок 25 %/год), потери `N·=0,25`, `A·=0,2`; контрразведка `ci_eff` режет рост чужой сети и поднимает её риск.
- Кража только в `CEff`: `c_money·(1 − max(KDiff·known, s_steal))`, `s_steal = min(s_max, σ·N·A)`, только по узлам, которые цель знает; мастерство не растёт (других записей нет).
- Оценки: диапазон узлов цели (`IntelEstimate`: `w = max(0,1; 1−conf)`, `conf = 1−(1−A)^s`), «ложные следы» портят источники наблюдателя (отброс при ≥ 2 честных, иначе сдвиг и флаг «данные искажены»); скрытое событие (лицензия цели, #508) видно при достоверности ≥ 0,3. В `ActorInputsView.Intel`: слова риска/следа/сети, диапазон «впереди на N…M узлов», проценты как производная от числа источников.
- Данные: `mods/core/intelligence.yaml` (kind `intelligence`, годовые нормы), вне `DataHash` (свой хеш, пишется в `run.intel.data`).
**Файлы:** src/Singularity.Core/Intelligence/*, Data/IntelligenceData.cs, Data/ModLoader.cs, GameData.cs, Knowledge/{KnowledgeMath,KnowledgeSystem,IKnowledgeInputs}.cs, Policy/PolicyProfile.cs, Commands/*, src/Singularity.Session/{Slice/*,Views/*,SliceSession.cs} (TestApiVersion 23), mods/core/{intelligence.yaml,mod.yaml}, tests/Singularity.Session.Tests/{IntelligenceTests.cs,ColumnProjection.cs,Api/*v23}, tests/Singularity.Core.Tests/Data/DataLoadDeterminismTests.cs.
**Проверено:** Session.Tests целиком (576 пройдено, 4 пропущено), Core.Tests (1147), Architecture.Tests (147) — последние два в копии дерева с правкой Lab (см. ниже). Мутации: без потолка `s_max` и с чужим ключом раскрытия соответствующие тесты падают.
**Не сделано / вне объёма:** источник прогресса в `NodeView` (§2.3); реакции жертвы на «много следов»; `turned`-сети (`intel.turn`), `taint`, переманивание (FUTURE); день раскрытия внутри месяца (`stable_hash`, ADR-0027 суточный тик ещё не в коде — бросок месячный); накопитель следа даёт слова, а не неизбежность раскрытия (Lab-проверка не проведена); `dir`-утечка кражи вне направления; `GDP_t` заменён долей бюджета. Числа в `intelligence.yaml` — плейсхолдеры (σ = 1,5, `ci_share_ref`, `hazard_base` подобраны, чтобы канал был ненулевым; Lab I-1…I-6 не запускались).
**Блокер вне зоны:** `src/Singularity.Lab/Models/Slice/JetSliceModel.cs`: `SurfaceVersion = 22` → `23` (иначе Lab не собирается: CS0162). Правка за simulation-engineer/Orchestrator.
