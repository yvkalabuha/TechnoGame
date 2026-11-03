using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Knowledge;
using Singularity.Core.Tests.Data;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Industry;

/// <summary>
/// L1/L2 and storage-order independence of knowledge + capability coupled on the full slice data (61 nodes,
/// all scenario actors): knowledge reads <c>discipline_volume</c> from the industry snapshot, industry reads
/// <c>known_processes</c> from the knowledge snapshot, and a product-layer stand-in posts
/// <c>report_output</c> and <c>reserve_workforce</c>. All inputs are keyed by stable ids (actor number,
/// process number), so permuting node, process or site storage must not change any value.
/// </summary>
public class IndustrySliceDeterminismTests
{
    private static readonly GameData Data = ModLoader.Load(TestMods.CoreDir);
    private static readonly ScenarioData Scenario = Data.Scenario("jet_slice_1945");
    private const int Categories = 2, Requesters = 4;

    [Fact]
    public void L2_FullData_KnowledgeAndIndustryLinked_SameHashForThreadCountsAndChunkSizes()
    {
        var runs = new[] { new LinkedRun(1, 1), new LinkedRun(4, 1), new LinkedRun(8, 1), new LinkedRun(4, 3), new LinkedRun(3, 2) };
        Assert.Equal(61, runs[0].Graph.Count);
        for (long t = 0; t < 96; t++)
        {
            string? first = null;
            foreach (LinkedRun r in runs)
            {
                r.RunThroughMonth(t);
                string h = StateHash.ComputeHex(r.Table, 1, r.Runner.Tick);
                first ??= h;
                Assert.True(first == h, $"month {t}: hash differs");
            }
        }
        // The run is not trivial: output was reported, someone was short of workforce, a process was learnt.
        Assert.True(runs[0].ReportsSeen > 0);
        Assert.True(runs[0].ShortSeen > 0);
        Assert.True(runs[0].DiscoveriesSeen > 0);
    }

    [Fact]
    public void L1_FullData_TwoRuns_SameHashEveryMonth()
    {
        var a = new LinkedRun(1, 1);
        var b = new LinkedRun(1, 1);
        for (long t = 0; t < 48; t++)
        {
            a.RunThroughMonth(t);
            b.RunThroughMonth(t);
            Assert.Equal(StateHash.ComputeHex(a.Table, 1, a.Runner.Tick), StateHash.ComputeHex(b.Table, 1, b.Runner.Tick));
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 2)]
    public void PermutedNodeProcessAndSiteStorage_SameValuesByStableIds(int threads, int chunkSize)
    {
        var baseRun = new LinkedRun(1, 1);
        int n = baseRun.Graph.Count, p = baseRun.Layout.ProcessCount, actors = baseRun.KStart.ActorCount;
        var perm = new LinkedRun(threads, chunkSize, Shuffle(n, 1), Shuffle(p, 2), Shuffle(actors, 3));
        Assert.NotEqual(baseRun.Layout.Number, perm.Layout.Number);
        for (long t = 0; t < 60; t++)
        {
            baseRun.RunThroughMonth(t);
            perm.RunThroughMonth(t);
            AssertSameByIds(baseRun, perm, t);
        }
    }

    private static void AssertSameByIds(LinkedRun a, LinkedRun b, long t)
    {
        for (int sa = 0; sa < a.KStart.ActorCount; sa++)
        {
            int number = a.KStart.ActorNumber[sa];
            int sb = b.KStart.ActorNumber.IndexOf(number);
            for (int c = 0; c < a.Layout.Clusters.Length; c++)
            {
                Assert.Equal(a.Layout.Clusters[c], b.Layout.Clusters[c]);
                for (int d = 0; d < 3; d++)
                {
                    Assert.True(a.Industry.CultureH(sa, c, (Dimension)d) == b.Industry.CultureH(sb, c, (Dimension)d), $"t={t} actor {number} H");
                    Assert.True(a.Industry.ToolingM(sa, c, (Dimension)d) == b.Industry.ToolingM(sb, c, (Dimension)d), $"t={t} actor {number} M");
                }
            }
            for (int ra = 0; ra < a.Layout.ProcessCount; ra++)
            {
                int rb = b.Layout.RowOf(a.Layout.Number[ra]);
                Assert.True(a.Industry.ProcX(sa, ra) == b.Industry.ProcX(sb, rb), $"t={t} actor {number} X {a.Layout.Number[ra]}");
                Assert.True(a.Industry.ProcV(sa, ra) == b.Industry.ProcV(sb, rb));
                Assert.True(a.Industry.ProcYield(sa, ra) == b.Industry.ProcYield(sb, rb));
            }
            for (int k = 0; k < Categories; k++)
            {
                for (int q = 0; q < Requesters; q++)
                    Assert.True(a.Industry.Granted(sa, k, q) == b.Industry.Granted(sb, k, q), $"t={t} actor {number} granted {k}.{q}");
            }
            for (int na = 0; na < a.Graph.Count; na++)
            {
                int nb = b.Graph.RowOf(a.Graph.Number[na]);
                Assert.True(a.Knowledge.IsKnown(sa, na) == b.Knowledge.IsKnown(sb, nb), $"t={t} actor {number} known {a.Graph.Number[na]}");
                Assert.True(a.Knowledge.Prog(sa, na) == b.Knowledge.Prog(sb, nb));
            }
            for (int d = 0; d < a.Graph.DisciplineNames.Length; d++)
            {
                Assert.True(a.Knowledge.KD(sa, d) == b.Knowledge.KD(sb, d));
                Assert.True(a.Industry.DisciplineVolume(sa, d) == b.Industry.DisciplineVolume(sb, d));
            }
        }
    }

