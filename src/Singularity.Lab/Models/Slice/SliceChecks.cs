using Singularity.Core.Products;
using Singularity.Kernel;
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
        ("slice: data_hash and data_files cover the mod files the slice loads", () => DataProvenance(tmp)),
        ("slice: data cache — a changed mod file is loaded again; stale data_files fail the run", () => DataCache(tmp)),
        ("slice: a40.force — the FORCE arm marks the forced series, the AI never", () => A40Force(tmp)),
        ("slice: COEXIST_MONEY needs left = 0 and no training money in every month, not Bind = Money alone", CoexistMoneyClass),
        ("slice: LIC_END — a lapse of LicenceView.Active gives a t_end and an event", () => LicenceEnd(tmp)),
    ];

    // Codex r4 (TASK-0044): Bind = Money also holds with positive money short of one unit or with training money; the spec class needs neither.
    private static string? CoexistMoneyClass()
    {
        Fx6 zero = Fx6.Zero, some = Fx6.FromInt(5);
        SliceObserver.ActorMonth M(Fx6? left, Fx6? training, LineBind bind, SliceObserver.Role role = SliceObserver.Role.N, bool two = true) =>
            new(Fx6.One, false, Fx6.Zero, false, two, false, false, [role], [Fx6.One], [-1], [left], [training], [zero], [bind]);
        var ok = M(zero, zero, LineBind.Money);
        var cases = new (string Name, SliceObserver.ActorMonth Bad, bool Want)[]
        {
            ("spec case", ok, true),
            ("left > 0", M(some, zero, LineBind.Money), false),
            ("training > 0", M(zero, some, LineBind.Money), false),
            ("no step-7 money row", M(null, null, LineBind.Money), false),
            ("not admitted (Bind None)", M(zero, zero, LineBind.None), false),
            ("role W", M(zero, zero, LineBind.Money, SliceObserver.Role.W), false),
            ("one line", M(zero, zero, LineBind.Money, two: false), false),
        };
        foreach (var (name, bad, want) in cases)
        {
            // The bad month is the last of three: the class needs the condition in every month.
            if (SliceObserver.CoexistMoney([ok, ok, bad], 0) != want)
                return $"COEXIST_MONEY {(want ? "rejected" : "accepted")} the case '{name}'";
        }
        return SliceObserver.CoexistMoney([], 0) ? "COEXIST_MONEY of no months" : null;
    }

    // LIC_END is the 1 -> 0 change of LicenceView.Active (format §11 п. 7): every licence row with a t_end has its event, and a lapse exists in the run.
    private static string? LicenceEnd(string tmp)
    {
        var (r, dir) = Run(tmp, Manifest("JetSlice", 11688), 1, 1, "lic-end");
        if (!r.Ok)
            return r.Error;
        var licences = Rows(dir, "licences.csv");
        var ends = Rows(dir, "events.csv").Where(e => e["kind"] == "LIC_END").ToList();
        var ended = licences.Where(l => l["t_end"].Length > 0).ToList();
        if (ended.Count == 0)
            return "seed 1: no licence has a t_end";
        if (ended.Count != ends.Count)
            return $"{ended.Count} licences with t_end, {ends.Count} LIC_END events";
        return ended.All(l => ends.Any(e => e["t"] == l["t_end"] && e["actor"] == l["buyer"])) ? null : "a t_end without a LIC_END of the buyer in that month";
    }

    private static List<Dictionary<string, string>> Rows(string dir, string file)
    {
        string[] lines = File.ReadAllLines(Path.Combine(dir, file));
        string[] head = lines[0].Split(',');
        return [.. lines.Skip(1).Select(l => head.Zip(l.Split(',')).ToDictionary(p => p.First, p => p.Second, StringComparer.Ordinal))];
    }

    // Codex r3 (TASK-0044): SliceData cached by directory only gave old data next to a new data_hash after a mod file changed in the process.
    private static string? DataCache(string tmp)
    {
        string repo = Path.Combine(tmp, "slice-cache-repo");
        string mods = Path.Combine(repo, "mods", "core");
        Directory.CreateDirectory(mods);
        File.WriteAllText(Path.Combine(repo, "Singularity.sln"), "");
        string source = Path.GetDirectoryName(Path.GetDirectoryName(SliceData.Files(AppContext.BaseDirectory)[0]))!;
        foreach (string f in Directory.GetFiles(Path.Combine(source, "core")))
            File.Copy(f, Path.Combine(mods, Path.GetFileName(f)), overwrite: true);
        string path = Path.Combine(repo, "lab", "m.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Manifest("JetSlice", Ticks60));
        Manifest m = Lab.Manifest.Load(path);

        Provenance before = Provenance.Collect(m);           // fills the cache
        string d1 = SliceData.Load(m.BaseDirectory).DataHash;
        string nodes = Path.Combine(mods, "nodes.yaml");
        string text = File.ReadAllText(nodes);
        int at = text.IndexOf("weight: 0.6}", StringComparison.Ordinal);
        if (at < 0)
            return "nodes.yaml has no 'weight: 0.6}' to change";
        File.WriteAllText(nodes, text[..at] + "weight: 0.5}" + text[(at + "weight: 0.6}".Length)..]);
        string fresh = Singularity.Core.Data.ModLoader.Load(mods).DataHash;
        string d2 = SliceData.Load(m.BaseDirectory).DataHash;
        if (fresh == d1)
            return "the edit of nodes.yaml did not change GameData.DataHash";
        if (d2 != fresh)
            return $"SliceData.Load gave the cached data {d2} after nodes.yaml changed (fresh {fresh})";

        // A run whose recorded data_files predate the change fails instead of pairing new data with the old data_hash.
        RunOutcome stale = RunExecutor.Execute(new RunRequest(m, 0, 3, 1, Path.Combine(repo, "stale"), "r"), before, TextWriter.Null);
        if (stale.Ok || stale.Error?.Contains("changed after data_hash was recorded", StringComparison.Ordinal) != true)
            return $"a run with stale data_files was not refused: {stale.Error}";

        Provenance now = Provenance.Collect(m);
        string outDir = Path.Combine(repo, "out");
        RunOutcome r = RunExecutor.Execute(new RunRequest(m, 0, 3, 1, outDir, "r"), now, TextWriter.Null);
        if (!r.Ok)
            return $"run failed: {r.Error}";
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, RunExecutor.RunJsonFile)));
        JsonElement root = doc.RootElement;
        if (root.GetProperty("data_hash").GetString() != now.DataHash || now.DataHash == before.DataHash)
            return "run.json data_hash is not the hash of the changed files";
        string? model = root.GetProperty("model_info").GetProperty("data_hash").GetString();
        return model == fresh ? null : $"model_info.data_hash {model}, want the changed data {fresh}";
    }

    // Codex r3 (TASK-0044): a40.force was always 0, though the FORCE arms launch series with force (format §4.9).
    private static string? A40Force(string tmp)
    {
        var (f, df) = Run(tmp, Manifest("JetSlice", 11688, pars: """{"FORCE": "X1"}"""), 1, 1, "force-x1");
        if (!f.Ok)
            return f.Error;
        var rows = A40Rows(df);
        int forcedC = rows.Count(r => r["branch"] == "C" && r["force"] == "1");
        if (forcedC == 0)
            return "FORCE=X1 seed 1: no A40C row with force = 1";
        if (rows.Any(r => r["force"] == "1" && (r["branch"] != "C" || long.Parse(r["t_launch"], System.Globalization.CultureInfo.InvariantCulture) <= 0)))
            return "force = 1 on a row that is not a series launched in the run";
        var (o, dOff) = Run(tmp, Manifest("JetSlice", 11688), 1, 1, "force-off");
        if (!o.Ok)
            return o.Error;
        var off = A40Rows(dOff);
        return off.Count > 0 && off.All(r => r["force"] == "0") ? null : $"FORCE=OFF seed 1: {off.Count} a40 rows, an AI row with force = 1 or none";
    }

    private static List<Dictionary<string, string>> A40Rows(string dir)
    {
        string[] lines = File.ReadAllLines(Path.Combine(dir, "a40.csv"));
        string[] head = lines[0].Split(',');
        return [.. lines.Skip(1).Select(l => head.Zip(l.Split(',')).ToDictionary(p => p.First, p => p.Second, StringComparer.Ordinal))];
    }

    private static string? DataProvenance(string tmp)
    {
        // A repository of its own: Singularity.sln + a copy of mods/core, the manifest beside them.
        string repo = Path.Combine(tmp, "slice-data-repo");
        string mods = Path.Combine(repo, "mods", "core");
        Directory.CreateDirectory(mods);
        File.WriteAllText(Path.Combine(repo, "Singularity.sln"), "");
        string source = Path.GetDirectoryName(Path.GetDirectoryName(SliceData.Files(AppContext.BaseDirectory)[0]))!;
        foreach (string f in Directory.GetFiles(Path.Combine(source, "core")))
            File.Copy(f, Path.Combine(mods, Path.GetFileName(f)), overwrite: true);
        string path = Path.Combine(repo, "lab", "m.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Manifest("JetSlice", Ticks60));
        Manifest m = Lab.Manifest.Load(path);

        IReadOnlyList<string> loaded = SliceData.Files(m.BaseDirectory);
        string[] want = [.. loaded.Select(f => Path.GetRelativePath(m.BaseDirectory, f).Replace('\\', '/')).Order(StringComparer.Ordinal)];
        (string h1, var files) = Provenance.HashData(m);
        if (!files.Select(f => f.Path).SequenceEqual(want))
            return $"data_files {string.Join(",", files.Select(f => f.Path))} != loaded {string.Join(",", want)}";
        foreach (string name in new[] { "mod.yaml", "nodes.yaml", "product_classes.yaml", "scenario_1945.yaml" })
        {
            if (!want.Contains("../mods/core/" + name, StringComparer.Ordinal))
                return $"../mods/core/{name} is not in data_files";
        }
        File.AppendAllText(Path.Combine(mods, "nodes.yaml"), "\n# provenance check\n");
        (string h2, _) = Provenance.HashData(m);
        if (h1 == h2)
            return "data_hash did not change with nodes.yaml";

        // The run.json of a real run carries the same data_hash and data_files.
        string outDir = Path.Combine(repo, "out");
        RunOutcome r = RunExecutor.Execute(new RunRequest(m, 0, 3, 1, outDir, "r"), Provenance.Collect(m), TextWriter.Null);
        if (!r.Ok)
            return $"run failed: {r.Error}";
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, RunExecutor.RunJsonFile)));
        if (doc.RootElement.GetProperty("data_hash").GetString() != h2)
            return "run.json data_hash differs from the hash of the loaded files";
        int n = doc.RootElement.GetProperty("data_files").GetArrayLength();
        return n == want.Length ? null : $"run.json data_files has {n} entries, want {want.Length}";
    }

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
