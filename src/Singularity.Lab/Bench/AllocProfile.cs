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
/// (ADR-0023 п. 2.1); the second is the full run. Snapshot path of a month = (full − GC listener) − (tick − GC listener).
/// [format 5, DEBT-0060] With one thread both runs count only the run thread's bytes (<see cref="GC.GetAllocatedBytesForCurrentThread"/>):
/// the process counter also takes runtime background threads (one-time ~240 KB seen in a month of one run under load, larger than the
/// snapshot path of a small world), so the per-month difference of two process counts is not a measurement. With more threads the
/// process counter stays (workers are pool threads) and <c>noise_floor_bytes</c> is null. Both runs must end
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
        // Always on (decision #416: GC pauses with kind); Verbose (allocation ticks) only for the LOH count or the trace.
        using var gcl = new GcEventListener(raw: gcTrace, verbose: lohEvents || gcTrace);
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
        // DEBT-0060: with one thread the whole step (TickRunner and ViewBuilder loops inline at Threads = 1) and Submit run on this
        // thread, so its own counter is the simulation's bytes; the process counter adds runtime background threads (seen: ~240 KB once
        // in one run under load). With more threads the workers are pool threads and only the process counter covers them.
        bool simThread = threads == 1;
        int[,] gc = full.Gc;
        int[] snapPlain = full.SnapPlain, snapStepped = full.SnapStepped;
        DateTime[] from = full.From, to = full.To;

        string hashBefore = StateHash.ComputeHex(world.Table, SliceWorld.SchemaVersion);
        long month = months;   // the next step's month: the reads the step's compute and views would make
        MonthStepView step = ViewBuilder.Step(world, month - 1);
        Func<long> counter = simThread ? GC.GetAllocatedBytesForCurrentThread : () => GC.GetTotalAllocatedBytes(true);
        ProbeResult Probe(string name, int calls, Action<int> call) => ProbeWith(counter, name, calls, call);
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
        (bool flushed, double? clockMs, double flushSpanMs) = gcl.Flush(TimeSpan.FromSeconds(5));
        IReadOnlyList<GcEventListener.GcEvent> events = gcl.Snapshot();
        GcPauses.Timeline timeline = GcPauses.Build(events);
        if (gcTrace)
        {
            File.WriteAllLines(outFile + ".gc.jsonl", gcl.Raw());
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
        // Format 4 (decision #416): settle collection after each SliceSession.Create, GC pauses from events. Format 5 (DEBT-0060): with one
        // thread the split counts the run thread's bytes only (byte_counter, noise_floor_bytes, *_run_thread_bytes, *_other_threads_bytes).
        j.WriteNumber("format", 5);
        j.WriteString("run_order", fullFirst ? "full-first" : "tick-first");
        j.WriteBoolean("gc_trace", gcTrace);
        BenchEnv.Write(j, rp.RepoDir, c => ProcessBench.WriteSettleCondition(c,
            $"GcEventListener, {(lohEvents || gcTrace ? "Verbose (allocation ticks)" : "Informational")}: GCStart/GCEnd/GCSuspendEEBegin/GCRestartEEEnd"));
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
        j.WriteBoolean("gc_events", true);
        j.WriteBoolean("gc_events_flushed", flushed);
        LoadMeter.WriteNum(j, "gc_events_clock_offset_ms", clockMs);
        j.WriteNumber("gc_events_flush_host_span_ms", flushSpanMs);
        j.WriteNumber("gc_events_unpaired_suspensions", timeline.Unpaired);
        var tickPath = new List<double>();
        var snapPath = new List<double>();
        var snapPerSnapshot = new List<double>();
        var tickModel = new List<double>();
        long noiseFloor = 0;
        int lohTicksWindow = 0, allocLargeWindow = 0, gen2Window = 0, gen2TickWindow = 0;
        j.WriteStartArray("months");
        for (int m = 0; m < months; m++)
        {
            var (lohTicks, lohBytes, allocLarge, gen2Gcs) = GcEventListener.In(events, from[m], to[m]);
            GcPauses.Summary pm = GcPauses.Summarize(timeline, [(from[m], to[m])], (to[m] - from[m]).TotalMilliseconds);
            GcPauses.Summary pmTick = GcPauses.Summarize(timeline, [(tickRun.From[m], tickRun.To[m])], (tickRun.To[m] - tickRun.From[m]).TotalMilliseconds);
            long own = GcEventListener.OwnBytesAt(events, to[m]) - GcEventListener.OwnBytesAt(events, from[m]);
            long ownTick = GcEventListener.OwnBytesAt(events, tickRun.To[m]) - GcEventListener.OwnBytesAt(events, tickRun.From[m]);
            // Bytes of other threads in the month window (process − run thread): the GC listener, tiered JIT, pool, timers. Only with
            // one simulation thread is this not simulation work.
            long other = bytes[m] - full.ThreadBytes[m], otherTick = tickRun.Bytes[m] - tickRun.ThreadBytes[m];
            long fullSim = simThread ? full.ThreadBytes[m] : bytes[m] - own;
            long tick = simThread ? tickRun.ThreadBytes[m] : tickRun.Bytes[m] - ownTick;
            long snap = fullSim - tick;
            int snapshots = snapPlain[m] + snapStepped[m];
            double snapModel = snapPlain[m] * snapPlainB + snapStepped[m] * snapSteppedB;
            double tickM = fullSim - snapModel;
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
            j.WriteNumber("sim_bytes", fullSim);
            j.WriteNumber("run_thread_bytes", full.ThreadBytes[m]);
            j.WriteNumber("other_threads_bytes", other);
            j.WriteNumber("tick_only_bytes", tickRun.Bytes[m]);
            j.WriteNumber("tick_only_gc0", tickRun.Gc[m, 0]);
            j.WriteNumber("tick_only_gc1", tickRun.Gc[m, 1]);
            j.WriteNumber("tick_only_gc2", tickRun.Gc[m, 2]);
            j.WriteNumber("tick_only_gc_listener_bytes", ownTick);
            j.WriteNumber("tick_only_run_thread_bytes", tickRun.ThreadBytes[m]);
            j.WriteNumber("tick_only_other_threads_bytes", otherTick);
            j.WriteNumber("tick_path_bytes", tick);
            j.WriteNumber("snapshot_path_bytes", snap);
            j.WriteNumber("snapshot_path_bytes_per_snapshot", snapshots > 0 ? snap / (double)snapshots : 0);
            j.WriteNumber("snapshot_path_bytes_model", Math.Round(snapModel));
            j.WriteNumber("tick_path_bytes_model", Math.Round(tickM));
            j.WriteNumber("loh_alloc_ticks", lohTicks);
            j.WriteNumber("loh_bytes_sampled", lohBytes);
            j.WriteNumber("gc_alloc_large", allocLarge);
            LoadMeter.WriteNum(j, "gc_pause_max_ms", pm.MaxMs);
            j.WriteNumber("gc_gen2_blocking", pm.BlockingGen2);
            LoadMeter.WriteNum(j, "tick_only_gc_pause_max_ms", pmTick.MaxMs);
            j.WriteNumber("tick_only_gc_gen2_blocking", pmTick.BlockingGen2);
            j.WriteEndObject();
            if (m >= rp.Warmup)
            {
                tickPath.Add(tick);
                snapPath.Add(snap);
                if (snapshots > 0)
                    snapPerSnapshot.Add(snap / (double)snapshots);
                tickModel.Add(tickM);
                noiseFloor = Math.Max(noiseFloor, Math.Max(other, otherTick));
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
        j.WriteString("method", simThread
            ? "direct: tick path = run-thread bytes of a SkipViews run (same seed, threads, commands); snapshot path = run-thread bytes of the full run − tick path (lab-cli.md «alloc»)"
            : "direct: tick path = process bytes of a SkipViews run (same seed, threads, commands) − its GC listener bytes; snapshot path = (full − GC listener) − tick path (lab-cli.md «alloc»)");
        // sim_thread (threads = 1): other threads excluded. process (threads > 1): the workers are pool threads, so each month's path also
        // carries other threads' bytes and the noise floor is not measurable (null) — DEBT-0060.
        j.WriteString("byte_counter", simThread ? "sim_thread" : "process");
        // Largest bytes of other threads in one measured month window of either run: what the process counter would have added to a
        // month's path (the sim_thread split excludes it).
        if (simThread)
            j.WriteNumber("noise_floor_bytes", noiseFloor);
        else
            j.WriteNull("noise_floor_bytes");
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
        // Decision #416: every GC pause of the measured months (from the first measured month's start to the last one's end, the
        // inter-month command submission included) with kind; blocking gen2 = FullBlocking GCs there.
        if (months > rp.Warmup)
        {
            (DateTime From, DateTime To) wFull = (from[rp.Warmup], to[months - 1]), wTick = (tickRun.From[rp.Warmup], tickRun.To[months - 1]);
            GcPauses.Summary sFull = GcPauses.Summarize(timeline, [wFull], (wFull.To - wFull.From).TotalMilliseconds);
            GcPauses.Summary sTick = GcPauses.Summarize(timeline, [wTick], (wTick.To - wTick.From).TotalMilliseconds);
            j.WriteNumber("gc_gen2_blocking_window", sFull.BlockingGen2);
            j.WriteNumber("gc_gen2_blocking_window_tick_only", sTick.BlockingGen2);
            LoadMeter.WriteNum(j, "gc_pause_max_ms_window", sFull.MaxMs);
            LoadMeter.WriteNum(j, "gc_pause_p99_ms_window", sFull.P99Ms);
            GcPauses.Write(j, "gc_pauses_window", sFull, wFull.From);
            GcPauses.Write(j, "gc_pauses_window_tick_only", sTick, wTick.From);
        }
        // By GC index, not by time (TASK-0116): event and host clocks differ by milliseconds off Linux.
        j.WriteNumber("gc_settle_pause_ms", GcPauses.SummarizeGcs(timeline, [full.SettleGcs, tickRun.SettleGcs], 0).TotalMs);
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

    private sealed record MonthRun(SliceSession? Session, long[] Bytes, long[] ThreadBytes, int[,] Gc, int[] SnapPlain, int[] SnapStepped, DateTime[] From, DateTime[] To, string Hash,
        (DateTime From, DateTime To) Settle, (int After, int Upto) SettleGcs);

    // One monthly run with the harness load (one SetBudgetShares per actor and month, as П2). atWarmupEnd runs at the start of month warmup.
    private static MonthRun RunMonths(SynthWorld w, ulong seed, int threads, int months, bool skipViews, int warmup, Action? atWarmupEnd, TextWriter log, string tag,
        List<StepMark>? steps = null)
    {
        var sw = new RunSwitches(HashCheckpoints: false, HashEveryTick: false, DailyNoop: false, FullCopy: false);
        SliceSession s = SliceSession.Create(w.Data, ProcessBench.Config(w, seed, threads, 8, daily: false, sw));
        s.SkipViews = skipViews;
        // Decision #416: the forced settle collection after the session is built, before month 0 (outside every month window).
        DateTime settle0 = DateTime.UtcNow;
        int settleGc0 = GC.CollectionCount(0);
        ProcessBench.SettleGc();
        (int, int) settleGcs = (settleGc0, GC.CollectionCount(0));
        (DateTime, DateTime) settle = (settle0, DateTime.UtcNow);
        SliceWorld world = s.World;
        int actors = world.Table.RowCount;
        var bytes = new long[months];
        var threadBytes = new long[months];
        var gc = new int[months, 3];
        var snapPlain = new int[months];
        var snapStepped = new int[months];
        var from = new DateTime[months];
        var to = new DateTime[months];
        int local = 0;
        bool stepsFull = false;
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
            long t0 = GC.GetAllocatedBytesForCurrentThread();
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
                // Past the preallocated capacity the list would grow inside the window: stop recording instead (warned once).
                if (steps is not null && steps.Count < steps.Capacity)
                    steps.Add(new StepMark(tag, m, tick0, st0, DateTime.UtcNow, kind));
                else if (steps is not null && !stepsFull)
                {
                    stepsFull = true;
                    log.WriteLine($"warning: --gc-trace step marks full ({steps.Capacity}); later steps of run '{tag}' are not recorded");
                }
            }
            bytes[m] = GC.GetTotalAllocatedBytes(true) - b0;
            threadBytes[m] = GC.GetAllocatedBytesForCurrentThread() - t0;
            to[m] = DateTime.UtcNow;
            gc[m, 0] = GC.CollectionCount(0) - g0;
            gc[m, 1] = GC.CollectionCount(1) - g1;
            gc[m, 2] = GC.CollectionCount(2) - g2;
            log.WriteLine($"{tag} month {m,3}: {bytes[m] / MiB,9:F2} MiB (run thread {threadBytes[m] / MiB:F2})  gc0 {gc[m, 0]} gc1 {gc[m, 1]} gc2 {gc[m, 2]}  snapshots {snapPlain[m]}+{snapStepped[m]} stepped");
        }
        return new MonthRun(s, bytes, threadBytes, gc, snapPlain, snapStepped, from, to, StateHash.ComputeHex(world.Table, SliceWorld.SchemaVersion), settle, settleGcs);
    }

    private static SliceView Fresh(SliceWorld world, Func<SliceView> build)
    {
        bool reuse = world.ReuseNodeBlocks;
        world.ReuseNodeBlocks = false;
        try { return build(); }
        finally { world.ReuseNodeBlocks = reuse; }
    }

    private static ProbeResult ProbeWith(Func<long> counter, string name, int calls, Action<int> call)
    {
        call(0);   // JIT and lazy memo outside the count
        DateTime t0 = DateTime.UtcNow;
        long b0 = counter();
        for (int i = 0; i < calls; i++)
            call(i);
        long b = counter() - b0;
        return new ProbeResult(name, b, calls, t0, DateTime.UtcNow);
    }
}
