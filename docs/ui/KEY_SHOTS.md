# Ключевые снимки клиента в git (TASK-0097, #391)

Автор: ui-art-director, 2026-11-18. Правило: ~25 кадров на принятую версию в git, остальное генерирует `--shots` в игнорируемую папку. Выполнено в TASK-0097: остальные кадры удалены обычным коммитом.

[Факт] Текущий принятый клиент — v0.5c (v0.5d нет). Снимки по умолчанию: 1600×900, RU, 100%. Объёмы — замеры `du -sh` (см. итог ниже).

Все пути относительно `docs/ui/prototypes/`.

## 1. Оставить: v0.5c (25 кадров, `client-v05c/`)

Меню и потоки (3): `main_menu.png`, `new_game_selected.png`, `settings_video.png`.
Карта и глобус (4): `globe_lit.png`, `map_click_USA.png`, `map_click_FRA.png`, `globe_panel.png`.
Дерево технологий (3): `tech_tree_a.png`, `tech_tree_b.png`, `tree_legend.png`.
Журнал и рейка (2): `journal.png`, `rail_focus.png`.
Области, по одной (8): `area_none.png`, `area_science.png`, `area_production.png`, `area_finance.png`, `area_forces.png`, `area_intel.png`, `area_politics.png`, `area_projects.png`.
Доказательства валидатора T1–T6 (5 + пересечения с группами выше):
- T1 (контраст текста на текстуре), T4 (хрома): `textures_normal.png`; базовый кадр для сравнения: `textures_off.png`.
- T2 (тревога на текстуре), T3 (плашка без шума): `alert_tint5.png`; T5 (серая шкала, ночная сторона): `alert_tint5_night.png`.
- T6 (кромка, отступ 10 du, прокрутка при 150%): `area_finance_150.png`.
Итого: 3+4+3+2+8+5 = 25.

Намеренно не оставлены (генерируются `--shots`): все `_en`, `1280x720`, `1920x1080`, `1600x900_en`; `textures_weak`, `alert_tint1`, `globe_unlit`, `globe_panel_*`, `signal_*`, `rail_hover/press/badge/wide`, `time_*`, `settings_sound/general/rollback`, `menu_ingame*`, `new_game_empty`, `tree_zoom_*`, `tree_nodes_frontier_names`, `tree_legend_closed`, `settings_video_150`, `menu_ingame_150`. Это вариации размера, языка или состояния; на ревью их не хватает только для проверки обрезки текста при другом разрешении, а это делает ui-tester по свежему прогону.

Не PNG, оставить: `client-v05c/run-log.txt`.

## 2. Оставить: старые версии (визуальная история, 9 кадров)

| Версия | Кадры | Почему |
|---|---|---|
| globe-spike | `globe-spike/shot_rotate.png` | лимб, обод, плашки у обода (первый глобус) |
| client-v02 | `client-v02/globe_panel.png` | первый клиент |
| client-v03 | нет | v03 целиком повторён в v04/v05 |
| client-v04 | `client-v04/signal_active_new.png` | первое появление сигнала |
| client-v05a | `client-v05a/tech_tree_a.png`, `client-v05a/tree_zoom_icons_card.png` | планисфера, масштаб и значки узлов |
| client-v05b | `client-v05b/main_menu.png`, `client-v05b/tree_legend.png`, `client-v05b/alert_tint5_night.png` | первое меню, легенда, плашка на ночной стороне (цитируется в ревью) |
| map-1945 | `map-1945/europe-1945-10-01.png` | данные, не клиент; цитируется README; оставить целиком |

Не PNG, оставить: `client-v05b/run-log.txt`, `client-v05b/audio_demo.ogg` (звук, не снимок; размер неизвестен, решает sound-director).

## 3. Удалить

Всё остальное PNG в `client-v02`, `client-v03`, `client-v04`, `client-v05a`, `client-v05b`, `client-v05c`, `globe-spike` (кроме перечисленного выше).

| Папка | PNG сейчас (оценка) | Остаётся | Удалить |
|---|---|---|---|
| client-v05c | ~122 | 25 | ~97 |
| client-v05b | ~92 | 3 | ~89 |
| client-v05a | 29 | 2 | 27 |
| client-v04 | 19 | 1 | 18 |
| client-v03 | 12 | 0 | 12 |
| client-v02 | 4 | 1 | 3 |
| globe-spike | 5 | 1 | 4 |
| map-1945 | 1 | 1 | 0 |
| **Итого** | **~284** | **34** | **~250** |

[Факт] Объём `docs/ui` по `du -sh`: было 86 МБ (275 PNG), стало 18 МБ (34 PNG); удалено 241 PNG, ~68 МБ. Список выполнен 2026-11-18 (TASK-0097), все 34 перечисленных файла существуют. Вывод `--shots` по умолчанию: `shots-out/` (в `.gitignore`), нужные кадры копируются в `docs/ui` вручную.

## 4. Документы, ссылающиеся на удаляемые кадры

Исправить текст: сослаться на команду `--shots` или убрать имя.
- `docs/ui/CLIENT_V05B_SPEC.md:117` — `alert_tint*.png`, `alert_tint7` (v05b, удаляются).
- `docs/ui/CLIENT_V05B_SPEC.md:118` — `map_click_FRA.png` (v05b, удаляется; в v05c остаётся).
- `docs/ui/reviews/task-0081-visual.md:3` — «49 снимков `client-v05b/*.png`» (остаётся 3).
- `docs/ui/reviews/task-0081-visual.md:10` — `settings_video.png` (v05b, удаляется), `main_menu.png` остаётся.
- `docs/ui/prototypes/client-v02-review.md:3` — четыре снимка v02 (остаётся только `globe_panel`).
- `docs/ui/prototypes/client-v02-review.md:89` — 13 снимков `client-v03/` (удаляются все).
- `docs/ui/prototypes/globe-spike-report.md:44` — `shot_idle`, `shot_zoom`, `shot_region`, `shot_construction` (удаляются; `shot_rotate` остаётся).
- `docs/ui/reviews/task-0077-visual.md:3` — папка `client-v05a/` целиком (остаётся 2 кадра).
- `docs/ui/reviews/task-0088-visual.md:3` — папка `client-v05c/` (остаётся 25); названия кадров в :9, :21, :22, :31, :35, :38, :40, :41 — все в списке «оставить».
- Вне docs/ui (проверить построчно разработчиком): `development/tasks/TASK-0070`, `-0071`, `-0077`, `-0081`, `-0088` (ссылки на .png; не менялись, т.к. закрытые задачи, достаточно пометки «см. KEY_SHOTS.md»).
Без правки: `CLIENT_V05C_SPEC.md:43` (`textures_off/normal` остаются), `map-1945/README.md:3`, `docs/research/*` (посторонние .png).
