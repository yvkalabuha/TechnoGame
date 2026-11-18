using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Singularity.Session;

namespace Singularity.Lab.Bench;

/// <summary>
/// <c>lab bench</c> (TASK-0064, ADR-0016 T1, ADR-0017 R1–R4): the tick benchmark of EXP-0007 and the copy/hash measurements of ADR-0017.
/// Modes (format: docs/simulation/lab-cli.md «lab bench»):
/// <list type="bullet">
/// <item><c>plan</c> — the shuffled process list of the manifest (EXP-0007 П2);</item>
/// <item><c>process</c> — one process: monthly + daily run (П3, R4 with switches), decomposition П4;</item>
/// <item><c>validity</c> — V1/V2 hashes over threads 1/4/8/16 and ChunkSize 1/8/32 (П6), no timing;</item>
/// <item><c>r1</c>, <c>r2</c>, <c>kcopy</c>, <c>r3</c> — ADR-0017 R1, R2 (а)–(д), the ADR-0016 п. 2 fallback, R3;</item>
/// <item><c>fref</c> — the frequency reference f_ref (П5).</item>
/// </list>
/// </summary>
internal static class BenchCommand
{
    public static int Run(Options o)
    {
        string mode = o.Required("mode");
        bool allowDebug = Bool(o, "allow-debug", false);
        if (!BenchEnv.IsOptimizedBuild() && mode != "plan" && !allowDebug)
            throw new UsageException("lab bench times code: build with -c Release (or pass --allow-debug true for a functional check).");
        return mode switch
        {
            "plan" => Plan(o),
            "process" => Process(o),
            "validity" => Validity(o),
            "r1" => R1(o),
            "r2" => R2(o, o.Optional("variants") ?? "a,b,c,d,e", "r2"),
            "kcopy" => R2(o, "a", "kcopy"),
            "r3" => R3(o),
            "fref" => FRef(o),
            _ => throw new UsageException($"Unknown bench mode '{mode}'."),
        };
    }

    private static string RepoDir() =>
        Path.GetDirectoryName(Path.GetDirectoryName(SynthWorldGenerator.FindCoreMod())!)!;

    private static int Plan(Options o)
    {
        BenchManifest m = BenchManifest.Load(o.Required("manifest"));
        string? outFile = o.Optional("out");
        o.CheckAllUsed();
        IReadOnlyList<BenchProcess> plan = m.Plan();
        foreach (BenchProcess p in plan)
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{p.Index,3} {p.Block,-3} {p.Point.Name,-28} seed {p.Seed} t{p.Threads}{(p.Background > 0 ? $" bg{p.Background}" : "")} r{p.Replica} {(p.DailyFirst ? "D,M" : "M,D")}"));
        Console.WriteLine($"manifest_hash={m.Hash} processes={plan.Count}");
        if (outFile is not null)
        {
            using var json = BenchJson.Open(outFile);
            Utf8JsonWriter j = json.Writer;
            j.WriteStartObject();
            j.WriteString("kind", "lab-bench/plan");
            j.WriteString("experiment", m.Experiment);
            j.WriteString("manifest_hash", m.Hash);
            j.WriteNumber("shuffle_seed", m.ShuffleSeed);
            j.WriteStartArray("processes");
            foreach (BenchProcess p in plan)
            {
                j.WriteStartObject();
                j.WriteNumber("index", p.Index);
                j.WriteNumber("table_index", p.TableIndex);
                j.WriteString("block", p.Block);
                j.WriteString("point", p.Point.Name);
                j.WriteNumber("seed", p.Seed);
                j.WriteNumber("threads", p.Threads);
                j.WriteNumber("background", p.Background);
                j.WriteNumber("replica", p.Replica);
                j.WriteString("order", p.DailyFirst ? "daily,monthly" : "monthly,daily");
                j.WriteEndObject();
            }
            j.WriteEndArray();
            j.WriteEndObject();
        }
        return 0;
    }

    private static RunSwitches Switches(Options o, bool hashCheckpointsDefault) => new(
        HashCheckpoints: Bool(o, "hash-checkpoints", hashCheckpointsDefault),
        HashEveryTick: Bool(o, "hash-every-tick", false),
        DailyNoop: Bool(o, "daily-noop", false),
        FullCopy: Bool(o, "full-copy", false));

