using System.Text.Json;

namespace Singularity.Lab;

/// <summary>
/// Built-in self-checks (`Singularity.Lab selftest`). A stop-gap until test-engineer adds
/// tests/Singularity.Lab.Tests; uses only the TEST MODEL and a temporary directory.
/// </summary>
public static class SelfTest
{
    private const string BaseManifest = """
        {"schema": 1, "experiment": "SELFTEST", "model": "test.random-walk", "ticks": 50, "chunk_size": 16,
         "seeds": [1, 2], "threads": [1, 4], "params": {"rows": 100, "noise": 0.5, "drift": 0.001}}
        """;

    public static int Run(TextWriter log)
    {
        string tmp = Directory.CreateTempSubdirectory("singularity-lab-selftest-").FullName;
        var checks = new List<(string Name, Func<string?> Body)>
        {
            ("canonical: key order, whitespace, 1.0 vs 1, exponent", CanonicalEquivalence),
            ("canonical: changed value or seed changes hash", CanonicalSensitivity),
            ("canonical: duplicate key and bad JSON rejected", CanonicalRejects),
            ("canonical: 64-bit seed kept exactly", CanonicalBigSeed),
            ("params: Fx6 parsing is exact, >6 decimals rejected", Fx6Params),
            ("metrics: Fx6 formatting", Fx6Format),
            ("manifest: series x sweep expansion order", MatrixExpansion),
            ("manifest: unknown key rejected", UnknownKey),
            ("run: same seed twice -> same golden_hash", () => SameSeed(tmp)),
            ("run: threads 1 vs 4 -> same golden_hash and CSV", () => ThreadInvariance(tmp)),
            ("run: different seed -> different golden_hash", () => SeedSensitivity(tmp)),
            ("run: failure recorded in run.json", () => FailurePath(tmp)),
            ("provenance: data_hash tracks file content", () => DataHash(tmp)),
        };

        int failed = 0;
        foreach (var (name, body) in checks)
        {
            string? problem;
            try { problem = body(); }
            catch (Exception e) { problem = $"{e.GetType().Name}: {e.Message}"; }
            log.WriteLine($"{(problem is null ? "PASS" : "FAIL")}  {name}{(problem is null ? "" : " — " + problem)}");
            if (problem is not null) failed++;
        }
        try { Directory.Delete(tmp, recursive: true); } catch (IOException) { }
        log.WriteLine($"selftest: {checks.Count - failed}/{checks.Count} passed");
        return failed == 0 ? 0 : 1;
    }

    private static string? Expect(bool ok, string message) => ok ? null : message;

    private static string? CanonicalEquivalence()
    {
        string a = """{"b": [1, 2.50, {"y": 1, "x": "é"}], "a": 1.0, "c": 1e2, "d": -0}""";
        string b = """{"a":1,"d":0,"c":100,"b":[1,2.5,{"x":"é","y":1.000}]}""";
        string ca = CanonicalJson.Canonicalize(a), cb = CanonicalJson.Canonicalize(b);
        return Expect(ca == cb && CanonicalJson.Sha256Hex(ca) == CanonicalJson.Sha256Hex(cb), $"{ca} != {cb}");
    }

    private static string? CanonicalSensitivity()
    {
        string h0 = Manifest.Parse(BaseManifest, "/x/m.json").Hash;
        string h1 = Manifest.Parse(BaseManifest.Replace("\"noise\": 0.5", "\"noise\": 0.6"), "/x/m.json").Hash;
        string h2 = Manifest.Parse(BaseManifest.Replace("[1, 2]", "[2, 1]"), "/x/m.json").Hash;
        return Expect(h0 != h1 && h0 != h2, "hash did not change");
    }

    private static string? CanonicalRejects()
    {
        foreach (string bad in new[] { """{"a":1,"a":2}""", """{"a":1,}""", "{\"a\":1 /* c */}", """{"a": 1e400}""" })
        {
            try
            {
                CanonicalJson.Canonicalize(bad);
                return $"accepted {bad}";
            }
            catch (Exception e) when (e is ManifestException or JsonException) { }
        }
        return null;
    }

    private static string? CanonicalBigSeed()
    {
        var m = Manifest.Parse(BaseManifest.Replace("[1, 2]", "[18446744073709551615, 9007199254740993]"), "/x/m.json");
        return Expect(m.Seeds[0] == ulong.MaxValue && m.Seeds[1] == 9007199254740993UL, "seed rounded");
    }

    private static string? Fx6Params()
    {
        if (ParamSet.ParseFx6Raw("0.85", "k") != 850_000 || ParamSet.ParseFx6Raw("-1.000001", "k") != -1_000_001
            || ParamSet.ParseFx6Raw("1e-6", "k") != 1)
            return "wrong raw value";
        try
        {
            ParamSet.ParseFx6Raw("0.0000001", "k");
            return "7 decimals accepted";
        }
        catch (ManifestException) { return null; }
    }

    private static string? Fx6Format()
    {
        var cases = new (long Raw, string Text)[] { (0, "0"), (1, "0.000001"), (-1, "-0.000001"), (1_500_000, "1.5"), (3_000_000, "3"), (long.MinValue, "-9223372036854.775808") };
        foreach (var (raw, text) in cases)
        {
            if (MetricFormat.Fx6(raw) != text) return $"{raw} -> {MetricFormat.Fx6(raw)}, expected {text}";
        }
        return null;
    }

