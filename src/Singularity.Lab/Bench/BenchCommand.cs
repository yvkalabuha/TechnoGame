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
/// <item><c>fref</c> — the frequency reference f_ref (П5);</item>
/// <item><c>alloc</c> — allocated bytes and GC counts per month, split into tick path and snapshot path, and of the main pure reads (TASK-0103, ADR-0023 п. 2), no timing;</item>
/// <item><c>series</c> — the EXP-0008 series driver: pre-series stop, f_ref(k), validity, main pass, foreign-load gate, repeats, exclusions, reference modes (TASK-0106).</item>
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
            "alloc" => Alloc(o),
            "series" => SeriesDriver.Run(o),
            _ => throw new UsageException($"Unknown bench mode '{mode}'."),
        };
    }

    internal static string RepoDir() =>
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
        int? kOpt = o.Int("k", 1, 4096);
        double thermal = Double(o, "thermal-ratio") ?? 0.85;
        string runs = o.Optional("runs") ?? "both";
        if (runs is not ("both" or "daily" or "monthly"))
            throw new UsageException("--runs must be both, daily or monthly.");
        string? reference = o.Optional("reference");
        int? attempt = o.Int("attempt", 0, 100);
        // Smoke series (gate.main_pass_only): no f_ref was measured; the run is marked smoke instead of refusing.
        bool noFref = Bool(o, "no-fref", false);
        if (noFref && fref is not null)
            throw new UsageException("--no-fref true and --fref exclude each other.");
        if (manifestPath is not null)
        {
            BenchManifest m = BenchManifest.Load(manifestPath);
            int k = o.Int("process", 0, int.MaxValue) ?? throw new UsageException("--process is required with --manifest.");
            IReadOnlyList<BenchProcess> plan = m.Plan();
            if (k >= plan.Count)
                throw new UsageException($"--process must be below {plan.Count}.");
            p = plan[k];
            RunSwitches sw = Switches(o, m.HashCheckpoints);
            // A reference variant (П9 п. 1) changes the switches on purpose: it is a reference run, not a smoke run.
            bool smoke = (warmup is int w && w != m.WarmupMonths) || (measure is int n && n != m.MeasureMonths)
                || (reference is null && sw.HashCheckpoints != m.HashCheckpoints) || runs != "both" || noFref;
            // П5: a manifest (official) run without f_ref could not be checked for throttling afterwards; refuse instead of writing
            // "throttled": null. Smoke runs are never part of the analysis, so they may go without it.
            if (fref is null && !smoke)
                throw new UsageException("--fref MHZ (f_ref(k), EXP-0008 П5.2) is required with --manifest; measure it with --mode fref --stat topk --k K.");
            rp = new BenchRunParams
            {
                Mode = "process", RepoDir = RepoDir(), ManifestPath = m.Path, ManifestHash = m.Hash, ChunkSize = m.ChunkSize,
                Warmup = warmup ?? m.WarmupMonths, Measure = measure ?? m.MeasureMonths, PrecheckMonths = precheck, Switches = sw, FRefMhz = fref,
                Smoke = smoke, K = kOpt, ThermalRatio = thermal, Runs = runs, Reference = reference, Attempt = attempt,
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
                K = kOpt, ThermalRatio = thermal, Runs = runs, Reference = reference, Attempt = attempt,
            };
        }
        o.CheckAllUsed();
        Console.WriteLine($"process {p.Index}: {p.Block} {p.Point.Name} seed {p.Seed} t{p.Threads} r{p.Replica} bg{p.Background}{(rp.Smoke ? " [smoke]" : "")}");
        return ProcessBench.Execute(p, rp, outFile, Console.Out);
    }

    private static int Alloc(Options o)
    {
        string outFile = Path.GetFullPath(o.Required("out"));
        SynthWorldSpec spec = Point(o);
        ulong seed = U64(o, "seed") ?? 1;
        int threads = o.Int("threads", 1, 1024) ?? 1;
        int months = o.Int("months", 1, 10_000) ?? 36;
        int warmup = o.Int("warmup", 0, 10_000) ?? Math.Min(2, months - 1);
        bool lohEvents = Bool(o, "loh-events", true);
        double snapshotX = Double(o, "snapshot-x-mib") ?? 8;   // ADR-0023 rev 2 п. 2.3 proposal; the owner's decision
        o.CheckAllUsed();
        if (warmup >= months)
            throw new UsageException("--warmup must be below --months.");
        long b0 = Stopwatch.GetTimestamp();
        SynthWorld w = Build(spec, outFile);
        double buildMs = ProcessBench.Ms(Stopwatch.GetTimestamp() - b0);
        var rp = new BenchRunParams { Mode = "alloc", RepoDir = RepoDir(), Warmup = warmup, Measure = months - warmup, Smoke = true };
        return AllocProfile.Run(w, seed, threads, months, lohEvents, snapshotX, rp, buildMs, outFile, Console.Out);
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
        using var meter = new LoadMeter();
        MachineSnapshot s0 = meter.Snapshot();
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
            meter.Write(j, [new MachineWindow("validity", s0, meter.Snapshot())], 16);
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
        using var meter = new LoadMeter();
        MachineSnapshot s0 = meter.Snapshot();
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
        // The window includes world builds between the points: R modes are gated by foreign_cpu only (EXP-0008 П9 п. 6).
        meter.Write(j, [new MachineWindow("r1", s0, meter.Snapshot())], 1);
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
        using var meter = new LoadMeter();
        MachineSnapshot s0 = meter.Snapshot();
        using var json = BenchJson.Open(outFile);
        Utf8JsonWriter j = Header(json, "r3", warmup, calls);
        j.WritePropertyName("r3");
        CopyBench.R3(j, w, s.World.Table, warmup, calls);
        meter.Write(j, [new MachineWindow("r3", s0, meter.Snapshot())], 1);
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
        using var meter = new LoadMeter();
        MachineSnapshot s0 = meter.Snapshot();
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
        j.WriteNumber("seed", seed);
        meter.Write(j, [new MachineWindow(name, s0, meter.Snapshot())], Math.Max(threads, threadsList.Max()));
        j.WriteEndObject();
        return 0;
    }

    /// <summary>
    /// f_ref (EXP-0008 П5.2): N threads of integer load for the given seconds; f_ref = median over the samples of the last tail seconds of
    /// the statistic: <c>topk</c> (default; mean of the k largest CPU values, k = --k, default the load threads) or <c>mean</c> (all CPUs, EXP-0007).
    /// </summary>
    private static int FRef(Options o)
    {
        string outFile = Path.GetFullPath(o.Required("out"));
        int seconds = o.Int("seconds", 2, 86_400) ?? 300;
        int tail = o.Int("tail", 1, 86_400) ?? 180;
        int threads = o.Int("threads", 1, 1024) ?? 8;
        string stat = o.Optional("stat") ?? "topk";
        if (stat is not ("topk" or "mean"))
            throw new UsageException("--stat must be topk or mean.");
        int k = o.Int("k", 1, 4096) ?? threads;
        o.CheckAllUsed();
        if (tail > seconds)
            throw new UsageException("--tail must not exceed --seconds.");
        using var freq = new FreqSampler();
        using var meter = new LoadMeter();
        MachineSnapshot s0 = meter.Snapshot();
        long t0 = Stopwatch.GetTimestamp();
        using (new BackgroundLoad(threads))
            Thread.Sleep(TimeSpan.FromSeconds(seconds));
        long t1 = Stopwatch.GetTimestamp();
        var win = new MachineWindow("fref", s0, meter.Snapshot());
        long tailStart = t1 - (long)(tail * (double)Stopwatch.Frequency);
        (long, long)[] w = [(Math.Max(t0, tailStart), t1)];
        double? topk = freq.TopKMedianIn(w, k), mean = freq.MedianIn(w);
        double? fref = stat == "topk" ? topk : mean;
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
            j.WriteString("stat", stat);
            j.WriteNumber("k", k);
            LoadMeter.WriteNum(j, "f_ref_mhz", fref);
            LoadMeter.WriteNum(j, "topk_median_mhz", topk);
            LoadMeter.WriteNum(j, "mean_median_mhz", mean);
            BenchJson.WriteArray(j, "samples_mhz", [.. freq.Samples.Select(x => x.Mhz)]);
            BenchJson.WriteArray(j, "samples_topk_mhz", [.. freq.PerCpu.Select(x => LoadMath.TopKMean(x.Mhz, k) ?? 0)]);
            meter.Write(j, [win], threads);
            j.WriteEndObject();
        }
        Console.WriteLine(fref is double ff ? string.Create(CultureInfo.InvariantCulture, $"f_ref={ff:F0} MHz ({stat}, k {k}, {freq.Source})") : "f_ref: no samples");
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
