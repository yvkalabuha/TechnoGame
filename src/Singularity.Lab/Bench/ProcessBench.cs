using System.Diagnostics;
using System.Text.Json;
using Singularity.Core.Commands;
using Singularity.Core.Policy;
using Singularity.Kernel;
using Singularity.Session;

namespace Singularity.Lab.Bench;

/// <summary>Execution switches of one bench run (ADR-0017 R4, R2 г). None of them may change the state hash.</summary>
public sealed record RunSwitches(bool HashCheckpoints, bool HashEveryTick, bool DailyNoop, bool FullCopy);

/// <summary>Timing of one run (monthly or daily) of a process (EXP-0007 П3).</summary>
public sealed class RunTiming
{
    public required bool Daily { get; init; }
    /// <summary>Host time of every month (warm-up included), ms: the sum of the tick times of the month (commands are submitted between ticks, untimed).</summary>
    public required double[] MonthMs { get; init; }
    /// <summary>Days of every month (ticks between two monthly steps).</summary>
    public required int[] MonthDays { get; init; }
    /// <summary>Every tick of the measured window, ms.</summary>
    public required List<double> WindowTickMs { get; init; }
    public required long AllocatedBytes { get; init; }
    public required int Gen0 { get; init; }
    public required int Gen1 { get; init; }
    public required int Gen2 { get; init; }
    public required double GcPauseMs { get; init; }
    public required double WindowWallMs { get; init; }
    public required long HashComputations { get; init; }
    public required long CommandsSubmitted { get; init; }
    public required string FinalHash { get; init; }
    public required long PeakWorkingSetBytes { get; init; }
    public required (long Start, long End) Window { get; init; }
}

/// <summary>
/// One EXP-0007 process (П2–П4): a monthly and a daily run of one point, seed and thread count with a fresh <see cref="SliceSession"/> each;
/// Stopwatch only in the host (ADR-0016 п. 1: world generation and session creation are not timed). Also the StateHash validity check
/// (П6 V1/V2) that runs before any timing.
/// </summary>
public static class ProcessBench
{
    /// <summary>Thread × ChunkSize pairs of V1 (ADR-0016 §4 INVALID (а)): threads 1/4/8/16 at ChunkSize 8, and t8 at ChunkSize 1/32.</summary>
    public static readonly (int Threads, int Chunk)[] ValidityGrid = [(1, 8), (4, 8), (8, 8), (16, 8), (8, 1), (8, 32)];

    public static SliceRunConfig Config(SynthWorld w, ulong seed, int threads, int chunk, bool daily, RunSwitches sw) => new()
    {
        Seed = seed,
        ScenarioId = w.ScenarioId,
        Threads = threads,
        ChunkSize = chunk,
        ViewMode = SliceViewMode.Full,
        DailyTickProbe = daily,
        HashCheckpoints = sw.HashCheckpoints,
        HashEveryTick = sw.HashEveryTick,
    };

