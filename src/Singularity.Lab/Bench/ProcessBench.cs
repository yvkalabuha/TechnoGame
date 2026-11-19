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
    /// <summary>
    /// Host time of every month (warm-up included), ms: the sum of the tick times of the month. Commands are submitted between ticks,
    /// outside the timed span (decision: docs/simulation/lab-cli.md «Submit вне M_i/T_i»).
    /// </summary>
    public required double[] MonthMs { get; init; }
    /// <summary>Days of every month (ticks between two monthly steps).</summary>
    public required int[] MonthDays { get; init; }
    /// <summary>Every tick of the measured window, ms.</summary>
    public required List<double> WindowTickMs { get; init; }
    public required long AllocatedBytes { get; init; }
    /// <summary>Part of <see cref="AllocatedBytes"/> made by the <see cref="FreqSampler"/> thread in the window (0 without a sampler).</summary>
    public required long SamplerAllocatedBytes { get; init; }
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
    /// <summary>Part of <see cref="AllocatedBytes"/> made by the <see cref="LoadMeter"/> loop in the window (0 without a meter).</summary>
    public required long MeterAllocatedBytes { get; init; }
    /// <summary>Machine snapshots at the bounds of the window (EXP-0008 П5.5); null without a meter.</summary>
    public MachineWindow? Machine { get; init; }
    /// <summary>Host UTC bounds of the measured window (the GC events are matched against them).</summary>
    public (DateTime From, DateTime To) WindowUtc { get; init; }
    /// <summary>Host UTC bounds of the forced collection after <c>SliceSession.Create</c> (<see cref="ProcessBench.SettleGc"/>).</summary>
    public (DateTime From, DateTime To) SettleUtc { get; init; }
    /// <summary><c>GC.CollectionCount(0)</c> before and after the settle collection: its GCs are the indices in (After, Upto].</summary>
    public (int After, int Upto) SettleGcs { get; init; }
    /// <summary>Part of <see cref="AllocatedBytes"/> made by the <see cref="GcEventListener"/> dispatch thread in the window (0 without a listener).</summary>
    public long ListenerAllocatedBytes { get; init; }
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

    /// <summary>
    /// Decision #416 / DEBT-0057 п. 3: after the world (session) is built and before the first month, outside every timed span, one forced
    /// blocking compacting gen2 collection and the pending finalizers, so the start-up gen2 (the world passing gen0 → gen1 → gen2) does not
    /// fall into the run. Written into <c>conditions</c> by <see cref="WriteSettleCondition"/>.
    /// </summary>
    public static void SettleGc()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
    }

    public const string SettleDescription = "GC.Collect(2, Forced, blocking: true, compacting: true) + GC.WaitForPendingFinalizers() after SliceSession.Create, before month 0, outside every timed span (decision #416)";

    /// <summary>The conditions entries of the settle collection and of the GC event listener.</summary>
    public static void WriteSettleCondition(Utf8JsonWriter j, string gcEvents)
    {
        j.WriteBoolean("gc_settle_before_window", true);
        j.WriteString("gc_settle", SettleDescription);
        j.WriteString("gc_events", gcEvents);
    }

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
    /// also gets, before every tick of the month except its first one, actor 0's shares from the start of the month again (R4 б: a
    /// no-op command on every day).
    /// </summary>
    internal static RunTiming Run(SynthWorld w, ulong seed, int threads, int chunk, bool daily, RunSwitches sw, int warmup, int measure,
        FreqSampler? sampler = null, LoadMeter? meter = null, GcEventListener? gcl = null)
    {
        SliceSession s = SliceSession.Create(w.Data, Config(w, seed, threads, chunk, daily, sw));
        s.World.Table.FullCopyMode = sw.FullCopy;
        DateTime settle0 = DateTime.UtcNow;
        int settleGc0 = GC.CollectionCount(0);
        SettleGc();
        int settleGc1 = GC.CollectionCount(0);
        DateTime settle1 = DateTime.UtcNow;
        int months = warmup + measure;
        var monthMs = new double[months];
        var days = new int[months];
        var ticks = new List<double>(measure * 31);
        int actors = s.World.Table.RowCount;
        int local = 0;
        long commands = 0;
        long alloc0 = 0, hash0 = 0, samplerAlloc0 = 0, meterAlloc0 = 0, listenerAlloc0 = 0;
        DateTime utc0 = default;
        MachineSnapshot? m0 = null;
        int g0 = 0, g1 = 0, g2 = 0;
        TimeSpan pause0 = TimeSpan.Zero;
        long win0 = 0;
        for (int m = 0; m < months; m++)
        {
            if (m == warmup)
            {
                // The snapshot allocates (it lists /proc): taken before the allocation count starts.
                m0 = meter?.Snapshot();
                meterAlloc0 = meter?.AllocatedBytes ?? 0;
                alloc0 = GC.GetTotalAllocatedBytes(true);
                samplerAlloc0 = sampler?.AllocatedBytes ?? 0;
                listenerAlloc0 = gcl?.OwnBytes ?? 0;
                g0 = GC.CollectionCount(0);
                g1 = GC.CollectionCount(1);
                g2 = GC.CollectionCount(2);
                pause0 = GC.GetTotalPauseDuration();
                hash0 = s.HashComputations;
                utc0 = DateTime.UtcNow;
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
        DateTime utc1 = DateTime.UtcNow;
        long alloc1 = GC.GetTotalAllocatedBytes(true);
        long samplerAlloc1 = sampler?.AllocatedBytes ?? 0, meterAlloc1 = meter?.AllocatedBytes ?? 0, listenerAlloc1 = gcl?.OwnBytes ?? 0;
        int g0b = GC.CollectionCount(0) - g0, g1b = GC.CollectionCount(1) - g1, g2b = GC.CollectionCount(2) - g2;
        TimeSpan pause = GC.GetTotalPauseDuration() - pause0;
        // After the allocation count: the end snapshot allocates.
        MachineSnapshot? m1 = m0 is null ? null : meter!.Snapshot();
        var timing = new RunTiming
        {
            Daily = daily,
            MonthMs = monthMs,
            MonthDays = days,
            WindowTickMs = ticks,
            AllocatedBytes = alloc1 - alloc0,
            SamplerAllocatedBytes = samplerAlloc1 - samplerAlloc0,
            MeterAllocatedBytes = meterAlloc1 - meterAlloc0,
            Machine = m0 is null ? null : new MachineWindow(daily ? "daily" : "monthly", m0, m1!),
            Gen0 = g0b,
            Gen1 = g1b,
            Gen2 = g2b,
            GcPauseMs = pause.TotalMilliseconds,
            WindowWallMs = Ms(win1 - win0),
            HashComputations = s.HashComputations - hash0,
            CommandsSubmitted = commands,
            FinalHash = s.LastHash,
            PeakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
            Window = (win0, win1),
            WindowUtc = (utc0, utc1),
            SettleUtc = (settle0, settle1),
            SettleGcs = (settleGc0, settleGc1),
            ListenerAllocatedBytes = listenerAlloc1 - listenerAlloc0,
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

    /// <summary>Writes the process JSON: provenance, conditions, world, validity pre-check, the runs, the decomposition (П4), frequency, machine.</summary>
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
                WriteProcessJson(outFile, p, rp, w, buildMs, pre, [], null, null, null);
                return 1;
            }
        }

        using var freq = new FreqSampler();
        using var meter = new LoadMeter();
        if (!meter.Available)
            log.WriteLine($"load meter unavailable: {meter.Reason}");
        // Decision #416: every GC of the windows with kind, generation and pause; Informational level (no allocation ticks in the timed path).
        using var gcl = new GcEventListener(raw: false, verbose: false);
        using BackgroundLoad? bg = p.Background > 0 ? new BackgroundLoad(p.Background) : null;
        bool[] order = rp.Runs switch
        {
            "daily" => [true],
            "monthly" => [false],
            _ => [p.DailyFirst, !p.DailyFirst],
        };
        var runs = new List<RunTiming>();
        foreach (bool daily in order)
        {
            RunTiming r = Run(w, p.Seed, p.Threads, rp.ChunkSize, daily, sw, rp.Warmup, rp.Measure, freq, meter, gcl);
            log.WriteLine($"{(r.Daily ? "daily" : "monthly")} run: p50 {BenchStats.Median(r.MonthMs[rp.Warmup..]):F1} ms/month");
            runs.Add(r);
        }
        // After the last window: the flush collection cannot fall into a timed span.
        (bool flushed, double? clockMs, double flushSpanMs) = gcl.Flush(TimeSpan.FromSeconds(5));
        var gcEv = new GcEvents(GcPauses.Build(gcl.Snapshot()), flushed, clockMs, flushSpanMs);
        GcPauses.Summary all = gcEv.Of(runs);
        log.WriteLine($"gc: {all.Count} pauses, max {all.MaxMs ?? 0:F2} ms, p99 {all.P99Ms ?? 0:F2} ms, blocking gen2 {all.BlockingGen2}, gen2 {all.Gen2}"
            + $"{(flushed ? "" : " (events not flushed)")}");
        WriteProcessJson(outFile, p, rp, w, buildMs, pre, runs, freq, meter, gcEv);
        if (runs.Count < 2)
        {
            log.WriteLine($"json {outFile}");
            return 0;
        }
        bool v2 = runs[0].FinalHash == runs[1].FinalHash;
        log.WriteLine($"V2 monthly vs daily hash: {(v2 ? "equal" : "DIFFERENT")}; json {outFile}");
        return v2 ? 0 : 1;
    }

    private static void WriteProcessJson(string outFile, BenchProcess p, BenchRunParams rp, SynthWorld w, double buildMs,
        (bool Valid, List<(string Label, string Hash)> Hashes)? pre, IReadOnlyList<RunTiming> runs, FreqSampler? freq, LoadMeter? meter, GcEvents? gcEv)
    {
        using var json = BenchJson.Open(outFile);
        Utf8JsonWriter j = json.Writer;
        j.WriteStartObject();
        j.WriteString("kind", "lab-bench/process");
        // Format 3 (decision #416): settle collection before every run, GC pauses from events per run and per process.
        j.WriteNumber("format", 3);
        BenchEnv.Write(j, rp.RepoDir, c => WriteSettleCondition(c, "GcEventListener, Informational: GCStart/GCEnd/GCSuspendEEBegin/GCRestartEEEnd"));
        BenchJson.WriteParams(j, rp);
        j.WriteStartObject("process");
        j.WriteNumber("index", p.Index);
        j.WriteNumber("table_index", p.TableIndex);
        j.WriteString("block", p.Block);
        j.WriteString("point", p.Point.Name);
        j.WriteNumber("seed", p.Seed);
        j.WriteNumber("threads", p.Threads);
        j.WriteNumber("replica", p.Replica);
        j.WriteNumber("background_threads", p.Background);
        j.WriteString("order", string.Join(",", runs.Select(r => r.Daily ? "daily" : "monthly")));
        j.WriteEndObject();
        BenchJson.WriteWorld(j, w, buildMs);
        if (pre is { } v)
            BenchJson.WriteValidity(j, "precheck", v.Valid, v.Hashes, rp.PrecheckMonths);
        if (runs.Count > 0)
        {
            j.WriteStartArray("runs");
            foreach (RunTiming r in runs)
                WriteRun(j, r, rp.Warmup, gcEv);
            j.WriteEndArray();
            RunTiming? monthly = runs.FirstOrDefault(r => !r.Daily), daily = runs.FirstOrDefault(r => r.Daily);
            if (monthly is not null && daily is not null)
            {
                WriteDecomposition(j, monthly, daily, rp.Warmup, w.Spec);
                j.WriteBoolean("v2_monthly_daily_hash_equal", monthly.FinalHash == daily.FinalHash);
            }
            // ADR-0023 п. 2.4: the GC pause share of the process (all its timed windows); the statistic over processes is the analysis's.
            double pauseMs = runs.Sum(r => r.GcPauseMs), wallMs = runs.Sum(r => r.WindowWallMs);
            j.WriteStartObject("gc");
            j.WriteNumber("pause_ms", pauseMs);
            j.WriteNumber("window_wall_ms", wallMs);
            j.WriteNumber("pause_share", wallMs > 0 ? pauseMs / wallMs : 0);
            j.WriteNumber("gen2", runs.Sum(r => r.Gen2));
            if (gcEv is not null)
            {
                // Decision #416: the pause criterion's inputs over all timed windows of the process (events; the share above is the runtime's).
                GcPauses.Summary s = gcEv.Of(runs);
                LoadMeter.WriteNum(j, "pause_max_ms", s.MaxMs);
                LoadMeter.WriteNum(j, "pause_p50_ms", s.P50Ms);
                LoadMeter.WriteNum(j, "pause_p99_ms", s.P99Ms);
                j.WriteNumber("pause_count", s.Count);
                j.WriteNumber("gen2_blocking_after_warmup", s.BlockingGen2);
                j.WriteNumber("gen2_background_after_warmup", s.Gcs.Count(g => g.Kind == GcPauses.Background));
                j.WriteNumber("pause_ms_events", s.TotalMs);
                LoadMeter.WriteNum(j, "pause_share_events", s.Share);
                gcEv.WriteChecks(j, runs, s);
                GcPauses.Write(j, "pauses", s, null);
            }
            j.WriteEndObject();
            int k = rp.K ?? p.Threads + p.Background;
            if (freq is not null)
                WriteFrequency(j, freq, [.. runs.Select(r => r.Window)], rp.FRefMhz, k, rp.ThermalRatio, daily, rp.Warmup);
            if (meter is not null)
                meter.Write(j, [.. runs.Where(r => r.Machine is not null).Select(r => r.Machine!)], k);
        }
        j.WriteEndObject();
    }

    private static void WriteRun(Utf8JsonWriter j, RunTiming r, int warmup, GcEvents? gcEv)
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
        // allocated_bytes counts every thread; the frequency sampler's share is given so the analysis can subtract it.
        j.WriteNumber("sampler_alloc_bytes", r.SamplerAllocatedBytes);
        j.WriteNumber("meter_alloc_bytes", r.MeterAllocatedBytes);
        j.WriteNumber("gc_listener_alloc_bytes", r.ListenerAllocatedBytes);
        // The simulation's own bytes: every thread minus the three measuring threads.
        j.WriteNumber("allocated_bytes_net", r.AllocatedBytes - r.SamplerAllocatedBytes - r.MeterAllocatedBytes - r.ListenerAllocatedBytes);
        j.WriteNumber("allocated_bytes_per_month", (double)r.AllocatedBytes / window.Length);
        // Reference only (the ADR-0016 §6 limit before ADR-0023): P8 is now split into tick path and snapshot path (bench --mode alloc).
        j.WriteBoolean("alloc_le_1mib_per_month_reference_pre_adr0023", r.AllocatedBytes / (double)window.Length <= 1 << 20);
        j.WriteNumber("gc_gen0", r.Gen0);
        j.WriteNumber("gc_gen1", r.Gen1);
        j.WriteNumber("gc_gen2", r.Gen2);
        j.WriteBoolean("gc_gen2_zero", r.Gen2 == 0);
        j.WriteNumber("gc_pause_ms", r.GcPauseMs);
        j.WriteNumber("gc_pause_share", r.WindowWallMs > 0 ? r.GcPauseMs / r.WindowWallMs : 0);
        if (gcEv is not null)
        {
            GcPauses.Summary settle = GcPauses.SummarizeGcs(gcEv.Timeline, [r.SettleGcs], (r.SettleUtc.To - r.SettleUtc.From).TotalMilliseconds);
            j.WriteNumber("gc_settle_host_ms", settle.WallMs);
            j.WriteNumber("gc_settle_gcs", settle.Gcs.Count);
            j.WriteNumber("gc_settle_pause_ms", settle.TotalMs);
            // Record only: the same span matched by host time (equal on Linux; off Linux the event clock may move it out, TASK-0116).
            j.WriteNumber("gc_settle_pause_ms_host_window", GcPauses.Summarize(gcEv.Timeline, [r.SettleUtc], 0).TotalMs);
            GcPauses.Write(j, "gc_pauses", gcEv.Of([r]), r.WindowUtc.From);
        }
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

    /// <summary>
    /// The frequency record (EXP-0008 П5.2): top-k median in the windows, the all-CPU mean (EXP-0007 metric, record only), f_ref(k) and the
    /// flag «possible thermal throttling» = top-k median &lt; ratio × f_ref(k). The flag decides nothing (ADR-0023 п. 1.2); the reference column
    /// T·f/f_ref uses the daily run's median T.
    /// </summary>
    internal static void WriteFrequency(Utf8JsonWriter j, FreqSampler f, IReadOnlyList<(long, long)> windows, double? fRef, int k, double ratio, RunTiming? daily, int warmup)
    {
        j.WriteStartObject("frequency");
        j.WriteString("source", f.Source);
        if (f.Failure is string fail) j.WriteString("sampler_failure", fail); else j.WriteNull("sampler_failure");
        j.WriteNumber("sampler_alloc_bytes_total", f.AllocatedBytes);
        j.WriteNumber("k", k);
        // A sampler that stopped on a fault gives no frequency (TASK-0116): the samples before the fault cover only part of the windows,
        // so the top-k median, the flag and the derived columns are null (samples are still written as a record).
        double? top = f.Failure is null ? f.TopKMedianIn(windows, k) : null;
        LoadMeter.WriteNum(j, "topk_median_mhz", top);
        LoadMeter.WriteNum(j, "median_mhz_in_windows", f.Failure is null ? f.MedianIn(windows) : null);
        LoadMeter.WriteNum(j, "f_ref_mhz", fRef);
        j.WriteNumber("thermal_ratio", ratio);
        bool? flag = LoadMath.PossibleThermalThrottle(top, fRef, ratio);
        if (flag is bool b) j.WriteBoolean("possible_thermal_throttle", b); else j.WriteNull("possible_thermal_throttle");
        LoadMeter.WriteNum(j, "f_over_f_ref", top is double t && fRef is double r ? t / r : null);
        LoadMeter.WriteNum(j, "T_p50_times_f_over_f_ref_ms",
            daily is not null && top is double t2 && fRef is double r2 ? BenchStats.Median(daily.MonthMs[warmup..]) * t2 / r2 : null);
        var per = f.PerCpu;
        j.WriteNumber("samples", per.Count);
        j.WriteNumber("cpus", per.Count > 0 ? per[0].Mhz.Length : 0);
        BenchJson.WriteArray(j, "samples_mhz", [.. per.Select(x => x.Mhz.Length == 0 ? 0 : x.Mhz.Average())]);
        BenchJson.WriteArray(j, "samples_topk_mhz", [.. per.Select(x => LoadMath.TopKMean(x.Mhz, k) ?? 0)]);
        j.WriteStartArray("samples_per_cpu_mhz");
        foreach ((_, double[] mhz) in per)
        {
            j.WriteStartArray();
            foreach (double x in mhz)
                j.WriteNumberValue(Math.Round(x, 1));
            j.WriteEndArray();
        }
        j.WriteEndArray();
        j.WriteEndObject();
    }
}

