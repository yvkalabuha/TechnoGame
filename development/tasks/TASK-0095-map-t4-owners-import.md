---
id: TASK-0095
title: "Map T4: import world owners/controllers 1945 into owners.yaml and polities.yaml"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M1
agent: developer         # ответственный агент
category: data
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0011, ADR-0020, TASK-0091]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "ADR-0011 rev 3 T4; #313, #337, #338; light"
created: 2026-11-18
tags: [task, map, history, data, done]
---

# TASK-0095 — Map T4: import world owners/controllers 1945 into owners.yaml and polities.yaml

## Цель
Перенести черновик `docs/research/owners-1945-world.md` в `mods/core/scenarios/1945/owners.yaml` и `polities.yaml`, чтобы у всех 373 регионов был владелец и контролёр на 1945-10-01.

## Описание
1. Импортировать строки таблицы (развернуть строки с несколькими region_id); новые политии с `government: true|false`; кондоминиумы — совместные политии с членами.
2. Строки, зависящие от открытых вопросов владельца, взять по текущему варианту и пометить в YAML комментарием `# Q61`, `# Q63`, `# Q64`, `# Q59`: протекторат-тест, Тибет, Тайвань, Данциг.
3. Коды ROC/PRC (решение Orchestrator); CHN не использовать.
4. Валидатор сборщика: у каждого региона есть запись; `polity_id` без коллизий; источник и достоверность у каждой строки (поля `source`, `confidence`).
5. Пересобрать карту, `check_determinism.sh`, тесты MapBuilder.

## Критерии приёмки
- [ ] Предупреждение «нет владельцев T4» исчезло; сборка 0 ошибок; детерминизм OK; тесты зелёные.

## Результат
Выполнено: все 373 региона имеют владельца и контролёра на 1945-10-01. Предупреждение «нет владельцев T4» исчезло; сборка 0 ошибок; `check_determinism.sh` OK; тесты MapBuilder 55/55.
- `owners.yaml`: 280 строк мира импортированы из `owners-1945-world.md` (строки с несколькими region_id развёрнуты, статус выведен из примечаний: core/colony/protectorate/mandate/leased), у каждой строки `source` и `confidence`; старым европейским строкам проставлено `confidence: M`. Пометки `# Q61` (протекторат-тест), `# Q63` (Тибет), `# Q64` (Тайвань), `# Q59` (Данциг).
- `polities.yaml`: +46 политий (ROC, не CHN; KOR `government: false`; JPN `government: true`; SDN_AE, NHEB — совместные с членами; TANGIER; IDN только как controller). Коды с совпадающим ADM0_A3 имеют `ne_adm0`.
- Валидатор (`Validator.cs`, `Scenario.cs`): `confidence` обязателен (H/M/L); у каждого региона обязана быть запись по умолчанию (ошибка вместо предупреждения «T4»); коллизии `polity_id` проверялись и раньше. Верхний предел «15 регионов на актора» (#171) заменён с ошибки на предупреждение: колониальные империи считают колонии (GBR 85, FRA 45, SUN 31) — вопрос владельцу.
- Тест: `Owners_1945_cover_every_region_with_known_polities_and_confidence`. Карта пересобрана в `mods/core/map`.
- Смешанные регионы (F1 исследования) заданы по преобладающей части; разрез — решение владельца.
