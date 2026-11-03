using System.Globalization;

namespace Singularity.Lab;

public static class Program
{
    private const string Usage = """
        Singularity.Lab — Simulation Lab CLI (TASK-0005)

          run    --manifest <json> --seed N --threads T --out <dir> [--point P] [--force] [--quiet]
          matrix --manifest <json> --out <dir> [--jobs J] [--timeout SECONDS] [--force]
          hash   --manifest <json> [--canonical]     print manifest_hash (and canonical form)
          models                                     list registered models
          selftest                                   built-in self-checks (until tests/ has Lab tests)
          smoke-t4s --out <dir> [--mods <dir>] [--seeds N] [--only c1|c2|c3]  anchor smoke C1(yield)/C2/C3 (TASK-0017)
          culture-rules --out <dir> [--mods <dir>] [--only check|gate|holdout|...]  culture_H rule selection (culture-rule-selection.md)

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
        foreach (ILabModelFactory f in ModelRegistry.All)
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
