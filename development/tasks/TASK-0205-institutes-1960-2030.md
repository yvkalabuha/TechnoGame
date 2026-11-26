---
id: TASK-0205
title: "Data: build candidate institutes 1960-2030 for USA, SUN, GBR, FRA, SWE with directions and levels (#589, O-110)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: research-scientist         # ответственный агент
category: data                # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: orchestrator
approved_at: 2026-11-26
approval_ref: "DECISIONS_LOG #589; O-110"
created: 2026-11-26
---

# TASK-0205 — Data: build candidate institutes 1960-2030 for USA, SUN, GBR, FRA, SWE with directions and levels (#589, O-110)

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

## Цель
#589/O-110: правилу ИИ «строить институт при излишке» нужно, что строить после 1990. В mods/core/institutes.yaml нет кандидатов позже 1961.

## Объём
1. research-scientist: исторические научные институты и лаборатории, основанные 1960–2030, по USA, SUN/RUS, GBR, FRA, SWE (по 6–12 на страну), с годом, 2–4 направлениями из 13 (aero, prop, met, mat, chem, struct, mfg, elec, sens, ctrl, nuc, ord, space) и уровнями, ценой/временем по SCIENCE_SLOTS §2a. Сбор и проверка фактов — через `python3 -m tools.llm_worker research` (DeepSeek, #577), Claude проверяет.
2. Записка docs/research/notes/institutes-1960-2030.md с источниками (URL + дата) и YAML-блоком для mods/core/institutes.yaml.
3. Правка данных — developer по записке (после проверки).

## Результат (2026-11-26, на паузе #595)
- Записка docs/research/notes/institutes-1960-2030.md: 31 институт 1960–2016, проверка DeepSeek 17/12/2.
- Ветка worktree-agent-a4e34095a3a969c03 (7f454dd5, поверх TASK-0199): 31 кандидат в mods/core/institutes.yaml; Core 1283 OK; Session 27 падений — эталоны до 1990 сдвинулись структурно (слоты inst.abuild/prog.3/4/6 по порядку id и хеш данных run.inst.data; остальные 3766 колонок равны на месяце 36). Поведение до 1990 на месяце 529 не подтверждено; CancelBuild-тест упал по неразобранной причине.
- Решение при возобновлении: перезаписать эталоны до 1990 одним пакетом с TASK-0193/0199 или исключить слоты/хеш данных из привязки теста 1989 года; разобрать CancelBuild.
