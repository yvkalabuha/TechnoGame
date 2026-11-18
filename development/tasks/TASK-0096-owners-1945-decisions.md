---
id: TASK-0096
title: "Owners 1945 data per decisions 397, 399-402"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M1
agent: developer
category: data
priority: medium
needs_owner: false
depends_on: []
related: [TASK-0091, TASK-0095]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "#397, #399-#402"
created: 2026-11-18
---

# TASK-0096 — Owners 1945 data per decisions #397, #399–#402

## Цель
Привести данные владения 1945 и карту к решениям владельца.

## Описание
- #397 Данциг: owner POL с t0 (сейчас DEU без события передачи).
- #399 протектораты: owner = держава-протектор — проверить, что так во всех случаях (`docs/research/owners-1945-world.md`).
- #400 Берлин: два региона (Запад, Восток) по границам округов из разрешённых источников (PD/CC0/CC BY, ADR-0009); dl-de/by-2.0 не использовать.
- #401 спорные: Тибет owner/controller TIB (до события 1950); Огаден GBR; Спратли без владельца (если формат не допускает — записать как отклонение и предложить представление).
- #402 Тайвань: #313 без изменений (owner JPN, controller ROC), добавить комментарий-условность в данных.
Файлы: `mods/core/scenarios/1945/owners.yaml`, `tools/map/spec/*`, при необходимости пересборка карты (`tools/map/check_determinism.sh`).

## Критерии приёмки
- [ ] Данные соответствуют #397, #399–#402; события передачи, ссылающиеся на изменённые строки, согласованы (RR-0020, `scripted-events-conditions.md`).
- [ ] Карта пересобрана, детерминизм (две сборки побайтно равны) и тесты MapBuilder зелёные; `dotnet test Singularity.sln` зелёный.

## Результат
Ветка `worktree-agent-a8fd86a0076c8db94`.
- #397: `danzig` owner POL, controller POL (событий передачи на эту строку нет).
- #399: проверены все 18 строк `status: protectorate` (и Сикким, Гоа и др.): owner = держава-протектор везде; изменений нет, метки `# Q61` убраны.
- #400: Берлин уже два региона `berlin_west`/`berlin_east` (разрез по OHM relation 2692239 «Westberlin», CC0); dl-de/by-2.0 не использовался. Новой геометрии не нужно. Секторы США/Британии/Франции по-прежнему не нарисованы (`not_drawn`).
- #401: Тибет owner=controller TIB; Огаден: выделен регион `ethiopia_ogaden` (изоляция ETH-3134 + ETH-3135, куски `somali_1..4`), owner/controller GBR; Спратли без владельца.
- #402: Тайвань без изменений, комментарий-условность в owners.yaml.
- Файлы: `mods/core/scenarios/1945/{owners,polities}.yaml`, `tools/map/spec/{12-isolate,regions/world,ids/world}.yaml`, пересобранные `mods/core/map/*`.
- Проверка: build 0 ошибок (3124 провинции, 374 региона); `check_determinism.sh` OK; MapBuilder 55/55; `dotnet test Singularity.sln -c Release` зелёный.

### Отклонения (после ревью)
1. Спратли: фиктивная полития `UNOWNED` убрана. В tools/map (не Core): `owner` nullable, новый статус `unclaimed`; Validator допускает `owner: null` только со статусом `unclaimed` и наоборот требует null; controller обязателен (JPN, гарнизон). Render не закрашивает такой регион и не тратит слот палитры. Тесты на правила: `Owners_unclaimed_allows_null_owner_only_with_that_status`.
2. Огаден: Дире-Дава (ETH-3135) перенесена в слияние/разбиение Оромии; Огаден = только ETH-3134 (NE «Somali», фактически Огаден + Хауд).
3. Статус Огадена: статуса для военной администрации нет, оставлен `colony`, причина в тексте source строки.
4. Регионов 374 при лимите 250 (предупреждение уже было).
