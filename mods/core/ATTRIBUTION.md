# Атрибуция данных мода `core` (ADR-0009)

| Данные | Файл | Источник | Лицензия |
|---|---|---|---|
| ВВП 1945 г. (`gdp_1945`) и темп роста 1945–1975 (`growth_pa`); через них `budget_index` | `scenario_1945.yaml` | Maddison Project Database 2023 — Bolt, J. and J. L. van Zanden (2024), «Maddison style estimates of the evolution of the world economy: A new 2023 update», *Journal of Economic Surveys*, 1–41. Получено через Our World in Data: https://ourworldindata.org/grapher/gdp-maddison-project-database (доступ 2026-11-03) | CC BY 4.0 |
| ВВП на душу населения 1945 г. (`gdppc_1945`); через него `culture_h`, `tooling_m`, `hcap` | `scenario_1945.yaml` | Maddison Project Database 2023 (то же), через Our World in Data: https://ourworldindata.org/grapher/gdp-per-capita-maddison-project-database (доступ 2026-11-03) | CC BY 4.0 |

Изменения: значения взяты для 1945 г. (для СССР — 1946 г.), округлены; `budget_index`, `culture_h`, `tooling_m`, `hcap` выведены из них по правилам R-BUD, R-H, R-M, R-HC (`docs/research/data/jet-slice/scenario_1945.md`).

Остальные данные мода — проектные параметры и даты событий (факты, не охраняются); источники дат — `docs/research/data/jet-slice/README.md`.
