---
id: TASK-0151
title: "Player levers core: research priorities through institutes (slots, funding first x1.5, institute bonus, capacity, upkeep, building institutes)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: gate
milestone: MS-M05
agent: developer
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0148]
related: [TASK-0148, TASK-0156]
approved_by: owner
approved_at: 2026-11-24
approval_ref: "#482; scope changed by #484-#496"
created: 2026-11-24
---

# TASK-0151 — Player levers core: research goals with a science share slider (SetResearchGoals)

## Цель
Приоритеты исследований через институты (#484–#496): слоты = институты, деньги приоритету первыми (×1,5, общий множитель с долей остальным ≥ 25 %), бонус института +5 % за уровень, мощность и содержание, стройка институтов как проект (игрок и ИИ).

## Описание
`docs/game-design/civilization/research-institutes-priorities.md` ред. 3 (v0: без забывания и «углубления»). Без приоритетов — старый путь кода, хеши как на main. Перекалибровка лет открытий (#493, #496) — вместе с simulation-engineer в том же пакете изменений, меняющих эталоны. Оценка ≈ 26–30 дней.

## Критерии приёмки
- [ ] Без приоритетов — хеши совпадают с main
- [ ] P-8: приоритетный узел никогда не медленнее неприоритетного
- [ ] P-7: «построить всё в 1946» не доминирует
- [ ] reviewer APPROVE, Codex

## Связанные документы
`docs/game-design/PLAYER_LEVERS_WAVE1.md`, `docs/game-design/economy/production-lines-and-licence-market.md`, `docs/game-design/playtests/2026-11-24-owner-m-play.md`

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
Часть 1 из 2 (по заданию: институты + приоритеты, без стройки; ветка `task-0151-institutes`, от main + коммиты TASK-0150, т.к. 0150 ещё не на main). За флагом `SliceRunConfig.Institutes` (по умолчанию выкл.).
- Данные: `mods/core/institutes.yaml` (22 института 1945, только с подтверждённым годом основания; Rolls-Royce Derby не внесён), тип `institute` в ModLoader, `InstituteTable` со своим хешем (не входит в `GameData.DataHash`, `CoreDataHash` не менялся).
- Ядро: `Core/Institutes` (`InstituteSystem`: опыт, цели ИИ; `InstituteOrders`: приказы игрока колонками сущности; `InstituteLayout`/`InstituteHandle`; `InstituteMath`: κ_eff, пороги, бонус). `KnowledgeSystem`: группы цели, κ_eff, бонус +5 %/уровень только на целевой узел; без целей — старый код (`PriorityGoals` = 0).
- Команда `SetPriorityNode(slot, node)` (кодек, валидатор, правило раз в 12 мес. на слот, узел занят/известен).
- Отклонение от rev 4 §3: пороги уровней строго растут минимум на 1 (при N=6 правило даёт 1/2/3/3, уровень 3 и 4 неразличимы по опыту).
- Часть 2 (BuildInstitute): команда `BuildInstitute(slot)`, окно ≥ ref_year−10, срок 12·(2+0,5·Σ) мес., цена 1,5 %·Σ·(1+0,02·y²) потока Research в месяц пока идёт проект, потолок Σ ≤ 3 для малой страны (доля budget_index ≤ 0,1 от максимума: FRA, SWE), после открытия опыт = пороги уровней, мощность +0,8 %/ур. и содержание 0,4 %/ур. (`BudgetFactor`, применяется к ResearchBudget); ИИ начинает проекты исторических кандидатов так, чтобы открыть около ref_year (те же проверки); кандидаты в данных: ONERA, НИИ-88, RRE, DARPA, CNES (уровни — оценки; ONERA снижена до Σ=3 из-за потолка малой страны). Виды: `ActorView.Institutes` (`InstituteView`: слот, id, стоит ли, цель, уровни по областям, старт/срок/ставка проекта, ref_year, Σ).
- Не сделано: оплата проекта из резерва, нехватка кадров (`reserve_workforce`), растяжение срока при нехватке денег (в модели доли потока денег не нехватает), лимит числа одновременных проектов.
- Правки ревью: защита от wsum = 0, опыт только за открытие своим исследованием (`know.disc.N`), тесты P-8 строже, узел пути без множителя уровня.
- Проверка: Session.Tests InstituteTests (12), ReserveTests, GoldenProjectionAgainstMain, SchemaTests, Command*; Core.Tests Data/Knowledge/Command (529); Architecture.Tests (147). Мутация: убрал max(1, ·) в KappaEff, тест KappaEff упал.
- Решение Orchestrator 2026-11-25: ИИ не ставит приоритеты и не начинает стройку за `SliceRunConfig.PlayerActor` (по умолчанию −1; клиент должен его задать), только предлагает (`InstituteView.ProposedGoal`, `ProposesBuild`); цена считается по годам от года открытия до `ref_year` (в срок = ×1); проект ИИ платит с месяца старта, как проект игрока (исправлен сдвиг на месяц).
- ONERA имеет Σ=3 из-за потолка малой страны; рост несколькими проектами по очереди в v0 не поддержан.
