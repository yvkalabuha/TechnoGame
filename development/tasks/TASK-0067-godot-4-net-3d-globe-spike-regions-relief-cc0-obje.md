---
id: TASK-0067
title: "Godot 4 .NET 3D globe spike: regions, relief, CC0 objects, FPS and memory"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: tooling
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0001, ADR-0009, ADR-0016]
approved_by: orchestrator
approved_at: 2026-11-17
approval_ref: "DECISIONS_LOG #284, #285 (owner asked for the spike)"
created: 2026-11-17
---


## Goal
A throwaway technical spike: show what a 3D-globe client costs on the owner's machine (i7-11800H, 31 GB). It is not production code and is not connected to the simulation (M2).

## Scope
1. Install Godot 4 .NET outside the repository, under `~/.local/opt/godot/`. Use the latest stable 4.x mono build for Linux from the official godotengine GitHub releases. Record the version and the sha256. Never commit binaries.
2. Create the Godot C# project in `src/Singularity.Client.Spike/`. Keep it out of the main solution, or make sure `dotnet build`/`dotnet test` of the solution are unaffected.
3. A globe:
   - sphere with an Earth texture or relief from a public-domain or CC0 source (for example NASA Blue Marble or Natural Earth raster; check the licence against ADR-0009);
   - region borders from Natural Earth admin-0 (and admin-1 if cheap), drawn as lines on the sphere;
   - camera orbit and zoom;
   - region pick on click, highlighting the region.
4. 3–5 CC0 3D objects placed on the globe (Kenney or Quaternius: a factory, an airfield, a plane). One simple "under construction → in service" animation, as in #263.
5. Measure FPS at idle, while rotating and zoomed in; VRAM and RAM; startup time; draw calls. Record the GPU model.
6. Data: download scripts or a README with the URLs. Put large data files in .gitignore, or commit only small, licence-checked files with a LICENSES note.

## Acceptance
- The project opens and runs headless-buildable (`godot --headless --build-solutions` or the equivalent) on this machine.
- A short report in `docs/ui/prototypes/globe-spike-report.md` with the numbers, screenshots (if a display is available; otherwise say so) and problems.
- The main solution's full `dotnet test` is still green.

## Результат
**Сделано** (с учётом правки `docs/ui/GLOBE_DIRECTION.md` v0.1: без фото, поверхность A «Глобус-лист»).
- Godot 4.7.2 mono установлен в `~/.local/opt/godot/` (sha256 архива `129f82db7bafd54ae14bb5bb284041c73860e8c7a009a3a026ca5e946cbff247`, sha512 совпал с официальным). Godot 4.7.2 работает на .NET 8 (в системе SDK 8.0.426 и 10.0.403), проект `Godot.NET.Sdk/4.7.2` + `net8.0`.
- Проект `src/Singularity.Client.Spike/` вне `Singularity.sln`; собственный `Directory.Build.props` отключает `src/Directory.Build.props`. Сборка: `godot --headless --path . --build-solutions --quit` — успешно (перед этим один раз `--import`).
- Глобус: фон `paper`, линия лимба, ID-текстура из Natural Earth 50m admin-0 (PD), заливка/границы в экранных пикселях/штриховка/сетка в шейдере, подписи и плашки тревог в 2D-слое, орбитальная камера, выбор региона кликом. 5 CC0-объектов (Kenney City Kit Industrial: завод, труба, башня, ангар; OpenGameArt Funky aircraft) + ВПП (процедурная) + 300 инстансов; стройка через параметр `progress`.
- Замеры, скриншоты (дисплей :0 был доступен), проблемы: `docs/ui/prototypes/globe-spike-report.md`, данные `docs/ui/prototypes/globe-spike/`.
- Файлы данных: небольшие (≈ 1,7 МБ JSON, 0,6 МБ моделей) закоммичены с `LICENSES.md`; `.godot/`, `*.import`, `bench-out/` в `.gitignore` проекта. Фото/Blue Marble не использовались.

**Проверено:** `bench` на RTX 3050 Ti: 500–960 к/с без vsync, VRAM 195 МБ (процесс 341 МиБ), RSS ≈ 0,8 ГБ, запуск ≈ 2,5–3,3 с. Главный вывод: ID-текстура 4096×2048 на Z2+ ступенчатая, нужны векторные линии.

**Отклонения / замечания:** kenney.nl отдаёт 403 на автозагрузку (обходить не стал), пакет взят с OpenGameArt (CC0). Самолёта и аэродрома в Kenney нет. Полный `dotnet test` решения см. в отчёте оркестратору.
