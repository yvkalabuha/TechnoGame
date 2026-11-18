using System.Text;
using System.Text.Json;
using Singularity.Kernel;
using Singularity.Lab.Bench;
using Singularity.Session;

namespace Singularity.Lab.Tests;

/// <summary>Tests of <c>lab bench</c> (TASK-0083 for TASK-0064). BenchCommand is internal: the CLI is driven through the public Program.Main.</summary>
[Collection("LabConsole")]
public sealed class BenchTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "sing-bench-tests-" + Guid.NewGuid().ToString("N"));

    public BenchTests() => Directory.CreateDirectory(_tmp);

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch (IOException) { }
    }

    private static string ManifestPath()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "lab", "manifests", "EXP-0007-bench.json")))
            dir = Path.GetDirectoryName(dir)!;
        return Path.Combine(dir!, "lab", "manifests", "EXP-0007-bench.json");
    }

    private static string Block(string id = "A", string threads = "[1]", string seeds = "[1]", string points = "\"points\": [[10, 100, \"clones\"]]") =>
        $"{{\"id\": \"{id}\", {points}, \"threads\": {threads}, \"seeds\": {seeds}}}";

    private static string Manifest(string blocks, string kind = "bench", string extra = "", ulong shuffle = 7) =>
        $"{{\"schema\": 1, \"experiment\": \"T\", \"kind\": \"{kind}\", \"generator_seed\": 5, \"shuffle_seed\": {shuffle}, \"chunk_size\": 8, " +
        $"\"warmup_months\": 1, \"measure_months\": 1, \"hash_checkpoints\": false, {extra}\"blocks\": [{blocks}]}}";

    // ---- 1, 2: BenchStats ----

    [Fact]
    public void Percentile_SingleValue_AnyP()
    {
        foreach (double p in (double[])[0, 0.3, 1])
            Assert.Equal(4.5, BenchStats.Percentile([4.5], p));
    }

    [Fact]
    public void Percentile_Extremes_AreMinAndMax()
    {
        double[] x = [5, 1, 9, 3];
        Assert.Equal(1, BenchStats.Percentile(x, 0));
        Assert.Equal(9, BenchStats.Percentile(x, 1));
    }

    [Fact]
    public void Percentile_Type7_OneToTen()
    {
        double[] x = [.. Enumerable.Range(1, 10).Select(i => (double)i)];
        Assert.Equal(5.5, BenchStats.Percentile(x, 0.5), 12);
        Assert.Equal(9.55, BenchStats.Percentile(x, 0.95), 12);
        Assert.Equal(5.5, BenchStats.Median(x), 12);
    }

    [Fact]
    public void Percentile_UnsortedInput_SameAsSorted_AndInputUntouched()
    {
        double[] x = [9, 2, 7, 1, 10, 4, 3, 8, 6, 5];
        double[] copy = [.. x];
        Assert.Equal(5.5, BenchStats.Percentile(x, 0.5), 12);
        Assert.Equal(9.55, BenchStats.Percentile(x, 0.95), 12);
        Assert.Equal(copy, x);
    }

    [Fact]
    public void Percentile_Invalid_Throws()
    {
        Assert.Throws<ArgumentException>(() => BenchStats.Percentile([], 0.5));
        Assert.Throws<ArgumentException>(() => BenchStats.Percentile([1, double.NaN, 3], 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => BenchStats.Percentile([1, 2], -0.01));
        Assert.Throws<ArgumentOutOfRangeException>(() => BenchStats.Percentile([1, 2], 1.01));
        Assert.Throws<ArgumentOutOfRangeException>(() => BenchStats.Percentile([1, 2], double.NaN));
    }

    [Fact]
    public void Summarize_MatchesPercentile()
    {
        double[] x = [9, 2.5, 7, 1, 10, 4, 3, 8, 6, 5, 11.25];
        BenchStats.Summary s = BenchStats.Summarize(x);
        Assert.Equal(x.Length, s.N);
        Assert.Equal(1, s.Min);
        Assert.Equal(11.25, s.Max);
        Assert.Equal(BenchStats.Percentile(x, 0.10), s.P10);
        Assert.Equal(BenchStats.Percentile(x, 0.50), s.P50);
        Assert.Equal(BenchStats.Percentile(x, 0.90), s.P90);
        Assert.Equal(BenchStats.Percentile(x, 0.95), s.P95);
        Assert.Equal(BenchStats.Percentile(x, 0.99), s.P99);
        Assert.Equal(x.Sum() / x.Length, s.Mean, 12);
        Assert.Throws<ArgumentException>(() => BenchStats.Summarize([]));
    }

    // ---- 3: BenchManifest.Parse ----

    [Fact]
    public void Parse_Valid_Minimal()
    {
        BenchManifest m = BenchManifest.Parse(Manifest(Block()), "x.json");
        Assert.Single(m.Blocks);
        Assert.Equal(64, m.Hash.Length);
        Assert.Equal(5UL, m.GeneratorSeed);
    }

    [Fact]
    public void Parse_UnknownKey_Rejected()
    {
        Assert.Throws<ManifestException>(() => BenchManifest.Parse(Manifest(Block(), extra: "\"warmup_month\": 3, "), "x.json"));
        Assert.Throws<ManifestException>(() => BenchManifest.Parse(Manifest(Block().Replace("\"threads\"", "\"thread\": [1], \"threads\"")), "x.json"));
    }

    [Fact]
    public void Parse_KindNotBench_Rejected() =>
        Assert.Throws<ManifestException>(() => BenchManifest.Parse(Manifest(Block(), kind: "matrix"), "x.json"));

    [Fact]
    public void Parse_EmptyThreadsOrSeeds_Rejected()
    {
        Assert.Throws<ManifestException>(() => BenchManifest.Parse(Manifest(Block(threads: "[]")), "x.json"));
        Assert.Throws<ManifestException>(() => BenchManifest.Parse(Manifest(Block(seeds: "[]")), "x.json"));
        Assert.Throws<ManifestException>(() => BenchManifest.Parse(Manifest(Block(threads: "[0]")), "x.json"));
    }

    [Fact]
    public void Parse_DuplicateBlockId_Rejected() =>
        Assert.Throws<ManifestException>(() => BenchManifest.Parse(Manifest(Block("A") + "," + Block("A")), "x.json"));

    [Fact]
    public void Parse_GridBenchmark_Is24Points()
    {
        BenchManifest m = BenchManifest.Parse(Manifest(Block(points: "\"grid\": \"benchmark\"")), "x.json");
        Assert.Equal(24, m.Blocks[0].Points.Count);
        Assert.Equal(24, m.Plan().Count);
    }

    [Fact]
    public void Parse_Exp0007_Has114Processes()
    {
        BenchManifest m = BenchManifest.Load(ManifestPath());
        IReadOnlyList<BenchProcess> plan = m.Plan();
        Assert.Equal(114, plan.Count);
        var by = plan.GroupBy(p => p.Block).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(new Dictionary<string, int> { ["A"] = 15, ["B"] = 15, ["C1"] = 6, ["C2"] = 3, ["C3"] = 3, ["D"] = 72 }, by);
    }

    [Fact]
    public void Hash_IndependentOfFormatting()
    {
        string compact = Manifest(Block());
        string pretty = "\n  " + compact.Replace(", ", ",\n    ").Replace("{", "{\n  ") + "\n";
        Assert.NotEqual(compact, pretty);
        Assert.Equal(BenchManifest.Parse(compact, "a").Hash, BenchManifest.Parse(pretty, "b").Hash);
        Assert.NotEqual(BenchManifest.Parse(compact, "a").Hash, BenchManifest.Parse(Manifest(Block(), shuffle: 8), "a").Hash);
    }

    // ---- 4: Plan ----

    private static BenchManifest Multi(ulong shuffle) =>
        BenchManifest.Parse(Manifest(Block("A", "[1, 4]", "[1, 2, 3]") + "," + Block("B", "[8]", "[1, 2]").Replace("}", ", \"replicas\": 4}"), shuffle: shuffle), "x");

    private static string Key(BenchProcess p) => $"{p.Block}/{p.Point.Name}/{p.Seed}/{p.Threads}/{p.Replica}";

    [Fact]
    public void Plan_Deterministic_AndAPermutationOfTheTable()
    {
        IReadOnlyList<BenchProcess> a = Multi(7).Plan(), b = Multi(7).Plan();
        Assert.Equal(a.Select(Key), b.Select(Key));
        Assert.Equal(14, a.Count);
        Assert.Equal(Enumerable.Range(0, 14), a.Select(p => p.Index));
        Assert.Equal(Enumerable.Range(0, 14), a.Select(p => p.TableIndex).OrderBy(i => i));
    }

    [Fact]
    public void Plan_DifferentShuffleSeed_DifferentOrder()
    {
        string[] a = [.. Multi(7).Plan().Select(Key)];
        string[] b = [.. Multi(8).Plan().Select(Key)];
        Assert.NotEqual(a, b);
        Assert.Equal(a.Order(), b.Order());
    }

    [Fact]
    public void Plan_DailyFirst_IsEvenReplica()
    {
        foreach (BenchProcess p in Multi(7).Plan())
            Assert.Equal(p.Replica % 2 == 0, p.DailyFirst);
    }

    // ---- 5: CopyBench.Pick ----

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 50)]
    [InlineData(7, 33)]
    [InlineData(40, 10)]
    [InlineData(40, 25)]
    [InlineData(37, 99)]
    [InlineData(113, 50)]
    public void Pick_CountIsFloor(int columns, int pct)
    {
        int[] pick = CopyBench.Pick(columns, pct);
        Assert.Equal(columns * pct / 100, pick.Length);
        Assert.Equal(pick.Distinct().Count(), pick.Length);
        Assert.All(pick, c => Assert.InRange(c, 0, Math.Max(columns - 1, 0)));
    }

    [Fact]
    public void Pick_ZeroIsEmpty_HundredIsAll()
    {
        Assert.Empty(CopyBench.Pick(25, 0));
        Assert.Equal(Enumerable.Range(0, 25), CopyBench.Pick(25, 100));
    }

    // ---- 6: Clone, R2(c) ----

    private static Table SmallTable()
    {
        var t = new Table(64);
        for (int c = 0; c < 6; c++)
        {
            if (c % 2 == 0)
                t.AddLong("l" + c).Load([.. Enumerable.Range(0, 64).Select(i => (long)i * 1000003 + c)]);
            else
                t.AddInt("i" + c).Load([.. Enumerable.Range(0, 64).Select(i => i * 31 - c)]);
        }
        return t;
    }

    [Fact]
    public void Clone_SameStateHash_AndIndependent()
    {
        Table src = SmallTable();
        Table clone = CopyBench.Clone(src);
        Assert.True(CopyBench.SameHash(src, clone));
        Assert.Equal(src.Columns.Count, clone.Columns.Count);
        Assert.Equal(src.Columns.Select(c => c.Name), clone.Columns.Select(c => c.Name));
        string h = StateHash.ComputeHex(src, 1);
        using var ms = new MemoryStream();
        using (var j = new Utf8JsonWriter(ms))
        {
            j.WriteStartObject();
            CopyBench.R2a(j, clone, 0, 2);   // full copy + swap on the clone only
            j.WriteEndObject();
        }
        HashAssert.Equal(h, StateHash.ComputeHex(src, 1));
    }

    [Fact]
    public void R2c_DoesNotChangeState()
    {
        Table clone = CopyBench.Clone(SmallTable());
        string h = StateHash.ComputeHex(clone, 1);
        using var ms = new MemoryStream();
        using (var j = new Utf8JsonWriter(ms))
        {
            j.WriteStartObject();
            CopyBench.R2c(j, clone, [0, 50, 100], 2, 8, 0, 2);
            j.WriteEndObject();
        }
        HashAssert.Equal(h, StateHash.ComputeHex(clone, 1));
        using JsonDocument d = JsonDocument.Parse(ms.ToArray());
        JsonElement r = d.RootElement.GetProperty("c_synthetic_daily_system");
        Assert.True(r.GetProperty("state_unchanged").GetBoolean());
        Assert.Equal(new[] { 0, 3, 6 }, r.GetProperty("curve").EnumerateArray().Select(e => e.GetProperty("columns_written").GetInt32()));
        Assert.False(clone.FullCopyMode);
    }

    // ---- 7: CLI ----

    private (int Code, string Out, string Err) Cli(params string[] args)
    {
        TextWriter o0 = Console.Out, e0 = Console.Error;
        var so = new StringWriter();
        var se = new StringWriter();
        Console.SetOut(so);
        Console.SetError(se);
        try { return (Program.Main(args), so.ToString(), se.ToString()); }
        finally { Console.SetOut(o0); Console.SetError(e0); }
    }

    [Fact]
    public void Cli_UnknownMode_Code2() => Assert.Equal(2, Cli("bench", "--mode", "nope", "--allow-debug", "true").Code);

    [Fact]
    public void Cli_MissingMode_Code2() => Assert.Equal(2, Cli("bench").Code);

    [Fact]
    public void Cli_UnknownKey_Code2()
    {
        Assert.Equal(2, Cli("bench", "--mode", "plan", "--manifest", ManifestPath(), "--bogus", "1").Code);
        Assert.Equal(2, Cli("bench", "--mode", "plan", "--manifest", ManifestPath(), "--mode", "plan").Code);
    }

    [Fact]
    public void Cli_Plan_Prints114()
    {
        var r = Cli("bench", "--mode", "plan", "--manifest", ManifestPath());
        Assert.Equal(0, r.Code);
        Assert.Contains("processes=114", r.Out);
    }

    [Fact]
    public void Cli_Percents101_Rejected()
    {
        var r = Cli("bench", "--mode", "r2", "--variants", "c", "--percents", "0,101", "--out", Path.Combine(_tmp, "r2.json"), "--allow-debug", "true");
        Assert.Equal(2, r.Code);
        Assert.False(File.Exists(Path.Combine(_tmp, "r2.json")));
    }

    [Fact]
    public void Cli_WarmupMeasureWithManifest_SetSmoke()
    {
        string m = Path.Combine(_tmp, "m.json");
        File.WriteAllText(m, Manifest(Block("A", "[1]", "[1]", "\"points\": [[10, 100, \"clones\"]]"))
            .Replace("\"warmup_months\": 1", "\"warmup_months\": 24").Replace("\"measure_months\": 1", "\"measure_months\": 120"));
        var r = Cli("bench", "--mode", "process", "--manifest", m, "--process", "0", "--warmup", "1", "--measure", "1",
            "--out", Path.Combine(_tmp, "p.json"), "--allow-debug", "true");
        Assert.True(r.Out.Contains("[smoke]"), r.Out + r.Err);
        // Same values as in the manifest: not a smoke run.
        string m2 = Path.Combine(_tmp, "m2.json");
        File.WriteAllText(m2, Manifest(Block()));
        var r2 = Cli("bench", "--mode", "process", "--manifest", m2, "--process", "0", "--warmup", "1", "--measure", "1",
            "--out", Path.Combine(_tmp, "p2.json"), "--allow-debug", "true", "--fref", "1");
        Assert.False(r2.Out.Contains("[smoke]"), r2.Out + r2.Err);
    }

    [Fact]
    public void Cli_NonSmokeManifestProcessWithoutFref_ExitsWithCode2()
    {
        string m = Path.Combine(_tmp, "m3.json");
        File.WriteAllText(m, Manifest(Block()));
        var r = Cli("bench", "--mode", "process", "--manifest", m, "--process", "0", "--warmup", "1", "--measure", "1",
            "--out", Path.Combine(_tmp, "p3.json"), "--allow-debug", "true");
        Assert.Equal(2, r.Code);
    }
}

[CollectionDefinition("LabConsole", DisableParallelization = true)]
public sealed class LabConsoleCollection;
