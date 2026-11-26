---
id: TASK-0072
title: "ADR-0018 spike S1: Linguini.Bundle on .NET 10, Russian case/gender, plurals"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: tooling
priority: high
needs_owner: false       # true = ждёт решения владельца
depends_on: []
related: [ADR-0018]
approved_by: owner
approved_at: 2026-11-17
approval_ref: "DECISIONS_LOG #300"
created: 2026-11-17
---


## Goal
Spike S1 from ADR-0018: a throwaway console project that proves the Linguini.Bundle assumptions. The ADR is ACCEPTED only on condition that S1 passes (#300).

## Checks (read ADR-0018 for the exact list)
1. Linguini.Bundle 0.11.0 restores and runs on net10.0.
2. Russian case and gender: a term with attributes (`-germany = Германия` with `.ins = Германией`, `.gender = f`), used through the DataRef/variable approach of the ADR, gives «война с Германией» and «Франция объявила».
3. Plurals: the RU categories for 1, 2, 5, 11, 21, 111 and 1.5. The already formatted "1,0" must select `other`.
4. A message reference sees the caller's arguments (month cases).
5. Custom NUMBER/DATE functions are deterministic and culture-independent.
6. Licence: record the Unicode V3 notice text.

## Where
`tools/spikes/fluent-s1/`, outside Singularity.sln. Do not touch Core, Session, Kernel, Lab or the client.

## Acceptance
- A short report docs/architecture/notes/fluent-spike-s1.md: PASS/FAIL per check with output samples.
- Any FAIL → STATUS: needs-decision. Do not work around it.
