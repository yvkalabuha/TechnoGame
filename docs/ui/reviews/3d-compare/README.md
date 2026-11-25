# Сравнение 3D: Claude и Gemini, 2026-11-25

Бриф: `docs/ui/specs/3d-compare-brief.md`. Оба исполнителя строили истребитель G4 в живом Blender 5.3 через Blender MCP. Рендер одинаковый: EEVEE, 1920×1080, одна камера, одно солнце, одно небо.

| | Claude | Gemini (gemini-4-neon-high) |
|---|---|---|
| Вызовов execute_blender_code | 12 | 10 |
| Время | ≈ 4 мин | 13,3 мин |
| Деталей | 17 | 41 |
| Кадр | `claude_g4.png` | `gemini_g4.png` |

**[Факт]** Владелец выбрал Gemini (решение #544): у него детальнее кабина, обтекатель, сопла, законцовки, ПВД и камуфляж.
Файл сцены (обе модели): `~/.cache/singularity/models3d/g4_compare_2026-11-25.blend` (вне репозитория).
Режим запуска Gemini — только Blender MCP разрешён: `agy -p "…" --model gemini-4-neon-high --sandbox --settings '{"permissions":{"allow":["mcp(blender)"]}}'`.

Модели в GLB (только свои детали, без камеры и света): `assets/models3d/g4_compare/claude_g4.glb` (17 деталей), `assets/models3d/g4_compare/gemini_g4.glb` (41 деталь). Экспорт без окна: `blender -b <файл.blend> --python export_g4.py`.

## Набор частей G4 (Gemini, 2026-11-25)
Бриф: `docs/ui/specs/3d-kit-g4-brief.md`. Gemini: 6 вызовов, 4,5 мин. Пять частей по слотам проекта: `assets/models3d/g4_kit/g4_{fuselage,wing,tail,engine,afterburner}.glb`. В фюзеляже точки крепления `ATTACH_wing`, `ATTACH_tail`, `ATTACH_engine`; в двигателе `ATTACH_afterburner`. Корень каждой части ставится в свою точку — сборка совпадает с моделью Gemini_G4 до 0 мм (проверка Gemini). Кадры: `gemini_g4_kit_assembled.png`, `gemini_g4_kit_exploded.png`.

Двигатель v2 (бриф `docs/ui/specs/3d-kit-g4-brief-v2-engine.md`, владелец: «двигатель — сопло только»): два целых ТРДДФ по 5,0 м, ⌀ 0,99 м — вентилятор, корпус, коробка приводов, трубопроводы, сухое сопло; форсаж — отдельный модуль 2,2 м. Снаружи сборка не изменилась. Gemini: 10 вызовов, 9,9 мин. Кадр: `gemini_g4_engine_closeup.png`.
