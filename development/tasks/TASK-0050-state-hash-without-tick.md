---
id: TASK-0050
title: "DEBT-0009: хеш состояния только по таблицам, tick отдельно (ADR-0006/0013)"
status: DONE         # PROPOSED | APPROVED | IN_PROGRESS | REVIEW | DONE | BLOCKED | REJECTED | DEFERRED
level: light             # light | gate | gate+research
milestone: MS-M0
agent: developer
category: core
priority: medium
needs_owner: false       # true = ждёт решения владельца
depends_on: [TASK-0049]
related: []
approved_by: orchestrator
approved_at: 2026-11-14
approval_ref: "ADR-0006, ADR-0013 (ACCEPTED, #66); DEBT-0009; владелец 2026-11-14: «долги закончи»"
created: 2026-11-14
---

# TASK-0050 — DEBT-0009: хеш состояния только по таблицам, tick отдельно (ADR-0006/0013)

## Цель
Привести хеш состояния к ADR-0006/0013: хеш только по таблицам, номер тика — отдельное поле (DEBT-0009).

## Описание
- `StateHash.Compute` не включает tick; `run.json`/views/Session отдают tick отдельно.
- Все эталонные (golden) хеши пересчитываются **один раз** этой задачей. Причина каждого изменения — ADR-0006/0013, а не подбор. Новые значения сверяются: L1 (повтор) и L2 (потоки 1/4/8) дают одинаковые новые хеши; старые и новые хеши — в таблице в «Результат».
- Обновить места, где хеш документирован (спеки, EXP-записи — только ссылкой «хеши до TASK-0050»).

