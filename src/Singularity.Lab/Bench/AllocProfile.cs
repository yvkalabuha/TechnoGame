using System.Text.Json;
using Singularity.Core.Commands;
using Singularity.Core.Industry;
using Singularity.Kernel;
using Singularity.Session;

namespace Singularity.Lab.Bench;

/// <summary>
/// <c>lab bench --mode alloc</c> (TASK-0103): allocated bytes and GC counts per month of a monthly run, and the bytes of single pure reads
/// that the trace names as the main sources (one allocation of effort, one tooling plan, the inputs view, the snapshot view).
/// Counts bytes, not time, so it is valid on a busy machine. The probes run after the last month and only read the snapshot;
/// the run prints the state hash before and after them.
/// </summary>
internal static class AllocProfile
{
    public static int Run(SynthWorld w, ulong seed, int threads, int months, string outFile, TextWriter log)
    {
        var sw = new RunSwitches(HashCheckpoints: false, HashEveryTick: false, DailyNoop: false, FullCopy: false);
        SliceSession s = SliceSession.Create(w.Data, ProcessBench.Config(w, seed, threads, 8, daily: false, sw));
        SliceWorld world = s.World;
        IndustryLayout l = world.IndustryLayout;
        int actors = world.Table.RowCount;
        var bytes = new long[months];
        var gc = new int[months, 3];
        int local = 0;
        for (int m = 0; m < months; m++)
        {
            for (int a = 0; a < actors; a++)
            {
                CommandVerdict v = s.Submit(SourceClass.Harness, 0, local++, new SetBudgetShares(a, world.SharesOf(a)));
                if (!v.Accepted)
                    throw new InvalidOperationException($"alloc profile command refused: {v.Message}");
            }
            long end = world.Calendar.TickOfMonth(m) + 1;
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            long b0 = GC.GetTotalAllocatedBytes(true);
            while (s.Tick < end)
                s.Step(1);
            bytes[m] = GC.GetTotalAllocatedBytes(true) - b0;
            gc[m, 0] = GC.CollectionCount(0) - g0;
            gc[m, 1] = GC.CollectionCount(1) - g1;
            gc[m, 2] = GC.CollectionCount(2) - g2;
            log.WriteLine($"month {m,3}: {bytes[m] / 1048576.0,9:F1} MB  gc0 {gc[m, 0]} gc1 {gc[m, 1]} gc2 {gc[m, 2]}");
        }
        string hashBefore = s.LastHash;
        long month = months;   // the next step's month: the reads the step's compute and views would make
        var probes = new List<(string Name, long Bytes, int Calls)>
        {
            Probe("Industry.RestActor (1 EffortAllocator.Allocate)", actors, a => world.Industry.RestActor(a, month)),
            Probe("IndustryInputs.Plan (ToolingPlan.Compute only)", actors, a => world.IndustryInputs.Plan(a, month, Fx6.Zero)),
            Probe("Industry.Profile (1 item, view Process)", actors, a => world.Industry.Profile(l.SitesOf(a)[0], [new PrcSetItem(l.Number[0], Fx6.One, Fx6.One)], month)),
            Probe("ViewBuilder.Inputs (t1)", 1, _ => ViewBuilder.Inputs(world, month, 1)),
            Probe("ViewBuilder.Snapshot (no inputs/step)", 1, _ => ViewBuilder.Snapshot(world, s.Tick, null, month - 1, null, null)),
            Probe("ViewBuilder.Step + Snapshot (stepped)", 1, _ => ViewBuilder.Snapshot(world, s.Tick, null, month - 1, null, ViewBuilder.Step(world, month - 1))),
        };
        string hashAfter = s.LastHash;
        foreach (var p in probes)
            log.WriteLine($"probe {p.Name}: {p.Bytes / (double)p.Calls / 1024.0:F1} KB/call × {p.Calls} = {p.Bytes / 1048576.0:F1} MB");
        log.WriteLine($"layout: actors {actors}, sites {l.SiteCount}, processes {l.ProcessCount}, clusters {l.Clusters.Length}, nodes {world.Graph.Count}");
        log.WriteLine($"hash before probes {hashBefore[..16]}, after {hashAfter[..16]} ({(hashBefore == hashAfter ? "equal" : "DIFFERENT")})");

        using var json = BenchJson.Open(outFile);
        Utf8JsonWriter j = json.Writer;
        j.WriteStartObject();
        j.WriteString("kind", "lab-bench/alloc");
        j.WriteString("world", w.Spec.Name);
        j.WriteNumber("seed", seed);
        j.WriteNumber("threads", threads);
        j.WriteStartObject("layout");
        j.WriteNumber("actors", actors);
        j.WriteNumber("sites", l.SiteCount);
        j.WriteNumber("processes", l.ProcessCount);
        j.WriteNumber("clusters", l.Clusters.Length);
        j.WriteNumber("nodes", world.Graph.Count);
        j.WriteEndObject();
        j.WriteStartArray("months");
        for (int m = 0; m < months; m++)
        {
            j.WriteStartObject();
            j.WriteNumber("month", m);
            j.WriteNumber("bytes", bytes[m]);
            j.WriteNumber("gc0", gc[m, 0]);
            j.WriteNumber("gc1", gc[m, 1]);
            j.WriteNumber("gc2", gc[m, 2]);
            j.WriteEndObject();
        }
        j.WriteEndArray();
        j.WriteStartArray("probes");
        foreach (var p in probes)
        {
            j.WriteStartObject();
            j.WriteString("name", p.Name);
            j.WriteNumber("calls", p.Calls);
            j.WriteNumber("bytes", p.Bytes);
            j.WriteEndObject();
        }
        j.WriteEndArray();
        j.WriteString("final_state_hash", hashBefore);
        j.WriteBoolean("probes_kept_hash", hashBefore == hashAfter);
        j.WriteEndObject();
        return hashBefore == hashAfter ? 0 : 1;
    }

    private static (string, long, int) Probe(string name, int calls, Action<int> call)
    {
        call(0);   // JIT and lazy memo outside the count
        long b0 = GC.GetTotalAllocatedBytes(true);
        for (int i = 0; i < calls; i++)
            call(i);
        return (name, GC.GetTotalAllocatedBytes(true) - b0, calls);
    }
}
