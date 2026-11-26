---
id: TASK-0194
title: "Air power goal (#565): Session goal state parity/superiority/dominance and a header indicator in the client"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: developer
category: core
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: owner
approved_at: 2026-11-25
approval_ref: "#565, #563"
created: 2026-11-26
---

# TASK-0194 — Air power goal (#565): Session goal state parity/superiority/dominance and a header indicator in the client

## Цель
Цель партии — воздушная мощь (#565): отношение своей мощи к лучшему сопернику, ступени паритет ≥ 1,0, превосходство ≥ 1,5, господство ≥ 2,0, удержание 5 лет.

## Описание
Session: поле цели игрока — текущее отношение (из столбца inst.mpi, TASK-0187), ступень, сколько месяцев удерживается, лучший соперник; событие «ступень достигнута / потеряна»; итог партии к 2030 (мягкое поражение #422 сохраняется по смыслу). Клиент: индикатор в шапке (число и ступень словами, не только цвет), всплывающее пояснение, строка в итоге партии. Учитывать разведку: мощь чужих — оценка с достоверностью, если разведка включена.

## Критерии приёмки
- [ ] Тесты Session на ступени и удержание (падают без изменения); клиент — чистые функции с тестами
- [ ] Кадр шапки 1920x1080 в следующей пачке; reviewer APPROVE
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
