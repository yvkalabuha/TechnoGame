---
id: TASK-0119
title: "Instrument data fields in the Session view: alloy coverage, research progress"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M05
agent: developer         # ответственный агент
category: core           # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: low            # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0111, TASK-0115]
related: [TASK-0111]
approved_by: orchestrator
approved_at: 2026-11-24
approval_ref: "PLAN Фокус п.2 (after train 2); light task approved by Orchestrator"
review_ref: "reviewer: APPROVE (alloy fix re-review); ui-art-director: PASS task-0119-visual.md; codex: waived #432 after 3rd return (fac02a13), remainder DEBT-0092; landed c898dce"
created: 2026-11-19
---

# TASK-0119 — Instrument data fields in the Session view: alloy coverage, research progress

## Цель
Вернуть колбу сплава в панель «Производство» с одним источником данных: поле покрытия сплавом в представлении сессии. Клиент не выводит это состояние сам (O-018).

## Описание
В TASK-0111 клиент определял состояние сплава по цепочке причин (`why_not`) одной цели. Codex трижды нашёл случаи, где колба показывала неверное: другая действующая линия ограничена сплавом, а колба «хватает»; один процент с разными знаменателями.

1. simulation-engineer или systems-architect: определить величину «покрытие сплавом» для актора (предложение: минимум по действующим линиям отношения поставки к потребности; отдельно — сбой снабжения политикой и отсутствие плавки) и где она считается в ядре без новых аллокаций на тике.
2. developer: поле в представлении сессии (смена версии тестового API — вместе с «поездом схемы»), подключить `InstrumentLogic.AlloyFlask`, `AlloyCaptionKey`, `FlaskPercent` (остались в коде с тестами) и вернуть ряд в панель.

### Научные колбы (добавлено 2026-11-19, O-020)
Замер на живой партии (TASK-0111, 360 шагов по месяцу, 1947–1977): научные колбы берут данные из цепочки причин (`why_not`) текущей цели и показывают что-то только у SUN — в 39,4 % месяцев; у USA, GBR, FRA, SWE — в 0 %. Прибор должен читать ход исследований в работе: узлы знаний, которые актор исследует сейчас (накоплено и нужно). В представлении сессии такого поля нет (`NodeView` несёт только «известен»).
3. systems-architect: поле «исследования в работе» (до N узлов: номер, накоплено, нужно, признак порога для опасных узлов) без аллокаций на тике.
4. developer: колбы «Наука» читают это поле; строка «нет хода исследования» остаётся только когда исследований в работе действительно нет.

## Критерии приёмки
- [ ] На живой партии у каждой из пяти стран научные колбы видны не меньше чем в 80 % месяцев, пока в данных есть неизвестные узлы; число записано в журнал прогона кадров.
- [ ] Колба читает только поле представления; состояния «хватает», «N % потребности», «N % от обычного», «сплава нет» покрыты тестами на данных сессии.
- [ ] Кадры 1920x1080 при 100 %: каждое из четырёх состояний.
- [ ] Ревьюер APPROVE, Codex без открытых высоких замечаний, ui-art-director PASS (#417).

## Тесты
Тесты сессии и клиента; `python3 tools/development/verify.py`.

## Связанные документы
`docs/decisions/ORCH_DECISIONS.md` (O-016, O-018), `docs/ui/UI_INSTRUMENTS.md`.

## REVIEW

## Результат
Сделано (пункты 1–4; C-1 не делался).
- Ядро: `KnowledgeSystem.ResearchInProgress` (узлы границы с накоплением > 0, ближайшие к завершению первыми, без аллокаций в куче) и `KnowledgeSystem.NeedOf`; `AlloyCoverage` (Core/Products): снабжение месяца, наибольшая потребность действующей линии (серия, темп > 0, сплав нужен), доля сбоя политики.
- Session v21: `ActorView.Alloy` (`AlloyCoverage`) и `ActorView.Research` (`ResearchView`: узел, накоплено, нужно, порог). Поля — необязательные параметры в конце, `new ActorView(...)` в Lab не ломается. Эталон поверхности `session-surface.v21.txt`; хэши и golden не менялись (вид не хэшируется).
- Клиент: колбы «Наука» и ряд «Сплав» в «Производстве» читают только поля вида; `InstrumentLogic.ResearchFlasks(ResearchView)`, `AlloyFlask(AlloyCoverage)`; прогон кадров проверяет живую сессию (число колб, подпись сплава, доля месяцев не ниже 80 % по каждой стране).
- Живой замер (360 шагов по месяцу, 1945–1975): USA, GBR, SUN, FRA, SWE — 360 из 360 (100 %); было 0 % у четырёх стран.
- Тесты: InstrumentFieldsTests (поля против полного перебора по Core, 80 % у каждой страны за 120 месяцев, потребность сплава), InstrumentTests (колбы по полю, четыре состояния сплава), ApiSurface, ViewTests (whitelist). Не проверялось, падают ли тесты без правки (поля нет — тесты не компилируются).
- Кадры: shots-out/flask_science_*, flask_production_* (100 % и 150 %, серые), flask_science_live.png.
