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
/// Split [model, not a direct count]: the session builds the snapshot inside <c>Step</c>, so a month's bytes cannot be cut there without
/// changing Session. The snapshot path of a month = (snapshots without a step) × the probe «Snapshot (no step)» + (snapshots with a step) ×
/// the probe «Snapshot (stepped, step given)»; the tick path = the month − the snapshot path − the GC listener's own bytes. The probes are
/// taken on the state after the last month, so a month whose snapshot differs (the yearly <c>MaxFleetValue</c>) is off by that difference.
/// </para>
/// <para>
/// The probes run after the last month and only read the world; the state hash is recomputed with <see cref="StateHash.ComputeHex"/> before
/// and after them (not the lazy <c>LastHash</c>).
/// </para>
/// </summary>
internal static class AllocProfile
{
    private const double MiB = 1 << 20;

    private sealed record ProbeResult(string Name, long Bytes, int Calls, DateTime From, DateTime To)
    {
        public double PerCall => Bytes / (double)Calls;
    }

    public static int Run(SynthWorld w, ulong seed, int threads, int months, bool lohEvents, double snapshotXMib, BenchRunParams rp, double buildMs, string outFile, TextWriter log)
    {
        using GcEventListener? gcl = lohEvents ? new GcEventListener() : null;
        using var meter = new LoadMeter();
        var sw = new RunSwitches(HashCheckpoints: false, HashEveryTick: false, DailyNoop: false, FullCopy: false);
        SliceSession s = SliceSession.Create(w.Data, ProcessBench.Config(w, seed, threads, 8, daily: false, sw));
        SliceWorld world = s.World;
        IndustryLayout l = world.IndustryLayout;
        int actors = world.Table.RowCount;
        var bytes = new long[months];
        var gc = new int[months, 3];
        var snapPlain = new int[months];
        var snapStepped = new int[months];
        var from = new DateTime[months];
        var to = new DateTime[months];
        TimeSpan pause0 = TimeSpan.Zero;
        long win0 = 0, win1;
        MachineSnapshot? m0 = null;
        int local = 0;
        for (int m = 0; m < months; m++)
        {
            if (m == rp.Warmup)
            {
                m0 = meter.Snapshot();
                pause0 = GC.GetTotalPauseDuration();
                win0 = System.Diagnostics.Stopwatch.GetTimestamp();
            }
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
                s.Step(1);
                SliceView now = s.View;
                if (!ReferenceEquals(now, prev))
                {
                    if (now.Step is null) snapPlain[m]++; else snapStepped[m]++;
                    prev = now;
                }
            }
            bytes[m] = GC.GetTotalAllocatedBytes(true) - b0;
            to[m] = DateTime.UtcNow;
            gc[m, 0] = GC.CollectionCount(0) - g0;
            gc[m, 1] = GC.CollectionCount(1) - g1;
            gc[m, 2] = GC.CollectionCount(2) - g2;
            log.WriteLine($"month {m,3}: {bytes[m] / MiB,9:F1} MiB  gc0 {gc[m, 0]} gc1 {gc[m, 1]} gc2 {gc[m, 2]}  snapshots {snapPlain[m]}+{snapStepped[m]} stepped");
        }
        win1 = System.Diagnostics.Stopwatch.GetTimestamp();
        TimeSpan pause = GC.GetTotalPauseDuration() - pause0;
        MachineSnapshot m1 = meter.Snapshot();

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

        // In the run the monthly (stepped) snapshot builds fresh blocks and the command-tick snapshot after it reuses them (TASK-0103 R2).
        double snapSteppedB = probes[5].PerCall, snapPlainB = probes[6].PerCall, snapPlainFreshB = probes[7].PerCall;
        double nodesB = snapPlainFreshB - snapPlainB;
        foreach (ProbeResult p in probes)
            log.WriteLine($"probe {p.Name}: {p.PerCall / 1024.0:F1} KB/call × {p.Calls} = {p.Bytes / MiB:F2} MiB");
        log.WriteLine($"layout: actors {actors}, sites {l.SiteCount}, processes {l.ProcessCount}, clusters {l.Clusters.Length}, nodes {world.Graph.Count}");
        log.WriteLine($"hash before probes {hashBefore[..16]}, after {hashAfter[..16]} ({(hashBefore == hashAfter ? "equal" : "DIFFERENT")})");

