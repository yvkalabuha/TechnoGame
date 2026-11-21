---
id: TASK-0117
title: "Client UI scale policy: text never below the A1 floor at 1280x720"
status: REVIEW         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M05
agent: developer         # ответственный агент
category: client
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0112, TASK-0111]
approved_by: orchestrator
approved_at: 2026-11-19
approval_ref: "#417 A1; ui-art-director review of TASK-0112 (light)"
created: 2026-11-19
---

# TASK-0117 — Client UI scale policy: text never below the A1 floor at 1280x720

## Goal
Owner decisions #424 and #425: the game runs fullscreen or as a borderless full window; the minimal supported screen is Full HD 1920x1080; larger screens scale the interface up. Today the client is laid out for a 1600x900 window and draws smaller text in smaller windows (ui-art-director, TASK-0112 review: criterion A1 fails). Set the scale policy for the whole client and give the owner one command to start the game.

## Description
1. ui-art-director: write the policy into `docs/ui/UI_DESIGN_SYSTEM.md` (§4 and §12): the base layout is 1920x1080; what one du is there; integer or fractional scale for 2560x1440 and 3840x2160; the text floor in rendered pixels; the settings "Масштаб интерфейса" 100–150 % on top of it. Windows smaller than 1920x1080 are test-only and may scale down.
2. developer: start fullscreen or borderless by default (a setting: fullscreen / borderless window; a framed window only with a command-line flag for tests and `--shots`); implement the policy (`ContentScaleMode`, `ContentScaleFactor`, `ContentScaleSize`); fix the layouts that break at 1920x1080; `--shots` at a real 1920x1080 render.
3. developer: `./play.sh` in the repository root starts the game fullscreen with one command (build if needed, then run); a line in README or `docs/ui/` says how to quit.

## Acceptance criteria
- [ ] The game starts fullscreen or borderless with `./play.sh`; Esc or the menu quits.
- [ ] A1, A2, A9 pass on the key screens at 1920x1080 (frames are a real 1920x1080 render), UI scale 100 % and 150 %.
- [ ] Client tests pass; code review APPROVE; ui-art-director PASS.
- [ ] TASK-0112 (credits) and TASK-0111 (instruments) get their visual PASS for A1 on the new frames.
- [ ] From DEBT-0066 (reviewer, 2026-11-19): gauge hatching in `GaugeControl.cs:39` has contrast of at least 3:1; shares at level 0 can be selected from the keyboard (`ActorPanel.cs:235`).
- [ ] Rail and tree legend layout follows `docs/ui/specs/task-0117-rail-and-legend.md`; check T6 sees the real rects of the lock and the badge.

## Результат
Ветка `worktree-agent-ad4c3f162fff85a3a` (developer, раунды 1–6). [Факт] Сделано:
- Политика масштаба: полноэкранный старт, кадры 1920x1080, масштаб 100/125/150 % (матрица кадров в `Main.cs` `BuildSizeShots`); крупные дисплеи 2560x1440 снимаются (`--shot-size`, режим fullscreen), 3440x1440 на этой машине невозможен: экран 2560x1440, окно зажато до 2560x1413, `SaveShot` отвергает кадр, прогон выходит с кодом 1.
- Рейл по спеке (`RailMetrics.Of` — один источник геометрии для рисования и T6), легенда дерева, сводки по клиньям, настоящая кнопка легенды, панель цели уступает легенде.
- Клавиатура: горячая клавиша или Enter на кнопке рейла переносит фокус в первую управляющую точку панели (`OpenArea(id, fromKeyboard)`), Esc возвращает фокус на кнопку области; Space ставит паузу при фокусе на ползунке. Проверка в прогоне идёт только настоящим путём.
- Прогон кадров завершается кодом 1 при любом провале живой проверки (T6, рейл, дерево, ввод); пустой набор измерений = FAIL; известные провалы палитры перечислены по точным именам как «known, DEBT».
- Тесты: `RailMetricsTests`, `TimeGlyphMetricsTests`, `ValidatorFormatTests`. Кадры раундов лежат вне репозитория в `shots-out/task-0117-r*`.
- Находка: Tab из рейла не входит в панель (отдельные области Tab) — решено явным переносом фокуса при открытии с клавиатуры.
- Раунд 7: список «known, DEBT» привязан к паре (палитра, имя) и только к V02; проверка итогов на приближении (zoom 1.2) требует хотя бы один итог в кадре; «fits without scroll False» роняет прогон.
- Раунд 7: отложенный запрос фокуса панели сбрасывается при закрытии и при открытии мышью; сообщение об отказе кадра называет запрошенный размер.
- Раунд 8 (Codex): квадрат «почему нет» 16 du в дереве и легенде (`SettingsLogic.WhyNotMarkDu`, тест `WhyNotMark_AtLeast16PhysicalPxAtBaseScale`); прогон `--shots` берёт fullscreen, когда экран равен размеру кадра (1920x1080), иначе borderless; итоги клиньев проверяются и на zoom 2.6 + pan; ошибка `SavePng` валит прогон.
- Раунд 8: `run-globe-spike.sh` передаёт все опции после `--shots` (исправил Orchestrator, 0aafb11: файл вне зоны developer); полный прогон кадров (r8, вне репозитория) завершён «shot run result: OK (exit 0)». Остаток ревьюера: квадрат «почему нет» в легенде стоит на 6 du выше значков строк (`TechTree.cs:738`) — DEBT-0068.
- Раунд 9 (Codex, обход по категориям): A значимые значки ≥16 du по измеренному контуру (легенда: радиус на тип `GlyphGeometry.LegendRadius`, глиф узла 0.62 радиуса плиты, квадрат «почему нет» выровнен по строке; тесты `IconFloorTests`); B оба режима окна = Godot Fullscreen, `ScaleFloorOk`, лог px/du через 1 с после `Apply`, проверка в прогоне; C главное меню и ведомость цели сняты на 100/125/150 %, прогон падает, если экран из набора 100 % отсутствует на 125/150; D скрытая легенда не резервирует место и не берёт Esc/L (`LegendOpen` = флаг и режим A).
- Раунд 9: полный прогон кадров (r9, вне репозитория) «shot run result: OK (exit 0)», покрытие масштабов 50 экранов на каждом из 125/150 %.
- Раунд 10: ведомость цели переносит строки вместо «…», подпись «сейчас»/«+n» в одной строке с заголовком группы над столбцами; проверка прогона `ShotClippedText` (обрезанные строки и Label) на 100/125/150 %; fullscreen = ExclusiveFullscreen, borderless = Fullscreen (решение Оркестратора); тесты берут геометрию из кода (`WhyNotLegendMark`, `IconsPlateMin`).
