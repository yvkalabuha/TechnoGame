---
id: TASK-0161
title: "Design pass 2 B: knowledge tree by year rings and direction sectors, 6 zoom steps, icons, node menu, prominent close (#473)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M05
agent: developer         # ответственный агент
category: client
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0132, TASK-0156, TASK-0157]
approved_by: orchestrator
approved_at: 2026-11-25
approval_ref: "owner #473 (layout), #453, #466; owner request 2026-11-25 to start; light client task inside approved design"
created: 2026-11-25
---

# TASK-0161 — Design pass 2 B: knowledge tree by year rings and direction sectors, 6 zoom steps, icons, node menu, prominent close (#473)

## Цель
Дерево знаний: радиус = год (кольца по десятилетиям 1950–2030), секторы = направления, 6 шагов зума, значки на крупном зуме, эффекты узла (из `NodeEffects`, TASK-0157), меню узла (ПКМ и удержание ЛКМ), заметная кнопка «Закрыть» и Esc (#473).

## Описание
Спека: `docs/ui/specs/design-pass-2-tree-science.md` §2б и задачи A/B, раздел «мнение Gemini». Только клиент. Выбор узлов с клавиатуры из ветки TASK-0132 (worktree-agent-a1bcca52483f7ba2b) перенести, если совместим с новой раскладкой. Кадры — одной пачкой с `--only`.

## Критерии приёмки
- [ ] Кадры 1920x1080 100 % и серый; ui-art-director PASS с мнением Gemini (#450)
- [ ] C1–C8, A1–A12 (§12); фокус ≥ 2 px
- [ ] reviewer APPROVE

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
### Раунд 5 (CHANGES арт-директора), ветка worktree-agent-a54c2ce2cc27cf78a
Подход изменён: вместо подвижек отдельных подписей введён слой «планируем рано, рисуем последним»; правила размещения вынесены в чистые функции `TreeLabelRules` (клип/пропуск) с тестами.
1. «Сейчас: 1945»: рамка и текст рисуются после рёбер и узлов (`DrawNowLabel`), плотная подложка, поле 4 px.
2. Подписи декад: планируются в `YearPlate`, рисуются последними (`DrawYearPlates`); не рисуются, если не помещаются целиком в окно карты, заходят под панель зума или на узел (`PlateFits`).
3. Подпись узла (и год у значка) только если значок виден целиком (`IconVisible`); сдвиг внутрь остался только для видимых значков.
4. Подпись сектора: сдвиг внутрь окна целиком (`ClampInto`, вместе с итогом под именем), скрывается, если ушла бы далее 90 du от сектора; усечение многоточием только для очень длинных имён.
5. Год справа от значка: отступ = реальный размер контура + 4 px (`Reach`, кэш размеров без аллокаций); «Узлов…» на подложке цвета бумаги и вне зоны подписей; «Сейчас» теперь двойная тонкая линия (пунктир остаётся только у «можно открыть»; легенда: «Двойное кольцо = сегодня»); несвязанные рёбра Ink2 α0.7 (темнее колец Rule α0.75).
6. Контраст вторичного текста Ink2 55575A: на Sheet FBF8EF 6.83:1, на Paper F3EEDF 6.25:1, на Hover EBE5D2 5.75:1 (порог 4.5:1; тест `SecondaryInkMeetsFourAndAHalfToOneOnPaperAndSheet`). Токен фокуса: `Ui.Focus` (Ui.cs:20, значение Palette.C.Command 2347A8, 7.13:1 на Paper), рамка `Ui.FocusBox()` 3 du.
Проверено: `dotnet test tests/Singularity.Client.Audio.Tests` 470/470; сборка клиента без ошибок. Кадры не снимались (нет разрешения).

### Раунд 6 и Codex, второй обзор (a9aeb20c)
- ui-art-director: **PASS** по блокирующим пунктам раунда 5 (раунд 6, `docs/ui/reviews/task-0161-visual.md`; кадры shots-out/TASK-0161-r2, 1920x1080, 100 %). Остальное арт-директор отнёс в этап 1 новой карты технологий (#561, `docs/ui/specs/pivot-561-tech-map.md`). Gemini на этот раунд недоступен (429/503), вердикт арт-директора.
- Codex #1: эффекты узла из `NodeEffects.Derive` показаны одной строкой в карточке и в подсказке («эффекты: …», до 4 пунктов и «ещё N»); пусто, если данные ничего не дают. #2: «Что мешает» из меню показывает требования самого узла без известного члена (`TreeClarity.UnmetGroups`), а не цепочку цели. #3: зум уважает «Меньше движения» (`Settings.Animations`: сразу в цель) и укладывается в 0,2 с (`PlanisphereMath.ZoomEase`, ставка 40; тест 1→12). #6: допуск удержания 6 du, перетаскивание только дальше него (`NodeMenuLogic.IsDrag`). #7: год на шагах 5–6 рисуется только целиком (`TreeLabelRules.PlateFits`). Низкие: убран дубль цикла St.Frontier, используется `ZoomKeepOut`.
- Codex #5 (радиальное меню по удержанию) и #8 (номер кольца в «Слой») не правились: входят в этап 1 новой карты технологий (#561).
- Тесты: Client.Audio.Tests 488 из 488 (nice -n 10; число 439 относится к ветке tree-0132, здесь добавлены тесты TreeLabelRules/TreeReviewFixes); сборка клиента без ошибок. Кадры этого раунда не снимались.
