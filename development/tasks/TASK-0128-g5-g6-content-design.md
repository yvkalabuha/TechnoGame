---
id: TASK-0128
title: "G5/G6 content for the jet slice: nodes, alternatives, anchors (train 2, T-C)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: gate
milestone: MS-M05
agent: research-scientist
category: design
priority: high
needs_owner: false
depends_on: [TASK-0121]
related: [TASK-0129, TASK-0118, TASK-0119]
approved_by: owner
approved_at: 2026-11-19
approval_ref: "#423 (new data for later generations allowed); gate review before data commit"
review_ref: "gate: critic + advocate + DeepSeek (docs/reviews/slice-g5-g6-content-rev3-gate.md), rev 3.1; owner #457 #458 #464 #465; data landed in train 2 88fa41b"
created: 2026-11-23
---

# TASK-0128 — Контент G5/G6 для среза «реактивная авиация» (поезд 2, шаг T-C)

## Цель
Срез считает до 2030 года (TASK-0121), но новые знания кончаются в 1961–1970 годах, высшее поколение — G4 (`docs/simulation/m-play-horizon-2030.md`). Без G5/G6 у партии M-play около 40 лет простоя. Задача — спроектировать данные поколений 5–6, чтобы после 1970 года в гонке было новое знание.

## Описание
Шаг T-C из `docs/architecture/notes/slice-horizon-2030.md` §3 и §5 «Поезд 2».
1. research-scientist: узлы FND/PRN/ENG/PRC/APP для G5–G6 (около 10–16 узлов: композиты, FADEC, малозаметность, монокристаллические лопатки и т. п.), альтернативы, `af_class 5`, `pu_ranks`, исторические якоря с источниками (URL + дата обращения). Ограничения: tier ≤ 5; дисциплины — только существующие, иначе вынести в gate. Поправки спецификации среза (`docs/simulation/slice-jet-aviation.md` §1.4, §2, §8.1; A19 G5/G6).
2. Список правок Core, которые нужны данным (§3 п. 1–4 заметки), с отметкой: какие обязательны, какие по решению.
3. Gate: critic + advocate + третий голос; затем утверждение владельцем. Данные в `mods/` пишет developer после утверждения (отдельная задача поезда 2).

## Критерии приёмки
- [ ] Документ `docs/research/slice-g5-g6-content.md` (зона research-scientist) с «Кратким выводом», таблицей узлов (id, тип, поколение, дисциплина, tier, предпосылки, якорь, источник) и списком правок Core.
- [ ] Каждый якорь с источником; каждое число помечено [Факт]/[Предп.]/[Предл.].
- [ ] Ожидаемый эффект на гонку после 1970 года описан как гипотеза, без утверждений о балансе.
- [ ] Gate пройден, разногласия записаны; владелец утвердил.

## Связанные документы
`docs/architecture/notes/slice-horizon-2030.md`, `docs/simulation/m-play-horizon-2030.md`, `docs/simulation/slice-jet-aviation.md`, `mods/core/product_classes.yaml`, `mods/core/nodes.yaml`.

## Результат
