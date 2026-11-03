# CURRENT STATE

## Сводка
- Фаза: PHASE F, волна 1. PHASE E завершена (HYP-0001 REJECTED, фреймворк v3, CI зелёный на 3 ОС).
- Решения владельца: старт 1945 (#23), асимметрия держав (#24), срез «реактивная авиация» (#25, #29), мягкий порог способности (#26), лидер по техуровню (#27), без случайности в способности/изделии (#28), культура спадает до порога (#30).
- Утверждено: LAYER_CONTRACT v2 и ADR-0012 (#31). RR-0005…0008 — DRAFT, требуют правок по LAYER_CONTRACT §12.
- Стек: .NET 10.0.403 в `~/.dotnet` (в неинтерактивных оболочках — полный путь, DEBT-0001); Python 3 + PyYAML.
- Последний handoff: `development/handoff/2026-11-03-1.md`.

## Активные задачи
<!-- registry:active:start -->
- TASK-0009 [REVIEW] Kernel: контрольный тест отсутствия FMA-сжатия на всех платформах CI (developer)
- TASK-0018 [REVIEW] Core.Products для среза: КБ, варианты, line_E, дефекты, когорты, upkeep, FV, заглушка L_P (T5) (developer)
- TASK-0017 [APPROVED] Смоук калибровочных якорей C1–C3 на слоях знания и способности (T4s) (simulation-engineer)
<!-- registry:active:end -->

## Блокеры
- ADR-0003 (численная политика): FMA на ARM64 проверяется TASK-0009 в CI при push фазы; реализация контракта ждёт ACCEPTED ADR-0003.

## Точка продолжения
Сессия 2026-11-03 (длинная): решения #32–#80 в DECISIONS_LOG. Срез «реактивная авиация»: HYP-0002 EXPERIMENTAL (#48), спека `docs/simulation/slice-jet-aviation.md` v0.9+, слои знания и способности в main (TASK-0014/0015 DONE), слой изделий — ветка `worktree-agent-adfc3fdb48ab41772` (TASK-0018 REVIEW, reviewer approve; не сливать до решения по правилу культуры и D-тестов).
1. Смоук T4s (`docs/simulation/smoke-t4s.md`): C2 недостижим из-за правила роста культуры (F2). Идёт выбор правила по предрегистрированным критериям (#80): `docs/simulation/culture-rule-selection.md` (simulation-engineer, Lab-прототипы). Затем gate по правке контракта §4.1 → владелец → LAYER_CONTRACT v2.4, эталон, Core, повтор T4s.
2. Спека изделий v0.10: масштаб выявления дефектов (F1), потолок поколения альтернатив (F2), D-тесты изделий (simulation-engineer).
3. Затем слить TASK-0018 (после test-engineer и D-тестов); T6 ИИ+события, T7 why_not, T8 тесты P0 + DEBT-0009 (хеш без tick), T10 модель Lab (CSV-формат и `lab/analysis/exp0002.py` готовы), T12 калибровка и gate, EXP-0002.
4. Параллельно отложено до вердикта HYP-0002: срез 2 (черновик `docs/architecture/LAYER_CONTRACT_v3_DRAFT.md` r2, #61–#72), срез 3 цепочки (RR-0010, #73–#75). UI: направление A, `docs/ui/UI_DESIGN_SYSTEM.md` v0.1 (#54–#56, #67, #76). Тест-инфраструктура: ADR-0013/0014 ACCEPTED (#66), реализация позже (#58).
5. Push в конце фазы (не делался в этой сессии).
