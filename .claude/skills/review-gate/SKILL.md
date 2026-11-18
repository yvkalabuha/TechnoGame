---
name: review-gate
description: Run independent critic and advocate reviews (plus reviewer for code or ADRs) on one artifact in parallel and merge their findings and disagreements into the artifact. Use at every gate-level step before owner approval.
argument-hint: "<path to the artifact> [criteria]"
---

# /review-gate — independent review

1. Read the artifact's frontmatter and "Краткий вывод" / goal only, to write the prompts.
2. Start in ONE message, in parallel:
   - `critic` — prompt: artifact path, approved decisions to check against, criteria. No author reasoning.
   - `advocate` — same inputs; ask for the strongest alternatives.
   - `reviewer` — only if the artifact is code or an ADR.
   - DeepSeek third voice (mandatory, owner decision): `python3 -m tools.llm_worker gate --item <repo path> --stance third --profile deepseek-flash` (Bash, background). Commit the artifact first. If it is refused with `secret_detected`, find the false positive with gitleaks and fix the source; never weaken the scan.
3. Wait for all results. Do not merge partial results. Critic and advocate have no Write tool: save their reports to `docs/reviews/<artifact>-<role>.md`, and DeepSeek's summary next to them.
4. For each finding decide: accept (change the artifact or create a task), reject (with reason), or escalate to the owner.
5. Write a "Разногласия" section in the artifact: | # | тема | позиции | итог |. Include only real disagreements and rejected findings.
6. Report to the owner in Russian: accepted, rejected, questions for the owner (ask them with AskUserQuestion).

Artifact and criteria: $ARGUMENTS
