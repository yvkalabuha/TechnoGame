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
3. Wait for all results. Do not merge partial results.
4. For each finding decide: accept (change the artifact or create a task), reject (with reason), or escalate to the owner.
5. Write a "Разногласия" section in the artifact: | # | тема | позиции | итог |. Include only real disagreements and rejected findings.
6. Report to the owner in Russian: accepted, rejected, questions for the owner (ask them with AskUserQuestion).

Artifact and criteria: $ARGUMENTS