    /// <summary>
    /// Runs <paramref name="warmup"/> + <paramref name="measure"/> months. At the start of every month each actor gets one
    /// <c>SetBudgetShares</c> with its current shares (П2, the same in both modes); with <see cref="RunSwitches.DailyNoop"/> the daily run
    /// also gets, on every other day, actor 0's shares of the month again (R4 б).
    /// </summary>
    public static RunTiming Run(SynthWorld w, ulong seed, int threads, int chunk, bool daily, RunSwitches sw, int warmup, int measure)
    {
        SliceSession s = SliceSession.Create(w.Data, Config(w, seed, threads, chunk, daily, sw));
        s.World.Table.FullCopyMode = sw.FullCopy;
        int months = warmup + measure;
        var monthMs = new double[months];
        var days = new int[months];
        var ticks = new List<double>(measure * 31);
        int actors = s.World.Table.RowCount;
        int local = 0;
        long commands = 0;
        long alloc0 = 0, hash0 = 0;
        int g0 = 0, g1 = 0, g2 = 0;
        TimeSpan pause0 = TimeSpan.Zero;
        long win0 = 0;
        for (int m = 0; m < months; m++)
        {
            if (m == warmup)
            {
                alloc0 = GC.GetTotalAllocatedBytes(true);
                g0 = GC.CollectionCount(0);
                g1 = GC.CollectionCount(1);
                g2 = GC.CollectionCount(2);
                pause0 = GC.GetTotalPauseDuration();
                hash0 = s.HashComputations;
                win0 = Stopwatch.GetTimestamp();
            }
            BudgetShares shares0 = s.World.SharesOf(0);
            for (int a = 0; a < actors; a++)
            {
                Submit(s, ref local, new SetBudgetShares(a, s.World.SharesOf(a)));
                commands++;
            }
            long end = s.World.Calendar.TickOfMonth(m) + 1;
            long first = s.Tick;
            long total = 0;
            while (s.Tick < end)
            {
                if (sw.DailyNoop && daily && s.Tick != first)
                {
                    Submit(s, ref local, new SetBudgetShares(0, shares0));
                    commands++;
                }
                long t0 = Stopwatch.GetTimestamp();
                s.Step(1);
                long dt = Stopwatch.GetTimestamp() - t0;
                total += dt;
                if (m >= warmup)
                    ticks.Add(Ms(dt));
            }
            monthMs[m] = Ms(total);
            days[m] = (int)(end - first);
        }
        long win1 = Stopwatch.GetTimestamp();
        var timing = new RunTiming
        {
            Daily = daily,
            MonthMs = monthMs,
            MonthDays = days,
            WindowTickMs = ticks,
            AllocatedBytes = GC.GetTotalAllocatedBytes(true) - alloc0,
            Gen0 = GC.CollectionCount(0) - g0,
            Gen1 = GC.CollectionCount(1) - g1,
            Gen2 = GC.CollectionCount(2) - g2,
            GcPauseMs = (GC.GetTotalPauseDuration() - pause0).TotalMilliseconds,
            WindowWallMs = Ms(win1 - win0),
            HashComputations = s.HashComputations - hash0,
            CommandsSubmitted = commands,
            FinalHash = s.LastHash,
            PeakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
            Window = (win0, win1),
        };
        return timing;
    }

    private static void Submit(SliceSession s, ref int local, SliceCommand c)
    {
        CommandVerdict v = s.Submit(SourceClass.Harness, 0, local++, c);
        if (!v.Accepted)
            throw new InvalidOperationException($"bench command refused at tick {s.Tick}: {v.Message}");
    }

    public static double Ms(long stopwatchTicks) => stopwatchTicks * 1000.0 / Stopwatch.Frequency;

    /// <summary>
    /// V1 + V2 (П6): final StateHash of the daily run for every (threads, ChunkSize) of <see cref="ValidityGrid"/>, and of the monthly run at
    /// t8/c8. Valid when all are equal. No timing.
    /// </summary>
    public static (bool Valid, List<(string Label, string Hash)> Hashes) Validity(SynthWorld w, ulong seed, int months, RunSwitches sw)
    {
        var hashes = new List<(string, string)>();
        foreach ((int t, int c) in ValidityGrid)
            hashes.Add(($"daily t{t} c{c}", FinalHash(w, seed, t, c, daily: true, sw, months)));
        hashes.Add(("monthly t8 c8", FinalHash(w, seed, 8, 8, daily: false, sw, months)));
        return (hashes.All(h => h.Item2 == hashes[0].Item2), hashes);
    }

    private static string FinalHash(SynthWorld w, ulong seed, int t, int c, bool daily, RunSwitches sw, int months) =>
        Run(w, seed, t, c, daily, sw with { DailyNoop = false }, 0, months).FinalHash;

