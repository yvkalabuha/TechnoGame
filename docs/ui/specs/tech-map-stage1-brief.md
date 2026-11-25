# Бриф для Gemini: карта технологий, этап 1 (TASK-0192, шаг 1)

## Кратко для владельца (RU)
- Это задание для Gemini (ведущий художник по UI, #578): нарисовать три экрана новой карты технологий. Gemini рисует, Claude-ревьюер проверяет по правилам §12.
- Экраны: (1) обзор «год от центра» с полосой из 6 институтов внизу; (2) карточка выбора узла (цена, срок, направления, военная ценность, кнопка «Исследовать в институте», фокус до/после); (3) путь к цели с итогом «N узлов · цена · ≈ мес.» и кнопкой «Разложить по институтам» (предложенные очереди подтверждает игрок).
- Рамки: 1920x1080 при 100 %, стиль «Надпечатка» (бумага, тушь, чертёжные линии), цвет никогда не единственный носитель смысла, всё достижимо с клавиатуры, у чисел есть единицы, чужие игры не копировать.
- Что нужно от ядра (поля и команды) ещё не существует: рисуем под поля из SCIENCE_SLOTS §12; до их появления клиент покажет «нет данных».
- Не проверено: набросков ещё нет; число «≈ мес.» в пути к цели — оценка ядра, формулы в статусе [ЗАГЛУШКА].

---

## 0. Задача
Sketch three screens for the main screen of the game, the technology map, after owner decision #561 (the player picks research nodes by hand; an institute is a slot). Output: three 1920x1080 frames at 100 % UI scale plus a greyscale twin of each, annotated with sizes in du (1 du = 1 px at 1920x1080, 100 %). State per screen: what is focused, what is disabled and why.

Sources to read first: `docs/ui/specs/pivot-561-tech-map.md` (§1.1–1.2 card order, §2.5 institute strip, §2.3 path to goal), `docs/game-design/SCIENCE_SLOTS.md` (§1–§3, §4.3, §12), `docs/ui/UI_DESIGN_SYSTEM.md` (§2 tokens, §7.10 controls, §12 criteria C1–C8, A1–A12). Current frames for reference only: `shots-out/TASK-0161-r2/tree_year_overview_1920x1080.png`, `tree_year_step3_1920x1080.png`.

## 1. What exists today (keep or fix)
- Keep: the year ring (#473): radius = year, 5 sector hatches (to become 12 directions later: aero, prop, met, mat, chem, struct, mfg, elec, sens, ctrl, nuc, ord; draw 5 now, leave room for more), node shapes by type (knowledge triangle, principle, engineering, component pentagon, process square, product diamond), state by form (unknown, can-open dotted, known, mastered filled, selected thick ring, goal double frame, goal route = thick double dash).
- Observed defects to avoid repeating (TASK-0161 verdicts): the legend block (about 560x360 du) sits in the empty field and covers the map; the node card starts with a 200 du illustration before any number; "effects: no data" text; the "Now: 1978" plate crossed by a ring stroke; decade labels clipped at the map edge; sector captions clipped; the node counter lies on hatching without a backing plate. Every label must sit on a solid paper backing, drawn last, fully inside the map window.
- The legend collapses into a "?" button. Counter "18/37" must be explained ("mastered / total").

## 2. Constraints (all three screens)
- Style "Nadpechatka": flat paper, ink lines, no shadows, no rounding, no gradients, no glow, no paper texture, no cards-with-shadow (§1.6). Panels separated by rules and frames (`DraftFrame`), buttons `DraftButton`, tabs `DraftTabs`, scale `DraftScale` (§7.10).
- Tokens only: paper `#F2EFE6`, sheet `#FAF8F2`, hover `#E8E4D8`, ink `#1D1E20`, ink-2 `#55575A`, ink-3 `#64625C` (only >= 12 du, never for essential data), rule `#BCB6A8`, alarm `#C42A1C` on `#FFFFFF`, warn `#E89A1A` plate with ink text (warn text on paper `#8A5300`), command `#2347A8` on `#FFFFFF` (player intent and goal route). alarm/warn/command are never decoration or headings. Direction colours come from the existing tint/ink set (§17.3); do not add new hues.
- Fonts: OpenGost Type B for frames, tabs, buttons, scales, captions; Golos Text for body; PT Mono for numbers. Check the font licence note in §3.2 (SIL OFL needs owner approval; propose nothing new).
- 1920x1080 at 100 % is the judged frame; body text >= 14 du, nothing legible only at 150 %. Layout must still scroll cleanly at 125 % (card scrolls, strip does not clip).
- Colour is never alone (A5): each state has a shape, hatch or word. Deliver greyscale twins and make them readable.
- Numbers always with units: "48 units", "14 mo", "+10 %", "12 units/yr". Gauges (progress bars) carry the number beside them ("6 / 14 mo") and have a plain-number reading (C3, C7).
- Keyboard: every control reachable by Tab / arrows / Enter / Esc; focus ring >= 2 px, >= 3:1 on every fill it can sit on (A6, A7); draw one focused state per screen. Space = pause everywhere. Mouse-only drag is not allowed as the only path (drag node onto institute may exist as a shortcut only).
- Text budgets and EN+RU: strings by key, +30 % reserve over the longest string (§13). Draw with Russian text as the main sample.
- Reduced motion ("Меньше движения"): no animation required to read state.
- Must be buildable in Godot 4 .NET with own procedural drawing (`_Draw()`), no raster assets for data. A node picture, if any, is a static illustration <= 100 du high and sits at the bottom of the card.
- Originality: do not copy layout, icons, wording or trade dress of any existing game (tech trees, grand strategy, 4X). The look comes from drafting sheets (GOST 2.303 line types), as in the design system.
- No invented simulation values: draw fields only from §4 below. Where the value is unknown show "no data" and a disabled control with its reason.

## 3. Screen 1 — Overview map with the institute strip
Purpose: "where are we, where are we going, who works on what".
- Top bar and tabs as today. Map window: year ring on the left, about 1144 du wide; right inspector 760 du wide (empty-state text "Pick a node on the map or in the strip" instead of an empty field).
- Header line above the map: "Nodes: 99 · open: 51 · shown: 14 of 99" (shown count only when a filter is on), search field "Find node…" with the key `/`, filter chips with "×" and "Reset".
- On the ring: node "in work" gets an hourglass shape and an institute number "I2" next to it (link node-to-institute visible on the map); military node = double contour; clusters "+N" instead of overlapping icons at overview; the "today" ring as a double thin line (not dotted: dotted already means "can be opened").
- **Institute strip** at the bottom, full width of the map area, about 1144x120 du (may extend under the inspector if you justify it). Six cards (one per institute slot; the country has 2–12 institutes, six is the design case; show how a 7th+ scrolls or pages, not shrinks):
  1. name + 2–4 direction words/hatches (e.g. "Aero · Prop");
  2. active node name (one line, ellipsis key allowed only via `.short`);
  3. progress bar with number: "6 / 14 mo" (and month-left word: "8 mo left");
  4. focus level 0 / 1 / 2 as a three-step mark with the word ("Focus 1"), not colour only;
  5. queue count: "Queue 3 / 5";
  6. free badge: a framed word "FREE — assign" on an empty slot (shape + word; warn plate allowed because the player must act).
  Show in one frame: one card active with focus, one with queue, one free, one under construction ("Building: 18 mo left"), one disabled/unaffordable. Above the strip: "Institutes: 4 of cap 6 · upkeep 12 units/yr" and button "Build institute".
- Selecting a card opens the institute's queue in the inspector (active + up to 5 queued, reorder with Alt+Up/Down or buttons "Up/Down"; "Remove").
- Focus state: show the focus ring on one strip card and one node.

## 4. Screen 2 — Node choice card (right inspector, <= 880 du high, scrolls at 125 %)
Order from top (see pivot-561 §1.2; edit if you can prove a better order with measurable reading time):
1. Header: node name, directions (2–4 words with hatch swatches, first is main), type shape, historical year "≈ 1970", status plate (word + shape).
2. **Three key numbers on one line (28 du):** "Cost 48 units · Time ≈ 14 mo · Military: direct". Military value is ONE WORD from three (direct / prep / base) or empty; no 0–5 scale and no dots (SCIENCE_SLOTS §3.2). A tooltip explains each word.
3. Main button "Research in institute ▾" (command style). When disabled, the reason is printed under it in words with a number, never colour only: e.g. "No free institute", "Taken: institute 2, free in 5 mo", "Prerequisite: Film cooling", "Queue full (5)", "Funding below 60 %", "Already known".
4. **Institute table**, one row per institute: name · overlap with the node's directions ("2 of 3: aero 4, prop 3") · speed ("+11.7 %") · time with it ("13 mo") · state ("free" / "busy until 5 mo" / "no bonus: directions differ"). Sorted by speed. Row Enter = assign (to slot or queue). Best row marked by a word "best", not only a tint.
5. **Focus before/after:** two lines "Without focus: 14 mo" / "Focus 1: 10 mo, +120 units", price and "pay now / monthly" choice, a button "Buy focus" with its own disabled reason.
6. "What it gives" (effects as numbers; "no data" until the core has them) and "What it opens next" (list rows >= 32 du high, node state by legend shape).
7. "What blocks it" (collapsed when the node is available), then parameters, then the small illustration.
- Draw two states: normal and disabled-with-reason. Show also the compare mode sketch (two columns A/B with "B cheaper by 12 units" arrow-and-word) as an inset, small.

## 5. Screen 3 — Path to goal
Purpose: "what do I have to research to reach the product, and in which order, with which institutes".
- Goal is a node or product; the route on the map is highlighted as a thick double dash in `command` plus numbered markers 1…N on the route nodes (number carries order; colour is not alone). Off-route nodes dim to outline, no labels.
- Header of the map: "To goal: 7 nodes · 310 units · ≈ 38 mo" (number + unit each; "≈" because it is a greedy-schedule estimate with a range, show "35–42 mo" in the inspector). Button "Next on route" (centres and selects the first available node).
- Inspector: ordered route list (rows: number, name, cost, time, state shape, bottleneck mark with word "bottleneck").
- Button "Lay out over institutes" opens a confirmation sheet (inset sheet, not a modal wall): table institute -> proposed queue (active + up to 5), for each queue "total 96 units · 12 mo", conflicts and waiting items ("waits: needs X"), then buttons "Apply" (command) and "Cancel"; every proposed line editable (move to another institute, remove). Nothing is applied without Apply; no auto-pick. State clearly "Queues do not reserve money".
- Draw a second state: goal unreachable (reason in words, e.g. "Not in frontier: first Film cooling").

## 6. Deliverables and acceptance
- 3 colour frames + 3 greyscale twins at 1920x1080, 100 %; one additional crop at 125 % of screen 2 showing the scroll.
- A table of sizes in du for strip card, key numbers line, buttons (touch/click targets >= 32 du high), focus ring (>= 2 px).
- A list of states per control (normal, hover, focus, pressed, disabled with reason).
- Self-check against C1–C8 and A1–A12 with contrast numbers for ink-2 / ink-3 text on paper and sheet, and for the focus ring on each fill.
- Open risks: more than six institutes; 300+ nodes; clutter at overview; Russian string length.

## 7. Data this design may use (SCIENCE_SLOTS §12) [Предл., not in Session yet]
Node: dirs[2..4], cost, teffMonths, monthsWithFocus[1..2], flowPerMonth, militaryTag (direct/prep/base/none), effects[], openedBy[], state, whyNot. Institute: id, dirs, dirLevel, activeNodeId, queue[0..4], progress, monthsLeft, focusLevel, overlapWithSelectedNode, speedWithSelectedNode, upkeep, buildProject, isFree. Country: instituteCount, cap and reason. Queries: FitInstitutes, FocusQuote, PathToGoal, SuggestQueues. Commands: AssignNode, Unassign, ReorderQueue, SetFocus, BuildInstitute, CancelBuild, ApplyQueues. Do not draw a value outside this list.


## Directions (update by Orchestrator, O-091/O-093)
There are 13 research directions, not 5: aero (aerodynamics), prop (propulsion), met (metals), mat (materials), chem (chemistry), struct (structures), mfg (manufacturing), elec (electronics), sens (sensors), ctrl (control), nuc (nuclear), ord (ordnance), space (space). Show them as short labels with a distinct shape mark each (not colour alone). A node shows 2–4 of them, main first; an institute shows 2–4 with levels 1–5.
