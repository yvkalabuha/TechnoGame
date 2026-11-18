using System.Text.Json;
using Singularity.Kernel;

namespace Singularity.Lab.Bench;

/// <summary>One block of the EXP-0007 design (П2): a set of points × threads × seeds × replicas, optionally with background load.</summary>
public sealed record BenchBlock(string Id, IReadOnlyList<SynthWorldSpec> Points, IReadOnlyList<int> Threads, IReadOnlyList<ulong> Seeds,
    int Replicas, int Background);

/// <summary>One process of the plan: one point, one seed, one thread count; it runs the monthly and the daily run (П2).</summary>
/// <param name="Index">Position in the shuffled plan (0-based): the order processes are run in.</param>
/// <param name="TableIndex">Position in the unshuffled list (blocks in manifest order).</param>
/// <param name="Replica">1-based replica of (block, point, seed, threads): odd → monthly first, even → daily first.</param>
public sealed record BenchProcess(int Index, int TableIndex, string Block, SynthWorldSpec Point, ulong Seed, int Threads, int Replica, int Background)
{
    public bool DailyFirst => Replica % 2 == 0;
}

/// <summary>
/// Manifest of <c>lab bench</c> (TASK-0064, EXP-0007 П1–П3): world generator seed, chunk size, warm-up and measured months, game hash flag,
/// the shuffle seed and the blocks. <c>manifest_hash</c> = SHA-256 of the canonical JSON (the same canonical form as run manifests).
/// Unknown keys are rejected so a typo cannot silently fall back to a default.
/// </summary>
public sealed class BenchManifest
{
    public required string Experiment { get; init; }
    public required string Hash { get; init; }
    public required string Path { get; init; }
    public ulong GeneratorSeed { get; init; } = SynthWorldSpec.DefaultGeneratorSeed;
    public ulong ShuffleSeed { get; init; }
    public int ChunkSize { get; init; } = 8;
    public int WarmupMonths { get; init; } = 24;
    public int MeasureMonths { get; init; } = 120;
    public bool HashCheckpoints { get; init; }
    public required IReadOnlyList<BenchBlock> Blocks { get; init; }

    private static readonly string[] TopKeys =
        ["schema", "experiment", "kind", "generator_seed", "shuffle_seed", "chunk_size", "warmup_months", "measure_months", "hash_checkpoints", "blocks", "meta"];

    private static readonly string[] BlockKeys = ["id", "points", "grid", "threads", "seeds", "replicas", "background", "note"];

    public static BenchManifest Load(string path)
    {
        string text = File.ReadAllText(path);
        return Parse(text, System.IO.Path.GetFullPath(path));
    }

    public static BenchManifest Parse(string text, string path)
    {
        string canonical;
        try
        {
            canonical = CanonicalJson.Canonicalize(text);
        }
        catch (Exception e) when (e is JsonException or ManifestException)
        {
            throw new ManifestException($"{path}: {e.Message}");
        }
        using JsonDocument doc = JsonDocument.Parse(text);
        JsonElement root = doc.RootElement;
        CheckKeys(root, TopKeys, "manifest");
        if (Int(root, "schema", 1, 1) != 1)
            throw new ManifestException("schema must be 1");
        if (Str(root, "kind") != "bench")
            throw new ManifestException("kind must be \"bench\"");
        ulong gen = U64(root, "generator_seed");
        var blocks = new List<BenchBlock>();
        foreach (JsonElement b in Arr(root, "blocks").EnumerateArray())
            blocks.Add(ParseBlock(b, gen));
        if (blocks.Count == 0 || blocks.Select(b => b.Id).Distinct(StringComparer.Ordinal).Count() != blocks.Count)
            throw new ManifestException("blocks must be non-empty with distinct ids");
        return new BenchManifest
        {
            Experiment = Str(root, "experiment"),
            Hash = CanonicalJson.Sha256Hex(canonical),
            Path = path,
            GeneratorSeed = gen,
            ShuffleSeed = U64(root, "shuffle_seed"),
            ChunkSize = Int(root, "chunk_size", 1, 1 << 20),
            WarmupMonths = Int(root, "warmup_months", 0, 10_000),
            MeasureMonths = Int(root, "measure_months", 1, 10_000),
            HashCheckpoints = Bool(root, "hash_checkpoints"),
            Blocks = blocks,
        };
    }

