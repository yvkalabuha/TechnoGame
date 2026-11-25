---
id: TASK-0180
title: "Data: real prerequisites for early G5/G6 knowledge nodes, restore G2-G4 pace (#555)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: developer
category: data
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [TASK-0171, TASK-0178, TASK-0179]
approved_by: owner
approved_at: 2026-11-25
approval_ref: "#555"
created: 2026-11-25
---

# TASK-0180 — Data: real prerequisites for early G5/G6 knowledge nodes, restore G2-G4 pace (#555)

## Цель
Вернуть темп поколений G2–G4 (#555): ранние узлы G5/G6 открываются около своей эпохи, а не в 1945.

## Описание
`docs/simulation/slice-pace-regression.md` (ветка worktree-agent-a8c6d813f0a636fb9): 10 узлов без предпосылок или с ранними (62 electronic_flight_control_theory, 63 edge_wave_diffraction, 64 single_crystal_growth, 82 phased_array_radar_pesa, 83 gaas_wafer_growth, 86 programmable_signal_processing, 90 electronic_engine_control, 91 high_modulus_fibre, 92 thrust_vectoring_nozzle, 95 silicon_carbide_fibre; проверить и остальные 62–99) делят бюджет науки с 1945. Шаг 1 (research-scientist): записка с настоящими предшественниками каждого узла из существующих узлов графа, с источником. Шаг 2 (developer): правка `requires` в `mods/core/nodes.yaml`. Шаг 3 (simulation-engineer): замер на 10 seeds — годы первой машины G2–G4 против окон C4 и прежней сборки (#555, #556), предрегистрация.

## Критерии приёмки
- [ ] У каждого изменённого узла предшественники с источником (URL + дата)
- [ ] Ни один узел G5/G6 не открывается раньше 1955 без явного основания
- [ ] Замер: APP G4 и доля G4 в окне C4 не хуже контрольного прогона из записки (≈ T12)
- [ ] Эталоны перезаписаны с объяснением (пакет изменений, меняющих эталоны); reviewer APPROVE
<!-- Какие тесты доказывают выполнение критериев. -->

## Связанные документы

## REVIEW
- [ ] Code review (reviewer) — находки high закрыты
- [ ] Тесты (test-engineer) — проходят, покрытие достаточно
- [ ] Детерминизм L1/L2 — если затронуты src/

## Результат
<!-- Заполняет исполнитель: что сделано, файлы, коммит. -->
