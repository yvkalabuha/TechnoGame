---
id: TASK-0181
title: "3D fighters G1-G6 v2 by Gemini (#544, #557): slot kits, paper-style sheet, art director acceptance"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: 3d-modeler
category: client
priority: low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: owner
approved_at: 2026-11-25
approval_ref: "#557"
review_ref: "ui-art-director: PASS round 2 + Gemini pass; reviewer: code acceptable, determinism and slot test run by orchestrator (3 equal sha256, test PASS); codex accept 851522b3, leftovers DEBT-0109; merge 446daee"
created: 2026-11-25
tags: [task, visual, external-llm, done]
---

# TASK-0181 — 3D fighters G1-G6 v2 by Gemini (#544, #557): slot kits, paper-style sheet, art director acceptance

## Цель
Набор G1–G6 v2 от Gemini по замечаниям арт-директора (#557), принятый на кадрах 1920×1080.

## Описание
Ветка worktree-agent-a29723ac651be56ae (cd4357e): BRIEF, fighters_v2.blend, 30 GLB, лист цветной и серый, скрипты рендера. Обзор: `docs/ui/reviews/3d-v2-review-2026-11-25.md`.

## Критерии приёмки
- [ ] ui-art-director PASS с мнением Gemini (#450)
- [ ] Рендер детерминирован (два прогона, одинаковые хеши)
- [ ] ATTRIBUTION после приёмки; reviewer APPROVE для скриптов
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
Ветка `worktree-agent-a29723ac651be56ae`: cd4357e (набор v2), 5839caa (правки раунда 1), a637766 (Codex раунд 1, #3–#5), следующий коммит (Codex раунд 2, #1–#3).

**Файлы**
- Бриф Gemini: `assets/models3d/fighters_v2/BRIEF.md`; сцена `assets/models3d/fighters_v2/fighters_v2.blend` (226 КБ).
- 30 GLB по слотам: `assets/models3d/fighters_v2/g1..g6/gN_{fuselage,wing,tail,engine,afterburner}.glb` (457 КБ всего; у G1/G2 форсаж только корень, слот ab_none).
- Кадры: `sheet_1920x1080.png` (113 КБ), `sheet_1920x1080_grey.png` (103 КБ), `pair_g2_g5.png` (69 КБ).
- Скрипты: `tools/models3d/fighters_v2.py` (export / render / test), `tools/models3d/fighters_v2_fix.py` (правки раунда 1 без окна), `tools/models3d/blender_exec.py` (код в живой Blender через сокет MCP; код выхода 1 при ошибке Blender).

**Приёмка**
- [Факт] ui-art-director: раунд 1 CHANGES, раунд 2 PASS по визуальной части (`docs/ui/reviews/3d-v2-review-2026-11-25.md` §5).
- [Факт] Gemini (#450): `visual-TASK-0181-3d1ffd39-gemini.md`, pass, 2 low (сокращение «Пок.»), расхождений с арт-директором нет.
- [Факт] Детерминизм: оркестратор сделал два рендера без окна в разные каталоги; у них и у закоммиченных PNG все три sha256 совпали. Исполнитель повторил это после правок Codex #3/#4, PNG не изменились:
  - sheet `7ab1d36c4be7fa2362331347c8fb4896ee0b5b890397241d715f36eb9d255873`
  - grey `da52b0a635edd54cc222421b47cde7f93590cee4920710eb89de8e88ec604cb0`
  - pair `fa104117f16bacf3a2695834206852b7ed7f54b9dfe6865da9c01a955b76dc07`
- [Факт] Тест слотов (`blender -b fighters_v2.blend -P tools/models3d/fighters_v2.py -- test`) PASS. Проверено:
  - у каждой части есть свой корень в начале координат и нужные точки ATTACH;
  - у каждой части G1–G6 после сборки из GLB положение корня и габарит совпадают со сценой (0,0000 м);
  - негативный случай (ATTACH_tail у G3 сдвинут на 0,10 м) ловится.
- [Факт] Codex раунд 2:
  - #1: экспорт снимает суффиксы Blender (`.001`…), и во всех GLB точки крепления названы точно по брифу: `ATTACH_wing`, `ATTACH_tail`, `ATTACH_engine` в фюзеляже, `ATTACH_afterburner` в двигателе. Тест читает имена узлов прямо из JSON внутри GLB. На старых GLB он дал FAIL (exit 1), на новых PASS. Перевыгружены фюзеляж и двигатель G2–G6.
  - #2: любое исключение даёт exit 1 через `sys.exit(1)` в `main`; команда в доке с `--python-exit-code 1`. Тест без сцен (`--factory-startup`) даёт exit 1.
  - #3: проходы рендера пишутся во временный каталог процесса (`tempfile.mkdtemp`), а не в каталог вывода.
  - Два рендера после правок в разные каталоги и закоммиченные PNG: все три sha256 прежние.
- Не проверено: кадр 1 плана (окно «Линия производства»), GLB в Godot, пиксельные толщины линий.

**Черновик строки ATTRIBUTION** (для `mods/core/ATTRIBUTION.md`, вносит оркестратор):
> Fighter models G1–G6 v2 (`assets/models3d/fighters_v2/`, GLB parts and sheets): procedural geometry made in Blender 5.3 (tool only; output not GPL) by Gemini gemini-4-neon-high via agy through the Blender MCP, 2026-11-25, brief `assets/models3d/fighters_v2/BRIEF.md`; headless fixes, export and render by `tools/models3d/fighters_v2_fix.py` and `tools/models3d/fighters_v2.py`. No third-party models or textures; generic shapes, no replica of a real aircraft. Licence: project's own (ADR-0009).
