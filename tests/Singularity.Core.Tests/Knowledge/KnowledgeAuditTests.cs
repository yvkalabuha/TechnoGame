using System.Text.Json;
using Singularity.Core.Data;
using Singularity.Core.Knowledge;
using Singularity.Core.Tests.Data;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Knowledge;

/// <summary>
/// Test-engineer audit of TASK-0014 (reference README D5, D6, D7 on the full 61-node slice data).
/// </summary>
public class KnowledgeAuditTests
{
    private static readonly GameData Data = ModLoader.Load(TestMods.CoreDir);
    private static readonly ScenarioData Scenario = Data.Scenario("jet_slice_1945");

    // ---- D5: hazard logic with injected u (independent of the RNG port)

    [Fact]
    public void D5_InjectedU_ReproducesEveryHazardDecisionOfTheTrajectory()
    {
        KnowledgeParams p = JetReference.Params();
        int active = 0, opened = 0;
        for (long t = 0; t < JetReference.RowCount; t++)
        {
            foreach (JsonElement k in JetReference.Row(t).GetProperty("knowledge").EnumerateArray())
            {
                if (!k.TryGetProperty("hazard", out JsonElement hz))
                    continue;
                HazardStep h = KnowledgeMath.Hazard(JetReference.Raw(k.GetProperty("prog_new")), p.ThetaStart, p.HMax, p.YHalf);
                Assert.Equal(hz.GetProperty("h").GetInt64(), h.H.Raw);
                JsonElement u = hz.GetProperty("u");
                Assert.Equal(u.ValueKind != JsonValueKind.Null, h.Active);
                if (!h.Active)
                {
                    Assert.False(hz.GetProperty("opened").GetBoolean());
                    continue;
                }
                active++;
                ulong injected = u.GetUInt64();
                ulong threshold = hz.GetProperty("threshold").GetUInt64();
                bool open = CounterRng.Chance(injected, h.H);
                Assert.Equal(hz.GetProperty("opened").GetBoolean(), open);
                Assert.Equal((injected >> 11) < threshold, open);
                // Values on both sides of the threshold flip the decision exactly there.
                Assert.True(CounterRng.Chance((threshold - 1) << 11, h.H));
                Assert.False(CounterRng.Chance(threshold << 11, h.H));
                Assert.True(CounterRng.Chance(0, h.H));
                Assert.False(CounterRng.Chance(ulong.MaxValue, h.H));
                opened += open ? 1 : 0;
            }
        }
        Assert.True(active >= 3 && opened >= 1, $"coverage: {active} active, {opened} opened");
    }

    [Fact]
    public void D5_SystemDraws_AreTheKeyedUOfTheReference_AndDecideByChance()
    {
        // System-level half of D5: the u the system used equals the reference key's u and its decision is Chance(u, h).
        var run = new JetReference.Run();
        for (long t = 0; t < JetReference.RowCount; t++)
        {
            run.RunThroughMonth(t);
            for (int r = 0; r < run.Graph.Count; r++)
            {
                KnowledgeNodeStep s = run.System.LastStep(0, r);
                if (!s.HazardActive)
                    continue;
                var rng = new CounterRng(JetReference.Trajectory.GetProperty("seed").GetUInt64());
                Assert.Equal(KnowledgeRng.Hazard(rng, t, 0, run.Graph.Number[r]), s.U);
                Assert.Equal(CounterRng.Chance(s.U, s.H), s.Opened);
            }
        }
    }

    // ---- D6 / D7 on the slice data (61 nodes, 5 actors)

