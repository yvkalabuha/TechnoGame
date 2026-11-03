---
id: TASK-0026
title: "D1: пакет стартовых данных (заменитель ранга 0, правки старта SUN/FRA/USA/GBR, узел 58)"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: gate
milestone: MS-M0
agent: developer         # ответственный агент
category: data
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0025]
related: [HYP-0002]
approved_by: owner
approved_at: 2026-11-03
approval_ref: "DECISIONS_LOG #127, #130–#136; спека v0.14 §11 D1"
created: 2026-11-03
---

# TASK-0026 — D1: пакет стартовых данных (заменитель ранга 0, правки старта SUN/FRA/USA/GBR, узел 58)

## Цель
Внести пакет стартовых данных, утверждённый владельцем (#127, #130–#135), и поля альтернатив v0.14, чтобы SUN мог производить реактивные самолёты без изменения правил культуры.

## Описание
По спеке `docs/simulation/slice-jet-aviation.md` v0.14 §11 строка «D1», §1.1 (таблица правок 1–8), §1.4 «Поля альтернатив v0.14» (`max_eng_gen`, `alloy_free`, `upkeep_mult`, `substitute`), A23, `why_not` `ENGINE_GEN_LIMIT`; пресеты по #136. Источник данных — `docs/research/sun-start-data.md` r2 (+ раздел R-KN). Абляции §6.9 — не в этой задаче (отдельный эксперимент после D1).

## Критерии приёмки
- [ ] Всё из §11 «D1» (схема, валидатор, writer и `data_hash`, данные mods, эталон `products_ref.py` при необходимости).
- [ ] 8 эталонных составов `product_class.md` §2 не меняются; тест K4.
- [ ] Общие пункты T6 (тесты, ADR-0003, нет RNG в Industry/Products, L1/L2); golden-хеши перезаписаны с причиной «D1».

## Тесты
По §11 D1.

## Связанные документы
Спека v0.14 (§1.1, §1.3, §1.4, A23, §6.9, §11); `docs/research/sun-start-data.md`; `docs/game-design/rank0-steel-g1-review.md`; `docs/game-design/COMPONENT_SELECTION.md`; DECISIONS_LOG #127, #130–#136.

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
