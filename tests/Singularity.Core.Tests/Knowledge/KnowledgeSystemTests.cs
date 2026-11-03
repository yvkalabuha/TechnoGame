using System.Collections.Immutable;
using Singularity.Core.Data;
using Singularity.Core.Knowledge;
using Singularity.Core.Tests.Data;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Knowledge;

/// <summary>Controls L_K / L_K0, lic_knows, the slice data graph and the L1/L2 checks of the knowledge system.</summary>
public class KnowledgeSystemTests
{
    private static readonly GameData Data = ModLoader.Load(TestMods.CoreDir);
    private static readonly ScenarioData Scenario = Data.Scenario("jet_slice_1945");

    // ---- L_K0

    [Fact]
    public void Lk0_OpensAtWindowMiddlePlusLag_AndNeverBeforeRequirements()
    {
        // Reference graph, all nodes G1 there: due = mid[1] + lag[1] = 5 + 0 for every unknown node.
        var run = new JetReference.Run(mode: KnowledgeMode.ScheduleLk0,
            schedule: (g, s) => KnowledgeSchedule.BuildLk0(g, s, [0, 5, 30, 60, 90]));
        run.RunThroughMonth(4);
        Assert.Equal([6], KnownNumbers(run, 0));
        run.RunThroughMonth(5);
        Assert.Equal([1, 2, 3, 4, 5, 6], KnownNumbers(run, 0));
        Assert.Equal(5, run.System.LastDiscoveries.Count);
        // Controls do not touch K_d, K_ind, prog (§6.1).
        Assert.Equal(JetReference.Dec("0.5"), run.System.KD(0, 0));
        Assert.Equal(Fx6.Zero, run.System.Prog(0, run.Graph.RowOf(2)));
    }

    [Fact]
    public void Lk0_DefaultMiddles_FromSpec()
    {
        // §6.1 v0.6: middles 0/0/64/172/292, due = 0/0/88/208/340 (1953-01, 1963-01, 1974-01).
        Assert.Equal([0, 0, 64, 172, 292], KnowledgeSchedule.Lk0WindowMid.ToArray());
        KnowledgeGraph g = KnowledgeGraph.Create(
        [.. Enumerable.Range(0, 5).Select(gen => new KnowledgeNodeSpec(gen + 1, NodeType.FND, gen, 0, false, Fx6.One, []))], ["prop"]);
        ImmutableArray<long> due = KnowledgeSchedule.BuildLk0(g, new KnowledgeStart([0], 5, 1));
        Assert.Equal([0L, 0, 88, 208, 340], due.ToArray());
        var cal = new TickCalendar(new CivilDate(1945, 9, 1));
        Assert.Equal(new CivilDate(1953, 1, 1), cal.DateOf(cal.TickOfMonth(88)));
        Assert.Equal(new CivilDate(1974, 1, 1), cal.DateOf(cal.TickOfMonth(340)));
    }

    [Fact]
    public void Lk0_RequirementsDelayDependents()
    {
        KnowledgeGraph g = KnowledgeGraph.Create(
        [
            new KnowledgeNodeSpec(1, NodeType.FND, 3, 0, false, Fx6.One, []),
            new KnowledgeNodeSpec(2, NodeType.FND, 2, 0, false, Fx6.One, []),
            new KnowledgeNodeSpec(3, NodeType.ENG, 2, 0, false, Fx6.One, [[1]]),
            new KnowledgeNodeSpec(4, NodeType.APP, 2, -1, false, Fx6.One, [[1, 2]]), // OR-group: earliest member
        ], ["prop"]);
        var start = new KnowledgeStart([0], 4, 1);
        ImmutableArray<long> due = KnowledgeSchedule.BuildLk0(g, start, [0, 0, 10, 20, 30]);
        Assert.Equal([20 + 36, 10 + 24, 20 + 36, 10 + 24], due.ToArray()); // node 3 waits for node 1; node 4 needs 1 or 2
    }

    // ---- L_K