    private static string? MatrixExpansion()
    {
        string json = """
            {"schema":1,"model":"test.random-walk","ticks":1,"seeds":{"start":10,"count":3},"threads":[1,2],
             "params":{"rows":4},
             "series":[{"name":"main","params":{"noise":2}},
                       {"name":"grid","sweep":{"noise":[1,2],"drift":[0,0.5,1]}}]}
            """;
        Manifest m = Manifest.Parse(json, "/x/m.json");
        if (m.Points.Count != 7 || m.RunCount != 42) return $"points={m.Points.Count} runs={m.RunCount}";
        if (m.Seeds[2] != 12) return "seed range";
        // grid axes in ordinal order: drift, noise; noise varies fastest.
        MatrixPoint p = m.Points[2];
        string drift = p.Params.First(kv => kv.Key == "drift").Value.Text, noise = p.Params.First(kv => kv.Key == "noise").Value.Text;
        return Expect(m.Points[0].Series == "main" && p.Series == "grid" && drift == "0" && noise == "2", $"point2 drift={drift} noise={noise}");
    }

    private static string? UnknownKey()
    {
        try
        {
            Manifest.Parse(BaseManifest.Replace("\"ticks\"", "\"tick\""), "/x/m.json");
            return "accepted unknown key";
        }
        catch (ManifestException) { return null; }
    }

    private static (RunOutcome Outcome, string Dir) RunOnce(string tmp, string json, ulong seed, int threads, string tag)
    {
        Manifest m = Manifest.Parse(json, Path.Combine(tmp, "m.json"));
        string dir = Path.Combine(tmp, tag);
        var prov = new Provenance("selftest", null, "selftest", []);
        RunOutcome r = RunExecutor.Execute(new RunRequest(m, 0, seed, threads, dir, RunExecutor.MakeRunId(m, 0, seed, threads)), prov, TextWriter.Null);
        return (r, dir);
    }

    private static string? SameSeed(string tmp)
    {
        var a = RunOnce(tmp, BaseManifest, 7, 1, "same-a").Outcome;
        var b = RunOnce(tmp, BaseManifest, 7, 1, "same-b").Outcome;
        return Expect(a.Ok && b.Ok && a.GoldenHash == b.GoldenHash, $"{a.GoldenHash} vs {b.GoldenHash} ({a.Error}{b.Error})");
    }

    private static string? ThreadInvariance(string tmp)
    {
        var (a, da) = RunOnce(tmp, BaseManifest, 7, 1, "thr-1");
        var (b, db) = RunOnce(tmp, BaseManifest, 7, 4, "thr-4");
        if (!a.Ok || !b.Ok || a.GoldenHash != b.GoldenHash) return $"{a.GoldenHash} vs {b.GoldenHash}";
        // Run ids differ only in "-t1"/"-t4"; the metric values must be identical.
        string ca = File.ReadAllText(Path.Combine(da, "test_walk.csv")).Replace("-t1,", "-tX,");
        string cb = File.ReadAllText(Path.Combine(db, "test_walk.csv")).Replace("-t4,", "-tX,");
        return Expect(ca == cb && ca.Split('\n').Length == 53, "CSV differs or wrong row count");
    }

    private static string? SeedSensitivity(string tmp)
    {
        var a = RunOnce(tmp, BaseManifest, 7, 1, "seed-7").Outcome;
        var b = RunOnce(tmp, BaseManifest, 8, 1, "seed-8").Outcome;
        return Expect(a.Ok && b.Ok && a.GoldenHash != b.GoldenHash, "same hash for different seeds");
    }

    private static string? FailurePath(string tmp)
    {
        string json = BaseManifest.Replace("\"rows\": 100", "\"rows\": 100, \"fail_at_tick\": 10");
        var (r, dir) = RunOnce(tmp, json, 7, 4, "fail");
        if (r.Ok || r.Error is null || !r.Error.Contains("TickComputeException", StringComparison.Ordinal)) return $"ok={r.Ok} error={r.Error}";
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, RunExecutor.RunJsonFile)));
        return Expect(doc.RootElement.GetProperty("status").GetString() == "failed"
                      && doc.RootElement.GetProperty("golden_hash").ValueKind == JsonValueKind.Null, "run.json status");
    }

    private static string? DataHash(string tmp)
    {
        string dataDir = Path.Combine(tmp, "data");
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(dataDir, "a.csv"), "x\n1\n");
        Manifest m = Manifest.Parse(BaseManifest.Replace("\"schema\": 1", "\"schema\": 1, \"data\": [\"a.csv\"]"), Path.Combine(dataDir, "m.json"));
        Manifest none = Manifest.Parse(BaseManifest, Path.Combine(dataDir, "m.json"));
        string h1 = Provenance.HashData(m).Hash;
        File.WriteAllText(Path.Combine(dataDir, "a.csv"), "x\n2\n");
        string h2 = Provenance.HashData(m).Hash;
        string empty = Provenance.HashData(none).Hash;
        return Expect(h1 != h2 && empty == "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", "data_hash insensitive");
    }
}
