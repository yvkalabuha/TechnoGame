---
id: TASK-0152
title: "Player levers core: licence market (multi-licence state, seller catalogue, BuyLicence, AdoptLicensedDesign, AI price rule, selling own projects)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: gate
milestone: MS-M05
agent: developer
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0148, TASK-0154, TASK-0150]
related: [TASK-0148, TASK-0156]
approved_by: owner
approved_at: 2026-11-24
approval_ref: "#482 (designs approved), #470-#481"
created: 2026-11-24
---

# TASK-0152 — Player levers core: licence market (multi-licence state, seller catalogue, BuyLicence, AdoptLicensedDesign, AI price rule, selling own projects)

## Цель
Рынок лицензий полного объёма (#471, #477, #478).

## Описание
docs/game-design/economy/production-lines-and-licence-market.md разд. 2: несколько лицензий на страну (ключ seller, gen), постоянный каталог LP-1 (#158), цена = m × справочная, BuyLicence через очередь сделок и ApplyDeals, AdoptLicensedDesign, правило покупки ИИ (цена ≤ k × справочная и не дороже своего исследования), продажа только своих проектов, круг покупателей по данным союзов (TASK-0154). Взнос из резерва (O-055). Может быть разделена на подзадачи developer'ом с согласия Orchestrator.

## Критерии приёмки
- [ ] Тесты на каждый отказ (Self, Empty, Gen, Money, круг)
- [ ] Игрок не может опустошить ИИ ценой (тест)
- [ ] reviewer APPROVE, Codex

## Связанные документы
`docs/game-design/PLAYER_LEVERS_WAVE1.md`, `docs/game-design/economy/production-lines-and-licence-market.md`, `docs/game-design/playtests/2026-11-24-owner-m-play.md`

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
Сделано (ветка worktree-agent-aa49e88c56a927355), всё за флагом `SliceRunConfig.LicenceMarket` (по умолчанию выкл.; при выключенном флаге нет ни одной новой колонки, хеш мира равен хешу main):
- **Данные:** `mods/core/scenarios/1945/alliances.yaml` — блоки и друзья годовыми интервалами, только строки, сверенные в `alliances-1945-2030.md` (NATO USA/GBR/FRA 1949, SWE 2024; Варшавский SUN 1955–1991; друзья GBR–SUN 1945–1955). Загрузчик `AllianceTable` (не входит в DataHash мода: файл вне `mod.yaml`).
- **Ядро (Policy):** несколько лицензий на страну по ключу (seller, gen), 8 слотов; постоянный каталог (m и круг по поколению, справочная цена = `LicenceRules.Price` собственного серийного проекта; купленное не продаётся, #477); `CheckBuy` — один валидатор для кнопки и для `ApplyDeals` (Self, Empty, Gen, Money, Circle, Capacity, Cap); `QueueBuy` (команда игрока пишет в очередь `pol.lic.q.*` между тиками) и `ComputeLicence` (ИИ, раз в год, в Next) пишут в очередь разных строк: ИИ никогда не покупает за внешне управляемую строку; `SetOffer` (m ∈ [0,5; 2], круг); правило покупки ИИ по O-062 (k = 1,5, свой срок ≥ 12 мес., запас L, профили: Quality не покупает, Mass m = 0,8, Quality m = 1,3); средняя мировая цена как чистая функция над каталогами ИИ (порог 2); потолок дохода игрока 10 % годового бюджета. Старые колонки одной лицензии хранят агрегат (макс. поколение, объединение масок), поэтому потребители маски не менялись.
- **Продукты:** `AdoptLicensedDesign` — лицензия, начавшаяся в месяце t, создаёт проект покупателя в стадии О без работы бюро, с альтернативами проекта продавца, собственным отсчётом ворот испытаний; колонка `licFrom` (только при флаге).
- **Команды и вид:** `BuyLicence`, `OfferLicence` (журнал, кодек, проверка формы, отказы `LicenceNotAllowed`/`LicenceRefused`/`BadOffer` с причиной в вердикте); `ActorView.Market` (удерживаемые лицензии, свой каталог, строки чужих с причиной отказа сейчас, мировые цены, последний отказ, доход года и потолок), `DesignView.LicensedFrom`. `TestApiVersion` 21 → 22, закреплён `session-surface.v22.txt`.
- **Оплата:** функция средств передаётся в `ApplyDeals`: сейчас закупки (как сегодня), после TASK-0150 — резерв. Из-за этого, пока резерва нет, цена не может превышать закупки месяца, а запас L = 6 мес. не действует (SWE не купит G1 до резерва); для ИИ добавлен `AiKeepShare` 0,5 (не из O-062): ИИ оставляет половину средств месяца (требование теста §7 «не ниже 0,5 месячной базы»).

Тесты: `LicenceMarketTests` (19, Core: каждый отказ, каталог, правила ИИ, «игрок не может опустошить ИИ ценой», данные союзов), `LicenceMarketSessionTests` (6, Session: покупка до проекта и повтор по журналу, отказы, чужая строка, нейтральность флага). Тест «ИИ не опустошается» проверен на падение без потолка k (k = 10 → «paid … above …»); остальные «падают без правки» не проверялись по каждому отдельно.

Не сделано / вынесено: аудитория «список стран» (O-056: маска) — есть Nobody/Bloc/Friends/All; данные о противниках (O-056: покупателям-врагам не продаём) — их в данных нет; неподтверждённые строки союзов не загружены; оплата из резерва ждёт TASK-0150; окно клиента — TASK-0156; Lab-пробы цен (k, L) — simulation-engineer.