    [Fact]
    public void Lk_DrawsFromKnowStreamAtMonthZero()
    {
        var run = new JetReference.Run(mode: KnowledgeMode.ScheduleLk, schedule: (g, s) => KnowledgeSchedule.BuildLk(g, s, Tables(), Rng()));
        ImmutableArray<long> due = KnowledgeSchedule.BuildLk(run.Graph, run.Start, Tables(), Rng());
        // Expected clock of G1: U[m − hw; m + hw] from (seed, 0, KNOW, actor·64, 1 + g).
        long clock = CounterRng.UniformInt(Rng().NextU64(0, 1, 0, 2), 10 - 4, 10 + 4);
        Assert.Equal(clock + 2, due[run.Graph.RowOf(1)]);
        Assert.Equal(KnowledgeSchedule.Never, due[run.Graph.RowOf(3)]); // δ = never
        Assert.Equal(KnowledgeSchedule.Never, due[run.Graph.RowOf(4)]); // requires 3
        Assert.Equal(KnowledgeSchedule.AtStart, due[run.Graph.RowOf(6)]);
        run.RunThroughMonth(30);
        for (int r = 0; r < run.Graph.Count; r++)
            Assert.Equal(due[r] <= 30, run.System.IsKnown(0, r));
    }

    [Fact]
    public void Lk_PCensOne_MeansNever()
    {
        LkTables t = Tables();
        t.PCens[0] = Fx6.One;
        var run = new JetReference.Run();
        ImmutableArray<long> due = KnowledgeSchedule.BuildLk(run.Graph, run.Start, t, Rng());
        for (int r = 0; r < run.Graph.Count; r++)
            Assert.Equal(run.Graph.Number[r] == 6 ? KnowledgeSchedule.AtStart : KnowledgeSchedule.Never, due[r]);
    }

    private static CounterRng Rng() => new(JetReference.Trajectory.GetProperty("seed").GetUInt64());

    private static LkTables Tables()
    {
        var t = new LkTables(1);
        t.Mean[0] = 10;
        t.HalfWidth[0] = 4;
        foreach ((int node, int delta) in new[] { (1, 2), (2, 5), (4, 0), (5, 1) })
            t.Delta[node] = delta;
        return t;
    }

    // ---- lic_knows (§6.7)

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LicKnows_GrantsRequiresClosure_DiscoveryWins_NoKdCredit(bool fourKnownAtStart)
    {
        // 6 ENG ← 5 ENG ← {2 PRN ← 1 FND} and the OR-group {3 | 4}. Licence for 6 bought in month 0, applied
        // in month 1 (§6.7 v0.6). FND 1 is cheap: its own research also opens it in month 1.
        KnowledgeGraph g = KnowledgeGraph.Create(
        [
            new KnowledgeNodeSpec(1, NodeType.FND, 1, 0, false, Fx6.FromRaw(41_700), []),
            new KnowledgeNodeSpec(2, NodeType.PRN, 1, 0, false, Fx6.FromInt(1000), [[1]]),
            new KnowledgeNodeSpec(3, NodeType.FND, 1, 0, false, Fx6.FromInt(1000), []),
            new KnowledgeNodeSpec(4, NodeType.FND, 1, 0, false, Fx6.FromInt(1000), []),
            new KnowledgeNodeSpec(5, NodeType.ENG, 1, 0, true, Fx6.FromInt(1000), [[2], [3, 4]]),
            new KnowledgeNodeSpec(6, NodeType.ENG, 1, 0, true, Fx6.FromInt(1000), [[5]]),
        ], ["prop"]);
        var start = new KnowledgeStart([0], 6, 1);
        start.Known[g.RowOf(4)] = fourKnownAtStart;
        var inputs = new FixedInputs { Licences = [g.RowOf(6)], LicenceMonth = 1 };
        (TickRunner runner, KnowledgeSystem sys, TickCalendar cal) = Build(g, start, inputs, licKnows: true);
        RunThrough(runner, cal, 0);
        Assert.False(sys.IsKnown(0, g.RowOf(1)));
        Assert.Empty(sys.LastLicenceGrants);
        RunThrough(runner, cal, 1); // Post checks I5 on the result

        Assert.Equal([(0, 1)], sys.LastDiscoveries); // discovered and licensed in one month: a discovery
        (int, int)[] grants = fourKnownAtStart ? [(0, 2), (0, 5), (0, 6)] : [(0, 2), (0, 3), (0, 5), (0, 6)];
        Assert.Equal(grants, sys.LastLicenceGrants); // OR-group without a known member: lowest number (3)
        Assert.False(fourKnownAtStart ? sys.IsKnown(0, g.RowOf(3)) : sys.IsKnown(0, g.RowOf(4)));
        // K_d: only own research money and the own discovery (§6.7 "Учёт").
        var p = new KnowledgeParams();
        int frontier = fourKnownAtStart ? 2 : 3;
        Fx6 s = Fx6.FromRaw(100_000), amount = Fx6.Zero;
        for (int i = 0; i < frontier; i++)
            amount += s * (Fx6.One / Fx6.FromInt(frontier));
        Fx6 expected = KnowledgeMath.KdDelta(amount, 0, p.MuK, p.SRef, p.KNode) + KnowledgeMath.KdDelta(amount, 1, p.MuK, p.SRef, p.KNode);
        Assert.Equal(expected, sys.KD(0, 0));
    }

