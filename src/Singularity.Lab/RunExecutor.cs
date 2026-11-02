using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Singularity.Kernel;

namespace Singularity.Lab;

public sealed record RunRequest(Manifest Manifest, int Point, ulong Seed, int Threads, string OutDir, string RunId);

public sealed record RunOutcome(bool Ok, string? GoldenHash, string? Error, long ElapsedMs);

/// <summary>One run in the current process: build model, tick, hash, write run.json + CSVs.</summary>
public static class RunExecutor
{
    public const int RunJsonFormat = 1;
    public const string RunsFile = "runs.csv";
    public const string RunJsonFile = "run.json";

    public static string MakeRunId(Manifest m, int point, ulong seed, int threads)
    {
        int width = Math.Max(1, (m.Points.Count - 1).ToString(CultureInfo.InvariantCulture).Length);
        return $"p{point.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0')}-s{seed.ToString(CultureInfo.InvariantCulture)}-t{threads.ToString(CultureInfo.InvariantCulture)}";
    }

    public static RunOutcome Execute(RunRequest req, Provenance provenance, TextWriter log)
    {
        Manifest m = req.Manifest;
        if (req.Point < 0 || req.Point >= m.Points.Count)
            throw new ManifestException($"Point {req.Point} is out of range [0, {m.Points.Count}).");
        MatrixPoint point = m.Points[req.Point];
        ILabModelFactory factory = ModelRegistry.Find(m.Model);
        if (factory.IsTestModel)
            log.WriteLine($"WARNING: '{factory.Id}' is a TEST MODEL; results carry no scientific meaning.");

        Directory.CreateDirectory(req.OutDir);
        DateTime startedUtc = DateTime.UtcNow; // metadata only, never reaches the model
        var clock = Stopwatch.StartNew();
        ParamSet parameters = point.CreateParamSet();
        string? golden = null;
        string? error = null;
        long finalTick = 0;
        var sinks = new List<StreamWriter>();
        try
        {
            ILabModel model = factory.Create(new ModelSetup(parameters, req.Seed, m.ChunkSize, m.Ticks));
            var sink = new FileMetricSink(req.OutDir, req.RunId, model.MetricTables, sinks);
            var runner = new TickRunner(model.Table, req.Seed, model.Systems, m.ChunkSize, req.Threads);
            model.Observe(runner.Tick, sink);
            for (long t = 0; t < m.Ticks; t++)
            {
                runner.RunTick();
                model.Observe(runner.Tick, sink);
            }
            finalTick = runner.Tick;
            golden = StateHash.ComputeHex(model.Table, model.SchemaVersion, runner.Tick);
        }
        catch (Exception e)
        {
            // Any model failure is a result of the run: record it in run.json instead of crashing.
            error = $"{e.GetType().Name}: {e.Message}";
            log.WriteLine($"ERROR: {error}");
        }
        finally
        {
            foreach (StreamWriter w in sinks)
                w.Dispose();
        }
        clock.Stop();

        WriteRunsCsv(Path.Combine(req.OutDir, RunsFile), [(req.RunId, point, req.Seed, req.Threads, golden, error is null)]);
        WriteRunJson(req, factory, point, parameters, provenance, golden, error, finalTick, startedUtc, clock.ElapsedMilliseconds);
        return new RunOutcome(error is null, golden, error, clock.ElapsedMilliseconds);
    }