    private static int Process(Options o)
    {
        string outFile = Path.GetFullPath(o.Required("out"));
        string? manifestPath = o.Optional("manifest");
        BenchProcess p;
        BenchRunParams rp;
        int? warmup = o.Int("warmup", 0, 10_000), measure = o.Int("measure", 1, 10_000);
        int precheck = o.Int("precheck-months", 0, 10_000) ?? 1;
        double? fref = Double(o, "fref");
        if (manifestPath is not null)
        {
            BenchManifest m = BenchManifest.Load(manifestPath);
            int k = o.Int("process", 0, int.MaxValue) ?? throw new UsageException("--process is required with --manifest.");
            IReadOnlyList<BenchProcess> plan = m.Plan();
            if (k >= plan.Count)
                throw new UsageException($"--process must be below {plan.Count}.");
            p = plan[k];
            RunSwitches sw = Switches(o, m.HashCheckpoints);
            rp = new BenchRunParams
            {
                Mode = "process", RepoDir = RepoDir(), ManifestPath = m.Path, ManifestHash = m.Hash, ChunkSize = m.ChunkSize,
                Warmup = warmup ?? m.WarmupMonths, Measure = measure ?? m.MeasureMonths, PrecheckMonths = precheck, Switches = sw, FRefMhz = fref,
                Smoke = (warmup is int w && w != m.WarmupMonths) || (measure is int n && n != m.MeasureMonths) || sw.HashCheckpoints != m.HashCheckpoints,
            };
        }
        else
        {
            var spec = Point(o);
            ulong seed = U64(o, "seed") ?? 1;
            int threads = o.Int("threads", 1, 1024) ?? 8;
            int replica = o.Int("replica", 1, 1000) ?? 1;
            int bg = o.Int("background", 0, 1024) ?? 0;
            p = new BenchProcess(0, 0, "adhoc", spec, seed, threads, replica, bg);
            rp = new BenchRunParams
            {
                Mode = "process", RepoDir = RepoDir(), ChunkSize = o.Int("chunk", 1, 1 << 20) ?? 8, Warmup = warmup ?? 24, Measure = measure ?? 120,
                PrecheckMonths = precheck, Switches = Switches(o, false), FRefMhz = fref, Smoke = true,
            };
        }
        o.CheckAllUsed();
        Console.WriteLine($"process {p.Index}: {p.Block} {p.Point.Name} seed {p.Seed} t{p.Threads} r{p.Replica} bg{p.Background}{(rp.Smoke ? " [smoke]" : "")}");
        return ProcessBench.Execute(p, rp, outFile, Console.Out);
    }

    private static int Validity(Options o)
    {
        string outFile = Path.GetFullPath(o.Required("out"));
        SynthWorldSpec spec = Point(o);
        ulong[] seeds = U64List(o, "seeds") ?? [1, 2, 3];
        int months = o.Int("months", 1, 10_000) ?? 144;
        RunSwitches sw = Switches(o, false);
        o.CheckAllUsed();
        SynthWorld w = Build(spec, outFile);
        bool all = true;
        using (var json = BenchJson.Open(outFile))
        {
            Utf8JsonWriter j = json.Writer;
            j.WriteStartObject();
            j.WriteString("kind", "lab-bench/validity");
            BenchEnv.Write(j, RepoDir());
            BenchJson.WriteParams(j, new BenchRunParams { Mode = "validity", RepoDir = RepoDir(), Measure = months, Switches = sw, Smoke = months != 144 });
            BenchJson.WriteWorld(j, w, 0);
            j.WriteStartArray("seeds");
            foreach (ulong s in seeds)
            {
                (bool valid, var hashes) = ProcessBench.Validity(w, s, months, sw);
                all &= valid;
                Console.WriteLine($"seed {s}: {(valid ? "valid" : "INVALID")} ({hashes[0].Hash[..12]})");
                j.WriteStartObject();
                j.WriteNumber("seed", s);
                BenchJson.WriteValidity(j, "v1_v2", valid, hashes, months);
                j.WriteEndObject();
            }
            j.WriteEndArray();
            j.WriteBoolean("valid", all);
            j.WriteEndObject();
        }
        return all ? 0 : 1;
    }

    private static int R1(Options o)
    {
        string outFile = Path.GetFullPath(o.Required("out"));
        string points = o.Optional("points") ?? "200x2000,200x500";
        SynthGraph g = Graph(o.Optional("graph"));
        int warmup = o.Int("warmup", 0, 1_000_000) ?? 100;
        int calls = o.Int("calls", 1, 10_000_000) ?? 1000;
        ulong seed = U64(o, "seed") ?? 1;
        o.CheckAllUsed();
        using var json = BenchJson.Open(outFile);
        Utf8JsonWriter j = Header(json, "r1", warmup, calls);
        j.WriteStartArray("points");
        foreach (string pt in points.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string[] an = pt.Split('x');
            if (an.Length != 2 || !int.TryParse(an[0], NumberStyles.None, CultureInfo.InvariantCulture, out int a)
                || !int.TryParse(an[1], NumberStyles.None, CultureInfo.InvariantCulture, out int n))
                throw new UsageException("--points is a list of AxN, e.g. 200x2000,200x500.");
            SynthWorld w = Build(new SynthWorldSpec(a, n, g), outFile);
            SliceSession s = Session(w, seed);
            CopyBench.R1(j, w, s.World.Table, warmup, calls);
            Console.WriteLine($"R1 {w.Spec.Name}: done");
        }
        j.WriteEndArray();
        j.WriteEndObject();
        return 0;
    }

