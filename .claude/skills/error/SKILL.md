---
name: error
description: Record a platform error in the catalogue (who, kind, cause, prevention) or a repeat of an existing one. Use the moment a mistake is found - by the owner, a reviewer, Codex, CI, a hook or yourself (owner #584).
---

# /error - record a platform error

1. Look for an existing entry: `python3 tools/development/errors.py list` (or `kb_db.py q errors text~<word>`). Same symptom: `errors.py repeat ERR-NNNN`.
2. New entry: `python3 tools/development/errors.py add --executor <orchestrator|agent:TYPE|tool:NAME|model:NAME> --kind <process|code|data|ui|comms|tooling|determinism> --area <short> --symptom "<what happened>" --cause "<why>" --found-by <owner|reviewer|codex|ci|hook|self> --cost <low|medium|high> --fix "<what fixed it now>"`.
3. Choose the prevention, strongest first: hook > test > skill > rule > memory. Make it, then `errors.py close ERR-NNNN --prevention <kind>:<ref>`.
4. Commit `development/errors.yaml` with the change that prevents it.