    /// <summary>Writes the process JSON: provenance, conditions, world, validity pre-check, both runs, the decomposition (П4) and frequency.</summary>
    public static int Execute(BenchProcess p, BenchRunParams rp, string outFile, TextWriter log)
    {
        string worldDir = Path.Combine(Path.GetDirectoryName(outFile)!, "worlds", p.Point.Name);
        long b0 = Stopwatch.GetTimestamp();
        SynthWorld w = SynthWorldGenerator.Build(p.Point, worldDir);
        double buildMs = Ms(Stopwatch.GetTimestamp() - b0);
        log.WriteLine($"world {p.Point.Name}: built in {buildMs:F0} ms, data_hash {w.Data.DataHash[..12]}");

        var sw = rp.Switches;
        (bool Valid, List<(string Label, string Hash)> Hashes)? pre = null;
        if (rp.PrecheckMonths > 0)
        {
            pre = Validity(w, p.Seed, rp.PrecheckMonths, sw);
            log.WriteLine($"precheck {rp.PrecheckMonths} month(s): {(pre.Value.Valid ? "valid" : "INVALID")}");
            if (!pre.Value.Valid)
            {
                WriteProcessJson(outFile, p, rp, w, buildMs, pre, null, null, null);
                return 1;
            }
        }

        using var freq = new FreqSampler();
        using BackgroundLoad? bg = p.Background > 0 ? new BackgroundLoad(p.Background) : null;
        RunTiming first = Run(w, p.Seed, p.Threads, rp.ChunkSize, p.DailyFirst, sw, rp.Warmup, rp.Measure);
        log.WriteLine($"{(first.Daily ? "daily" : "monthly")} run: p50 {BenchStats.Median(first.MonthMs[rp.Warmup..]):F1} ms/month");
        RunTiming second = Run(w, p.Seed, p.Threads, rp.ChunkSize, !p.DailyFirst, sw, rp.Warmup, rp.Measure);
        log.WriteLine($"{(second.Daily ? "daily" : "monthly")} run: p50 {BenchStats.Median(second.MonthMs[rp.Warmup..]):F1} ms/month");
        WriteProcessJson(outFile, p, rp, w, buildMs, pre, first, second, freq);
        RunTiming monthly = first.Daily ? second : first, dailyRun = first.Daily ? first : second;
        bool v2 = monthly.FinalHash == dailyRun.FinalHash;
        log.WriteLine($"V2 monthly vs daily hash: {(v2 ? "equal" : "DIFFERENT")}; json {outFile}");
        return v2 ? 0 : 1;
    }

    private static void WriteProcessJson(string outFile, BenchProcess p, BenchRunParams rp, SynthWorld w, double buildMs,
        (bool Valid, List<(string Label, string Hash)> Hashes)? pre, RunTiming? first, RunTiming? second, FreqSampler? freq)
    {
        using var json = BenchJson.Open(outFile);
        Utf8JsonWriter j = json.Writer;
        j.WriteStartObject();
        j.WriteString("kind", "lab-bench/process");
        j.WriteNumber("format", 1);
        BenchEnv.Write(j, rp.RepoDir);
        BenchJson.WriteParams(j, rp);
        j.WriteStartObject("process");
        j.WriteNumber("index", p.Index);
        j.WriteNumber("table_index", p.TableIndex);
        j.WriteString("block", p.Block);
        j.WriteNumber("seed", p.Seed);
        j.WriteNumber("threads", p.Threads);
        j.WriteNumber("replica", p.Replica);
        j.WriteNumber("background_threads", p.Background);
        j.WriteString("order", p.DailyFirst ? "daily,monthly" : "monthly,daily");
        j.WriteEndObject();
        BenchJson.WriteWorld(j, w, buildMs);
        if (pre is { } v)
            BenchJson.WriteValidity(j, "precheck", v.Valid, v.Hashes, rp.PrecheckMonths);
        if (first is not null && second is not null)
        {
            RunTiming monthly = first.Daily ? second : first, daily = first.Daily ? first : second;
            j.WriteStartArray("runs");
            WriteRun(j, first, rp.Warmup);
            WriteRun(j, second, rp.Warmup);
            j.WriteEndArray();
            WriteDecomposition(j, monthly, daily, rp.Warmup, w.Spec);
            j.WriteBoolean("v2_monthly_daily_hash_equal", monthly.FinalHash == daily.FinalHash);
            if (freq is not null)
                WriteFrequency(j, freq, [monthly.Window, daily.Window], rp.FRefMhz);
        }
        j.WriteEndObject();
    }