    [Fact]
    public void LicKnows_Off_IgnoresLicences()
    {
        KnowledgeGraph g = KnowledgeGraph.Create([new KnowledgeNodeSpec(1, NodeType.ENG, 1, 0, false, Fx6.FromInt(1000), [])], ["prop"]);
        var inputs = new FixedInputs { Licences = [0], LicenceMonth = 0 };
        (TickRunner runner, KnowledgeSystem sys, TickCalendar cal) = Build(g, new KnowledgeStart([0], 1, 1), inputs, licKnows: false);
        RunThrough(runner, cal, 0);
        Assert.False(sys.IsKnown(0, 0));
    }

    [Fact]
    public void Post_KnownNodeWithoutRequirements_Throws()
    {
        KnowledgeGraph g = KnowledgeGraph.Create(
        [
            new KnowledgeNodeSpec(1, NodeType.FND, 1, 0, false, Fx6.FromInt(1000), []),
            new KnowledgeNodeSpec(2, NodeType.ENG, 1, 0, false, Fx6.FromInt(1000), [[1]]),
        ], ["prop"]);
        var start = new KnowledgeStart([7], 2, 1);
        start.Known[1] = true;
        (TickRunner runner, _, _) = Build(g, start, new FixedInputs(), licKnows: false);
        var e = Assert.Throws<KnowledgeInvariantException>(runner.RunTick);
        Assert.Equal(7, e.ActorNumber);
    }

    [Fact]
    public void Construction_RejectsBadGraphs()
    {
        Assert.Throws<ArgumentException>(() => KnowledgeGraph.Create([new KnowledgeNodeSpec(64, NodeType.FND, 1, 0, false, Fx6.One, [])], ["prop"]));
        Assert.Throws<ArgumentException>(() => KnowledgeGraph.Create([new KnowledgeNodeSpec(1, NodeType.APP, 1, 0, false, Fx6.One, [])], ["prop"]));
        Assert.Throws<ArgumentException>(() => KnowledgeGraph.Create(
            [new KnowledgeNodeSpec(1, NodeType.FND, 1, 0, false, Fx6.One, []), new KnowledgeNodeSpec(1, NodeType.FND, 1, 0, false, Fx6.One, [])], ["prop"]));
        Assert.Throws<ArgumentException>(() => KnowledgeGraph.Create([new KnowledgeNodeSpec(1, NodeType.FND, 1, 0, false, Fx6.One, [[2]])], ["prop"]));
    }

    // ---- slice data

    [Fact]
    public void SliceData_RunsTwoYears_Deterministic_AndThreadIndependent()
    {
        string h1 = RunSlice(threads: 1, chunkSize: 1, months: 24);
        Assert.Equal(h1, RunSlice(threads: 1, chunkSize: 1, months: 24));   // L1
        Assert.Equal(h1, RunSlice(threads: 4, chunkSize: 1, months: 24));   // L2
        Assert.Equal(h1, RunSlice(threads: 8, chunkSize: 2, months: 24));
    }

