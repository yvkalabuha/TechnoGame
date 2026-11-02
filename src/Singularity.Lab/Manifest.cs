using System.Globalization;
using System.Text.Json;

namespace Singularity.Lab;

/// <summary>
/// One point of the experiment matrix: a series name, the full parameter set and the seeds
/// of its series (series "seeds" if given, else the top-level "seeds").
/// </summary>
public sealed record MatrixPoint(int Index, string Series, IReadOnlyList<KeyValuePair<string, ParamValue>> Params, IReadOnlyList<ulong> Seeds)
{
    public ParamSet CreateParamSet() => new(Params);
}

/// <summary>
/// Lab manifest, JSON (pilot deviation from ADR-0008's TOML, EXP-0001 review item 11).
/// Format: docs/simulation/lab-cli.md. Unknown top-level keys are errors, except the free-form
/// "meta" object and "description" string, which are hashed but not interpreted.
/// </summary>
public sealed class Manifest
{
    public const int SupportedSchema = 1;

    private static readonly string[] KnownKeys =
        ["chunk_size", "data", "description", "experiment", "meta", "model", "params", "schema", "seeds", "series", "threads", "ticks"];

    private Manifest() { }

    public required string SourcePath { get; init; }
    public required string BaseDirectory { get; init; }
    public required string CanonicalText { get; init; }
    public required string Hash { get; init; }
    public required string? Experiment { get; init; }
    public required string Model { get; init; }
    public required long Ticks { get; init; }
    public required int ChunkSize { get; init; }
    public required IReadOnlyList<ulong> Seeds { get; init; }
    public required IReadOnlyList<int> Threads { get; init; }
    public required IReadOnlyList<string> DataFiles { get; init; }
    public required IReadOnlyList<MatrixPoint> Points { get; init; }

    public int RunCount => Points.Sum(p => p.Seeds.Count) * Threads.Count;

    public static Manifest Load(string path)
    {
        string full = Path.GetFullPath(path);
        return Parse(File.ReadAllText(full), full);
    }

    public static Manifest Parse(string json, string sourcePath)
    {
        string canonical;
        try
        {
            canonical = CanonicalJson.Canonicalize(json);
        }
        catch (JsonException e)
        {
            throw new ManifestException($"{sourcePath}: invalid JSON: {e.Message}");
        }

        using JsonDocument doc = JsonDocument.Parse(canonical);
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new ManifestException("Manifest root must be an object.");
        foreach (JsonProperty p in root.EnumerateObject())
        {
            if (Array.BinarySearch(KnownKeys, p.Name, StringComparer.Ordinal) < 0)
                throw new ManifestException($"Unknown manifest key '{p.Name}'. Use \"meta\" for free-form data.");
        }

        int schema = (int)RequireLong(root, "schema", 1, int.MaxValue);
        if (schema != SupportedSchema)
            throw new ManifestException($"Manifest schema {schema} is not supported (expected {SupportedSchema}).");

        var baseParams = ReadParams(root, "params", "$.params");
        List<ulong> seeds = ReadSeeds(root, "$")
            ?? throw new ManifestException("Manifest key 'seeds' is required.");

        return new Manifest
        {
            SourcePath = sourcePath,
            BaseDirectory = Path.GetDirectoryName(sourcePath) ?? ".",
            CanonicalText = canonical,
            Hash = CanonicalJson.Sha256Hex(canonical),
            Experiment = OptionalString(root, "experiment"),
            Model = OptionalString(root, "model") ?? throw new ManifestException("Manifest key 'model' is required."),
            Ticks = RequireLong(root, "ticks", 0, long.MaxValue),
            ChunkSize = root.TryGetProperty("chunk_size", out _) ? (int)RequireLong(root, "chunk_size", 1, int.MaxValue) : 64,
            Seeds = seeds,
            Threads = ReadThreads(root),
            DataFiles = ReadData(root),
            Points = ReadPoints(root, baseParams, seeds),
        };
    }

    private static List<KeyValuePair<string, ParamValue>> ReadParams(JsonElement parent, string key, string path)
    {
        var list = new List<KeyValuePair<string, ParamValue>>();
        if (!parent.TryGetProperty(key, out JsonElement obj))
            return list;
        if (obj.ValueKind != JsonValueKind.Object)
            throw new ManifestException($"{path} must be an object.");
        foreach (JsonProperty p in obj.EnumerateObject())
            list.Add(new(p.Name, ParamValues.FromJson(p.Value,$"{path}.{p.Name}")));
        return list;
    }

