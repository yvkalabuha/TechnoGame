---
name: new
description: Create a new registry item (milestone, task, hypothesis, experiment, ADR, research report) from its template with the next free ID. Use whenever a new tracked item is needed.
argument-hint: "<MS|TASK|HYP|EXP|ADR|RR> <title> [--slug ascii-file-slug]"
---

# /new — create a registry item

1. Run: `python3 tools/development/registry.py new <TYPE> "<title>" --slug <ascii-slug>`
   - The slug is required for Cyrillic titles: a short English kebab-case name for the file.
   - The script prints the path of the new file.
2. Open the file and fill the frontmatter (milestone, agent, level, related ...) and the body sections that have content now. Delete template comments and sections that are not relevant; do not leave empty placeholders.
3. Run `python3 tools/development/registry.py check` and fix errors.

Types: MS (milestone), TASK, HYP (hypothesis), EXP (experiment), ADR, RR (research report).
Risks, tech debt and owner decisions are not items: they are rows in `development/RISKS.md`, `development/TECH_DEBT.md`, `docs/decisions/DECISIONS_LOG.md`.

Arguments: $ARGUMENTS
