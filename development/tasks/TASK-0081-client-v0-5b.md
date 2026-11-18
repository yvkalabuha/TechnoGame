---
id: TASK-0081
title: "Client v0.5b: main menu, settings, new game, palette C with alert validator, time icons"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: tooling
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0077, ADR-0018]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "#302, #303, #326; spec docs/ui/CLIENT_V05B_SPEC.md"
created: 2026-11-18
---

# TASK-0081 — Client v0.5b: main menu, settings, new game, palette C with alert validator, time icons

## Цель
Клиент v0.5b по спецификации `docs/ui/CLIENT_V05B_SPEC.md` и макету `docs/ui/prototypes/client-v05b.html`.

## Описание
1. Главное меню: Новая игра, Загрузка (неактивна до кода сохранений ADR-0013), Настройки, Выход.
2. «Новая игра»: выбор державы игрока (заменяет флаг `--power`).
3. Настройки: Видео, Звук, Общие (язык переезжает сюда). DLSS — серый пункт «требует плагин» (предложение ui-art-director, может быть изменено владельцем).
4. Иконки времени: пауза, медленнее, быстрее.
5. Палитра C (токены §17), скругления, `warn` #D9860F с ореолом, контуром и знаком ▲ (#326).
6. Валидатор контраста тревог по алгоритму спецификации (ΔE2000 и оттенки серого, включая затенённую сушу); запуск в `--shots`, результат в лог; провал не блокирует сборку, а печатается.
7. Пункты v0.5b из `docs/ui/reviews/task-0077-visual.md`: легенда цветов и глифов, центр камеры с учётом правой панели, отклик панели на клик по другой стране.

Не менять Core, Session, Kernel.

## Критерии приёмки
- [ ] Критерии приёмки по экранам из спецификации.
- [ ] Валидатор печатает PASS/FAIL по каждой паре; снимки в `docs/ui/prototypes/client-v05b/`.
- [ ] `dotnet build src/Singularity.Client.Spike`, `./run-globe-spike.sh --shots`, тесты клиента зелёные.

## REVIEW
- [ ] Code review (reviewer)
- [ ] Визуальное ревью (ui-art-director)

## Результат
