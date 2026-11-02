using System.Globalization;
using Singularity.Kernel;

namespace Singularity.Lab;

/// <summary>Everything a model needs to build its initial state.</summary>
public sealed record ModelSetup(ParamSet Parameters, ulong Seed, int ChunkSize, long Ticks);

/// <summary>
/// A model the Lab can run. Registered in <see cref="ModelRegistry"/>.
/// Must live in a world-state assembly later (see docs/simulation/lab-cli.md, OPEN on placement).
/// </summary>
public interface ILabModelFactory
{
    /// <summary>Manifest "model" value.</summary>
    string Id { get; }

    /// <summary>True for stand-in models that produce no scientific result; recorded in run.json.</summary>
    bool IsTestModel { get; }

    string Description { get; }

    ILabModel Create(ModelSetup setup);
}

/// <summary>
/// One model instance for one run. All persistent state is in <see cref="Table"/> (ADR-0005),
/// so <c>golden_hash</c> = StateHash(Table, SchemaVersion, final tick) covers it.
/// </summary>
public interface ILabModel
{
    int SchemaVersion { get; }

    Table Table { get; }

    IReadOnlyList<ITickSystem> Systems { get; }

    /// <summary>CSV files this model writes. The Lab prepends a "run" column to each.</summary>
    IReadOnlyList<MetricTableSpec> MetricTables { get; }

    /// <summary>
    /// Called after setup (tick 0) and after every tick with the new Current state.
    /// Reads only; writing state here would bypass the tick contract.
    /// </summary>
    void Observe(long tick, IMetricSink sink);
}

/// <summary>A metric CSV: file name without extension and column headers (without "run").</summary>
public sealed record MetricTableSpec(string Name, IReadOnlyList<string> Columns);

public interface IMetricSink
{
    /// <summary>Appends one row to metric table <paramref name="table"/> (index into MetricTables).</summary>
    void Row(int table, params string[] cells);
}

/// <summary>Exact, culture-independent number formatting for metric CSVs.</summary>
public static class MetricFormat
{
    public static string Int(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Fixed-point raw value as decimal raw/10^6, exact, trailing zeros trimmed ("1.5", "-0.000001", "3").</summary>
    public static string Fx6(long raw)
    {
        // Int128 so that long.MinValue negates safely.
        Int128 v = raw;
        bool negative = v < 0;
        if (negative) v = -v;
        Int128 whole = v / Singularity.Kernel.Fx6.Scale;
        Int128 frac = v % Singularity.Kernel.Fx6.Scale;
        string text = whole.ToString(CultureInfo.InvariantCulture);
        if (frac != 0)
            text += "." + frac.ToString(CultureInfo.InvariantCulture).PadLeft(6, '0').TrimEnd('0');
        return negative ? "-" + text : text;
    }

    public static string Fx6(Fx6 value) => Fx6(value.Raw);
}
