---
tags: [handoff, platform, core, done]
---
# TASK-0115 doc patch (for the Orchestrator: docs/architecture is outside the developer zone)

## docs/architecture/SLICE_COMPOSITION.md §7.5 «Журнал»

Старый текст, первый пункт, после «кодек `CommandCodec`.» добавить (новый текст вместо предложения «Формат — JSON Lines ...» не менять, только дополнить):

> **Формат v2 (TASK-0115, DEBT-0052).** Первая строка — заголовок `{"commands_log":2}`. Ссылка на проект в команде — пара `design` + `design_gen` (у `compose`/`derive` с родителем — `parent` + `parent_gen`); поколение — то, которое видел отправитель. Журнал без заголовка (v1), версия ≠ 2 и строка без `*_gen` отвергаются `FormatException`, без миграции (журналов не сохранено, повтор — артефакт сборки; O-003). Устаревание поколения при `Submit` не проверяется (`BadHandle` только при `Generation < 1`): отказ по состоянию даёт слой изделия (`StaleHandle`) и воспроизводится сам.

Также в таблицу слотов §7.3 (после строки про «Одноразовые»): колонки слотов команд получили парные `cmd.dsn.N.gen`, `cmd.flt.N.gen`, `cmd.series.D.gen`, `cmd.close.D.gen`; у ИИ — `pol.dsn|flt|start|close.N.gen` (SchemaVersion поднята).

## docs/architecture/LAYER_CONTRACT.md

1. §3.1, после строки `g_p[design]` (стр. 93) добавить строки:

| `generation[design]` | изделие | `int` ≥ 0 | поколение слота: +1 при каждом занятии слота; 0 — слот не занимался. Освобождение не меняет | ADR-0005 п. 3, TASK-0099 |
| `parent_gen[design]` | изделие | `int` ≥ 0 | поколение слота родителя на момент создания производной (0 — родителя нет) | ADR-0005 п. 3 |

2. §4.4 (команды изделия), добавить абзац:

> **Ссылка на проект — `DesignHandle(Slot, Generation)`, не индекс (TASK-0115).** Команды `derive`, `cancel_design`, `retire_cohort`, `sell_cohort`, `start_series`, `close_line` несут handle. `ProductsSystem` безусловно проверяет `IsCurrent(handle)`: слот занят проектом того же поколения; иначе команда отвергается с причиной `StaleHandle` (счётчик `prd.rejected.StaleHandle`), состояние нового проекта не затрагивается. `SeriesStarts`/`LineCloses` отдают до двух кандидатов (внешний, затем ИИ) без аллокаций; применяется первый разрешимый, каждый устаревший считается как `StaleHandle`. Handle другого слота в кандидате — `ProductsInvariantException`. Решения ИИ несут поколение из снимка t−1 в момент решения (`PolicyDesign.SlotGeneration`; колонки `pol.*.gen`). Строка `ProductsSystem.StageOMonth` слота сбрасывается в −1 при каждом занятии слота (`Create` и канонический запуск L_P).
