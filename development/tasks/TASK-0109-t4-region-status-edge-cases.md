---
id: TASK-0109
title: "T4: region status edge cases for 1945 (ADR-0022 p.2.6)"
status: IN_PROGRESS         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M1
agent: research-scientist
category: data
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0022, TASK-0105]
approved_by: orchestrator
approved_at: 2026-11-18
approval_ref: "ADR-0022 follow-up (3), #410 (light)"
created: 2026-11-18
---

# TASK-0109 — T4: region status edge cases for 1945 (ADR-0022 p.2.6)

## Goal
ADR-0022 p.2.6 (ACCEPTED, #410): the legal status of edge-case territories in 1945 is to be checked by research-scientist and then fixed in `mods/core/scenarios/1945/owners.yaml` (T4). This unblocks TASK-0105 criterion 2 (the p.2.7 warnings).

## Description
1. research-scientist: verify each row of the p.2.6 table with sources (PD/CC0/CC BY data per ADR-0009, but legal facts may be cited from any source): the Channel Islands and Man; Puerto Rico and the US Virgin Islands; Alaska and Hawaii; the Algerian Southern Territories (Ouargla, Tamanrasset); Taiwan (#313); the Baltic states and Moldavia (#406). Also explain why ROC has 7 core regions in the data against the ADR's 6. Report in this task with sources.
2. developer: apply the confirmed `status` changes in owners.yaml; the map build then prints exactly the p.2.7 warnings (GBR, FRA "< 5"); MapBuilder tests and check_determinism.sh pass.

## Acceptance criteria
- [ ] Every edge case has a sourced verdict.
- [ ] owners.yaml is updated; the build warnings match ADR-0022 p.2.7 or the difference is explained.

## Отчёт research-scientist
Полный отчёт: `docs/research/TASK-0109-edge-case-report.md`. Итог: 8 регионов core -> colony (guernsey, jersey, isle_of_man, puerto_rico, united_states_virgin_islands, algeria_ouargla, algeria_tamanghasset, taiwan); Аляска, Гавайи, Прибалтика, Молдавия остаются core. Ожидание: USA 5, SUN 31, GBR 2, FRA 4, ROC 7 (два региона Монголии — сомнительная пара, оставлены core).
