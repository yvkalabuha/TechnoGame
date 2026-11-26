---
id: TASK-0062
title: "llm_worker gate: file markers trigger gitleaks generic-api-key"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer         # ответственный агент
category: tooling        # core | lab | data | tooling | docs | design
priority: medium         # high | medium | low
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: []
approved_by: orchestrator
approved_at: 2026-11-17
approval_ref: light task, gate of ADR-0016 blocked
created: 2026-11-17
tags: [task, platform, external-llm, done]
---


## Problem
`python3 -m tools.llm_worker gate --item docs/decisions/ADR-0016-tick-budget.md` is refused with `secret_detected: generic-api-key@491`. The gate input contains the closing fence produced by `review._fence` (tools/llm_worker/review.py:91):
`=== END FILE docs/decisions/ADR-0014-session-test-api.md sha256:<12 hex chars> ===`.
gitleaks' default `generic-api-key` rule reads `api` (from the file name) + `:` + a 12-hex-char value as a key assignment. This is a false positive from our own marker, not a secret in the repository. Suppression is deliberately impossible (`--ignore-gitleaks-allow`, empty ignore path, ADR-0015 §8), and that must stay so.

## Change
- Change the closing fence format so that no keyword-plus-`:`-plus-hex pattern can appear, while keeping the anti-forgery hash. Example: `=== END {label} (sha256 {hex12}) ===`, the same style as the opening `=== FILE … (sha256 …) ===` in context.py:70, which passes the scan.
- Update `tests/test_review.py:271`.
- Add a regression test: a fence for a label containing `api`, `key` or `token` passes `gitleaks.scan_text`. Skip it if gitleaks is not installed, like the existing secret tests.

## Acceptance
- `python3 -m unittest discover tools/llm_worker/tests` is green.
- The gate input for ADR-0016 at HEAD scans clean: build with `review.build_gate_input`, then call `gitleaks.scan_text`.
- No weakening of the scanner configuration.

## Результат
- Закрывающий маркер в `tools/llm_worker/review.py` (`_fence`) теперь `=== END {label} (sha256 {hex12}) ===`; хеш сохранён, конфигурация gitleaks не менялась.
- `tests/test_review.py`: обновлён regex в `test_fence_end_marker_cannot_be_forged`; добавлен `test_fence_with_keyword_labels_passes_gitleaks` (метки с api/key/token; skip без gitleaks).
- Проверка: unittest discover зелёный; скан входа гейта ADR-0016 печатает `clean`.
