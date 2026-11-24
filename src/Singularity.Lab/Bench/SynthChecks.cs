using Singularity.Core.Data;
using Singularity.Session;

namespace Singularity.Lab.Bench;

/// <summary>
/// Checks of the synthetic benchmark world (TASK-0065), run by <c>Singularity.Lab selftest</c>. Each returns null or the problem.
/// No timing: these only prove that the overlay is what the EXP-0007 pre-registration says and that it is deterministic.
/// </summary>
public static class SynthChecks
{
    private const long Months = 12;

    public static IEnumerable<(string Name, Func<string?> Body)> All(string tmp) =>
    [
        ("synth: generator — same spec → same overlay bytes, other seed → other bytes, the 24 grid points load with exact counts", () => Generator(tmp)),
        ("synth: clones keep the source columns; Clones has no edge between copies, CrossEdges only adds edges between copies", () => CloneColumns(tmp)),
        ("synth: overlay of 5 actors × all core nodes reproduces the core run (same StateHash after 12 months)", () => RoundTrip(tmp)),
        ("synth: (24, 180, cross) 12 months — same StateHash at threads 1/4/8/16 and ChunkSize 1/8/32", () => ThreadInvariance(tmp)),
        ("synth: full scale (200, 2000) of both graphs passes the start checks of a session; (96, 500, cross) 3 months — same StateHash at t1 and t8", () => FullScale(tmp)),
    ];

    private static string Dir(string tmp, SynthWorldSpec spec, string tag) => Path.Combine(tmp, "synth", tag, spec.Name);

    private static string? Generator(string tmp)
    {
        var spec = new SynthWorldSpec(24, 500, SynthGraph.CrossEdges, 7);
        SynthWorld a = SynthWorldGenerator.Build(spec, Dir(tmp, spec, "a"));
        SynthWorld b = SynthWorldGenerator.Build(spec, Dir(tmp, spec, "b"));
        if (a.OverlaySha256 != b.OverlaySha256 || a.Data.DataHash != b.Data.DataHash)
            return "the same spec gave different overlays";
        foreach (SynthGraph g in (SynthGraph[])[SynthGraph.Clones, SynthGraph.CrossEdges])
        {
            var s8 = new SynthWorldSpec(24, 500, g, 8);
            if (SynthWorldGenerator.Build(s8, Dir(tmp, s8, "c")).OverlaySha256 == SynthWorldGenerator.Build(spec with { Graph = g }, Dir(tmp, spec with { Graph = g }, "c")).OverlaySha256)
                return $"{g}: generator seed 8 gave the overlay of seed 7";
        }
        IReadOnlyList<SynthWorldSpec> grid = SynthWorldSpec.BenchmarkGrid();
        if (grid.Count != 24 || grid.Select(p => p.Name).Distinct().Count() != 24)
            return $"grid has {grid.Count} points";
        foreach (SynthWorldSpec p in grid)
        {
            SynthWorld w = SynthWorldGenerator.Build(p, Dir(tmp, p, "grid"));
            ScenarioData sc = w.Data.Scenario(w.ScenarioId);
            if (sc.ActorCount != p.Actors || w.Data.Nodes.Count != p.Nodes)
                return $"{p.Name}: {sc.ActorCount} actors, {w.Data.Nodes.Count} nodes";
            if (sc.ActorIndex("USA") != 0 || sc.ActorIndex("GBR") != 1 || sc.ActorIndex("SUN") != 2)
                return $"{p.Name}: USA, GBR, SUN are not the first actors";
            if ((p.Graph == SynthGraph.CrossEdges) != (w.Stats.CrossInforms + w.Stats.CrossRequires > 0))
                return $"{p.Name}: cross edges {w.Stats.CrossInforms} + {w.Stats.CrossRequires}";
        }
        return null;
    }

    private static string? CloneColumns(string tmp)
    {
        foreach (SynthGraph g in (SynthGraph[])[SynthGraph.Clones, SynthGraph.CrossEdges])
        {
            var spec = new SynthWorldSpec(6, 500, g);
            SynthWorld w = SynthWorldGenerator.Build(spec, Dir(tmp, spec, "cols"));
            NodeTable n = w.Data.Nodes;
            int crossReq = 0, crossInf = 0;
            for (int r = 0; r < n.Count; r++)
            {
                int copy = n.Number[r] / SynthWorldGenerator.NumberStride;
                int src = Source(n, r);
                if (src < 0)
                    return $"{n.Id[r]}: no source node";
                if (n.Type[r] != n.Type[src] || n.Gen[r] != n.Gen[src] || n.Discipline[r] != n.Discipline[src] || n.Cluster[r] != n.Cluster[src]
                    || n.Tier[r] != n.Tier[src] || n.ReqPrecision[r] != n.ReqPrecision[src] || n.ReqPurity[r] != n.ReqPurity[src]
                    || n.ReqQualityControl[r] != n.ReqQualityControl[src] || n.LabourP[r] != n.LabourP[src] || n.Hazard[r] != n.Hazard[src]
                    || n.AnchorYear[r] != n.AnchorYear[src] || n.AnchorBefore[r] != n.AnchorBefore[src]
                    || n.AnchorSource[r] != n.AnchorSource[src] || n.AnchorEstimated[r] != n.AnchorEstimated[src])
                    return $"{n.Id[r]}: a column differs from {n.Id[src]}";
                // In-copy requires groups map one to one onto the source's; any other member is a cross edge.
                var own = new List<string>();
                for (int gr = n.Requires.GroupStart[r]; gr < n.Requires.GroupStart[r + 1]; gr++)
                {
                    int[] m = n.Requires.GroupMembers(gr).ToArray();
                    if (m.All(x => n.Number[x] / SynthWorldGenerator.NumberStride == copy))
                        own.Add(string.Join("|", m.Select(x => n.Number[Source(n, x)]).Order()));
                    else if (m.Length == 1)
                        crossReq++;
                    else
                        return $"{n.Id[r]}: a mixed any_of group";
                }
                var expect = new List<string>();
                for (int gr = n.Requires.GroupStart[src]; gr < n.Requires.GroupStart[src + 1]; gr++)
                    expect.Add(string.Join("|", n.Requires.GroupMembers(gr).ToArray().Select(x => n.Number[x]).Order()));
                if (!own.Order().SequenceEqual(expect.Order()))
                    return $"{n.Id[r]}: requires differ from {n.Id[src]}";
                for (int e = n.InformsStart[r]; e < n.InformsStart[r + 1]; e++)
                {
                    if (n.Number[n.InformsTo[e]] / SynthWorldGenerator.NumberStride != copy)
                        crossInf++;
                }
            }
            if (g == SynthGraph.Clones && crossReq + crossInf != 0)
                return $"Clones has {crossReq} + {crossInf} edges between copies";
            if (g == SynthGraph.CrossEdges && (crossReq != w.Stats.CrossRequires || crossInf != w.Stats.CrossInforms || crossReq == 0 || crossInf == 0))
                return $"CrossEdges: found {crossReq}/{crossInf}, stats {w.Stats.CrossRequires}/{w.Stats.CrossInforms}";
        }
        return null;
    }