    private static int R3(Options o)
    {
        string outFile = Path.GetFullPath(o.Required("out"));
        SynthWorldSpec spec = Point(o);
        int warmup = o.Int("warmup", 0, 1_000_000) ?? 100;
        int calls = o.Int("calls", 1, 10_000_000) ?? 1000;
        ulong seed = U64(o, "seed") ?? 1;
        o.CheckAllUsed();
        SynthWorld w = Build(spec, outFile);
        SliceSession s = Session(w, seed);
        using var json = BenchJson.Open(outFile);
        Utf8JsonWriter j = Header(json, "r3", warmup, calls);
        j.WritePropertyName("r3");
        CopyBench.R3(j, w, s.World.Table, warmup, calls);
        j.WriteEndObject();
        Console.WriteLine($"R3 {w.Spec.Name}: done");
        return 0;
    }

    private static int R2(Options o, string variants, string name)
    {
        string outFile = Path.GetFullPath(o.Required("out"));
        SynthWorldSpec spec = Point(o);
        int warmup = o.Int("warmup", 0, 1_000_000) ?? 100;
        int reps = o.Int("reps", 1, 10_000_000) ?? 1000;
        ulong seed = U64(o, "seed") ?? 1;
        int threads = o.Int("threads", 1, 1024) ?? 8;
        int chunk = o.Int("chunk", 1, 1 << 20) ?? 8;
        int[] percents = IntList(o, "percents") ?? [0, 10, 50, 100];
        if (percents.Any(x => x > 100))
            throw new UsageException("--percents must be in 0…100.");
        int[] threadsList = IntList(o, "threads-list") ?? [1, 8];
        int cowMonth = o.Int("cow-month", 0, 10_000) ?? 1;
        int monthWarmup = o.Int("month-warmup", 0, 10_000) ?? 24;
        int months = o.Int("months", 1, 10_000) ?? 120;
        string[] v = variants.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (v.Length == 0 || v.Any(x => x is not ("a" or "b" or "c" or "d" or "e")))
            throw new UsageException("--variants is a list of a,b,c,d,e (R2 а–д).");
        o.CheckAllUsed();
        SynthWorld w = Build(spec, outFile);
        SliceSession s = Session(w, seed);
        using var json = BenchJson.Open(outFile);
        Utf8JsonWriter j = Header(json, name, warmup, reps);
        BenchJson.WriteWorld(j, w, 0);
        BenchJson.WriteTableShape(j, s.World.Table);
        j.WriteStartObject("r2");
        if (v.Contains("b"))
        {
            // The opened columns come from a real monthly step; the timing runs on a clone taken after it.
            int[] opened = CopyBench.OpenedByMonthlyStep(s, cowMonth);
            Kernel.Table cb = CopyBench.Clone(s.World.Table);
            CopyBench.R2b(j, cb, opened, cowMonth, warmup, reps);
            Console.WriteLine($"R2(б): {opened.Length}/{cb.Columns.Count} columns opened by month {cowMonth}");
        }
        Kernel.Table clone = CopyBench.Clone(s.World.Table);
        if (!CopyBench.SameHash(clone, s.World.Table))
            throw new InvalidOperationException("the clone table hashes differently from the session table");
        if (v.Contains("a"))
        {
            CopyBench.R2a(j, clone, warmup, reps);
            Console.WriteLine("R2(а)/k_copy: done");
        }
        if (v.Contains("c"))
        {
            CopyBench.R2c(j, clone, percents, threads, chunk, warmup, reps);
            Console.WriteLine("R2(в): done");
        }
        if (v.Contains("e"))
        {
            CopyBench.R2e(j, clone, warmup, reps);
            Console.WriteLine("R2(д): done");
        }
        if (v.Contains("d"))
            CopyBench.R2d(j, w, seed, threadsList, chunk, monthWarmup, months, Console.Out);
        j.WriteEndObject();
        j.WriteEndObject();
        return 0;
    }

