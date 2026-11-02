---
name: release
description: Milestone release. Checks completion criteria, writes the release file and CHANGELOG entry, creates a git tag and pushes.
disable-model-invocation: true
argument-hint: "<version, e.g. 0.1.0> <milestone id, e.g. MS-M0>"
---

# /release — milestone release

A release means the milestone reached a verified state, not just that code was written.

1. Read the milestone file. Every completion criterion must be checked and every linked task must be DONE (or explicitly REJECTED/DEFERRED with a reason). Otherwise stop and report what is missing.
2. Run the full checks: `python3 tools/development/registry.py check`; `dotnet build` and `dotnet test` if there is code; determinism checks L1/L2 if there is a core.
3. Create `development/releases/v<version>.md` from `development/templates/release.md`: goal, tasks, criteria, test results, known limitations, remaining problems.
4. Add a section to `CHANGELOG.md` (Keep a Changelog style, newest first).
5. Set the milestone `status: RELEASED`. Run `registry.py index` and `check`.
6. Show the owner a summary and ask for confirmation (AskUserQuestion) before tagging.
7. After confirmation: commit, `git tag -a v<version> -m "<milestone title>"`, `git push` and `git push origin v<version>`. Never force.

Arguments: $ARGUMENTS
