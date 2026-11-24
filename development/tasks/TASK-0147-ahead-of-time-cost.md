---
id: TASK-0147
title: "Ahead-of-time cost for knowledge nodes in Core (#458) and G5/G6 rev 3.1 data on the train-2 branch"
status: APPROVED
level: light
milestone: MS-M05
agent: developer
category: core
priority: medium
needs_owner: false
depends_on: []
related: [TASK-0128, TASK-0133, TASK-0134]
approved_by: orchestrator
approved_at: 2026-11-24
approval_ref: "owner decisions #457, #458 (chat 2026-11-24); light task inside approved decisions, approved by Orchestrator"
created: 2026-11-24
---

# TASK-0147 — Цена опережения (#458) и данные G5/G6 ред. 3.1 на ветке поезда 2

## Цель
Решения владельца #457 (промежуточные ступени) и #458 (мягкая цена опережения): открытия G5/G6 ближе к историческим годам, опережение возможно, но дорого.

## Описание
1. Данные: узлы 82–99 и правки `requires` по `docs/research/slice-g5-g6-content.md` ред. 3.1 в `mods/core/nodes.yaml` (и локализация RU/EN) на ветке поезда 2 (`worktree-agent-a94cedd4bf9d686b2`: TASK-0133 + TASK-0134). Валидатор принимает; узел 87 — лист (оставить).
2. Core: множитель цены узла `1 + k · max(0, anchor_year − t_years)^p` (или иной монотонный вид, `[Предл.]` simulation-engineer), параметры `AheadCostK`, `AheadCostPow`, `AheadCostMax`; `k = 0` — выключено (поведение как сейчас). Детерминизм L1/L2, без аллокаций на тике, таблица цен не пересчитывается каждый тик без нужды.
3. Lab: ключи манифеста для параметров; simulation-engineer — зонд 2×2 (цепочки × цена) с пре-регистрацией по `docs/reviews/slice-g5-g6-content-rev3-gate.md` (сдвиг G5 APP ≥ +3, АФАР ≥ +8 лет; годы первого открытия против целей §4.2 зонда), выбор k по правилу, показ владельцу.
4. Перезапись эталонов — одна, вместе с поездом 2 (§5 оркестрации).

## Критерии приёмки
- [ ] Unit-тесты формулы (монотонность, k=0 = без изменений, потолок); L1/L2 на 1020 мес.
- [ ] Зонд 2×2: VALIDITY OK, таблица годов; владелец утверждает точку.
- [ ] Ревьюер APPROVE; второе ревью.

## Результат (developer, ветка task-0147; пункты 1, 2 и часть пункта 3)

**Данные (п. 1).** `mods/core/nodes.yaml`: узлы 82–99 (источники `G56-R-1..18`), правки `requires` 65, 66, 67, 68, 71, 76 (развилка `(90|89)`), 77, 78 (теперь 85), 79, 80, 81; `informs` по ред. 3.1 п.5: у 62 теперь 67 0,6 / 70 0,3 / 94 0,3, снято 62→78 и transonic→78. RU/EN имена и якоря, `_sources.tsv` проштампован. Валидатор принял (корни-PRN 82, 86 с пустым `requires` приняты).

**Core (п. 2).** Множитель `min(AheadCostMax, 1 + AheadCostK·ahead^AheadCostPow)`, `ahead` — годы от месяца шага до 1 января `anchor.year` (0, если якорь прошёл); степень целая 1–4 (без Math.Pow); умножает `c_money` в `KnowledgeSystem.CEffOf` (`KnowledgeMath.AheadMultiplier`). Функция чистая от (узел, месяц): таблицы и статических буферов нет, аллокаций нет. `AheadCostK = 0` (умолчание) возвращает ровно 1. `KnowledgeSystem.CEff` получил параметр `month` (WhyNot передаёт его). `KnowledgeGraph.AnchorYear`, `KnowledgeParams.Validate()`. Session: `SliceRunConfig.AheadCostK/Pow/Max` (nullable, колонки `run.k.ahead_*` только при явном значении, отпечаток), `TestApiVersion` 18→19, `session-surface.v19.txt`.

**Тесты** (`KnowledgeAuditTests`): формула (1 при k=0 и после якоря, монотонность, потолок), валидация, k=0 с нестандартными pow/max даёт те же ежемесячные хэши 240 мес., CEff дороже до якоря и равен после, L2 (3 и 8 потоков, 1020 мес., k>0), L1. Не проверено на «падает без правки»: тесты вызывают новый API, без правки не компилируются.

**k = 0 и хэши.** На прежних данных (`git checkout HEAD -- mods`) с новым кодом весь набор прошёл (Core 1132/1133, единственный сбой — мой старый `Shuffle(81)`, исправлен на `Data.Nodes.Count`; Session 523/527, 4 пропущены; Localization, Kernel, Audio зелёные): эталоны не изменились. На новых данных падают только закреплённые хэши: Core `CoreDataHash_IsFrozen`; Session 32 теста (эталоны траекторий, `SchemaTests`, TwoWorlds, WhyNotF25 и др.). Перезапись — в поезде. Счётчики узлов в тестах (81→99, типы, hazard 31→45, informs, локаль) обновлены.

**Не сделано: Lab (п. 3) — вне зоны developer (`src/Singularity.Lab/**`, `docs/**`).** Из-за подъёма `TestApiVersion` до 19 сборка Lab сейчас красная (CS0162 в `JetSliceModel.cs`, `SurfaceVersion = 18`): simulation-engineer должен (а) поставить `SurfaceVersion = 19`; (б) в `SliceParameters.cs` добавить ключи `AHEAD_K` (`AtLeast 0`), `AHEAD_MAX` (`AtLeast 1`), `AHEAD_POW` (целое 1–4) в список допустимых и в `SliceRunConfig.AheadCostK/Pow/Max`; (в) проверки в `SliceChecks.cs` по образцу HCAP_*; (г) строку в `docs/simulation/slice-csv-format.md`. Зонд 2×2 (п. 3) и выбор k тоже за simulation-engineer.
**verify.py** не пройден: падает сборка Lab (причина выше); тесты прогонялись по проектам вручную.
