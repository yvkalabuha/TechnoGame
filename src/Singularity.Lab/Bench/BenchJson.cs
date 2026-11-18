using System.Text.Json;
using Singularity.Session;

namespace Singularity.Lab.Bench;

/// <summary>Parameters shared by the bench modes; written into every output as "params".</summary>
public sealed record BenchRunParams
{
    public required string Mode { get; init; }
    public required string RepoDir { get; init; }
    public string? ManifestPath { get; init; }
    public string? ManifestHash { get; init; }
    public int ChunkSize { get; init; } = 8;
    public int Warmup { get; init; } = 24;
    public int Measure { get; init; } = 120;
    public int PrecheckMonths { get; init; }
    public RunSwitches Switches { get; init; } = new(HashCheckpoints: false, HashEveryTick: false, DailyNoop: false, FullCopy: false);
    public double? FRefMhz { get; init; }
    /// <summary>True when an option overrode the manifest (warm-up, months): such output is a smoke run, never an EXP measurement.</summary>
    public bool Smoke { get; init; }
}

/// <summary>Indented JSON output of the bench, written to a file.</summary>
public sealed class BenchJson : IDisposable
{
    private readonly FileStream _stream;

    private BenchJson(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _stream = File.Create(path);
        Writer = new Utf8JsonWriter(_stream, new JsonWriterOptions { Indented = true });
    }

    public Utf8JsonWriter Writer { get; }

    public static BenchJson Open(string path) => new(path);

    public void Dispose()
    {
        Writer.Flush();
        Writer.Dispose();
        _stream.Dispose();
    }

    public static void WriteParams(Utf8JsonWriter j, BenchRunParams p)
    {
        j.WriteStartObject("params");
        j.WriteString("mode", p.Mode);
        if (p.ManifestPath is null) j.WriteNull("manifest"); else j.WriteString("manifest", p.ManifestPath);
        if (p.ManifestHash is null) j.WriteNull("manifest_hash"); else j.WriteString("manifest_hash", p.ManifestHash);
        j.WriteBoolean("smoke", p.Smoke);
        j.WriteNumber("chunk_size", p.ChunkSize);
        j.WriteNumber("warmup_months", p.Warmup);
        j.WriteNumber("measure_months", p.Measure);
        j.WriteNumber("precheck_months", p.PrecheckMonths);
        j.WriteString("view_mode", nameof(SliceViewMode.Full));
        j.WriteBoolean("hash_checkpoints", p.Switches.HashCheckpoints);
        j.WriteBoolean("hash_every_tick", p.Switches.HashEveryTick);
        j.WriteBoolean("daily_noop_command", p.Switches.DailyNoop);
        j.WriteBoolean("full_copy_mode", p.Switches.FullCopy);
        j.WriteEndObject();
    }

    public static void WriteWorld(Utf8JsonWriter j, SynthWorld w, double buildMs)
    {
        j.WriteStartObject("world");
        j.WriteString("name", w.Spec.Name);
        j.WriteNumber("actors", w.Spec.Actors);
        j.WriteNumber("nodes", w.Spec.Nodes);
        j.WriteString("graph", w.Spec.Graph == SynthGraph.Clones ? "clones" : "cross");
        j.WriteNumber("generator_seed", w.Spec.GeneratorSeed);
        j.WriteString("scenario", w.ScenarioId);
        j.WriteString("data_hash", w.Data.DataHash);
        j.WriteString("overlay_sha256", w.OverlaySha256);
        j.WriteNumber("copies", w.Stats.Copies);
        j.WriteNumber("cross_requires", w.Stats.CrossRequires);
        j.WriteNumber("cross_informs", w.Stats.CrossInforms);
        j.WriteNumber("build_ms", buildMs);
        j.WriteEndObject();
    }

    public static void WriteTableShape(Utf8JsonWriter j, Kernel.Table t)
    {
        long bytes = 0;
        int longs = 0, ints = 0;
        foreach (Kernel.Column c in t.Columns)
        {
            if (c is Kernel.LongColumn) { longs++; bytes += 8L * c.Length; }
            else { ints++; bytes += 4L * c.Length; }
        }
        j.WriteStartObject("table");
        j.WriteNumber("rows", t.RowCount);
        j.WriteNumber("columns", t.Columns.Count);
        j.WriteNumber("long_columns", longs);
        j.WriteNumber("int_columns", ints);
        j.WriteNumber("bytes", bytes);
        j.WriteEndObject();
    }

    public static void WriteValidity(Utf8JsonWriter j, string name, bool valid, IReadOnlyList<(string Label, string Hash)> hashes, int months)
    {
        j.WriteStartObject(name);
        j.WriteNumber("months", months);
        j.WriteBoolean("valid", valid);
        j.WriteStartObject("final_state_hash");
        foreach ((string label, string hash) in hashes)
            j.WriteString(label, hash);
        j.WriteEndObject();
        j.WriteEndObject();
    }

    public static void WriteSummary(Utf8JsonWriter j, string name, BenchStats.Summary s)
    {
        j.WriteStartObject(name);
        j.WriteNumber("n", s.N);
        j.WriteNumber("min", s.Min);
        j.WriteNumber("p10", s.P10);
        j.WriteNumber("p50", s.P50);
        j.WriteNumber("p90", s.P90);
        j.WriteNumber("p95", s.P95);
        j.WriteNumber("p99", s.P99);
        j.WriteNumber("max", s.Max);
        j.WriteNumber("mean", s.Mean);
        j.WriteEndObject();
    }

    public static void WriteArray(Utf8JsonWriter j, string name, IReadOnlyList<double> values)
    {
        j.WriteStartArray(name);
        foreach (double v in values)
            j.WriteNumberValue(Math.Round(v, 6));
        j.WriteEndArray();
    }
}