    private static int Source(NodeTable n, int row)
    {
        int number = n.Number[row] % SynthWorldGenerator.NumberStride;
        for (int r = 0; r < n.Count; r++)
        {
            if (n.Number[r] == number)
                return r;
        }
        return -1;
    }

    private static string Run(GameData data, SliceRunConfig config, long months)
    {
        SliceSession s = SliceSession.Create(data, config);
        s.StepThroughMonth(months - 1);
        return s.LastHash;
    }

    private static string? RoundTrip(string tmp)
    {
        // TASK-0133: the node count is that of mods/core (61 before the G5/G6 data, 81 after): the overlay holds copy 0 only.
        var spec = new SynthWorldSpec(5, ModLoader.Load(SynthWorldGenerator.FindCoreMod()).Nodes.Count, SynthGraph.Clones);
        // The overlay overrides the core scenario with the emitted copy: same id, so the scenario hash (a state column) must come out equal.
        SynthWorld w = SynthWorldGenerator.Build(spec, Dir(tmp, spec, "rt"), null, overrideCoreScenario: true);
        GameData core = ModLoader.Load(SynthWorldGenerator.FindCoreMod());
        if (w.Data.Scenario(w.ScenarioId).Hash != core.Scenario(w.ScenarioId).Hash)
            return "the emitted scenario has another scenario_hash than the core one";
        string h0 = Run(core, new SliceRunConfig { Seed = 1 }, Months);
        string h1 = Run(w.Data, new SliceRunConfig { Seed = 1, ScenarioId = w.ScenarioId }, Months);
        return h0 == h1 ? null : $"core {h0[..12]} vs overlay {h1[..12]}";
    }

    private static string? ThreadInvariance(string tmp)
    {
        var spec = new SynthWorldSpec(24, 180, SynthGraph.CrossEdges);
        SynthWorld w = SynthWorldGenerator.Build(spec, Dir(tmp, spec, "thr"));
        var cfg = new SliceRunConfig { Seed = 3, ScenarioId = w.ScenarioId };
        string h = Run(w.Data, cfg with { Threads = 1, ChunkSize = 8 }, Months);
        foreach ((int t, int c) in (ValueTuple<int, int>[])[(4, 8), (8, 8), (16, 8), (8, 1), (8, 32)])
        {
            string x = Run(w.Data, cfg with { Threads = t, ChunkSize = c }, Months);
            if (x != h)
                return $"threads {t}, ChunkSize {c}: {x[..12]} vs {h[..12]}";
        }
        string other = Run(w.Data, cfg with { Seed = 4 }, Months);
        return other == h ? "simulation seed 4 gave the hash of seed 3" : null;
    }

    // A month of (200, 2000) costs tens of seconds before any optimisation (TASK-0065 smoke), so the full scale is only built here (start
    // validators S-2, L-C1…L-C3a run in SliceSession.Create); stepping is checked on a mid-size point.
    private static string? FullScale(string tmp)
    {
        foreach (SynthGraph g in (SynthGraph[])[SynthGraph.Clones, SynthGraph.CrossEdges])
        {
            var spec = new SynthWorldSpec(200, 2000, g);
            SynthWorld w = SynthWorldGenerator.Build(spec, Dir(tmp, spec, "full"));
            SliceSession s = SliceSession.Create(w.Data, new SliceRunConfig { Seed = 1, ScenarioId = w.ScenarioId, Threads = 8, ChunkSize = 8 });
            if (s.Layout is null)
                return $"{spec.Name}: no layout";
        }
        var mid = new SynthWorldSpec(96, 500, SynthGraph.CrossEdges);
        SynthWorld m = SynthWorldGenerator.Build(mid, Dir(tmp, mid, "mid"));
        var cfg = new SliceRunConfig { Seed = 1, ScenarioId = m.ScenarioId, ChunkSize = 8 };
        string h1 = Run(m.Data, cfg with { Threads = 1 }, 3);
        string h8 = Run(m.Data, cfg with { Threads = 8 }, 3);
        return h1 == h8 ? null : $"t1 {h1[..12]} vs t8 {h8[..12]}";
    }
}
