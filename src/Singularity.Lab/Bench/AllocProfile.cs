using System.Text.Json;
using Singularity.Core.Commands;
using Singularity.Core.Industry;
using Singularity.Kernel;
using Singularity.Session;

namespace Singularity.Lab.Bench;

/// <summary>
/// <c>lab bench --mode alloc</c> (TASK-0103, ADR-0023 п. 2, TASK-0106): allocated bytes and GC counts per month of a monthly run, split
/// into the tick path and the snapshot path, and the bytes of single pure reads (one allocation of effort, one tooling plan, the inputs
/// view, the step view, a snapshot with fresh and with reused per-actor node blocks: «without nodes» = all blocks reused). Counts bytes, not time, so it is valid on a busy machine.
/// <para>
/// Split [direct count, format 3]: two sessions with the same seed, threads and commands. The first runs with
/// <see cref="SliceSession.SkipViews"/> (TASK-0107 seam: the step view is built, no snapshot is published), so its month bytes are the tick path
/// (ADR-0023 п. 2.1); the second is the full run. Snapshot path of a month = (full − GC listener) − (tick − GC listener). Both runs must end
/// with the same <see cref="StateHash.ComputeHex"/> (views are pure reads), else the profile fails. The tick-only run goes first, so bytes of
/// one-time warm-up that outlast the warm-up months (static pools, lazy statics) fall on the tick path, the strict side of P8.
/// The old subtraction model (snapshots × probe bytes) is still written as <c>*_model</c> for comparison.
/// </para>
/// <para>
/// The probes run after the last month of the full run and only read the world; the state hash is recomputed before and after them
/// (not the lazy <c>LastHash</c>).
/// </para>
/// </summary>
internal static class AllocProfile
{
    private const double MiB = 1 << 20;

    private sealed record ProbeResult(string Name, long Bytes, int Calls, DateTime From, DateTime To)
    {
        public double PerCall => Bytes / (double)Calls;
    }

