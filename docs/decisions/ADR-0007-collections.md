---
id: ADR-0007
title: "Запрет коллекций с нестабильным порядком обхода"
status: PROPOSED
date: 2026-11-02
supersedes:
superseded_by:
related: [ADR-0002]
approved_by:
approved_at:
approval_ref:
---

# ADR-0007 — Запрет коллекций с нестабильным порядком обхода

## Решение (предложение)
- В коде, влияющем на состояние мира, запрещён обход `Dictionary`, `HashSet`, `ConcurrentDictionary`, а также `Parallel.ForEach` с общей записью и `AsParallel()`.
- Разрешено: массивы, `List`, сортированные коллекции, обход по индексу.
- `DateTime.Now`, `Environment.TickCount`, `Guid.NewGuid()` в состоянии мира запрещены.

## Проверка
Правило `csharp-core.md`; архитектурный тест или Roslyn analyzer на сборки ядра.