    [Fact]
    public void D6_SliceData_PermutedNodeAndActorStorage_SameResultsByStableIds()
    {
        var a = new SliceRun(nodePerm: null, actorPerm: null, threads: 1, chunkSize: 1);
        var b = new SliceRun(nodePerm: Shuffle(61, 7), actorPerm: [3, 0, 4, 2, 1], threads: 4, chunkSize: 2);
        var c = new SliceRun(nodePerm: Shuffle(61, 8), actorPerm: [4, 3, 2, 1, 0], threads: 3, chunkSize: 1);
        int discoveries = 0;
        for (long t = 0; t < 120; t++)
        {
            a.RunThroughMonth(t);
            b.RunThroughMonth(t);
            c.RunThroughMonth(t);
            Assert.Equal(a.System.LastDiscoveries, b.System.LastDiscoveries);
            Assert.Equal(a.System.LastDiscoveries, c.System.LastDiscoveries);
            discoveries += a.System.LastDiscoveries.Count;
            AssertSameByIds(a, b, t);
            AssertSameByIds(a, c, t);
        }
        Assert.True(discoveries > 10, $"only {discoveries} discoveries: the run does not exercise the frontier");
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(8, 1)]
    [InlineData(16, 1)]
    [InlineData(4, 5)]
    public void D7_SliceData_L2_EveryMonthlyHash_EqualsSingleThread(int threads, int chunkSize)
    {
        var single = new SliceRun(null, null, 1, 1);
        var multi = new SliceRun(null, null, threads, chunkSize);
        for (long t = 0; t < 120; t++)
        {
            single.RunThroughMonth(t);
            multi.RunThroughMonth(t);
            Assert.Equal(StateHash.ComputeHex(single.Table, 1, single.Runner.Tick), StateHash.ComputeHex(multi.Table, 1, multi.Runner.Tick));
        }
    }

    [Fact]
    public void D7_SliceData_L1_TwoRunsSameSeed_SameHash_DifferentSeed_Differs()
    {
        string Run(ulong seed)
        {
            var r = new SliceRun(null, null, 4, 1, seed);
            r.RunThroughMonth(119);
            return StateHash.ComputeHex(r.Table, 1, r.Runner.Tick);
        }
        Assert.Equal(Run(42), Run(42));
        var x = new SliceRun(null, null, 1, 1, 42);
        var y = new SliceRun(null, null, 1, 1, 43);
        x.RunThroughMonth(119);
        y.RunThroughMonth(119);
        Assert.NotEqual(StateHash.ComputeHex(x.Table, 1, 0), StateHash.ComputeHex(y.Table, 1, 0)); // the seed matters (hazard draws)
    }

    private static void AssertSameByIds(SliceRun a, SliceRun b, long t)
    {
        for (int ra = 0; ra < a.Start.ActorCount; ra++)
        {
            int rb = b.Start.ActorNumber.IndexOf(a.Start.ActorNumber[ra]);
            Assert.Equal(a.System.LastSpent(ra), b.System.LastSpent(rb));
            for (int d = 0; d < a.Graph.DisciplineNames.Length; d++)
            {
                Assert.Equal(a.System.KD(ra, d), b.System.KD(rb, d));
                Assert.Equal(a.System.KInd(ra, d), b.System.KInd(rb, d));
            }
            for (int na = 0; na < a.Graph.Count; na++)
            {
                int nb = b.Graph.RowOf(a.Graph.Number[na]);
                Assert.True(a.System.LastStep(ra, na) == b.System.LastStep(rb, nb), $"t={t} actor {a.Start.ActorNumber[ra]} node {a.Graph.Number[na]}");
                Assert.Equal(a.System.IsKnown(ra, na), b.System.IsKnown(rb, nb));
            }
        }
    }

    private static int[] Shuffle(int n, int seed)
    {
        int[] p = [.. Enumerable.Range(0, n)];
        new Random(seed).Shuffle(p);
        return p;
    }