    private static int[] Shuffle(int n, int seed)
    {
        int[] p = [.. Enumerable.Range(0, n)];
        new Random(seed).Shuffle(p);
        return p;
    }

    private sealed class LinkedRun
    {
        public LinkedRun(int threads, int chunkSize, int[]? nodePerm = null, int[]? procPerm = null, int[]? actorPerm = null, ulong seed = 42)
        {
            var kp = new KnowledgeParams();
            KnowledgeGraph baseGraph = KnowledgeGraph.FromData(Data.Nodes, Scenario.Disciplines, kp);
            KnowledgeStart baseStart = KnowledgeStart.FromScenario(Scenario, baseGraph);
            int n = baseGraph.Count, dc = baseGraph.DisciplineNames.Length;
            nodePerm ??= [.. Enumerable.Range(0, n)];
            actorPerm ??= [.. Enumerable.Range(0, baseStart.ActorCount)];

            Graph = KnowledgeGraph.Create([.. nodePerm.Select(r => SpecOf(baseGraph, r))], [.. baseGraph.DisciplineNames]);
            KStart = new KnowledgeStart([.. actorPerm.Select(a => baseStart.ActorNumber[a])], n, dc);
            for (int a = 0; a < actorPerm.Length; a++)
            {
                int src = actorPerm[a];
                for (int r = 0; r < n; r++)
                    KStart.Known[a * n + r] = baseStart.IsKnown(src, baseGraph.RowOf(Graph.Number[r]));
                for (int d = 0; d < dc; d++)
                {
                    KStart.KD[a * dc + d] = baseStart.KD[src * dc + d];
                    KStart.KInd[a * dc + d] = baseStart.KInd[src * dc + d];
                }
            }

            var ip = new IndustryParams();
            IndustryLayout baseLayout = IndustryLayout.FromData(Data.Nodes, Scenario, Categories, Requesters);
            IndustryStart baseIStart = IndustryStart.FromScenario(Scenario, Data.Nodes, baseLayout, ip);
            procPerm ??= [.. Enumerable.Range(0, baseLayout.ProcessCount)];
            Layout = IndustryLayout.Create([.. baseLayout.Clusters],
                [.. procPerm.Select(r => new IndustryProcessSpec(baseLayout.Number[r], baseLayout.Clusters[baseLayout.ClusterIndex[r]],
                    baseLayout.Discipline[r], baseLayout.Tier[r], baseLayout.Req[r]))],
                [.. Enumerable.Range(0, actorPerm.Length)], Categories, IndustryLayout.SliceSkilled, Requesters, IndustryLayout.SliceAlloyGrades);
            IStart = new IndustryStart(Layout);
            for (int s = 0; s < actorPerm.Length; s++)
            {
                int src = actorPerm[s];
                for (int c = 0; c < Layout.Clusters.Length; c++)
                {
                    for (int d = 0; d < 3; d++)
                    {
                        IStart.H[IStart.Cell(s, c, d)] = baseIStart.H[baseIStart.Cell(src, c, d)];
                        IStart.M[IStart.Cell(s, c, d)] = baseIStart.M[baseIStart.Cell(src, c, d)];
                    }
                }
                for (int r = 0; r < Layout.ProcessCount; r++)
                    IStart.X[s * Layout.ProcessCount + r] = baseIStart.X[src * baseLayout.ProcessCount + baseLayout.RowOf(Layout.Number[r])];
            }

            Table = new Table(actorPerm.Length);
            Calendar = new TickCalendar(new CivilDate(Scenario.StartYear, Scenario.StartMonth, 1));
            int chunks = TickRunner.ChunkCountFor(Table.RowCount, chunkSize);
            var rng = new CounterRng(seed);
            Knowledge = new KnowledgeSystem(Table, Graph, KStart, kp, new KInputs(this, rng), rng, chunks);
            Industry = new IndustrySystem(Table, Layout, IStart, ip, new IInputs(this), chunks, new V23CultureRule());
            Lines = new Sender(this, Industry.CreateMailbox(chunks, Cadence.Monthly));
            Runner = new TickRunner(Table, seed, [Knowledge, Industry, Lines], chunkSize, threads, Calendar);
        }

