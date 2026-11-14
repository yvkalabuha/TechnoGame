using System.Text.Json;

namespace Singularity.Lab.Models.Slice;

/// <summary>
/// Checks of the slice models (TASK-0044, T10), run by <c>Singularity.Lab selftest</c> until tests/ has a Lab test project: the calibration header,
/// L1 (two runs → the same CSV and hash), L2 (threads 1 and 4 → the same CSV and hash), the Session golden through the Lab path, the parameter errors,
/// and a small matrix in which every run writes every table. Each returns null or the problem.
/// </summary>
public static class SliceChecks
{
    // Seed 1, months 0…149, default config: the golden of the Session (SeriesSwitchTests.MainSeed1Month149, F17b YProdGolden[0]).
    private const string Golden150 = "4d219de4c3fd3c2dbc948662d9fea67cdb634b3f4f49eb74605840d1d20d328e";
    private const long Ticks150 = 4564;   // 1945-09-01 → 1958-03-01: the tick of month 150, so month 149 is the last one run
    private const long Ticks60 = 1826;    // 1945-09-01 → 1950-09-01

    private static string Manifest(string model, long ticks, string threads = "1", string seeds = "[3]", string pars = "{}", string series = "") =>
        $$"""
        {"schema": 1, "experiment": "SELFTEST-SLICE", "model": "{{model}}", "ticks": {{ticks}}, "chunk_size": 1, "threads": {{threads}},
         "seeds": {{seeds}}, "params": {{pars}}{{series}}}
        """;

    public static IEnumerable<(string Name, Func<string?> Body)> All(string tmp) =>
    [
        ("slice: JetSliceCalib tables have no forbidden file or column (spec §8.2 п. 2)", CalibrationHeader),
        ("slice: JetSliceCalib run writes only its 8 tables; forbidden parameters rejected", () => CalibrationRun(tmp)),
        ("slice: parameters — unknown key, bad value, LAYER=LK rejected", Parameters),
        ("slice: Lab path gives the Session golden (seed 1, month 149)", () => Golden(tmp)),
        ("slice: L1 — two runs, same CSV and golden_hash", () => L1(tmp)),
        ("slice: L2 — threads 1 vs 4, same CSV and golden_hash", () => L2(tmp)),
        ("slice: matrix 2 points x 2 seeds x threads 1,2 — every run has every table", () => Matrix(tmp)),
    ];

    private static string? CalibrationHeader()
    {
        var problems = new List<string>();
        foreach (MetricTableSpec t in SliceTables.Calibration)
        {
            if (SliceTables.CalibrationForbiddenFiles.Contains(t.Name + ".csv"))
                problems.Add(t.Name + ".csv");
            foreach (string c in t.Columns.Prepend("run").Prepend(t.Name))
            {
                if (SliceParameters.CalibrationForbidden.Any(s => c.Contains(s, StringComparison.OrdinalIgnoreCase)))
                    problems.Add($"{t.Name}:{c}");
            }
        }
        if (SliceTables.Calibration.Length != 8 || SliceTables.Full.Length != 14)
            problems.Add($"table counts {SliceTables.Calibration.Length}/{SliceTables.Full.Length}");
        return problems.Count == 0 ? null : string.Join(", ", problems);
    }

    private static string? CalibrationRun(string tmp)
    {
        var (r, dir) = Run(tmp, Manifest("JetSliceCalib", Ticks60, pars: """{"LINE_CLOSE": "YPROD", "TOOLING_TARGET": "V5", "POOL_ORDER": "CAN"}"""), 10001, 1, "calib");
        if (!r.Ok)
            return r.Error;
        string[] files = [.. Directory.GetFiles(dir, "*.csv").Select(Path.GetFileName).Order(StringComparer.Ordinal)!];
        string[] expected = [.. SliceTables.Calibration.Select(t => t.Name + ".csv").Append(RunExecutor.RunsFile).Order(StringComparer.Ordinal)];
        if (!files.SequenceEqual(expected))
            return $"files {string.Join(",", files)}";
        foreach (string f in files)
        {
            string header = File.ReadLines(Path.Combine(dir, f)).First();
            if (header.Split(',').Any(c => SliceParameters.CalibrationForbidden.Any(s => c.Contains(s, StringComparison.OrdinalIgnoreCase))))
                return $"{f}: forbidden column in '{header}'";
        }
        foreach (string bad in new[] { """{"TOOLING_M_LEAD": 0.2}""", """{"detail": "full"}""" })
        {
            var (b, _) = Run(tmp, Manifest("JetSliceCalib", Ticks60, pars: bad), 10001, 1, "calib-bad");
            if (b.Ok || b.Error?.Contains("ParamException", StringComparison.Ordinal) != true)
                return $"accepted {bad}: {b.Error}";
        }
        return null;
    }

