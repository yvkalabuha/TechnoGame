---
paths:
  - "src/**"
  - "tests/**"
---

# C# core rules (ADR-0001..0007)

## Determinism (world-state code: Singularity.Kernel and Singularity.Core)
- No iteration over `Dictionary`, `HashSet`, `ConcurrentDictionary` when the order can affect state. Use arrays, `List<T>`, sorted collections, index loops.
- No `System.Random`. Use the counter-based RNG from `Singularity.Kernel` keyed by (seed, tick, system, entity, n).
- No `float`/`MathF` in world state. No `Math.Sin/Cos/Exp/Log/Pow` in world state; use Kernel implementations.
- Money, stocks, population: fixed-point `long` types from Kernel.
- No `DateTime.Now`, `Environment.TickCount`, `Guid.NewGuid()`, `Stopwatch` values in simulation logic.
- The architecture test is conservative: a method that touches a hash or set type (including `IDictionary`/`ISet` interfaces) must not enumerate anything. Move the lookup into its own method.
- `double.Parse`/`TryParse` only with an `IFormatProvider` (InvariantCulture).
- Parallel code: the compute phase reads the previous state and writes only to its own columns or per-chunk buffers; merge applies buffers in a deterministic order. No `Parallel.ForEach` with shared writes, no `AsParallel()`.

## Structure
- `Singularity.Kernel`: tick pipeline, RNG, fixed-point, tables. No domain logic.
- `Singularity.Core`: domain systems; namespaces per domain (`Singularity.Core.Economy`, ...).
- `Singularity.Lab`: CLI only; Core never references Lab.
- `tests/Singularity.Architecture.Tests` checks these dependencies.

## Style
- .NET 10, nullable enabled, warnings as errors.
- Small, readable code. Comments in English, explain why, not what.
- Every behaviour change comes with tests.
