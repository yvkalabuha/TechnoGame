# CURRENT STATE

## Сводка
- Фаза: MS-M0 (срез «реактивная авиация», HYP-0002) — T10, T11, подготовка T12 (TASK-0054/0055) DONE; T12 (TASK-0056) — калибровка запущена 2026-11-15; далее gate и заморозка → EXP-0002. PHASE F: волны 1–2 закрыты (RR-0005…0014 FINAL), волна 3 (RR-0015…0019) не начата.
- Решения: до #230 (2026-11-14: #197–#230 — ключ RNG, сеть заводов, личности, торговля, космос/ядро, `c_eff`, дневная нарезка со свежим чтением #228).
- Контракт слоёв v2.4 r11 (erratum); спека среза v0.25 r2; формат CSV v0.5 r7; предложения к контракту — `docs/architecture/notes/contract-proposals-wave1.md` rev 2 (ждут gate).
- Модель не откалибрована (T12 впереди); golden пересчитаны 2026-11-14 (хеш без tick, ключ RNG `actor·65536`).
- Внешние исполнители (ADR-0015): Codex — второй ревьюер кода; DeepSeek — третий голос каждого gate, с размышлением (TASK-0052, лимит gate-задачи $1, #226); потрачено $1,62 из $5.
- Стек: .NET 10.0.403 в `~/.dotnet`; Python 3 + PyYAML; gitleaks 8.30.1 в `~/.local/bin`.
- Последний handoff: `development/handoff/2026-11-14-1.md`.

## Активные задачи
<!-- registry:active:start -->
- TASK-0076 [REVIEW] Localization fixes after Codex review of TASK-0074 (developer)
- TASK-0064 [APPROVED] lab bench harness (ADR-0016 T1) (developer)
- TASK-0066 [APPROVED] Tick benchmark run and outcome (ADR-0016 T3) (simulation-engineer)
- TASK-0073 [APPROVED] Audio listening pack from AUDIO_DIRECTION r2 recordings (developer)
- TASK-0075 [APPROVED] Map builder T2: tools/map on NTS, Europe first, 1945 cuts, coverage simplification check (developer)
<!-- registry:active:end -->

## Блокеры
- ADR-0003 (численная политика) PROPOSED; ADR-0011 (карта) PROPOSED — нужен до E1 (плюс иерархия регионов/провинций #195 и граф `d_max`).
- #228 требует ADR к ADR-0006 (gate+research) до кода E1.
- CI job `llm-worker` не проверен на GitHub.

## Точка продолжения
См. `development/handoff/2026-11-14-1.md`: принять TASK-0054 → ревью TASK-0052 → T12 калибровка; параллельно gate по `contract-proposals-wave1.md`.
