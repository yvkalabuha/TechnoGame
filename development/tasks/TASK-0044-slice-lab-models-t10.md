---
id: TASK-0044
title: "T10: Lab-модели JetSlice и JetSliceCalib, CSV v0.24, манифесты, печати A39/A40"
status: APPROVED         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light
milestone: MS-M0
agent: simulation-engineer
category: lab
priority: high
needs_owner: false
depends_on: [TASK-0040, TASK-0042]
related: [HYP-0002, EXP-0002]
approved_by: orchestrator
approved_at: 2026-11-13
approval_ref: "Спека среза v0.24 §11, строка T10; DECISIONS_LOG #182"
created: 2026-11-13
---

# TASK-0044 — T10: Lab-модели среза

## Цель
Срез запускается из Lab сериями по манифесту. Выходные CSV пригодны для анализа T11 (`lab/analysis/exp0002.py`) и калибровки T12.

## Описание
Источник: спека `docs/simulation/slice-jet-aviation.md` v0.24, §11 строка T10 (grep «T10»: 9 мест — печати A39, A40 и §6.x); §8.2 п. 2 (калибровочная сборка без колонок лидерства); §9 (детерминизм, хеши, манифест); `docs/simulation/slice-csv-format.md` v0.2. Формат v0.2 писался по спеке v0.6 и устарел.
Сборка мира и прогон уже есть в `Singularity.Session` (T6a–T6d; `tests/Singularity.Session.Tests` — `LinkedRunTests`, `ProductAiSessionTests`). Lab: `src/Singularity.Lab/` (`ModelRegistry`, `MatrixRunner`, `RunExecutor`, `Manifest`, `Csv`; ADR-0008).

**Этап 1 — формат.** `slice-csv-format.md` → v0.3 под спеку v0.24:
- таблицы и колонки печатей §6.x, леджера A39, `cancel_design`, срабатываний A40 О/С с классом причины;
- `tool_unmet`, `why_not`;
- C1 по двум постановкам — если печать идёт из Lab;
- запрещённый список для `JetSliceCalib`.
Этап 1 сверяется с `exp0002.py` (T11 его доработает) и коротко отражается в «Результат».

**Этап 2 — код.**
- `JetSlice` и `JetSliceCalib` в `ModelRegistry` поверх Session, без дублирования логики Session.
- Параметры из `ParamSet` манифеста: seeds, `T6D_OFF`, `LIC_OFF`, `FIX_*`, `START_PERT`, `START_SYM`, `TOOLING_TARGET`, `POOL_ORDER` — какие есть в Session.
- Манифест калибровки и шаблон манифеста EXP-0002 — в `lab/manifests/`. Манифест EXP-0002 не замораживается: это делает T12.

**Тесты** (в `tests/`):
- заголовок CSV `JetSliceCalib` не содержит запрещённых колонок и файлов;
- L1: два прогона дают одинаковые CSV и хеш;
- L2 на малой матрице;
- прогон через `MatrixRunner` даёт все таблицы у всех прогонов.

**Печать** на seeds 1…20 (как TASK-0026, информационно): ветки О и С по акторам и классам — для сверки с ожиданиями A40.

## Критерии приёмки
- [ ] `slice-csv-format.md` v0.3 согласован со спекой v0.24.
- [ ] Обе модели зарегистрированы; `dotnet run --project src/Singularity.Lab -- <manifest>` выполняет малую матрицу.
- [ ] Тесты: заголовок калибровочной сборки, L1, L2, полнота таблиц; `dotnet test` всего решения зелёный.
- [ ] Печать A40 на seeds 1…20 и её сверка с ожиданиями — в «Результат».
- [ ] `src/Singularity.{Kernel,Core,Session}` не меняются; если нужно изменить — остановиться и описать.

## REVIEW
- [ ] Code review (reviewer)
- [ ] Второе ревью (codex) — до чистого accept (#181)
- [ ] Тесты (test-engineer)

## Результат