    /// <summary>
    /// Matrix points: for each series in file order, the cartesian product of its sweep axes,
    /// axes in ordinal key order, last axis varying fastest. No "series" → one series "default".
    /// </summary>
    private static List<MatrixPoint> ReadPoints(JsonElement root, List<KeyValuePair<string, ParamValue>> baseParams, List<ulong> baseSeeds)
    {
        var points = new List<MatrixPoint>();
        if (!root.TryGetProperty("series", out JsonElement series))
        {
            AddSeries(points, "default", baseParams, [], baseSeeds, "$");
            return points;
        }
        if (series.ValueKind != JsonValueKind.Array || series.GetArrayLength() == 0)
            throw new ManifestException("$.series must be a non-empty array.");

        var names = new SortedSet<string>(StringComparer.Ordinal);
        int i = 0;
        foreach (JsonElement s in series.EnumerateArray())
        {
            string path = $"$.series[{i++}]";
            if (s.ValueKind != JsonValueKind.Object)
                throw new ManifestException($"{path} must be an object.");
            foreach (JsonProperty p in s.EnumerateObject())
            {
                if (p.Name is not ("name" or "params" or "seeds" or "sweep"))
                    throw new ManifestException($"{path}: unknown key '{p.Name}'.");
            }
            string name = OptionalString(s, "name") ?? throw new ManifestException($"{path}.name is required.");
            if (!names.Add(name))
                throw new ManifestException($"{path}: duplicate series name '{name}'.");

            var merged = new SortedDictionary<string, ParamValue>(StringComparer.Ordinal);
            foreach (var kv in baseParams)
                merged[kv.Key] = kv.Value;
            foreach (var kv in ReadParams(s, "params", $"{path}.params"))
                merged[kv.Key] = kv.Value; // series params override base params

            var axes = new List<(string Key, List<ParamValue> Values)>();
            if (s.TryGetProperty("sweep", out JsonElement sweep))
            {
                if (sweep.ValueKind != JsonValueKind.Object)
                    throw new ManifestException($"{path}.sweep must be an object.");
                foreach (JsonProperty axis in sweep.EnumerateObject())
                {
                    if (axis.Value.ValueKind != JsonValueKind.Array || axis.Value.GetArrayLength() == 0)
                        throw new ManifestException($"{path}.sweep.{axis.Name} must be a non-empty array.");
                    if (merged.ContainsKey(axis.Name))
                        throw new ManifestException($"{path}: '{axis.Name}' is both a parameter and a sweep axis.");
                    var values = new List<ParamValue>();
                    int j = 0;
                    foreach (JsonElement v in axis.Value.EnumerateArray())
                        values.Add(ParamValues.FromJson(v,$"{path}.sweep.{axis.Name}[{j++}]"));
                    axes.Add((axis.Name, values));
                }
            }
            List<ulong> seriesSeeds = ReadSeeds(s, path) ?? baseSeeds; // series seeds replace top-level seeds
            AddSeries(points, name, merged.ToList(), axes, seriesSeeds, path);
        }
        return points;
    }

    private static void AddSeries(List<MatrixPoint> points, string name, List<KeyValuePair<string, ParamValue>> fixedParams,
        List<(string Key, List<ParamValue> Values)> axes, List<ulong> seeds, string path)
    {
        long total = 1;
        foreach (var axis in axes)
            total = checked(total * axis.Values.Count);
        if (points.Count + total > 1_000_000)
            throw new ManifestException($"{path}: matrix has more than 1,000,000 points.");

        var index = new int[axes.Count];
        for (long n = 0; n < total; n++)
        {
            var p = new List<KeyValuePair<string, ParamValue>>(fixedParams);
            for (int a = 0; a < axes.Count; a++)
                p.Add(new(axes[a].Key, axes[a].Values[index[a]]));
            p.Sort((x, y) => string.CompareOrdinal(x.Key, y.Key));
            points.Add(new MatrixPoint(points.Count, name, p, seeds));

            for (int a = axes.Count - 1; a >= 0; a--)
            {
                if (++index[a] < axes[a].Values.Count) break;
                index[a] = 0;
            }
        }
    }

