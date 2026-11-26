---
id: TASK-0199
title: "Core: AI builds institutes on science surplus + era node price curve D by min(node year, calendar year) (#589)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: core                # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0193]
related: []
approved_by: owner
approved_at: 2026-11-26
approval_ref: "DECISIONS_LOG #589"
created: 2026-11-26
---

# TASK-0199 — Core: AI builds institutes on science surplus + era node price curve D by min(node year, calendar year) (#589)

## Цель

## Описание

## Критерии приёмки
- [ ]

## Тесты
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
**Сделано (developer, ветка worktree-agent-ab03f95d4984d29b1).**
- Эра-цена: `SlotParams.EraPrice` (кривая D: 1990 ×1, 2000 ×2, 2010 ×4, 2020 ×8, 2030 ×16, линейно между точками, после 2030 плоско), таблица по годам 1945–2100 считается один раз в конструкторе (`KnowledgeSystem._era`, целые числа); `CEffOf` в режиме слотов умножает на множитель года min(год узла, календарный год). Пустая кривая = ×1.
- Правило ИИ «строить институт при излишке»: `InstituteSystem.AiSurplusBuild`: среднее (экспоненциальное окно 12 мес.) доли возврата денег науки > 20 %, CanBuild = Ok (в нём потолок по содержанию #574, окно и цена стройки по SCIENCE_SLOTS §2a), не чаще раза в 24 мес. на страну, ничего не строится в этот момент. Колонки `inst.surplus`, `inst.surplus_built`; до 1990 не пишутся (нули).
- Параметры: `SlotParams` (код, как все константы слотов; `SliceRunConfig.SlotParameters` internal для тестов и Lab, в отпечатке конфигурации только при переопределении).
- Эталоны перезаписаны одним пакетом (TwoWorldsOnOnePool, LinkedRun Golden60, ViewTests): причина — две новые колонки в хэше; старые колонки до 1990 не меняются (тест).
- Тесты: `tests/Singularity.Session.Tests/EraPriceSurplusTests.cs`. Прогон: Session 716 ок / 4 пропущено, Core 1279 ок, Architecture 147 ок, Lab.Tests 231 ок.

## Цель
Решение #589: после 1990 деньги науки лидеров идут на науку — ИИ строит институты при излишке и цена тем мягко растёт с эпохой.

## Объём
1. Core/Session: множитель цены узла по эпохе, кривая D (×2 за 10 лет от 1990, потолок ×16 к 2030), год = min(год узла, календарный год) — мир до 1990 побитно прежний. Параметры в данных (mods/core). Описание правки: docs/simulation/era-node-price.md §5.1 (ветка worktree-agent-ac007901863348194).
2. ИИ: правило «строить институт при излишке»: если доля возврата денег науки за последние 12 мес. > 20 % и число институтов < потолка по содержанию (#574) — заложить институт (стройка с ценой и временем по SCIENCE_SLOTS §2a); не чаще раза в N мес.
3. Lab: замер simulation-engineer после ядра — возврат после 1990 < 20 % (USA, SUN), GAP36, полосы G2–G4, простой, SWE/GBR не теряют открытий сверх 2 узлов.

## Критерии приёмки
- Тесты: мир до 1990 не меняется (хэш по месяцам 1945–1989 равен прежнему); правило стройки срабатывает при излишке и не срабатывает без него; L1/L2.
- Эталоны перезаписаны одним пакетом с объяснением.
