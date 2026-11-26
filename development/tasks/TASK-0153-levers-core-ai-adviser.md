---
id: TASK-0153
title: "Player levers core: AI adviser for the player country production (no AI opening or closing of player lines)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: gate
milestone: MS-M05
agent: developer
category: core
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0148, TASK-0156]
approved_by: owner
approved_at: 2026-11-24
approval_ref: "#482 (designs approved), #470-#481"
review_ref: "reviewer: APPROVE (after merge, v25); codex 2 rounds closed; second review agy-sonnet accept 2 medium 3 low -> DEBT-0105; merge 7ed4860"
created: 2026-11-24
tags: [task, core, gameplay, done]
---

# TASK-0153 — Player levers core: AI adviser for the player country production (no AI opening or closing of player lines)

## Цель
ИИ страны игрока в производстве только советует (#479).

## Описание
docs/game-design/economy/production-lines-and-licence-market.md разд. 1: ИИ не открывает и не закрывает линии страны игрока; выдаёт предложения в View; принять/отказать одной командой. Предел линий игрока и правило пары линий (IsWorkingOfPair) — по документу.

## Критерии приёмки
- [ ] Тест: ИИ не меняет линии актора игрока
- [ ] Предложения видны во View
- [ ] reviewer APPROVE, Codex

## Связанные документы
`docs/game-design/PLAYER_LEVERS_WAVE1.md`, `docs/game-design/economy/production-lines-and-licence-market.md`, `docs/game-design/playtests/2026-11-24-owner-m-play.md`

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
Режим «ИИ — советник» за флагом (по умолчанию выключен; `SliceRunConfig.AdviserActors` пуст = как в main, новых колонок нет, отпечаток снимка не меняется).

- Core: `AdviserPlan`, `PolicySystem.Adviser.cs`. Для строк-советников `Commit` не пишет команды проекта, старт и закрытие линии; вместо них пишутся предложения в колонки `pol.adv.{d}.*` (по проекту, с handle). Отказ хранится в `pol.adv.{d}.no.open/close` (годовая норма `RefuseYears`, по умолчанию 1 год = 12 мес.).
- Команда `RefuseProposal(actor, handle, close)` (журнал, кодек, валидатор); принять = обычные `StartSeries`/`CloseLine` с handle предложения (`ProposalView.Accept/Refuse`).
- Session: `PolicyDecisionView.Proposals`; предел линий (`AdviserMaxLines`, 2) проверяется в `Submit` (`CommandRefusal.LineLimit`); `TestApiVersion` 22, файл `session-surface.v22.txt`.
- Тесты: `tests/Singularity.Session.Tests/AdviserTests.cs` (8): нейтральность (выкл = main: хэш, схема, отпечаток), ИИ не стартует/не закрывает/не проектирует для актора-игрока, предложение видно во View и совпадает со стартом ИИ, принять = старт серии, отказ молчит 11 месяцев и возвращается на 12-й (+ повтор по журналу), предел линий, кодек.
- Отказ молчит по слоту проекта независимо от поколения (новый проект в том же слоте тоже молчит до конца срока).
- Codex, раунд 1: предел линий считает и старты, ждущие месячного шага; игрок на стартовой линии берёт скорость на себя (StartSeries на идущую линию меняет скорость для актора-советника, ИИ-признак снимается); отказ глушит только предложение, показанное именно для этого проекта (иначе NoProposal/NoAdviser); вид скрывает «открыть» для уже идущей линии; тест пути закрытия. Колонки отказа: cmd.refuse.{d}.open|close.stamp|gen.
- Codex, раунд 2: устаревший handle в StartSeries не снимает ИИ-признак и не останавливает стартовую линию; непринятые предложения не влияют на автоматические решения по парку; отказ пропускает проект при поиске запуска (следующий подходящий получает предложение — отдельным тестом не покрыто); предел линий выше 2 отклоняется при загрузке (если не ADMIT/DERIV); тест возврата предложения на 12-й месяц.
- Проверка: без `Advise` 4 из 8 тестов падают (проверено); Core.Tests 1147 прошли; Session.Tests 553 прошли, 4 пропущены.
- Не проверено: Architecture.Tests и сборка Lab (Lab не собирается: `SurfaceVersion` в `src/Singularity.Lab/Models/Slice/JetSliceModel.cs` должна стать 22 — зона simulation-engineer); клиент не запускался; эталоны не перезаписывались.