    private static string? Parameters()
    {
        foreach (string bad in new[] { """{"FOO": 1}""", """{"START": "X"}""", """{"LAYER": "LK"}""", """{"GRID": "LO_MID"}""", """{"CHANNELS": ["B", "B"]}""" })
        {
            Manifest m = Lab.Manifest.Parse(Manifest("JetSlice", Ticks60, pars: bad), "/x/m.json");
            try
            {
                SliceParameters.Parse(m.Points[0].CreateParamSet(), 1, 1, 1, calibration: false);
                return $"accepted {bad}";
            }
            catch (ParamException) { }
        }
        return null;
    }

    private static string? Golden(string tmp)
    {
        var (r, _) = Run(tmp, Manifest("JetSlice", Ticks150, pars: """{"detail": "lite"}"""), 1, 2, "golden");
        return r.Ok && r.GoldenHash == Golden150 ? null : $"{r.GoldenHash} {r.Error}";
    }

    private static string? L1(string tmp)
    {
        var (a, da) = Run(tmp, Manifest("JetSlice", 11688), 3, 1, "l1-a");
        var (b, db) = Run(tmp, Manifest("JetSlice", 11688), 3, 1, "l1-b");
        if (!a.Ok || !b.Ok || a.GoldenHash != b.GoldenHash)
            return $"{a.GoldenHash} vs {b.GoldenHash} ({a.Error}{b.Error})";
        return SameCsv(da, db, "", "");
    }

    private static string? L2(string tmp)
    {
        var (a, da) = Run(tmp, Manifest("JetSlice", Ticks150), 5, 1, "l2-1");
        var (b, db) = Run(tmp, Manifest("JetSlice", Ticks150), 5, 4, "l2-4");
        if (!a.Ok || !b.Ok || a.GoldenHash != b.GoldenHash)
            return $"{a.GoldenHash} vs {b.GoldenHash} ({a.Error}{b.Error})";
        return SameCsv(da, db, "-t1,", "-t4,");
    }

    // The run id is the first column; it differs only in the thread count. Every other cell must be identical.
    private static string? SameCsv(string da, string db, string ta, string tb)
    {
        foreach (MetricTableSpec t in SliceTables.Full)
        {
            string fa = Path.Combine(da, t.Name + ".csv"), fb = Path.Combine(db, t.Name + ".csv");
            if (!File.Exists(fa) || !File.Exists(fb))
                return $"missing {t.Name}.csv";
            string ca = File.ReadAllText(fa), cb = File.ReadAllText(fb);
            if (ta.Length > 0)
            {
                ca = ca.Replace(ta, "-tX,");
                cb = cb.Replace(tb, "-tX,");
            }
            if (ca != cb)
                return $"{t.Name}.csv differs";
        }
        return null;
    }

    private static string? Matrix(string tmp)
    {
        string dir = Path.Combine(tmp, "slice-matrix");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "m.json");
        File.WriteAllText(path, Manifest("JetSlice", Ticks150, threads: "[1, 2]", seeds: "[1, 2]", pars: """{"detail": "full"}""",
            series: """, "series": [{"name": "main"}, {"name": "lite", "params": {"detail": "lite", "LIC_OFF": true}}]"""));
        Manifest m = Lab.Manifest.Load(path);
        string outDir = Path.Combine(dir, "out");
        int rc = MatrixRunner.Run(m, outDir, 4, TextWriter.Null);
        if (rc != 0)
            return $"matrix exit {rc}: {File.ReadAllText(Path.Combine(outDir, MatrixRunner.MatrixJsonFile))}";
        string[] expected = [.. SliceTables.Full.Select(t => t.Name + ".csv").Append(RunExecutor.RunsFile).Order(StringComparer.Ordinal)];
        foreach (string run in Directory.GetDirectories(Path.Combine(outDir, MatrixRunner.RunsDir)))
        {
            string[] files = [.. Directory.GetFiles(run, "*.csv").Select(Path.GetFileName).Order(StringComparer.Ordinal)!];
            if (!files.SequenceEqual(expected))
                return $"{Path.GetFileName(run)}: files {string.Join(",", files)}";
        }
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, MatrixRunner.MatrixJsonFile)));
        int merged = doc.RootElement.GetProperty("merged_csv").GetArrayLength();
        return merged == expected.Length ? null : $"merged {merged} files";
    }

    private static (RunOutcome Outcome, string Dir) Run(string tmp, string json, ulong seed, int threads, string tag)
    {
        Manifest m = Lab.Manifest.Parse(json, Path.Combine(tmp, "m.json"));
        string dir = Path.Combine(tmp, tag);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        var prov = new Provenance("selftest", null, "selftest", []);
        RunOutcome r = RunExecutor.Execute(new RunRequest(m, 0, seed, threads, dir, RunExecutor.MakeRunId(m, 0, seed, threads)), prov, TextWriter.Null);
        return (r, dir);
    }
}