        public KnowledgeGraph Graph { get; }
        public KnowledgeStart KStart { get; }
        public IndustryLayout Layout { get; }
        public IndustryStart IStart { get; }
        public Table Table { get; }
        public TickCalendar Calendar { get; }
        public KnowledgeSystem Knowledge { get; }
        public IndustrySystem Industry { get; }
        public Sender Lines { get; }
        public TickRunner Runner { get; }

        public long ReportsSeen, ShortSeen, DiscoveriesSeen;

        public int ActorNumber(int row) => KStart.ActorNumber[row];

        public bool Knows(int site, int processRow) => Knowledge.IsKnown(Layout.SiteOwner[site], Graph.RowOf(Layout.Number[processRow]));

        public void RunThroughMonth(long month)
        {
            long last = Calendar.TickOfMonth(month);
            while (Runner.Tick <= last)
            {
                Runner.RunTick();
                if (Calendar.IsDue(Cadence.Monthly, Runner.Tick - 1))
                {
                    ReportsSeen += Industry.LastReports.Count;
                    DiscoveriesSeen += Knowledge.LastDiscoveries.Count;
                    for (int s = 0; s < Table.RowCount; s++)
                    {
                        for (int k = 0; k < Categories; k++)
                        {
                            for (int q = 0; q < Requesters; q++)
                                ShortSeen += Industry.Granted(s, k, q) < Industry.Requested(s, k, q) ? 1 : 0;
                        }
                    }
                }
            }
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

    private sealed class KInputs(LinkedRun run, CounterRng rng) : IKnowledgeInputs
    {
        public Fx6 ResearchBudget(int actorRow, long month) =>
            Fx6.FromRaw(300_000 + 40_000 * run.ActorNumber(actorRow) + 1_000 * (month % 17));

        public Fx6 Priority(int actorRow, int nodeRow, long month) =>
            run.Graph.Type[nodeRow] == NodeType.PRC ? Fx6.FromInt(2) : Fx6.One;

        public Fx6 J(int actorRow, int nodeRow, long month) =>
            KnowledgeRng.J(rng, month == 0 ? 0 : (int)((month - 1) / 12), run.ActorNumber(actorRow), run.Graph.Number[nodeRow]);

        public Fx6 DisciplineVolume(int actorRow, int discipline, long month) => run.Industry.DisciplineVolume(actorRow, discipline);
    }

    private sealed class IInputs(LinkedRun run) : IIndustryInputs
    {
        public bool KnowsProcess(int siteRow, int processRow, long month) => run.Knows(siteRow, processRow);

        public Fx6 W(int siteRow, int cluster, long month) => Fx6.One;

        public Fx6 Hcap(int siteRow, Dimension dim, long month) => Fx6.FromRaw(5_000_000 + 100_000 * (int)dim);

        public bool QualityProgramme(int siteRow, long month) => run.ActorNumber(siteRow) % 3 == 0;

        public Fx6 ToolingInvest(int siteRow, int cluster, Dimension dim, long month) =>
            Fx6.FromRaw(2_000 * (1 + (run.ActorNumber(siteRow) + cluster + (int)dim) % 4));

        public long WorkforceAvailable(int siteRow, int category, long month) => (run.ActorNumber(siteRow) + category + month) % 12;

        public Fx6 AlloyCap(int siteRow, long month) => Fx6.FromInt(50);

        public Fx6 AlloyDeficit(int siteRow, long month) => Fx6.Zero;

        public Fx6 LicenceShare(int siteRow, int processRow, long month) => Fx6.Zero;

        public long KnownSince(int siteRow, int processRow, long month) => -1;
    }

    /// <summary>Product-layer stand-in: runs every known process with yield &gt; 0, asks for workforce in reverse id order.</summary>
    private sealed class Sender(LinkedRun run, IndustryMailbox box) : ITickSystem
    {
        public int SystemId => 300;

        public Cadence Cadence => Cadence.Monthly;

        public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd)
        {
            long t = context.Calendar!.MonthIndex(context.Tick);
            for (int s = rowStart; s < rowEnd; s++)
            {
                int a = run.ActorNumber(s);
                // Storage order on purpose (not Layout.Order): the merge must sort by the contract key.
                for (int r = 0; r < run.Layout.ProcessCount; r++)
                {
                    int number = run.Layout.Number[r];
                    if (!run.Knows(s, r) || run.Industry.Yield(s, r, t).Yield <= Fx6.Zero)
                        continue;
                    Fx6 units = Fx6.FromInt(1 + (a * 7 + number * 3 + t) % 5);
                    box.ReportOutput(chunk, new OutputReport(s, number, units, units * Fx6.FromInt(40)));
                }
                for (int k = Categories - 1; k >= 0; k--)
                {
                    for (int q = Requesters - 1; q >= 0; q--)
                    {
                        long need = (a * 13 + q * 7 + k * 5 + t) % 9;
                        if (need != 4)
                            box.ReserveWorkforce(chunk, new WorkforceRequest(s, k, q, need));
                    }
                }
            }
        }

        public void Merge(in TickContext context, int chunkCount) { }

        public void Post(in TickContext context) { }
    }
}