        using var json = BenchJson.Open(outFile);
        Utf8JsonWriter j = json.Writer;
        j.WriteStartObject();
        j.WriteString("kind", "lab-bench/alloc");
        j.WriteNumber("format", 2);
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
        int lohTicksWindow = 0, allocLargeWindow = 0, gen2Window = 0;
        j.WriteStartArray("months");
        for (int m = 0; m < months; m++)
        {
            var (lohTicks, lohBytes, allocLarge, gen2Gcs) = GcEventListener.In(events, from[m], to[m]);
            long own = GcEventListener.OwnBytesAt(events, to[m]) - GcEventListener.OwnBytesAt(events, from[m]);
            double snap = snapPlain[m] * snapPlainB + snapStepped[m] * snapSteppedB;
            double tick = bytes[m] - snap - own;
            j.WriteStartObject();
            j.WriteNumber("month", m);
            j.WriteBoolean("measured", m >= rp.Warmup);
            j.WriteNumber("bytes", bytes[m]);
            j.WriteNumber("gc0", gc[m, 0]);
            j.WriteNumber("gc1", gc[m, 1]);
            j.WriteNumber("gc2", gc[m, 2]);
            j.WriteNumber("snapshots_no_step", snapPlain[m]);
            j.WriteNumber("snapshots_stepped", snapStepped[m]);
            j.WriteNumber("snapshot_path_bytes_est", Math.Round(snap));
            j.WriteNumber("gc_listener_bytes", own);
            j.WriteNumber("tick_path_bytes_est", Math.Round(tick));
            j.WriteNumber("loh_alloc_ticks", lohTicks);
            j.WriteNumber("loh_bytes_sampled", lohBytes);
            j.WriteNumber("gc_alloc_large", allocLarge);
            j.WriteEndObject();
            if (m >= rp.Warmup)
            {
                tickPath.Add(tick);
                snapPath.Add(snap);
                lohTicksWindow += lohTicks;
                allocLargeWindow += allocLarge;
                gen2Window += gc[m, 2];
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
        j.WriteString("method", "tick path = month − snapshots × probe bytes − GC listener bytes (model; see lab-cli.md «alloc»)");
        if (tickPath.Count > 0)
        {
            j.WriteNumber("measured_months", tickPath.Count);
            j.WriteNumber("tick_path_mib_per_month_p50", BenchStats.Median(tickPath) / MiB);
            j.WriteNumber("tick_path_mib_per_month_max", tickPath.Max() / MiB);
            j.WriteBoolean("tick_path_le_1mib_per_month", BenchStats.Median(tickPath) <= MiB);
            j.WriteNumber("snapshot_path_mib_per_month_p50", BenchStats.Median(snapPath) / MiB);
        }
        j.WriteNumber("snapshot_stepped_fresh_bytes", snapSteppedB);
        j.WriteNumber("snapshot_no_step_reused_bytes", snapPlainB);
        j.WriteNumber("snapshot_no_step_fresh_bytes", snapPlainFreshB);
        // «Snapshot without nodes» (ADR-0023 п. 2.3) = a snapshot whose node blocks are all reused; nodes = fresh − reused.
        j.WriteNumber("snapshot_without_nodes_bytes", snapPlainB);
        j.WriteNumber("snapshot_nodes_bytes", nodesB);
        j.WriteNumber("snapshot_x_mib", snapshotXMib);
        j.WriteBoolean("snapshot_le_x", Math.Max(snapSteppedB, snapPlainFreshB) <= snapshotXMib * MiB);
        j.WriteNumber("loh_alloc_ticks_window", lohTicksWindow);
        j.WriteNumber("loh_alloc_ticks_snapshot_probes", probes.Where(p => p.Name.StartsWith("ViewBuilder.Snapshot", StringComparison.Ordinal))
            .Sum(p => GcEventListener.In(events, p.From, p.To).LohTicks));
        j.WriteNumber("gc_alloc_large_window", allocLargeWindow);
        j.WriteNumber("gc_gen2_window", gen2Window);
        double wallMs = ProcessBench.Ms(win1 - win0);
        j.WriteNumber("gc_pause_ms_window", pause.TotalMilliseconds);
        j.WriteNumber("gc_pause_share_window", wallMs > 0 ? pause.TotalMilliseconds / wallMs : 0);
        j.WriteEndObject();

        j.WriteString("state_hash_before_probes", hashBefore);
        j.WriteString("state_hash_after_probes", hashAfter);
        j.WriteBoolean("probes_kept_hash", hashBefore == hashAfter);
        if (m0 is not null)
            meter.Write(j, [new MachineWindow("alloc", m0, m1)], threads);
        j.WriteEndObject();
        return hashBefore == hashAfter ? 0 : 1;
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
