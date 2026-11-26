# Code mode rules for the Orchestrator (owner #596)

- One script = one batch of work: gather what the next decision needs, act on it, verify, commit in the same script when the steps depend on each other.
- Independent reads, commands and agent starts go in one `Promise.all`.
- Do not call Poll after a script that already returned its result; wait only for scripts still running.
- Keep long outputs out of context: `preview(out)`, slice files, or `kb_db.py q` for registry/decisions/debt lookups.
- AskUserQuestion only when the owner is active (a fresh owner message in this turn); never leave it waiting for hours (ERR-0004). Every question is interactive (#587).
- Before dispatching: commit the task file with its final status first (ERR-0002); one agent per task, never re-dispatch while an unblocked agent may resume (ERR-0006).
- Never commit on main while a landing runs; run landings one at a time.
- After landing and pushing, read CI (`gh run list --branch main --limit 3`).