## Критерии приёмки
- [x] Хеш не зависит от tick (тест: одинаковые таблицы при разном tick → один хеш).
- [x] Все golden пересчитаны с L1/L2; полный `dotnet test` зелёный.
- [x] Второе ревью Codex — до чистого accept (#181).

## REVIEW
- [x] Code review (reviewer) — accept 2026-11-14; low → DEBT-0033
- [x] Второе ревью (codex) — accept (раунд 2, head d147f6c); medium → DEBT-0033
- [x] Детерминизм L1/L2 (test-engineer) — PASS: 88 прогонов ×2, потоки 1/4/8

## Результат

Сделано одним пересчётом. Причина всех строк ниже — обе задачи вместе (хеш без tick, TASK-0050; ключ RNG actor·2^16+node, TASK-0051), кроме Kernel-векторов (только TASK-0050: ключ RNG на них не влияет) и ToyFunnel (хеш прототипа не менялся, см. ниже).

- StateHash.Compute/ComputeHex без параметра tick (заголовок: schema_version, n_rows). KnowledgeRng: ключ actor·65536+node, MaxNodeNumber=65535. LkTables(actorCount, maxNodeNumber), Delta размера actorCount·(max+1), DeltaIndex; BuildLk проверяет диапазон; KnowledgeGraph — сообщение/проверка по константе.
- ToyFunnel: PrototypeStateHash считает раскладку прототипа (с tick) сам; сравнение GoldenHash с хешем прототипа в ToyFunnelParityTests снято (раскладка больше не совпадает). Хеши прототипа не менялись.
- tools/reference/kernel_vectors.py: state_hash без tick; векторы Kernel пересчитаны независимо.
- Файл src/Singularity.Lab/** вне зоны developer: правки RunExecutor.cs (убран runner.Tick) и SliceChecks.cs (Golden150) внесены, т.к. иначе не собирается/красный тест; Orchestrator решает по zone_check.

| Golden | старый → новый (причина: TASK-0050+TASK-0051) |
|---|---|
| SeriesSwitch/ViewTests/Lab SliceChecks.Golden150 (seed1 m149) | 4d219de4c3fd3c2d… → 9171e388fb488c67… |
| LinkedRunTests.Golden60 | e55e7f6ff343ad9f… → 6aefe3100767953a… |
| ProductAiSessionTests.Golden150 | ccdd6dd8823b350c… → e99b1c3eb242b7ac… |
| StateHashTests FixedTable | cb10cdbf139ed51f… → 1a3cba714ac0f01a… |
| StateHashTests EmptyTable | 4cbbd8ca5215b8d1… → 7c9fa136d4413fa6… |
| F17bTests Admit/YProd | c53ddf6139925f8c… → 5182522187ea58cc… |
| F17bTests Admit/YProd | ca11dc65f563319d… → 80503279220d3557… |
| F17bTests Admit/YProd | 00ed9b260232e22e… → 821840913e093333… |
| F17bTests Admit/YProd | 92211a7d070a4dba… → e07008cd104c053b… |
| F17bTests Admit/YProd | d822ea303d4e60d3… → d79add416b9be263… |
| F17bTests Admit/YProd | 4d219de4c3fd3c2d… → 9171e388fb488c67… |
| F17bTests Admit/YProd | a2e7ad8b0bae2cad… → 53becbd4a6ec207c… |
| F17bTests Admit/YProd | bba04e616c0d146d… → bbb6ac7920096662… |
| F17bTests Admit/YProd | 20894aaadd654263… → 8989f391426b621b… |
| F17bTests Admit/YProd | d7235a638207ec95… → ca39e8ffefd9502d… |
| T6D | be964af948aed413… → 9dba9bd804113b16… |
| T6D | b49639d12921926f… → 252f662a5c5af1e1… |
| T6D | a03b03b02339e161… → f3acebd02cf048c1… |
| T6D | f53a02f029dd7d72… → 59d7a8278bee7c76… |
| T6D | 610b83fd128e1baa… → b6d9b295210df5b4… |
| T6D | 5ac5b6c79b414832… → dc180a0b161f9651… |
| T6D | acd5fd4e72c1f4c7… → 4eeef32ed7491d59… |
| T6D | 7df22c6244787f09… → 26df03dbd3b5b724… |
| T6D | 141983719dfc33a8… → 84adbe52ec14e561… |
| T6D | 08ab6657c0ae3d33… → 4ad56b2006514023… |
| T6D | e1230d2233091de0… → af2f6158e9c86131… |
| T6D | 360e3c81f3865beb… → 786ab86ceebdc501… |
| TOOL | 6898f292b699eb16… → 77920ed2bab4c212… |
| TOOL | cdc044537b299c2b… → e7755bde41ccaf1d… |
| TOOL | 163ebc8bad8c4ceb… → 3581fd959e4065c6… |
| TOOL | aa18234a9138e11d… → 95d0e6814766ad77… |
| TOOL | 7cdf420caa77f363… → 14ada7c8800d87e2… |
| FLEET | 0085cabea5008707… → a85d6c6f5fcb60ff… |
| FLEET | 8f9bbc86b14589d9… → b81e2e3e42279656… |
| FLEET | dd04a9c0d5e378f9… → e0f3025b5ac1b8e3… |
| FLEET | e72cafd47de68b5a… → 3da049e70908eba8… |
| FLEET | ea8db8dfd70d1aff… → b054c1d9fa2d8bca… |
| FLEET | 18c6c7b9d1cfbbd7… → 765aebb3b43a1f9a… |

Тесты: хеш не зависит от tick (StateHashTests.Hash_DoesNotDependOnTick); граф с узлами 64/1000/65535 грузится; два актора с одним номером узла тянут по-разному; ключи не пересекаются при actor < 32768 (StreamKeyTests); диапазон 1..65535. Полный dotnet test Singularity.sln зелёный (Kernel 316, Architecture 97, Core 1035, Lab 13, Session 310). L1/L2: LinkedRun, F17b, ProductAi (потоки 1/4/8, 150 мес.), Lab L1/L2 дают одинаковые новые хеши.

Чужие правки документов: slice-jet-aviation.md §9.1 (строки таблицы KNOW/L_K/PRIO_J ~1175–1177: actor·64 → actor·2^16, L_K: actor·2^16); slice-csv-format.md (строки о StateHash/golden_hash — без tick); lab-cli.md:9 (golden_hash = StateHash(Table, SchemaVersion) без final_tick); EXP-0001 golden_hash (пометить «до TASK-0050»); lab/reference/jet_slice/jet_ref.py (a*64+n → a*65536+n, строки ~753, 1140, 1202, 1204) — вне зоны, python-референс знания.