    private static void WriteRun(Utf8JsonWriter j, RunTiming r, int warmup)
    {
        double[] window = r.MonthMs[warmup..];
        j.WriteStartObject();
        j.WriteString("mode", r.Daily ? "daily" : "monthly");
        j.WriteString("final_state_hash", r.FinalHash);
        BenchJson.WriteSummary(j, "month_ms", BenchStats.Summarize(window));
        BenchJson.WriteSummary(j, "tick_ms", BenchStats.Summarize(r.WindowTickMs));
        j.WriteNumber("window_months", window.Length);
        j.WriteNumber("window_wall_ms", r.WindowWallMs);
        j.WriteNumber("allocated_bytes", r.AllocatedBytes);
        j.WriteNumber("allocated_bytes_per_month", (double)r.AllocatedBytes / window.Length);
        j.WriteBoolean("alloc_le_1mb_per_month", r.AllocatedBytes / (double)window.Length <= 1 << 20);
        j.WriteNumber("gc_gen0", r.Gen0);
        j.WriteNumber("gc_gen1", r.Gen1);
        j.WriteNumber("gc_gen2", r.Gen2);
        j.WriteBoolean("gc_gen2_zero", r.Gen2 == 0);
        j.WriteNumber("gc_pause_ms", r.GcPauseMs);
        j.WriteNumber("gc_pause_share", r.WindowWallMs > 0 ? r.GcPauseMs / r.WindowWallMs : 0);
        j.WriteNumber("hash_computations", r.HashComputations);
        j.WriteNumber("commands_submitted", r.CommandsSubmitted);
        j.WriteNumber("peak_working_set_bytes", r.PeakWorkingSetBytes);
        BenchJson.WriteArray(j, "month_ms_all", r.MonthMs);
        j.WriteStartArray("month_days_all");
        foreach (int d in r.MonthDays) j.WriteNumberValue(d);
        j.WriteEndArray();
        j.WriteEndObject();
    }

    /// <summary>ADR-0016 п. 2 / EXP-0007 П4 per month pair: k = (T − M)/29, K_d = 30·k, S = M − k; also k by days (T − M)/(d − 1).</summary>
    private static void WriteDecomposition(Utf8JsonWriter j, RunTiming monthly, RunTiming daily, int warmup, SynthWorldSpec spec)
    {
        int n = monthly.MonthMs.Length - warmup;
        var k = new double[n];
        var kd = new double[n];
        var s = new double[n];
        var kDays = new List<double>(n);
        for (int i = 0; i < n; i++)
        {
            double m = monthly.MonthMs[warmup + i], t = daily.MonthMs[warmup + i];
            k[i] = (t - m) / 29.0;
            kd[i] = 30.0 * k[i];
            s[i] = m - k[i];
            int d = monthly.MonthDays[warmup + i];
            if (d > 1)
                kDays.Add((t - m) / (d - 1));
        }
        j.WriteStartObject("decomposition");
        BenchJson.WriteSummary(j, "k_ms", BenchStats.Summarize(k));
        BenchJson.WriteSummary(j, "K_d_ms", BenchStats.Summarize(kd));
        BenchJson.WriteSummary(j, "S_ms", BenchStats.Summarize(s));
        if (kDays.Count > 0)
            BenchJson.WriteSummary(j, "k_by_days_ms", BenchStats.Summarize(kDays));
        double tP95 = BenchStats.Percentile(daily.MonthMs[warmup..], 0.95);
        double mP95 = BenchStats.Percentile(monthly.MonthMs[warmup..], 0.95);
        double kP95 = BenchStats.Percentile(k, 0.95);
        j.WriteNumber("T_p95_ms", tP95);
        j.WriteNumber("M_p95_plus_29_k_p95_ms", mP95 + 29 * kP95);
        j.WriteBoolean("K_d_p95_gt_150", BenchStats.Percentile(kd, 0.95) > 150);
        j.WriteNumber("S_p50_us_per_actor_month", BenchStats.Median(s) * 1000 / spec.Actors);
        j.WriteNumber("S_p95_us_per_actor_month", BenchStats.Percentile(s, 0.95) * 1000 / spec.Actors);
        j.WriteNumber("S_p50_us_per_node", BenchStats.Median(s) * 1000 / spec.Nodes);
        BenchJson.WriteArray(j, "k_ms_all", k);
        j.WriteEndObject();
    }

    private static void WriteFrequency(Utf8JsonWriter j, FreqSampler f, IReadOnlyList<(long, long)> windows, double? fRef)
    {
        j.WriteStartObject("frequency");
        j.WriteString("source", f.Source);
        double? med = f.MedianIn(windows);
        if (med is double m) j.WriteNumber("median_mhz_in_windows", m); else j.WriteNull("median_mhz_in_windows");
        if (fRef is double r)
        {
            j.WriteNumber("f_ref_mhz", r);
            if (med is double mm) j.WriteBoolean("throttled", mm < 0.9 * r); else j.WriteNull("throttled");
        }
        else
        {
            j.WriteNull("f_ref_mhz");
            j.WriteNull("throttled");
        }
        var samples = f.Samples;
        j.WriteNumber("samples", samples.Count);
        BenchJson.WriteArray(j, "samples_mhz", [.. samples.Select(x => x.Mhz)]);
        j.WriteEndObject();
    }
}