    public static int Run(SynthWorld w, ulong seed, int threads, int months, bool lohEvents, double snapshotXMib, BenchRunParams rp, double buildMs, string outFile, TextWriter log,
        bool fullFirst = false, bool gcTrace = false)
    {
        using GcEventListener? gcl = lohEvents || gcTrace ? new GcEventListener(raw: gcTrace) : null;
        using var meter = new LoadMeter();
        // Preallocated so that the marks do not allocate inside the month windows.
        List<StepMark>? steps = gcTrace ? new List<StepMark>(1 << 16) : null;
        TimeSpan pause0 = TimeSpan.Zero;
        long win0 = 0;
        MachineSnapshot? m0 = null;
        long win1 = 0;
        TimeSpan pause = TimeSpan.Zero;
        MachineSnapshot? m1 = null;
        void WindowStart()
        {
            m0 = meter.Snapshot();
            pause0 = GC.GetTotalPauseDuration();
            win0 = System.Diagnostics.Stopwatch.GetTimestamp();
        }
        void WindowEnd()
        {
            win1 = System.Diagnostics.Stopwatch.GetTimestamp();
            pause = GC.GetTotalPauseDuration() - pause0;
            m1 = meter.Snapshot();
        }
        // DEBT-0057 diagnosis (--full-first): the full run is the first run of the process, as in the format 2 probe; the tick-only run follows
        // after a forced collection. Default (format 3): the tick-only run first.
        MonthRun? early = null;
        if (fullFirst)
        {
            early = RunMonths(w, seed, threads, months, skipViews: false, rp.Warmup, WindowStart, log, "full", steps);
            WindowEnd();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        // 1. Tick path alone (SkipViews): the same seed, threads, commands and months as the full run below.
        // The session itself is dropped before the full run (only the counts are kept).
        MonthRun tickRun = RunMonths(w, seed, threads, months, skipViews: true, rp.Warmup, null, log, "tick", steps) with { Session = null };
        string hashTick = tickRun.Hash;
        if (!fullFirst)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        // 2. Full run (views on): machine window and GC pause window of the measured months.
        MonthRun full;
        if (early is not null)
            full = early;
        else
        {
            full = RunMonths(w, seed, threads, months, skipViews: false, rp.Warmup, WindowStart, log, "full", steps);
            WindowEnd();
        }
        SliceSession s = full.Session!;
        SliceWorld world = s.World;
        IndustryLayout l = world.IndustryLayout;
        int actors = world.Table.RowCount;
        long[] bytes = full.Bytes;
        int[,] gc = full.Gc;
        int[] snapPlain = full.SnapPlain, snapStepped = full.SnapStepped;
        DateTime[] from = full.From, to = full.To;

        string hashBefore = StateHash.ComputeHex(world.Table, SliceWorld.SchemaVersion);
        long month = months;   // the next step's month: the reads the step's compute and views would make
        MonthStepView step = ViewBuilder.Step(world, month - 1);
        var probes = new List<ProbeResult>
        {
            Probe("Industry.RestActor (1 EffortAllocator.Allocate)", actors, a => world.Industry.RestActor(a, month)),
            Probe("IndustryInputs.Plan (ToolingPlan.Compute only)", actors, a => world.IndustryInputs.Plan(a, month, Fx6.Zero)),
            Probe("Industry.Profile (1 item, view Process)", actors, a => world.Industry.Profile(l.SitesOf(a)[0], [new PrcSetItem(l.Number[0], Fx6.One, Fx6.One)], month)),
            Probe("ViewBuilder.Inputs (t1)", 1, _ => ViewBuilder.Inputs(world, month, 1)),
            Probe("ViewBuilder.Step", 1, _ => ViewBuilder.Step(world, month - 1)),
            // The node blocks (TASK-0103 R1/R2) are reused when no column they read changed since the last snapshot of the month: fresh
            // blocks are forced with ReuseNodeBlocks = false (views only, the hash is untouched). Order matters: (6) leaves blocks that (7) reuses.
            Probe("ViewBuilder.Snapshot (stepped, step given, fresh node blocks)", 1, _ => Fresh(world, () => ViewBuilder.Snapshot(world, s.Tick, null, month - 1, null, step))),
            Probe("ViewBuilder.Snapshot (no step, node blocks reused)", 1, _ => ViewBuilder.Snapshot(world, s.Tick, null, month - 1, null, null)),
            Probe("ViewBuilder.Snapshot (no step, fresh node blocks)", 1, _ => Fresh(world, () => ViewBuilder.Snapshot(world, s.Tick, null, month - 1, null, null))),
            Probe("ViewBuilder.Step + Snapshot (stepped)", 1, _ => ViewBuilder.Snapshot(world, s.Tick, null, month - 1, null, ViewBuilder.Step(world, month - 1))),
        };
        string hashAfter = StateHash.ComputeHex(world.Table, SliceWorld.SchemaVersion);
        DateTime last = DateTime.UtcNow;
        bool flushed = gcl?.Flush(last, TimeSpan.FromSeconds(5)) ?? false;
        IReadOnlyList<GcEventListener.GcEvent> events = gcl?.Snapshot() ?? [];
        if (gcTrace)
        {
            File.WriteAllLines(outFile + ".gc.jsonl", gcl!.Raw());
            File.WriteAllLines(outFile + ".steps.jsonl", steps!.Select(x =>
                $"{{\"run\":\"{x.Run}\",\"month\":{x.Month},\"tick\":{x.Tick},\"from\":\"{x.From:O}\",\"to\":\"{x.To:O}\",\"snapshot\":\"{x.Snapshot}\"}}"));
        }

        // In the run the monthly (stepped) snapshot builds fresh blocks and the command-tick snapshot after it reuses them (TASK-0103 R2).
        double snapSteppedB = probes[5].PerCall, snapPlainB = probes[6].PerCall, snapPlainFreshB = probes[7].PerCall;
        double nodesB = snapPlainFreshB - snapPlainB;
        foreach (ProbeResult p in probes)
            log.WriteLine($"probe {p.Name}: {p.PerCall / 1024.0:F1} KB/call × {p.Calls} = {p.Bytes / MiB:F2} MiB");
        log.WriteLine($"layout: actors {actors}, sites {l.SiteCount}, processes {l.ProcessCount}, clusters {l.Clusters.Length}, nodes {world.Graph.Count}");
        log.WriteLine($"hash before probes {hashBefore[..16]}, after {hashAfter[..16]} ({(hashBefore == hashAfter ? "equal" : "DIFFERENT")})");
        bool runsSameHash = hashTick == hashBefore;
        log.WriteLine($"hash of the tick-only run {hashTick[..16]} vs the full run {hashBefore[..16]} ({(runsSameHash ? "equal" : "DIFFERENT")})");

        using var json = BenchJson.Open(outFile);
        Utf8JsonWriter j = json.Writer;
        j.WriteStartObject();
        j.WriteString("kind", "lab-bench/alloc");
        j.WriteNumber("format", 3);
        j.WriteString("run_order", fullFirst ? "full-first" : "tick-first");
        j.WriteBoolean("gc_trace", gcTrace);
        BenchEnv.Write(j, rp.RepoDir);
        BenchJson.WriteParams(j, rp);
        BenchJson.WriteWorld(j, w, buildMs);
        j.WriteNumber("seed", seed);
        j.WriteNumber("threads", threads);
        j.WriteNumber("months", months);
        j.WriteNumber("warmup_months", rp.Warmup);
        j.WriteStartObject("layout");
        j.WriteNumber("actors", actors);
        j.WriteNumber("sites", l.SiteCount);
        j.WriteNumber("processes", l.ProcessCount);
        j.WriteNumber("clusters", l.Clusters.Length);
        j.WriteNumber("nodes", world.Graph.Count);
        j.WriteEndObject();
        j.WriteBoolean("gc_events", gcl is not null);
        j.WriteBoolean("gc_events_flushed", flushed);
        var tickPath = new List<double>();
        var snapPath = new List<double>();
        var snapPerSnapshot = new List<double>();
        var tickModel = new List<double>();
        int lohTicksWindow = 0, allocLargeWindow = 0, gen2Window = 0, gen2TickWindow = 0;
        j.WriteStartArray("months");
        for (int m = 0; m < months; m++)
        {
            var (lohTicks, lohBytes, allocLarge, gen2Gcs) = GcEventListener.In(events, from[m], to[m]);
            long own = GcEventListener.OwnBytesAt(events, to[m]) - GcEventListener.OwnBytesAt(events, from[m]);
            long ownTick = GcEventListener.OwnBytesAt(events, tickRun.To[m]) - GcEventListener.OwnBytesAt(events, tickRun.From[m]);
            long tick = tickRun.Bytes[m] - ownTick;
            long snap = bytes[m] - own - tick;
            int snapshots = snapPlain[m] + snapStepped[m];
            double snapModel = snapPlain[m] * snapPlainB + snapStepped[m] * snapSteppedB;
            double tickM = bytes[m] - snapModel - own;
            j.WriteStartObject();
            j.WriteNumber("month", m);
            j.WriteBoolean("measured", m >= rp.Warmup);
            j.WriteNumber("bytes", bytes[m]);
            j.WriteNumber("gc0", gc[m, 0]);
            j.WriteNumber("gc1", gc[m, 1]);
            j.WriteNumber("gc2", gc[m, 2]);
            j.WriteNumber("snapshots_no_step", snapPlain[m]);
            j.WriteNumber("snapshots_stepped", snapStepped[m]);
            j.WriteNumber("gc_listener_bytes", own);
            j.WriteNumber("tick_only_bytes", tickRun.Bytes[m]);
            j.WriteNumber("tick_only_gc0", tickRun.Gc[m, 0]);
            j.WriteNumber("tick_only_gc1", tickRun.Gc[m, 1]);
            j.WriteNumber("tick_only_gc2", tickRun.Gc[m, 2]);
            j.WriteNumber("tick_only_gc_listener_bytes", ownTick);
            j.WriteNumber("tick_path_bytes", tick);
            j.WriteNumber("snapshot_path_bytes", snap);
            j.WriteNumber("snapshot_path_bytes_per_snapshot", snapshots > 0 ? snap / (double)snapshots : 0);
            j.WriteNumber("snapshot_path_bytes_model", Math.Round(snapModel));
            j.WriteNumber("tick_path_bytes_model", Math.Round(tickM));
            j.WriteNumber("loh_alloc_ticks", lohTicks);
            j.WriteNumber("loh_bytes_sampled", lohBytes);
            j.WriteNumber("gc_alloc_large", allocLarge);
            j.WriteEndObject();
            if (m >= rp.Warmup)
            {
                tickPath.Add(tick);
                snapPath.Add(snap);
                if (snapshots > 0)
                    snapPerSnapshot.Add(snap / (double)snapshots);
                tickModel.Add(tickM);
                lohTicksWindow += lohTicks;
                allocLargeWindow += allocLarge;
                gen2Window += gc[m, 2];
                gen2TickWindow += tickRun.Gc[m, 2];
            }
        }
        j.WriteEndArray();
        j.WriteStartArray("probes");
        foreach (ProbeResult p in probes)
        {
            var (lohTicks, lohBytes, allocLarge, _) = GcEventListener.In(events, p.From, p.To);
            j.WriteStartObject();
            j.WriteString("name", p.Name);
            j.WriteNumber("calls", p.Calls);
            j.WriteNumber("bytes", p.Bytes);
            j.WriteNumber("bytes_per_call", p.PerCall);
            j.WriteNumber("loh_alloc_ticks", lohTicks);
            j.WriteNumber("loh_bytes_sampled", lohBytes);
            j.WriteNumber("gc_alloc_large", allocLarge);
            j.WriteEndObject();
        }
        j.WriteEndArray();

        // ADR-0023 п. 2: the three parts of P8, as records; the limit X of the snapshot is the owner's decision (rev 2 proposal: 8 MiB).
        j.WriteStartObject("p8_split");
        j.WriteString("method", "direct: tick path = bytes of a SkipViews run (same seed, threads, commands) − its GC listener bytes; snapshot path = (full − GC listener) − tick path (lab-cli.md «alloc»)");
        j.WriteString("state_hash_tick_only_run", hashTick);
        j.WriteBoolean("runs_same_hash", runsSameHash);
        if (tickPath.Count > 0)
        {
            j.WriteNumber("measured_months", tickPath.Count);
            j.WriteNumber("tick_path_mib_per_month_p50", BenchStats.Median(tickPath) / MiB);
            j.WriteNumber("tick_path_mib_per_month_mean", tickPath.Average() / MiB);
            j.WriteNumber("tick_path_mib_per_month_max", tickPath.Max() / MiB);
            j.WriteNumber("tick_path_max_month", rp.Warmup + tickPath.IndexOf(tickPath.Max()));
            j.WriteBoolean("tick_path_le_1mib_every_month", tickPath.Max() <= MiB);
            j.WriteBoolean("tick_path_le_1mib_p50", BenchStats.Median(tickPath) <= MiB);
            j.WriteNumber("snapshot_path_mib_per_month_p50", BenchStats.Median(snapPath) / MiB);
            j.WriteNumber("snapshot_path_mib_per_month_max", snapPath.Max() / MiB);
            if (snapPerSnapshot.Count > 0)
                j.WriteNumber("snapshot_path_mib_per_snapshot_max", snapPerSnapshot.Max() / MiB);
            j.WriteNumber("tick_path_mib_per_month_p50_model", BenchStats.Median(tickModel) / MiB);
        }
        j.WriteNumber("snapshot_stepped_fresh_bytes", snapSteppedB);
        j.WriteNumber("snapshot_no_step_reused_bytes", snapPlainB);
        j.WriteNumber("snapshot_no_step_fresh_bytes", snapPlainFreshB);
        // «Snapshot without nodes» (ADR-0023 п. 2.3) = a snapshot whose node blocks are all reused; nodes = fresh − reused.
        j.WriteNumber("snapshot_without_nodes_bytes", snapPlainB);
        j.WriteNumber("snapshot_nodes_bytes", nodesB);
        j.WriteNumber("snapshot_x_mib", snapshotXMib);
        // Per snapshot: the worst probe (fresh blocks) and the direct month mean per snapshot must both stay within X.
        j.WriteBoolean("snapshot_le_x", Math.Max(snapSteppedB, snapPlainFreshB) <= snapshotXMib * MiB
            && (snapPerSnapshot.Count == 0 || snapPerSnapshot.Max() <= snapshotXMib * MiB));
        j.WriteNumber("loh_alloc_ticks_window", lohTicksWindow);
        j.WriteNumber("loh_alloc_ticks_snapshot_probes", probes.Where(p => p.Name.StartsWith("ViewBuilder.Snapshot", StringComparison.Ordinal))
            .Sum(p => GcEventListener.In(events, p.From, p.To).LohTicks));
        j.WriteNumber("gc_alloc_large_window", allocLargeWindow);
        j.WriteNumber("gc_gen2_window", gen2Window);
        j.WriteNumber("gc_gen2_window_tick_only", gen2TickWindow);
        double wallMs = ProcessBench.Ms(win1 - win0);
        j.WriteNumber("gc_pause_ms_window", pause.TotalMilliseconds);
        j.WriteNumber("gc_pause_share_window", wallMs > 0 ? pause.TotalMilliseconds / wallMs : 0);
        j.WriteEndObject();

        j.WriteString("state_hash_before_probes", hashBefore);
        j.WriteString("state_hash_after_probes", hashAfter);
        j.WriteBoolean("probes_kept_hash", hashBefore == hashAfter);
        if (m0 is not null)
            meter.Write(j, [new MachineWindow("alloc", m0, m1!)], threads);
        j.WriteEndObject();
        return hashBefore == hashAfter && runsSameHash ? 0 : 1;
    }

    private readonly record struct StepMark(string Run, int Month, long Tick, DateTime From, DateTime To, string Snapshot);

    private sealed record MonthRun(SliceSession? Session, long[] Bytes, int[,] Gc, int[] SnapPlain, int[] SnapStepped, DateTime[] From, DateTime[] To, string Hash);

    // One monthly run with the harness load (one SetBudgetShares per actor and month, as П2). atWarmupEnd runs at the start of month warmup.
    private static MonthRun RunMonths(SynthWorld w, ulong seed, int threads, int months, bool skipViews, int warmup, Action? atWarmupEnd, TextWriter log, string tag,
        List<StepMark>? steps = null)
    {
        var sw = new RunSwitches(HashCheckpoints: false, HashEveryTick: false, DailyNoop: false, FullCopy: false);
        SliceSession s = SliceSession.Create(w.Data, ProcessBench.Config(w, seed, threads, 8, daily: false, sw));
        s.SkipViews = skipViews;
        SliceWorld world = s.World;
        int actors = world.Table.RowCount;
        var bytes = new long[months];
        var gc = new int[months, 3];
        var snapPlain = new int[months];
        var snapStepped = new int[months];
        var from = new DateTime[months];
        var to = new DateTime[months];
        int local = 0;
        for (int m = 0; m < months; m++)
        {
            if (m == warmup)
                atWarmupEnd?.Invoke();
            for (int a = 0; a < actors; a++)
            {
                CommandVerdict v = s.Submit(SourceClass.Harness, 0, local++, new SetBudgetShares(a, world.SharesOf(a)));
                if (!v.Accepted)
                    throw new InvalidOperationException($"alloc profile command refused: {v.Message}");
            }
            long end = world.Calendar.TickOfMonth(m) + 1;
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            from[m] = DateTime.UtcNow;
            long b0 = GC.GetTotalAllocatedBytes(true);
            SliceView prev = s.View;
            while (s.Tick < end)
            {
                DateTime st0 = steps is null ? default : DateTime.UtcNow;
                long tick0 = s.Tick;
                s.Step(1);
                SliceView now = s.View;
                string kind = "none";
                if (!ReferenceEquals(now, prev))
                {
                    if (now.Step is null) snapPlain[m]++; else snapStepped[m]++;
                    kind = now.Step is null ? "plain" : "stepped";
                    prev = now;
                }
                steps?.Add(new StepMark(tag, m, tick0, st0, steps is null ? default : DateTime.UtcNow, kind));
            }
            bytes[m] = GC.GetTotalAllocatedBytes(true) - b0;
            to[m] = DateTime.UtcNow;
            gc[m, 0] = GC.CollectionCount(0) - g0;
            gc[m, 1] = GC.CollectionCount(1) - g1;
            gc[m, 2] = GC.CollectionCount(2) - g2;
            log.WriteLine($"{tag} month {m,3}: {bytes[m] / MiB,9:F2} MiB  gc0 {gc[m, 0]} gc1 {gc[m, 1]} gc2 {gc[m, 2]}  snapshots {snapPlain[m]}+{snapStepped[m]} stepped");
        }
        return new MonthRun(s, bytes, gc, snapPlain, snapStepped, from, to, StateHash.ComputeHex(world.Table, SliceWorld.SchemaVersion));
    }

    private static SliceView Fresh(SliceWorld world, Func<SliceView> build)
    {
        bool reuse = world.ReuseNodeBlocks;
        world.ReuseNodeBlocks = false;
        try { return build(); }
        finally { world.ReuseNodeBlocks = reuse; }
    }

    private static ProbeResult Probe(string name, int calls, Action<int> call)
    {
        call(0);   // JIT and lazy memo outside the count
        DateTime t0 = DateTime.UtcNow;
        long b0 = GC.GetTotalAllocatedBytes(true);
        for (int i = 0; i < calls; i++)
            call(i);
        long b = GC.GetTotalAllocatedBytes(true) - b0;
        return new ProbeResult(name, b, calls, t0, DateTime.UtcNow);
    }
}