    /// <summary>f_ref (П5): N threads of integer load for the given seconds; f_ref = median of the samples of the last tail seconds.</summary>
    private static int FRef(Options o)
    {
        string outFile = Path.GetFullPath(o.Required("out"));
        int seconds = o.Int("seconds", 2, 86_400) ?? 300;
        int tail = o.Int("tail", 1, 86_400) ?? 180;
        int threads = o.Int("threads", 1, 1024) ?? 8;
        o.CheckAllUsed();
        if (tail > seconds)
            throw new UsageException("--tail must not exceed --seconds.");
        using var freq = new FreqSampler();
        long t0 = Stopwatch.GetTimestamp();
        using (new BackgroundLoad(threads))
            Thread.Sleep(TimeSpan.FromSeconds(seconds));
        long t1 = Stopwatch.GetTimestamp();
        long tailStart = t1 - (long)(tail * (double)Stopwatch.Frequency);
        double? fref = freq.MedianIn([(Math.Max(t0, tailStart), t1)]);
        using (var json = BenchJson.Open(outFile))
        {
            Utf8JsonWriter j = json.Writer;
            j.WriteStartObject();
            j.WriteString("kind", "lab-bench/fref");
            BenchEnv.Write(j, RepoDir());
            j.WriteNumber("load_threads", threads);
            j.WriteNumber("seconds", seconds);
            j.WriteNumber("tail_seconds", tail);
            j.WriteString("source", freq.Source);
            if (fref is double f) j.WriteNumber("f_ref_mhz", f); else j.WriteNull("f_ref_mhz");
            BenchJson.WriteArray(j, "samples_mhz", [.. freq.Samples.Select(x => x.Mhz)]);
            j.WriteEndObject();
        }
        Console.WriteLine(fref is double ff ? string.Create(CultureInfo.InvariantCulture, $"f_ref={ff:F0} MHz ({freq.Source})") : "f_ref: no samples");
        return fref is null ? 1 : 0;
    }

    private static Utf8JsonWriter Header(BenchJson json, string mode, int warmup, int reps)
    {
        Utf8JsonWriter j = json.Writer;
        j.WriteStartObject();
        j.WriteString("kind", $"lab-bench/{mode}");
        BenchEnv.Write(j, RepoDir());
        j.WriteNumber("warmup_calls", warmup);
        j.WriteNumber("timed_calls", reps);
        return j;
    }

    private static SynthWorld Build(SynthWorldSpec spec, string outFile)
    {
        long t0 = Stopwatch.GetTimestamp();
        SynthWorld w = SynthWorldGenerator.Build(spec, Path.Combine(Path.GetDirectoryName(outFile)!, "worlds", spec.Name));
        Console.WriteLine($"world {spec.Name}: built in {ProcessBench.Ms(Stopwatch.GetTimestamp() - t0):F0} ms");
        return w;
    }

    private static SliceSession Session(SynthWorld w, ulong seed) =>
        SliceSession.Create(w.Data, ProcessBench.Config(w, seed, 8, 8, daily: false,
            new RunSwitches(HashCheckpoints: false, HashEveryTick: false, DailyNoop: false, FullCopy: false)));

    private static SynthWorldSpec Point(Options o) => new(
        o.Int("actors", 1, 10_000) ?? 200, o.Int("nodes", 1, 100_000) ?? 2000, Graph(o.Optional("graph")),
        U64(o, "generator-seed") ?? SynthWorldSpec.DefaultGeneratorSeed);

    private static SynthGraph Graph(string? s) => s switch
    {
        null or "cross" => SynthGraph.CrossEdges,
        "clones" => SynthGraph.Clones,
        _ => throw new UsageException("--graph must be cross or clones."),
    };

    private static bool Bool(Options o, string key, bool def) => o.Optional(key) switch
    {
        null => def,
        "true" => true,
        "false" => false,
        _ => throw new UsageException($"--{key} must be true or false."),
    };

    private static double? Double(Options o, string key) =>
        o.Optional(key) is not string s ? null
        : double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d > 0 ? d
        : throw new UsageException($"--{key} must be a positive number.");

    private static ulong? U64(Options o, string key) =>
        o.Optional(key) is not string s ? null
        : ulong.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out ulong u) ? u
        : throw new UsageException($"--{key} must be an integer in [0, 2^64).");

    private static ulong[]? U64List(Options o, string key) =>
        o.Optional(key) is not string s ? null
        : [.. s.Split(',', StringSplitOptions.TrimEntries).Select(x => ulong.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out ulong u)
            ? u : throw new UsageException($"--{key} is a comma list of integers."))];

    private static int[]? IntList(Options o, string key) =>
        o.Optional(key) is not string s ? null
        : [.. s.Split(',', StringSplitOptions.TrimEntries).Select(x => int.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= 0
            ? n : throw new UsageException($"--{key} is a comma list of integers ≥ 0."))];
}