/// <summary>The paired GC events of a process and the flush record; <see cref="Of"/> gives the summary over the windows of some runs.</summary>
internal sealed record GcEvents(GcPauses.Timeline Timeline, bool Flushed, double? ClockOffsetMs, double FlushHostSpanMs)
{
    public GcPauses.Summary Of(IReadOnlyList<RunTiming> runs) =>
        GcPauses.Summarize(Timeline, [.. runs.Select(r => r.WindowUtc)], runs.Sum(r => r.WindowWallMs));

    /// <summary>Completeness: events flushed, GC and gen2 counts of the events equal the runtime counters, clock offset of the flush GC.</summary>
    public void WriteChecks(Utf8JsonWriter j, IReadOnlyList<RunTiming> runs, GcPauses.Summary s)
    {
        int gcsRuntime = runs.Sum(r => r.Gen0), gen2Runtime = runs.Sum(r => r.Gen2);
        j.WriteBoolean("events_flushed", Flushed);
        j.WriteNumber("gcs_events", s.Gcs.Count);
        j.WriteNumber("gcs_runtime", gcsRuntime);
        j.WriteNumber("gen2_events", s.Gen2);
        j.WriteBoolean("events_complete", Flushed && s.Gcs.Count == gcsRuntime && s.Gen2 == gen2Runtime);
        j.WriteNumber("unpaired_suspensions", Timeline.Unpaired);
        LoadMeter.WriteNum(j, "clock_offset_ms", ClockOffsetMs);
        j.WriteNumber("flush_host_span_ms", FlushHostSpanMs);
        j.WriteNumber("listener_alloc_bytes", runs.Sum(r => r.ListenerAllocatedBytes));
    }
}
