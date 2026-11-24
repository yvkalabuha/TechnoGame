---
id: TASK-0134
title: "Hcap growth and production mastery in Core: series gate, candidate close, foresight, mastery programme (#438-#440)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M05
agent: developer
category: core
priority: high
needs_owner: false
depends_on: [TASK-0133, TASK-0129]
related: [TASK-0128, TASK-0133]
approved_by: owner
approved_at: 2026-11-24
approval_ref: "#438, #439, #440 (mechanic approved by owner; gate docs/reviews/production-mastery-*.md)"
created: 2026-11-24
---

# TASK-0134 — Рост потолка кадров и освоение производства в Core (#438–#440)

## Цель
Зонд G5/G6 (TASK-0133) показал: процессы уровня 5 никто не осваивает, ИИ годами держит линии на неосваиваемых проектах. Владелец решил: потолок кадров растёт (#438), ИИ не запускает неосваиваемую серию и есть механика освоения (#439), платная программа освоения — сразу (#440), для игрока — «обучение персонала» (за границей при наличии донора или свой опытный участок).

## Описание
Спецификации: `docs/simulation/notes/hcap-growth.md` (правило `Hcap(t)`, тесты), `docs/game-design/notes/production-mastery.md` ред. 3 (ворота старта серии, закрытие линии-кандидата, статическое предвидение с гистерезисом, программа освоения, команды `StartMasteryProgramme`/`CancelMasteryProgramme`). Работа идёт на ветке поезда 2 поверх TASK-0133 (данные G5/G6 и старение парка). Одно повышение `SchemaVersion` в поезде (колонка `prog_state`).

Порядок: (1) simulation-engineer — пилот на 1 seed: почему процессы 43/73 стоят при ненулевом `GrowthNominal` (диагноз «Б»); (2) developer — `Hcap(t)`, ворота и закрытие, предвидение; (3) developer — программа и команды; (4) simulation-engineer — пре-регистрация и зонд по веткам B0–B3, пороги O-038 и записок.

## Критерии приёмки
- [ ] Тесты, падающие без правки: нет старта серии при `y_min < y_start`; нет линии-кандидата ниже `y_prod` дольше `T_Ab`; `Hcap` до 1977-12 равен сценарию побитно; программа выводит процесс к «готов к серии».
- [ ] 1945–1977 без изменений (проекция хешей на месяц 387).
- [ ] L1/L2 на 1020 мес.; без аллокаций на тике; без float.
- [ ] Зонд: таблица по порогам, `VALIDITY: OK`; страница для владельца.
- [ ] Ревьюер APPROVE; второе ревью по #432.

## Связанные документы
`docs/simulation/g5-g6-probe.md` (ветка TASK-0133), `docs/reviews/production-mastery-*.md`, `docs/decisions/DECISIONS_LOG.md` #438–#440.

## Результат
