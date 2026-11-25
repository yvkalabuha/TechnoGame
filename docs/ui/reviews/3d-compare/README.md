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
