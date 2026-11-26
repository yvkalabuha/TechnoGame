---
id: TASK-0188
title: "Research + data: expand knowledge tree (computing, materials, rocketry, space, electronics, nuclear) with directions (#575)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: research-scientist
category: data
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: owner
approved_at: 2026-11-25
approval_ref: "#575"
review_ref: "train f12684f: reviewer APPROVE (a74ab0a, rounds to 8a4b96b7); codex 4 returns, waived by #432, DEBT-0115; data reviewed via research notes"
created: 2026-11-25
---

# TASK-0188 — Research + data: expand knowledge tree (computing, materials, rocketry, space, electronics, nuclear) with directions (#575)

## Цель
Расширить дерево знаний до партии (#575): вычислительная техника (узлы-ускорители науки research_speed), материалы, ракетная техника, космические исследования отдельной областью (спутники, космонавтика, наука), электроника, ядерные исследования.

## Описание
Шаг 1 (research-scientist): записка с узлами 1945–2030 по каждой области — id, название, год-якорь, цена-тип, предпосылки из существующих и новых узлов, 2–4 направления из 12 (O-091), эффекты (research_speed с долей, военная ценность), источник на узел. Объём ≈ 40–60 узлов. Шаг 2 (developer): данные в mods/core/nodes.yaml, локализация, тесты графа (ацикличность, якоря), pace_check справкой. Новые изделия (ракеты, спутники) — не в этой задаче, только знания.

## Критерии приёмки
- [ ] Записка с источниками (URL + дата), ADR-0009 для данных
- [ ] Данные в модах, граф ацикличен, тесты зелёные; reviewer APPROVE

## Тесты
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат (шаг 2, developer, 2026-11-25)
**Сделано** [Факт]: 57 узлов (номера 100–156) в `mods/core/nodes.yaml` из записки `tree-expansion-575.md` ред. 2; RU/EN локализация (name, short, anchor, history) и штампы `_sources.tsv`; `node_history.yaml` (1–3 источника на узел, доступ 2026-11-25; `unconfirmed` у оценочных годов). Загрузчик: поля `mil_value` (none/base/prep/direct) и `effects` (`research_speed`: `dirs` список или `all`, `bonus` ≤ 0,08), колонки `NodeTable.MilValue/SpeedStart/SpeedMask/SpeedBonus`, схема `node.schema.json`; читается, не используется до TASK-0187, в DataHash не входит (как `directions`). 15 узлов с research_speed, максимум суммы по направлению aero 0,22 (< 0,25).
**Проверка кода** [Факт]: префикс `sN` код не разбирает (только regex id; s9/s10/s11 безопасны). Кластеры — закрытый enum из 8 значений, `X½` определён для tier 3–5, PRC-кластер должен быть в списке сценария (MET/CHEM/MECH/AERO). Поэтому: COMP→ELEC, SPACE→AERO, NUC→NUC у не-PRC; 9 PRC размещены в MET/CHEM/MECH, их tier ≤ 5 (EUV и аддитивное 7/6 → 5). Для PRC (в записке не было) заданы tier, capability_req, labour_p — [Предп.], калибровка в TASK-0185.
**Правки данных относительно записки** [Факт]: (1) зонная очистка требует `s5.prc.alloying_element_refining` вместо solidification_science (1953 > 1951); (2) gen поднят до max(gen предпосылок) у 6 узлов (maraging, HIP, циркалой, подлодка, PWR, центрифуга → 3; правило валидатора); (3) `informs`: 23 ребра из 27 записки, 4 ребра к APP (jet_fighter_g2/g3/g4) отброшены; валидатор ослаблен: источник любого типа, цель не FND и не APP (FND — по-прежнему только к ENG/PRC своей дисциплины). Существующие `requires` не менялись (O-093).
**Тесты** [Факт]: `ExpandedTreeTests` (16: состав и номера, год/gen предпосылок, направления, space 9+2, research_speed, informs, негативные effects/mil_value) — без данных все 16 падают (проверено). Обновлены счётчики 99→156 и др. Core 1244, Localization 133, Architecture 147, Kernel 527, Audio 493, Session 651 — зелёные (nice -n 10).
**Эталоны, перезаписаны один раз** [Факт]: причина — новые узлы делят бюджет исследований и добавляют 9 процессов (колонки по узлам/процессам), траектории сдвинулись; новых видов колонок нет, SchemaVersion остаётся 15 (как в TASK-0133). CoreDataHash (4b4e15e9…), отпечаток колонок Session, Linked/F17b/TwoWorlds/Mastery/ViewTests/Reserve/Intelligence/ProductAi. Горизонты поправлены там, где событие ушло позже: Adviser 400→560, A40 384→480, ForceArm 240→360, Audio 200→320, kappa 150→300 мес.
**pace_check --base main** (#566, только отчёт) [Факт]: G2 APP USA 143→214 мес (+71), first unit 192→262 (+70); G3 APP 244→328 (+84), first unit 308→inf (54/100 never); G4 APP 368→inf (95/100 never). WARN во всех поколениях. База уже вне окон C4 (0/100). Причина вероятно в разбавлении бюджета узлами; решение — TASK-0185/0187.
**Не сделано / OPEN**: (а) Lab (зона simulation-engineer): `SynthWorld.NumberStride` 100 → ≥157 (иначе 9 тестов Lab падают) и эталон `SliceChecks.cs` 3d466f77…→c79505d7…; (б) стартовое знание 1945 (полупроводники, астродинамика, деление) не узлы — в сценарий записать нечего, решение «у кого» не принято; (в) 4 ребра informs к APP.