    /// <summary>
    /// Seeds of <paramref name="parent"/> (manifest root or a series): an array of non-negative
    /// integers, or {"start": a, "count": n}. Null when the key is absent.
    /// </summary>
    private static List<ulong>? ReadSeeds(JsonElement parent, string parentPath)
    {
        if (!parent.TryGetProperty("seeds", out JsonElement e))
            return null;
        string path = $"{parentPath}.seeds";
        var seeds = new List<ulong>();
        if (e.ValueKind == JsonValueKind.Array)
        {
            int i = 0;
            foreach (JsonElement s in e.EnumerateArray())
                seeds.Add(ParseSeed(s, $"{path}[{i++}]"));
        }
        else if (e.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty p in e.EnumerateObject())
            {
                if (p.Name is not ("start" or "count"))
                    throw new ManifestException($"{path}: unknown key '{p.Name}'.");
            }
            ulong start = ParseSeed(e.TryGetProperty("start", out JsonElement s) ? s : throw new ManifestException($"{path}.start is required."), $"{path}.start");
            long count = RequireLong(e, "count", 1, 1_000_000);
            for (long i = 0; i < count; i++)
                seeds.Add(checked(start + (ulong)i));
        }
        else
        {
            throw new ManifestException($"{path} must be an array or {{\"start\", \"count\"}}.");
        }
        if (seeds.Count == 0)
            throw new ManifestException($"{path} is empty.");
        if (seeds.Distinct().Count() != seeds.Count)
            throw new ManifestException($"{path} contains duplicates.");
        return seeds;
    }

    private static ulong ParseSeed(JsonElement e, string path)
    {
        if (e.ValueKind != JsonValueKind.Number || !ulong.TryParse(e.GetRawText(), NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed))
            throw new ManifestException($"{path} must be an integer in [0, 2^64).");
        return seed;
    }

    private static List<int> ReadThreads(JsonElement root)
    {
        if (!root.TryGetProperty("threads", out JsonElement e))
            return [1];
        var list = new List<int>();
        if (e.ValueKind == JsonValueKind.Number)
        {
            list.Add((int)RequireLong(root, "threads", 1, 1024));
        }
        else if (e.ValueKind == JsonValueKind.Array && e.GetArrayLength() > 0)
        {
            int i = 0;
            foreach (JsonElement t in e.EnumerateArray())
            {
                if (t.ValueKind != JsonValueKind.Number || !int.TryParse(t.GetRawText(), NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < 1 || n > 1024)
                    throw new ManifestException($"$.threads[{i}] must be an integer in [1, 1024].");
                list.Add(n);
                i++;
            }
        }
        else
        {
            throw new ManifestException("$.threads must be an integer or a non-empty array of integers.");
        }
        if (list.Distinct().Count() != list.Count)
            throw new ManifestException("$.threads contains duplicates.");
        return list;
    }

    /// <summary>Data files hashed into data_hash; paths are relative to the manifest directory.</summary>
    private static List<string> ReadData(JsonElement root)
    {
        var list = new List<string>();
        if (!root.TryGetProperty("data", out JsonElement e))
            return list;
        if (e.ValueKind != JsonValueKind.Array)
            throw new ManifestException("$.data must be an array of relative paths.");
        foreach (JsonElement p in e.EnumerateArray())
        {
            string path = p.ValueKind == JsonValueKind.String ? p.GetString()! : throw new ManifestException("$.data entries must be strings.");
            if (Path.IsPathRooted(path))
                throw new ManifestException($"$.data: '{path}' must be relative to the manifest directory.");
            list.Add(path.Replace('\\', '/'));
        }
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    private static string? OptionalString(JsonElement obj, string key)
    {
        if (!obj.TryGetProperty(key, out JsonElement e))
            return null;
        return e.ValueKind == JsonValueKind.String ? e.GetString() : throw new ManifestException($"'{key}' must be a string.");
    }

    private static long RequireLong(JsonElement obj, string key, long min, long max)
    {
        if (!obj.TryGetProperty(key, out JsonElement e))
            throw new ManifestException($"Manifest key '{key}' is required.");
        if (e.ValueKind != JsonValueKind.Number || !long.TryParse(e.GetRawText(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long v) || v < min || v > max)
            throw new ManifestException($"'{key}' must be an integer in [{min}, {max}].");
        return v;
    }
}
