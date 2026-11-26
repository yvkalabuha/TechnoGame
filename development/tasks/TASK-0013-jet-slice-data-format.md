---
id: TASK-0013
title: "Формат данных среза: схемы, YAML мода core, загрузчик и валидатор (ADR-0010)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: developer
category: data
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0011]
related: [ADR-0010, ADR-0009, HYP-0002]
approved_by: orchestrator
approved_at: 2026-11-03
approval_ref: DECISIONS_LOG #48, #53
created: 2026-11-03
---

# TASK-0013 — Формат данных среза: схемы, YAML мода core, загрузчик и валидатор (ADR-0010)

## Цель
Перевести содержание данных среза (TASK-0011) в формат мода `core` по ADR-0010 и дать ядру загрузчик с валидатором — вторая часть задачи T2 спецификации.

## Описание
Источники: `docs/decisions/ADR-0010-data-format-modding.md` (YAML строгий подмножество + JSON Schema 2020-12), `docs/research/data/jet-slice/` (nodes.csv, informs.csv, product_class.md, scenario_1945.md), `docs/simulation/slice-jet-aviation.md` §1, §3.5, контракт `docs/architecture/LAYER_CONTRACT.md` v2.3 §2.
1. `mods/core/`: манифест, YAML узлов, класса `jet_fighter`, сценария 1945; `scenario_hash` по правилу спецификации §1.1.
2. JSON Schema для каждого типа; валидатор `cluster`/`discipline`/`capability_req` (RR-0008, спека §1).
3. Загрузчик в ядре: define/override/patch/delete по ID, громкая ошибка при конфликте или битой ссылке; результат — неизменяемые таблицы struct-of-arrays (ADR-0005) с детерминированным порядком (ADR-0007). Только Fx6/целые (ADR-0003 R1, тест `Core_OutsideR4Exceptions_HasNoDouble`).
4. Атрибуция Maddison (CC BY 4.0) в `mods/core/ATTRIBUTION.md`.
Зависимость YAML-парсера — с лицензией MIT/BSD/Apache; парсер не должен давать неявных типов.

## Критерии приёмки
- Загрузка `core` проходит; 61 узел, ключи и значения совпадают с таблицами TASK-0011.
- Отрицательные тесты: битая ссылка, дубликат ID, неизвестный кластер, неявный bool — громкая ошибка.
- Порядок загрузки и хеш данных стабильны (L1); архитектурные тесты зелёные.
- Ревью reviewer.
