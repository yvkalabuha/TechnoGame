---
id: TASK-0158
title: "Client shot runs: silent by default and a frame filter (only the frames a task needs)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M05
agent: developer         # ответственный агент
category: tooling
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: orchestrator
approved_at: 2026-11-24
approval_ref: "owner complaint 2026-11-24: sounds and long shot runs; light task"
created: 2026-11-24
---

# TASK-0158 — Client shot runs: silent by default and a frame filter (only the frames a task needs)

## Цель
Кадровые прогоны клиента не шумят и снимают только нужные кадры (жалоба владельца 2026-11-24: «куча звуков при тестовых запусках», «почему такие долгие запуски»).

## Описание
Сейчас `./run-globe-spike.sh --shots` снимает 351 кадр (ru/en, 100/125/150 %, все экраны) и пишет звуковое демо, звук идёт в колонки. Нужно: (1) кадровые прогоны по умолчанию с `--audio-driver Dummy`; звуковое демо только по явному флагу (`--audio-demo`); (2) фильтр `--only <имена через запятую или маска>` — снимать только перечисленные кадры; (3) `--time-shots` так же; (4) README и подсказка скрипта.

## Критерии приёмки
- [ ] Прогон `--shots` без звука в колонках
- [ ] `--shots --only area_science_1920x1080` снимает один кадр и завершается быстро (время указать)
- [ ] Полный набор по-прежнему доступен
- [ ] reviewer APPROVE

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