    private static BenchBlock ParseBlock(JsonElement b, ulong gen)
    {
        CheckKeys(b, BlockKeys, "block");
        string id = Str(b, "id");
        var points = new List<SynthWorldSpec>();
        if (b.TryGetProperty("points", out JsonElement pts))
        {
            foreach (JsonElement p in pts.EnumerateArray())
            {
                if (p.ValueKind != JsonValueKind.Array || p.GetArrayLength() != 3)
                    throw new ManifestException($"block {id}: a point is [actors, nodes, \"clones\"|\"cross\"]");
                points.Add(new SynthWorldSpec(p[0].GetInt32(), p[1].GetInt32(), Graph(p[2].GetString(), id), gen));
            }
        }
        if (b.TryGetProperty("grid", out JsonElement grid))
        {
            if (grid.ValueKind != JsonValueKind.String || grid.GetString() != "benchmark")
                throw new ManifestException($"block {id}: grid must be \"benchmark\" (SynthWorldSpec.BenchmarkGrid)");
            points.AddRange(SynthWorldSpec.BenchmarkGrid(gen));
        }
        if (points.Count == 0)
            throw new ManifestException($"block {id}: no points");
        int[] threads = [.. Arr(b, "threads").EnumerateArray().Select(t => t.GetInt32())];
        ulong[] seeds = [.. Arr(b, "seeds").EnumerateArray().Select(s => s.GetUInt64())];
        if (threads.Length == 0 || threads.Any(t => t < 1) || seeds.Length == 0)
            throw new ManifestException($"block {id}: threads and seeds must be non-empty, threads ≥ 1");
        int replicas = b.TryGetProperty("replicas", out _) ? Int(b, "replicas", 1, 1000) : 1;
        int background = b.TryGetProperty("background", out _) ? Int(b, "background", 0, 1024) : 0;
        return new BenchBlock(id, points, threads, seeds, replicas, background);
    }

    /// <summary>
    /// The processes in table order (blocks in manifest order; inside a block: point, threads, seed, replica), then shuffled once with
    /// Fisher–Yates on <c>CounterRng(shuffle_seed)</c> (EXP-0007 П2): for i = n−1 … 1, j = Index(NextU64(0, 0, i, 0), i + 1), swap i and j.
    /// </summary>
    public IReadOnlyList<BenchProcess> Plan()
    {
        var list = new List<BenchProcess>();
        foreach (BenchBlock b in Blocks)
        {
            foreach (SynthWorldSpec p in b.Points)
            {
                foreach (int t in b.Threads)
                {
                    foreach (ulong s in b.Seeds)
                    {
                        for (int r = 1; r <= b.Replicas; r++)
                            list.Add(new BenchProcess(0, list.Count, b.Id, p, s, t, r, b.Background));
                    }
                }
            }
        }
        BenchProcess[] order = [.. list];
        Shuffle(order, ShuffleSeed);
        return [.. order.Select((p, i) => p with { Index = i })];
    }

    public static void Shuffle<T>(T[] items, ulong seed)
    {
        var rng = new CounterRng(seed);
        for (int i = items.Length - 1; i > 0; i--)
        {
            int j = CounterRng.Index(rng.NextU64(0, 0, i, 0), i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    private static SynthGraph Graph(string? s, string block) => s switch
    {
        "clones" => SynthGraph.Clones,
        "cross" => SynthGraph.CrossEdges,
        _ => throw new ManifestException($"block {block}: graph must be \"clones\" or \"cross\""),
    };

    private static void CheckKeys(JsonElement e, string[] allowed, string what)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new ManifestException($"{what} must be an object");
        foreach (JsonProperty p in e.EnumerateObject())
        {
            if (Array.IndexOf(allowed, p.Name) < 0)
                throw new ManifestException($"unknown key '{p.Name}' in {what}");
        }
    }

    private static JsonElement Get(JsonElement e, string key) =>
        e.TryGetProperty(key, out JsonElement v) ? v : throw new ManifestException($"missing key '{key}'");

    private static string Str(JsonElement e, string key) =>
        Get(e, key) is { ValueKind: JsonValueKind.String } v ? v.GetString()! : throw new ManifestException($"'{key}' must be a string");

    private static bool Bool(JsonElement e, string key) => Get(e, key).ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new ManifestException($"'{key}' must be true or false"),
    };

    private static ulong U64(JsonElement e, string key) =>
        Get(e, key) is { ValueKind: JsonValueKind.Number } v && v.TryGetUInt64(out ulong u) ? u : throw new ManifestException($"'{key}' must be an integer ≥ 0");

    private static int Int(JsonElement e, string key, int min, int max) =>
        Get(e, key) is { ValueKind: JsonValueKind.Number } v && v.TryGetInt32(out int n) && n >= min && n <= max
            ? n : throw new ManifestException($"'{key}' must be an integer in [{min}, {max}]");

    private static JsonElement Arr(JsonElement e, string key) =>
        Get(e, key) is { ValueKind: JsonValueKind.Array } v ? v : throw new ManifestException($"'{key}' must be an array");
}
