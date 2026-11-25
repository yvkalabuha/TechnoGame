---
id: TASK-0170
title: "Data: friend pairs FRA-SUN 1945-1955 and GBR-FRA from 1947 in alliances.yaml, checked against treaty texts"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: research-scientist         # ответственный агент
category: data           # core | lab | client | data | tooling | docs | design (lab -> agent simulation-engineer)
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: orchestrator # owner | orchestrator (orchestrator — только level: light)
approved_at: 2026-11-25
approval_ref: O-079
created: 2026-11-25
---

# TASK-0170 — Data: friend pairs FRA-SUN 1945-1955 and GBR-FRA from 1947 in alliances.yaml, checked against treaty texts

## Цель
Убрать перекос круга «друзей» 1945–1948 в `mods/core/scenarios/1945/alliances.yaml`: сейчас дружит только GBR–SUN.

## Описание
Проверить по первичному тексту (Avalon, UNTS; факты, не текст Википедии) и добавить FRA–SUN (договор 1944-12-10, денонсирован 1955) и GBR–FRA (Дюнкерк, 1947-03-04). Точность — год, оба года включительно. Добавить проверку на данных: SUN вне блока в 1954 и в Варшавском договоре в 1955. Поправить образец формата в `docs/research/notes/alliances-1945-2030.md` под схему загрузчика.

## Критерии приёмки
- [ ] Пары добавлены с источником
- [ ] Тест на данных
- [ ] Образец в заметке совпадает со схемой

## Тесты
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