    [Fact]
    public void SliceGraph_FromData_HazardDomainAndCosts()
    {
        var p = new KnowledgeParams();
        KnowledgeGraph g = KnowledgeGraph.FromData(Data.Nodes, Scenario.Disciplines, p);
        Assert.Equal(61, g.Count);
        Assert.Equal(20, g.IsHazard.Count(h => h)); // spec §1.3: 11 key + 9 hz
        int app = g.RowOf(58);
        Assert.Equal(-1, g.Discipline[app]);
        Assert.Equal(Fx6.FromRaw(4_800_000), g.CostN[g.RowOf(19)]); // ENG G2: 3 × 1.6
        KnowledgeStart s = KnowledgeStart.FromScenario(Scenario, g);
        Assert.True(s.IsKnown(Scenario.ActorIndex("USA"), g.RowOf(58)));
        Assert.True(s.IsKnown(Scenario.ActorIndex("SUN"), g.RowOf(58)));   // D1, R-KN (#134)
    }

    private static string RunSlice(int threads, int chunkSize, int months)
    {
        var p = new KnowledgeParams();
        KnowledgeGraph g = KnowledgeGraph.FromData(Data.Nodes, Scenario.Disciplines, p);
        KnowledgeStart s = KnowledgeStart.FromScenario(Scenario, g);
        var table = new Table(s.ActorCount);
        var cal = new TickCalendar(new CivilDate(Scenario.StartYear, Scenario.StartMonth, 1));
        var inputs = new FixedInputs { Budget = Fx6.FromRaw(300_000) };
        var sys = new KnowledgeSystem(table, g, s, p, inputs, new CounterRng(42), TickRunner.ChunkCountFor(s.ActorCount, chunkSize));
        var runner = new TickRunner(table, 42, [sys], chunkSize, threads, cal);
        RunThrough(runner, cal, months - 1);
        int opened = 0;
        for (int a = 0; a < s.ActorCount; a++)
        {
            for (int r = 0; r < g.Count; r++)
                opened += sys.IsKnown(a, r) && !s.IsKnown(a, r) ? 1 : 0;
        }
        Assert.True(opened > 0); // the run does something
        return StateHash.ComputeHex(table, 1, 0);
    }

    // ---- helpers

    private static List<int> KnownNumbers(JetReference.Run run, int actorRow) =>
        [.. Enumerable.Range(0, run.Graph.Count).Where(r => run.System.IsKnown(actorRow, r)).Select(r => run.Graph.Number[r]).Order()];

    private static (TickRunner, KnowledgeSystem, TickCalendar) Build(KnowledgeGraph g, KnowledgeStart s, FixedInputs inputs, bool licKnows)
    {
        var table = new Table(s.ActorCount);
        var cal = new TickCalendar(new CivilDate(1945, 9, 1));
        var sys = new KnowledgeSystem(table, g, s, new KnowledgeParams(), inputs, new CounterRng(1),
            TickRunner.ChunkCountFor(s.ActorCount, 1), licenceTransfersKnowledge: licKnows);
        return (new TickRunner(table, 1, [sys], 1, 1, cal), sys, cal);
    }

    private static void RunThrough(TickRunner runner, TickCalendar cal, long month)
    {
        while (runner.Tick <= cal.TickOfMonth(month))
            runner.RunTick();
    }

    /// <summary>Flat budget, prio 1, j 1, no industry output.</summary>
    private sealed class FixedInputs : IKnowledgeInputs
    {
        public Fx6 Budget = Fx6.FromRaw(100_000);
        public int[] Licences = [];
        public long LicenceMonth = -1;

        public Fx6 ResearchBudget(int actorRow, long month) => Budget;
        public Fx6 Priority(int actorRow, int nodeRow, long month) => Fx6.One;
        public Fx6 J(int actorRow, int nodeRow, long month) => Fx6.One;
        public Fx6 DisciplineVolume(int actorRow, int discipline, long month) => Fx6.Zero;
        public ReadOnlySpan<int> LicensedEngines(int actorRow, long month) => month == LicenceMonth ? Licences : [];
    }
}
