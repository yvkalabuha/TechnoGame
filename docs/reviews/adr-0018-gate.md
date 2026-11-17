# ADR-0018 localization — gate reviews (2026-11-17, commit 60c832e)

Transcribed by the Orchestrator. DeepSeek record: `tools/llm_worker/runs/gate-ADR-0018-localization-keys-locale-files-in-mods-en-and-ru-60c832e1-third.json` (changes_requested: 2 high, 8 medium, 4 low).

## Critic (no blockers, 6 major)
1. **Frozen experiments.** Do not edit frozen manifests or EXP records when DataHash changes. Record the old→new hash pair in DECISIONS_LOG, and prove that dynamics are unchanged with the EXP-0002-det `golden_set_hash`.
2. **`AnchorEvent` users.** TechTree.cs:421 and CoreNodesTests.cs:55 use it. Move the anchor check to Localization.Tests.
3. **Orphan keys.** An orphan key is an error only in the core test. At runtime it is skipped with a warning, because a mod may `delete` a node (ADR-0010).
4. **Two mods override the same key.** State the rule (ADR-0010 loud error, or last wins plus a warning) and add a test.
5. **Plural rules for added languages.** Either a built-in, hand-written table of CLDR rule families, or drop the claim. Do not ship CLDR data files: the Unicode licence is not in ADR-0009.
6. **Glyph coverage.** Test that the shipped fonts (#294) cover every character, the missing-key marker and the `xx` pseudo-locale.
7. **Stale translations.** A per-entry `source_hash`, or a sidecar file. An `mt: true` flag for machine-translated text.
8. Minor:
   - key grammar with dotted ids;
   - `enum.*` keys must come from data names, not C# member names;
   - L5 must also scan .tscn/.tres;
   - plural category from the formatted operands;
   - compact numbers, percent, minus sign;
   - quoted YAML values;
   - grapheme versus pixel width;
   - `xx` locale in Godot;
   - RTL out of scope;
   - Russian case as a known limit;
   - an explicit ModLoader rule for `locale/`.

## Advocate
- **(high) Russian case and gender.** A cheap subset: optional `name_gen/dat/acc/ins/prep` and `gender` for actors, countries and regions; placeholders `{country:ins}` and `{country:gender|m=…|f=…}`. Fluent stays rejected for now, but the premise "no .NET library" is wrong: Linguini.Bundle exists (Apache/MIT).
- **(high) Stale translations**, as critic item 7.
- **(moderate)** A `yaml <-> .po` export/import roundtrip in v1, for Poedit/Weblate, and an optional `note:` field per entry.
- **(moderate)** App-local ICU recorded as the future route.
- **(moderate)** Plural rules for mod languages, as critic item 5.
- **(low)** A project glossary of technical terms.

## DeepSeek
- **(high)** The key grammar claim "last segment is a closed field" is false for `enum`, `unit`, `fmt` and `event.option`.
- **(high)** `product_class` slot and alt keys are ambiguous with dotted ids; the `alt` placement is unclear.
- **Medium:**
  - L1 checks only `name`;
  - the L6 budgets do not match UI_DESIGN_SYSTEM §13.2;
  - the status of `budgets.yaml` under `locale/` is unclear;
  - the L5 pattern does not match the `L.T` facade;
  - the §14 change needs recording;
  - the override conflict policy;
  - plural rules for mod languages;
  - state-word limits 16/18.
- **Low:**
  - `date.month` keys are missing from the key table;
  - `Names.CountryOfActor` shows SUN as "Russia" (in 1945 it is the USSR);
  - auto_translate with plural keys;
  - ADR-0006 is missing from `related`.
