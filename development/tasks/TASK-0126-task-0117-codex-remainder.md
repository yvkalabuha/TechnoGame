---
id: TASK-0126
title: "Open Codex findings after TASK-0117 landing (#432)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M05
agent: developer
category: client
priority: medium
needs_owner: false
depends_on: [TASK-0117]
related: [TASK-0117]
approved_by: orchestrator
approved_at: 2026-11-22
approval_ref: "#432 (light)"
created: 2026-11-22
---

# TASK-0126 — Open Codex findings after TASK-0117 landing (#432)

## Цель
По решению владельца #432 задача TASK-0117 слита после круга, на котором ревьюер дал APPROVE; открытые замечания второго ревью Codex перенесены сюда, чтобы не потеряться.

## Описание
Замечания из `tools/llm_worker/runs/review-TASK-0117-6883be22-codex.json`:
- [high] `src/Singularity.Client.Spike/TechTree.cs:488` — The technology-tree clock draws its decade ticks at half the A2 minimum width.
- [medium] `src/Singularity.Client.Spike/Main.cs:890` — The screenshot matrix can pass without verifying that the requested UI scale was applied.
- [medium] `src/Singularity.Client.Spike/Main.cs:892` — The scaled main-menu screenshots capture the title menu over the live game HUD.
- [medium] `src/Singularity.Client.Spike/Main.cs:1417` — The screenshot run can report success after failing to save the flask probe frames.
- Ревьюер (круги 9–13): `Para` не ловит слово шире строки на всех путях; арт-директор (круг 13): подписи «Теплообмен в турбине», «Сборка и испытание ТРД» стоят далеко от узлов; подпись «№2» без названия. Аллокации в `_Draw` — DEBT-0073.

Каждое замечание: исправить или отклонить с причиной (в «Разногласия»). Высокие замечания по детерминизму и сохранению по #432 не переносятся — их в этом списке нет.

## Критерии приёмки
- [ ] Каждое замечание закрыто: исправлено с тестом или отклонено с причиной.
- [ ] reviewer APPROVE; второе ревью по правилу #432.

## Ход работы
1. TechTree DrawClock: (тест `ScaleTick_AtLeast2PhysicalPxAtBaseScale` проверяет только константу; отрисовку подтверждает замер кадра 100 %: тик 1910 шириной 2 px, x=428–429) засечки десятилетий 2 du (50 лет — 3 du), константа `SettingsLogic.MinTickDu` (A2); тест `ScaleTick_AtLeast2PhysicalPxAtBaseScale`. Исправлено.
2. Матрица кадров: `SettingsLogic.ScaleApplied` сравнивает наблюдаемые px/du (ширина окна / ширина холста) с политикой для настройки; `CheckScaleApplied` в Main.cs валит прогон, в логе строки «scale applied … PASS» (1.0/1.25/1.5). Тест `ScaleApplied_ComparesTheObservedScaleWithTheSetting`. Исправлено.
3. Заглавный экран в матрице масштабов: `TitleScene(true)` скрывает HUD (рейка, панели, дерево, кнопка «?») на время кадра main_menu; проверено кадром s150. Автотеста нет (код сцены Godot).
4. SaveProbe: оба SavePng идут через CheckSave. Автотеста нет (Godot).
5. Para: путь из кругов 9–13 — проверка в прогоне кадров сравнивала строку только с краями всей области, не с шириной абзаца. Теперь Para при записи сравнивает строку с её maxW (`TextWrap.Overflows`, тест), ошибки идут в список `bad` проверки заявления. Кроме того `TextWrap.Lines` не мерит \n как часть слова и не рвёт суррогатную пару (тесты). Раунд 2.
6. Подписи узлов: у подписи, к которой ближе другой узел, чем её собственный, рисуется линия-выноска (`LabelGeometry.NeedsLeader`, тест). Подпись «№2» без названия — см. «Разногласия».

## Разногласия
- «№2» без названия: номер — осознанный запасной вариант, когда имя не помещается рядом с узлом; название и номер стоят рядом в списке рубежей в панели заявления и в подсказке узла. Отклонено; ui-art-director может оспорить.

## Раунд 2 (ревью 638b59b)
Выноска и плашка цели: линия идёт от узла до края непрозрачной плашки имени (для выбранного узла/цели всегда; старая «полка» не рисуется, если имя подписано). Ядро проверки выноски одно (`LabelGeometry.OtherIsAsNear`). Кадры подписей: tree_caption_turbine/jet_test/frontier (кадр frontier пропускается с записью в лог, если в состоянии нет узла фронтира) в shots-out/task-0126. Текст «Нужен код сохранений (ADR-0013)» заменён на «Сохранение пока недоступно» (RU/EN). Подсказка узла с клавиатуры не открывается (выбор узла только мышью) — условие арт-директора по «№2» не выполнено, нужна отдельная задача.