    /// <summary>runs.csv: fixed columns, then the union of parameter keys in ordinal order (empty if absent).</summary>
    public static void WriteRunsCsv(string path, IReadOnlyList<(string RunId, MatrixPoint Point, ulong Seed, int Threads, string? Golden, bool Ok)> rows)
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            foreach (var kv in r.Point.Params)
                keys.Add(kv.Key);
        }
        string[] fixedCols = ["run", "series", "point", "seed", "threads", "status", "golden_hash"];
        foreach (string k in keys)
        {
            if (Array.IndexOf(fixedCols, k) >= 0)
                throw new ManifestException($"Parameter name '{k}' collides with a runs.csv column.");
        }
        using StreamWriter w = Csv.Create(path);
        w.WriteLine(Csv.Line(fixedCols.Concat(keys)));
        foreach (var r in rows)
        {
            var cells = new List<string>
            {
                r.RunId, r.Point.Series, r.Point.Index.ToString(CultureInfo.InvariantCulture),
                r.Seed.ToString(CultureInfo.InvariantCulture), r.Threads.ToString(CultureInfo.InvariantCulture),
                r.Ok ? "ok" : "failed", r.Golden ?? "",
            };
            foreach (string k in keys)
            {
                string value = "";
                foreach (var kv in r.Point.Params)
                {
                    if (string.Equals(kv.Key, k, StringComparison.Ordinal)) { value = kv.Value.Text; break; }
                }
                cells.Add(value);
            }
            w.WriteLine(Csv.Line(cells));
        }
    }

    private static void WriteRunJson(RunRequest req, ILabModelFactory factory, MatrixPoint point, ParamSet parameters,
        Provenance prov, string? golden, string? error, long finalTick, DateTime startedUtc, long elapsedMs)
    {
        Manifest m = req.Manifest;
        using var stream = File.Create(Path.Combine(req.OutDir, RunJsonFile));
        using var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        w.WriteStartObject();
        w.WriteNumber("lab_format", RunJsonFormat);
        w.WriteString("status", error is null ? "ok" : "failed");
        if (error is not null) w.WriteString("error", error);
        w.WriteString("run", req.RunId);
        w.WriteString("experiment", m.Experiment);
        w.WriteString("model", factory.Id);
        w.WriteBoolean("model_is_test", factory.IsTestModel);
        w.WriteString("series", point.Series);
        w.WriteNumber("point", point.Index);
        w.WriteNumber("seed", req.Seed);
        w.WriteNumber("threads", req.Threads);
        w.WriteNumber("chunk_size", m.ChunkSize);
        w.WriteNumber("ticks", m.Ticks);
        w.WriteNumber("final_tick", finalTick);
        if (golden is null) w.WriteNull("golden_hash"); else w.WriteString("golden_hash", golden);
        w.WriteString("core_commit", prov.CoreCommit);
        if (prov.Dirty is bool d) w.WriteBoolean("dirty", d); else w.WriteNull("dirty");
        if (prov.DirtyUntrackedSrc is bool u) w.WriteBoolean("dirty_untracked_src", u); else w.WriteNull("dirty_untracked_src");
        w.WriteString("build_commit", prov.BuildCommit);
        w.WriteString("manifest_hash", m.Hash);
        w.WriteString("manifest_path", m.SourcePath);
        w.WriteString("data_hash", prov.DataHash);
        w.WriteStartArray("data_files");
        foreach (DataFileHash f in prov.DataFiles)
        {
            w.WriteStartObject();
            w.WriteString("path", f.Path);
            w.WriteString("sha256", f.Sha256);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteStartObject("params");
        foreach (var kv in point.Params)
            w.WriteString(kv.Key, kv.Value.Text);
        w.WriteEndObject();
        w.WriteStartArray("unused_params");
        foreach (string k in parameters.Unread())
            w.WriteStringValue(k);
        w.WriteEndArray();
        w.WriteString("started_utc", startedUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
        w.WriteNumber("elapsed_ms", elapsedMs);
        w.WriteString("runtime", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        w.WriteString("os", System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier);
        w.WriteEndObject();
    }

    private sealed class FileMetricSink : IMetricSink
    {
        private readonly string _runId;
        private readonly int[] _widths;
        private readonly StreamWriter[] _writers;

        public FileMetricSink(string dir, string runId, IReadOnlyList<MetricTableSpec> tables, List<StreamWriter> owned)
        {
            _runId = runId;
            _widths = new int[tables.Count];
            _writers = new StreamWriter[tables.Count];
            for (int i = 0; i < tables.Count; i++)
            {
                string name = tables[i].Name;
                if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name == "runs")
                    throw new ManifestException($"Invalid metric table name '{name}'.");
                _widths[i] = tables[i].Columns.Count;
                _writers[i] = Csv.Create(Path.Combine(dir, name + ".csv"));
                owned.Add(_writers[i]);
                _writers[i].WriteLine(Csv.Line(new[] { "run" }.Concat(tables[i].Columns)));
            }
        }

        public void Row(int table, params string[] cells)
        {
            if (cells.Length != _widths[table])
                throw new InvalidOperationException($"Metric table {table}: expected {_widths[table]} cells, got {cells.Length}.");
            _writers[table].WriteLine(Csv.Line(new[] { _runId }.Concat(cells)));
        }
    }
}
