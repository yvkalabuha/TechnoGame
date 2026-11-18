using System.Globalization;

namespace Singularity.Lab;

public static class Program
{
    private const string Usage = """
        Singularity.Lab — Simulation Lab CLI (TASK-0005)

          run    --manifest <json> --seed N --threads T --out <dir> [--point P] [--force] [--quiet]
          matrix --manifest <json> --out <dir> [--jobs J] [--timeout SECONDS] [--force] [--point P] [--seeds N]
          hash   --manifest <json> [--canonical]     print manifest_hash (and canonical form)
          models                                     list registered models
          selftest                                   built-in self-checks (until tests/ has Lab tests)
          smoke-t4s --out <dir> [--mods <dir>] [--seeds N] [--only c1|c2|c3]  anchor smoke C1(yield)/C2/C3 (TASK-0017)
          c1-stand --out <dir> [--mods <dir>] [--manifest <json> [--point P]]  C1 stand -> c1_stand.csv per point (TASK-0054/0055, format §6)
          c3-stand --out <dir> --manifest <json> [--point P] [--seeds N] [--mods <dir>]  C3 stand of calibration points -> c3_summary.csv
          culture-rules --out <dir> [--mods <dir>] [--only check|gate|holdout|...]  culture_H rule selection (culture-rule-selection.md)
          c5-stand --out <dir> [--mods <dir>] [--manifest <json> [--point P]] [--horizon H] [--threshold Y]  C5 stand (#248); H < 383 or Y != 0.7 = smoke
          lk-draws --out <dir> --manifest <json> [--point P] [--seeds N]  L_K schedule draws (never, clock, own, req) -> lk_draws.csv
          bench  --mode plan|process|validity|r1|r2|kcopy|r3|fref --out <json> [...]  tick benchmark EXP-0007 / ADR-0017 R1-R4 (TASK-0064; lab-cli.md «lab bench»)
          calib-print --out <dir> --manifest <json> [--point P] [--seeds N] [--jobs J]  #247 G by fielded_L vs r6, designs (JetSliceCalib)

        Exit codes: 0 ok, 1 run/matrix failure or self-check failure, 2 usage or manifest error.
        Outputs belong in lab/results/ (git-ignored). Format: docs/simulation/lab-cli.md
        """;

    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 2 : 0;
        }
        try
        {
            var opts = Options.Parse(args.Skip(1).ToArray());
            return args[0] switch
            {
                "run" => Run(opts),
                "matrix" => Matrix(opts),
                "hash" => Hash(opts),
                "models" => Models(),
                "selftest" => SelfTest.Run(Console.Out),
                "smoke-t4s" => SmokeT4s(opts),
                "culture-rules" => CultureRules(opts),
                "c1-stand" => C1Stand(opts),
                "c3-stand" => C3Stand(opts),
                "calib-print" => CalibPrint(opts),
                "c5-stand" => C5Stand(opts),
                "lk-draws" => LkDraws(opts),
                "bench" => Bench.BenchCommand.Run(opts),
                _ => throw new UsageException($"Unknown command '{args[0]}'."),
            };
        }
        catch (UsageException e)
        {
            Console.Error.WriteLine($"usage error: {e.Message}");
            Console.Error.WriteLine(Usage);
            return 2;
        }
        catch (ManifestException e)
        {
            Console.Error.WriteLine($"manifest error: {e.Message}");
            return 2;
        }
        catch (ParamException e)
        {
            // Core reports bad model parameters with its own type (Core never references Lab).
            Console.Error.WriteLine($"parameter error: {e.Message}");
            return 2;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            // A missing manifest (or other input path) is an argument error.
            Console.Error.WriteLine($"input error: {e.Message}");
            return 2;
        }
        catch (Exception e)
        {
            // Anything else (I/O, permissions, a bug) fails the command, never with a .NET crash code.
            Console.Error.WriteLine($"error: {e.GetType().Name}: {e.Message}");
            Console.Error.WriteLine(e.StackTrace);
            return 1;
        }
    }

    private static int Run(Options o)
    {
        Manifest m = Manifest.Load(o.Required("manifest"));
        if (!ulong.TryParse(o.Required("seed"), NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed))
            throw new UsageException("--seed must be an integer in [0, 2^64).");
        int threads = o.Int("threads", 1, 1024) ?? throw new UsageException("--threads is required.");
        int point = o.Int("point", 0, int.MaxValue) ?? 0;
        string outDir = Path.GetFullPath(o.Required("out"));
        bool force = o.Flag("force");
        TextWriter log = o.Flag("quiet") ? TextWriter.Null : Console.Out;
        o.CheckAllUsed();
        if (File.Exists(Path.Combine(outDir, RunExecutor.RunJsonFile)) && !force)
            throw new UsageException($"{outDir} already has run.json; use --force to overwrite.");

        string runId = RunExecutor.MakeRunId(m, point, seed, threads);
        RunOutcome r = RunExecutor.Execute(new RunRequest(m, point, seed, threads, outDir, runId), Provenance.Collect(m), Console.Error);
        log.WriteLine($"run={runId} status={(r.Ok ? "ok" : "failed")} golden_hash={r.GoldenHash ?? "-"} elapsed_ms={r.ElapsedMs}");
        return r.Ok ? 0 : 1;
    }

    private static int Matrix(Options o)
    {
        Manifest m = Manifest.Load(o.Required("manifest"));
        string outDir = Path.GetFullPath(o.Required("out"));
        int jobs = o.Int("jobs", 1, 1024) ?? Environment.ProcessorCount;
        int timeout = o.Int("timeout", 1, int.MaxValue) ?? MatrixRunner.DefaultRunTimeoutSeconds;
        int? point = o.Int("point", 0, int.MaxValue);
        int? firstSeeds = o.Int("seeds", 1, int.MaxValue);
        if (point is not null || firstSeeds is not null)
            m = m.Select(point, firstSeeds);
        bool force = o.Flag("force");
        o.CheckAllUsed();
        if (Directory.Exists(outDir) && Directory.EnumerateFileSystemEntries(outDir).Any() && !force)
            throw new UsageException($"{outDir} is not empty; use --force to reuse it.");
        return MatrixRunner.Run(m, outDir, jobs, Console.Out, timeout);
    }

    private static int Hash(Options o)
    {
        Manifest m = Manifest.Load(o.Required("manifest"));
        bool canonical = o.Flag("canonical");
        o.CheckAllUsed();
        Console.WriteLine($"manifest_hash={m.Hash} points={m.Points.Count} point_seeds={m.Points.Sum(p => p.Seeds.Count)} threads={m.Threads.Count} runs={m.RunCount}");
        if (canonical) Console.WriteLine(m.CanonicalText);
        return 0;
    }

    private static int SmokeT4s(Options o)
    {
        string outDir = Path.GetFullPath(o.Required("out"));
        string mods = o.Optional("mods") ?? Path.Combine("mods", "core");
        int seeds = o.Int("seeds", 2, 100_000) ?? 200;
        string? only = o.Optional("only");
        if (only is not null && only is not ("c1" or "c2" or "c3"))
            throw new UsageException("--only must be c1, c2 or c3.");
        o.CheckAllUsed();
        return Smoke.SmokeT4s.Run(mods, outDir, seeds, only, Console.Out);
    }

    // Without --manifest: the §8.1 values. With it: every point of the manifest (or --point P), each into <out>/p{P}/ (lab-cli.md, T12 stage C1).
    private static int C1Stand(Options o)
    {
        string outDir = Path.GetFullPath(o.Required("out"));
        string mods = o.Optional("mods") ?? Path.Combine("mods", "core");
        string? manifest = o.Optional("manifest");
        int? point = o.Int("point", 0, int.MaxValue);
        o.CheckAllUsed();
        if (manifest is null)
            return point is null ? Smoke.C1Stand.Run(mods, outDir, Console.Out) : throw new UsageException("--point needs --manifest.");
        foreach (Smoke.StandPoint p in Smoke.StandPoint.FromManifest(manifest, point))
        {
            Console.WriteLine(p.Label);
            int rc = Smoke.C1Stand.Run(mods, Path.Combine(outDir, $"p{p.Index:00}"), Console.Out, p.Industry(new Core.Industry.IndustryParams()));
            if (rc != 0)
                return rc;
        }
        return 0;
    }

    // C5 stand (spec v0.33 §8.2 п.1 C5, #248): deterministic, one evaluation at the frozen point; --horizon < 383 is a smoke run (no statistic).
    private static int C5Stand(Options o)
    {
        string outDir = Path.GetFullPath(o.Required("out"));
        string mods = o.Optional("mods") ?? Path.Combine("mods", "core");
        string? manifest = o.Optional("manifest");
        int? point = o.Int("point", 0, int.MaxValue);
        int horizon = o.Int("horizon", 1, Smoke.C5Stand.Horizon) ?? Smoke.C5Stand.Horizon;
        Kernel.Fx6? threshold = o.Optional("threshold") is string th ? Kernel.Fx6.FromRaw(ParamSet.ParseFx6Raw(th, "threshold")) : null;
        o.CheckAllUsed();
        if (manifest is null)
            return point is null ? Smoke.C5Stand.Run(mods, outDir, Console.Out, new Core.Industry.IndustryParams(), horizon, threshold) : throw new UsageException("--point needs --manifest.");
        foreach (Smoke.StandPoint p in Smoke.StandPoint.FromManifest(manifest, point))
        {
            Console.WriteLine(p.Label);
            int rc = Smoke.C5Stand.Run(mods, Path.Combine(outDir, $"p{p.Index:00}"), Console.Out, p.Industry(new Core.Industry.IndustryParams()), horizon, threshold);
            if (rc != 0)
                return rc;
        }
        return 0;
    }

    // L_K schedule draws of a LAYER = LK point (spec v0.34 §6.1, #251): the check of the control reads them with the L_K run.
    private static int LkDraws(Options o)
    {
        string outDir = Path.GetFullPath(o.Required("out"));
        string manifest = o.Required("manifest");
        int? point = o.Int("point", 0, int.MaxValue);
        int? seeds = o.Int("seeds", 1, 100_000);
        o.CheckAllUsed();
        return Smoke.LkDraws.Run(manifest, point, seeds, outDir, Console.Out);
    }

    // C3 stand of a calibration point (stage C3+C4): the point's K_DIV, THETA_START, H_MAX on the point's seeds (or the first --seeds N of them).
    private static int C3Stand(Options o)
    {
        string outDir = Path.GetFullPath(o.Required("out"));
        string mods = o.Optional("mods") ?? Path.Combine("mods", "core");
        string manifest = o.Required("manifest");
        int? point = o.Int("point", 0, int.MaxValue);
        int? seeds = o.Int("seeds", 2, 100_000);
        o.CheckAllUsed();
        Core.Data.GameData data = Core.Data.ModLoader.Load(mods);
        foreach (Smoke.StandPoint p in Smoke.StandPoint.FromManifest(manifest, point))
        {
            Console.WriteLine(p.Label);
            IReadOnlyList<ulong> s = seeds is int n ? [.. p.Seeds.Take(n)] : p.Seeds;
            Smoke.SmokeT4s.RunC3Point(data, p.Knowledge(new Core.Knowledge.KnowledgeParams()), s, Path.Combine(outDir, $"p{p.Index:00}"), Console.Out);
        }
        return 0;
    }

    // Prints of the calibration build (TASK-0056): #247 / RISK-0022 G by fielded_L against G by the r6 rule per actor-month; the design dump of the C2 pool classes.
    private static int CalibPrint(Options o)
    {
        string outDir = Path.GetFullPath(o.Required("out"));
        string manifest = o.Required("manifest");
        int? point = o.Int("point", 0, int.MaxValue);
        int? seeds = o.Int("seeds", 1, 100_000);
        int jobs = o.Int("jobs", 1, 1024) ?? Environment.ProcessorCount;
        o.CheckAllUsed();
        return Smoke.CalibPrint.Run(manifest, point, seeds, outDir, jobs, Console.Out);
    }

    private static int CultureRules(Options o)
    {
        string outDir = Path.GetFullPath(o.Required("out"));
        string mods = o.Optional("mods") ?? Path.Combine("mods", "core");
        string? only = o.Optional("only");
        o.CheckAllUsed();
        return Smoke.CultureRules.Run(mods, outDir, only, Console.Out);
    }

    private static int Models()
    {
        foreach (RegisteredModel f in ModelRegistry.All)
            Console.WriteLine($"{f.Id}{(f.IsTestModel ? " [TEST]" : "")}: {f.Description}");
        return 0;
    }
}

