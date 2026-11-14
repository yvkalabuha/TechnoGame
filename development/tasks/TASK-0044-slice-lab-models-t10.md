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
### simulation-engineer, 2026-11-13 — этап 1 сделан, этап 2 остановлен (нужна правка Session)
**Этап 1.** `docs/simulation/slice-csv-format.md` переведён на v0.3 под спеку v0.24. В нём: серии и параметры манифеста (`ParamSet` → `SliceRunConfig`, §3). Новые таблицы `JetSlice`:
- `a40.csv` — ветки О и С с классом: `PROCESSES`/`DEFECTS` и `CULTURE`/`COEXIST_MONEY`/`OTHER`; `cancel_design` = строки `branch = O`;
- `a40_c_months.csv`;
- `tooling_ledger.csv` — леджер A39 и `tool_unmet` по кластерам;
- `lines.csv` (F17b), `fleet_cmd.csv`, `licences.csv`.

`whynot.csv` получила колонки `cluster`/`order`/`unmet`. Запрещённый список `JetSliceCalib` дополнен: параметр `TOOLING_M_LEAD` содержит подстроку `lead`, поэтому в калибровочный манифест его не передают. Стенд C1 в двух постановках — `c1_stand.csv` (§6). Сверка с `exp0002.py` (§10): конфликтов колонок нет; новые таблицы и серии добавит T11.

**Этап 2 — остановлен.** [Факт: код] Lab не может прочитать мир:
- `SliceWorld` и адаптеры — `internal`, `InternalsVisibleTo` есть только у `Session.Tests`;
- `SliceSession` публично отдаёт только хеш, тик, дату, `why_not` и `CurrentRequirement`.

ADR-0014 п.5 и `SLICE_COMPOSITION.md` (строка 38) требуют read views и прямо говорят сделать их «вместе с первым потребителем (T10)». Это правка `src/Singularity.Session`, которую эта задача запрещает. Нужный набор описан в формате, §9: `ActorView`, `NodeView`, `DesignView`, `PolicyDecisionView`, `ToolingLedgerView`, `CapabilityView`. Это обёртки над уже публичными методами Core, новой логики в них нет. Модели не зарегистрированы, код Lab и тесты не писались: без чтения мира они были бы заглушками.

**Манифесты (черновики, не заморожены):**
- `lab/manifests/EXP-0002.json` — 53 точки, 12 880 прогонов, `hash` проходит;
- `lab/manifests/EXP-0002-calib.json` — seeds 10001…10100.

Модели в них ещё не зарегистрированы, поэтому команда `run` сейчас падает с «Unknown model».

**Печать A40, seeds 1…20** (информационно, как TASK-0026). Прогон через `lab/runners/t6e/run_t6e.sh` (T6eHarness, архив HEAD 58d23a4, плечо V5, main, 384 мес.; golden pass), разбор — `lab/analysis/t10_a40_print.py`.

| Актор | С: seeds с ≥ 1 срабатыванием | О `PROCESSES`, всего (на seed) | О `DEFECTS` | Ожидание A40 (б) v0.16: С; О |
|---|---|---|---|---|
| USA | 0 | 5 (0,25) — **ниже полосы** | 0 | ≤ 0,16; 0,5–2,0 |
| GBR | 0 | 0 | 0 | ≤ 0,16; ≤ 0,15 |
| SUN | 0 | 4 (0,20) — на нижней границе | 0 | ≤ 0,16; 0,2–1,0 |
| FRA | 0 | 0 — **ниже полосы** | 0 | ≤ 0,16; 0,05–0,6 |
| SWE | 0 | 0 | 0 | ≤ 0,16; ≤ 0,15 |

- `n_O` = 0,45 на seed при ожидании 2,0 (полоса 1,0–3,0) — ниже полосы.
- `n_C` = 0 при ожидании ≤ 0,2.
- L-O0 (`DEFECTS` = 0) и L-C0 (срабатываний С нет) выполнены. Наибольший `idle_c` линии — 1 мес.
- Все 9 срабатываний О приходятся на мес. 323–381: проекты G3 USA (P1 ×4, P3 ×1) и G1/G2 SUN (P1, P3).
- Охрана v0.21 не сработала ни разу.

Как читать: полосы v0.16 объявлены для сборки T6c2 (`LINE_CLOSE = ADMIT`, без T6d, F17b и V5). Спека (A40 v0.20) требует объявить их заново до первого прогона main. Поэтому расхождение — материал для переобъявления, а не дефект. [Предп.] Ветка О стала реже, потому что V5 (A39) и F17b быстрее доводят процессы до допуска. FRA не доходит до проекта в стадии О ни в одном seed.