    /// <summary>
    /// Knowledge-only run of the slice data with inputs keyed by stable ids (actor number, node number), so
    /// storage permutations must not change any value. j from <see cref="KnowledgeRng.J"/>; budget and
    /// discipline volume vary by actor and month so K_ind and the frontier move.
    /// </summary>
    private sealed class SliceRun
    {
        public SliceRun(int[]? nodePerm, int[]? actorPerm, int threads, int chunkSize, ulong seed = 42)
        {
            var p = new KnowledgeParams();
            KnowledgeGraph baseGraph = KnowledgeGraph.FromData(Data.Nodes, Scenario.Disciplines, p);
            KnowledgeStart baseStart = KnowledgeStart.FromScenario(Scenario, baseGraph);
            int n = baseGraph.Count, dc = baseGraph.DisciplineNames.Length;
            nodePerm ??= [.. Enumerable.Range(0, n)];
            actorPerm ??= [.. Enumerable.Range(0, baseStart.ActorCount)];

            Graph = KnowledgeGraph.Create([.. nodePerm.Select(r => SpecOf(baseGraph, r))], [.. baseGraph.DisciplineNames]);
            Start = new KnowledgeStart([.. actorPerm.Select(a => baseStart.ActorNumber[a])], n, dc);
            for (int a = 0; a < actorPerm.Length; a++)
            {
                int src = actorPerm[a];
                for (int r = 0; r < n; r++)
                    Start.Known[a * n + r] = baseStart.IsKnown(src, baseGraph.RowOf(Graph.Number[r]));
                for (int d = 0; d < dc; d++)
                {
                    Start.KD[a * dc + d] = baseStart.KD[src * dc + d];
                    Start.KInd[a * dc + d] = baseStart.KInd[src * dc + d];
                }
            }
            Table = new Table(Start.ActorCount);
            Calendar = new TickCalendar(new CivilDate(Scenario.StartYear, Scenario.StartMonth, 1));
            var rng = new CounterRng(seed);
            System = new KnowledgeSystem(Table, Graph, Start, p, new Inputs(Graph, Start, rng), rng,
                TickRunner.ChunkCountFor(Start.ActorCount, chunkSize));
            Runner = new TickRunner(Table, seed, [System], chunkSize, threads, Calendar);
        }

        public KnowledgeGraph Graph { get; }
        public KnowledgeStart Start { get; }
        public Table Table { get; }
        public TickCalendar Calendar { get; }
        public KnowledgeSystem System { get; }
        public TickRunner Runner { get; }

        public void RunThroughMonth(long month)
        {
            long last = Calendar.TickOfMonth(month);
            while (Runner.Tick <= last)
                Runner.RunTick();
        }

        private static KnowledgeNodeSpec SpecOf(KnowledgeGraph g, int r)
        {
            RequirementLists req = g.Requires;
            var groups = new int[req.GroupStart[r + 1] - req.GroupStart[r]][];
            for (int k = 0; k < groups.Length; k++)
                groups[k] = [.. req.GroupMembers(req.GroupStart[r] + k).ToArray().Select(m => g.Number[m])];
            var informed = new List<(int, Fx6)>();
            for (int e = g.InformedStart[r]; e < g.InformedStart[r + 1]; e++)
                informed.Add((g.Number[g.InformedFrom[e]], g.InformedWeight[e]));
            return new KnowledgeNodeSpec(g.Number[r], g.Type[r], g.Gen[r], g.Discipline[r], g.IsHazard[r], g.CostN[r], groups, [.. informed]);
        }
    }

    private sealed class Inputs(KnowledgeGraph g, KnowledgeStart s, CounterRng rng) : IKnowledgeInputs
    {
        public ReadOnlySpan<int> LicensedEngines(int actorRow, long month) => ReadOnlySpan<int>.Empty;

        public Fx6 ResearchBudget(int actorRow, long month) =>
            Fx6.FromRaw(200_000 + 40_000 * s.ActorNumber[actorRow] + 1_000 * (month % 17));

        public Fx6 Priority(int actorRow, int nodeRow, long month) =>
            g.Type[nodeRow] == (s.ActorNumber[actorRow] % 2 == 0 ? NodeType.ENG : NodeType.PRC) ? Fx6.FromInt(2) : Fx6.One;

        public Fx6 J(int actorRow, int nodeRow, long month) =>
            KnowledgeRng.J(rng, month == 0 ? 0 : (int)((month - 1) / 12), s.ActorNumber[actorRow], g.Number[nodeRow]);

        public Fx6 DisciplineVolume(int actorRow, int discipline, long month) =>
            Fx6.FromRaw(((s.ActorNumber[actorRow] + 1) * 37_000_000L * (discipline + 1) * (month % 9)) % 900_000_000);
    }
}