public sealed class UsageException : Exception
{
    public UsageException(string message) : base(message) { }
}

/// <summary>"--key value" and "--flag" options; every option given must be used by the command.</summary>
internal sealed class Options
{
    private static readonly string[] Flags = ["force", "quiet", "canonical"];
    private readonly SortedDictionary<string, string?> _values = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _used = new(StringComparer.Ordinal);

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
                throw new UsageException($"Unexpected argument '{args[i]}'.");
            string key = args[i][2..];
            string? value = null;
            if (Array.IndexOf(Flags, key) < 0)
            {
                if (i + 1 >= args.Length) throw new UsageException($"--{key} needs a value.");
                value = args[++i];
            }
            if (!o._values.TryAdd(key, value))
                throw new UsageException($"--{key} given twice.");
        }
        return o;
    }

    public string Required(string key)
    {
        _used.Add(key);
        return _values.TryGetValue(key, out string? v) && v is not null ? v : throw new UsageException($"--{key} is required.");
    }

    public string? Optional(string key)
    {
        _used.Add(key);
        return _values.TryGetValue(key, out string? v) ? v : null;
    }

    public int? Int(string key, int min, int max)
    {
        _used.Add(key);
        if (!_values.TryGetValue(key, out string? v) || v is null) return null;
        if (!int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < min || n > max)
            throw new UsageException($"--{key} must be an integer in [{min}, {max}].");
        return n;
    }

    public bool Flag(string key)
    {
        _used.Add(key);
        return _values.ContainsKey(key);
    }

    public void CheckAllUsed()
    {
        foreach (string k in _values.Keys)
        {
            if (!_used.Contains(k)) throw new UsageException($"Option --{k} is not valid for this command.");
        }
    }
}
