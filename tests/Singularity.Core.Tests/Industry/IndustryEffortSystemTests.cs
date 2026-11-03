using System.Collections.Immutable;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Industry;

/// <summary>
/// IndustrySystem with the culture rule C-B (contract v2.4 §4.0, §4.1b): proc_E column, budget conservation,
/// rest_a into ToolingInvest, one-month lag, ablation to v2.3, determinism L1/L2.
/// </summary>
public class IndustryEffortSystemTests
{
    private static Fx6 F(long raw) => Fx6.FromRaw(raw);

    private static Fx6 D(int units) => Fx6.FromInt(units);

    private sealed class Inputs : IIndustryInputs
    {
        public Fx6 Budget0 = F(3_000);
        public Fx6 Budget1 => Fx6.Zero;
        public int[] Focus = [-1, -1, -1];
        public readonly List<(int Site, Fx6 RestA)> RestSeen = [];

        public bool KnowsProcess(int siteRow, int processRow, long month) => true;

        public Fx6 W(int siteRow, int cluster, long month) => Fx6.One;

        public Fx6 Hcap(int siteRow, Dimension dim, long month) => D(5);

        public bool QualityProgramme(int siteRow, long month) => false;

        public Fx6 ToolingInvest(int siteRow, int cluster, Dimension dim, long month, Fx6 restActor)
        {
            lock (RestSeen)
                RestSeen.Add((siteRow, restActor));
            return Fx6.Zero;
        }

        public Fx6 EffortBudget(int actorRow, long month) => actorRow == 0 ? Budget0 : Budget1;

        public int EffortFocus(int actorRow, int k, long month) => actorRow == 0 ? Focus[k] : -1;

        public Fx6 PlannedVolume(int siteRow, int processRow, long month) => D(10);

        public bool Planned(int siteRow, int processRow, long month) => true;

        public Fx6 LicenceShare(int siteRow, int processRow, long month) => Fx6.Zero;

        public long WorkforceAvailable(int siteRow, int category, long month) => 10;

        public Fx6 AlloyCap(int siteRow, long month) => Fx6.Zero;

        public Fx6 AlloyDeficit(int siteRow, long month) => Fx6.Zero;

        public long KnownSince(int siteRow, int processRow, long month) => 0;
    }

    private sealed class Lines(IndustryMailbox box) : ITickSystem
    {
        public int SystemId => 301;

        public Cadence Cadence => Cadence.Monthly;

        public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd)
        {
            for (int s = rowStart; s < rowEnd; s++)
            {
                foreach (int number in new[] { 39, 7 })
                    box.ReportOutput(chunk, new OutputReport(s, number, D(10), D(10)));
            }
        }

        public void Merge(in TickContext context, int chunkCount) { }

        public void Post(in TickContext context) { }
    }

    private sealed class Rig
    {
        public Rig(ICultureRule? rule = null, int threads = 1, IndustryMode mode = IndustryMode.Full, ImmutableArray<CapabilityCurve> curves = default)
        {
            Layout = IndustryLayout.Create([Cluster.MET, Cluster.CHEM],
            [
                new IndustryProcessSpec(39, Cluster.MET, 0, 4, new Dims3(Fx6.Zero, D(4), F(3_500_000)), Fx6.One),
                new IndustryProcessSpec(40, Cluster.MET, 0, 4, new Dims3(Fx6.Zero, F(4_500_000), D(4)), Fx6.One),
                new IndustryProcessSpec(7, Cluster.CHEM, 1, 3, new Dims3(D(3), D(3), F(2_500_000)), Fx6.One),
            ], [0, 0, 1], workforceCategories: 2, skilledCategory: 1, requesters: 4);
            Start = new IndustryStart(Layout);
            for (int s = 0; s < 3; s++)
            {
                for (int c = 0; c < 2; c++)
                {
                    for (int d = 0; d < 3; d++)
                    {
                        Start.H[Start.Cell(s, c, d)] = F(3_800_000);
                        Start.M[Start.Cell(s, c, d)] = F(4_000_000);
                    }
                }
                for (int r = 0; r < Layout.ProcessCount; r++)
                    Start.X[s * Layout.ProcessCount + r] = D(1000);
            }
            Table = new Table(3);
            Calendar = new TickCalendar(new CivilDate(1945, 9, 1));
            int chunks = TickRunner.ChunkCountFor(3, 1);
            System = new IndustrySystem(Table, Layout, Start, new IndustryParams(), In, chunks, rule ?? new CbCultureRule(), mode, curves);
            Runner = new TickRunner(Table, 1, [System, new Lines(System.CreateMailbox(chunks, Cadence.Monthly))], 1, threads, Calendar);
        }

        public Inputs In { get; } = new();
        public IndustryLayout Layout { get; }
        public IndustryStart Start { get; }
        public Table Table { get; }
        public TickCalendar Calendar { get; }
        public IndustrySystem System { get; }
        public TickRunner Runner { get; }

        public void RunThroughMonth(long month)
        {
            long last = Calendar.TickOfMonth(month);
            while (Runner.Tick <= last)
                Runner.RunTick();
        }

        public string Hash() => StateHash.ComputeHex(Table, 1, Runner.Tick);
    }

    [Fact]
    public void Rule_IsRequired_AndCbRejectsProcessesWithoutLabour()
    {
        var rig = new Rig();
        Assert.Throws<ArgumentNullException>(() => new IndustrySystem(new Table(3), rig.Layout, rig.Start, new IndustryParams(), rig.In, 1, null!));
        IndustryLayout noLabour = IndustryLayout.Create([Cluster.MET],
            [new IndustryProcessSpec(39, Cluster.MET, 0, 4, new Dims3(Fx6.Zero, D(4), F(3_500_000)))], [0], 2, 1, 4);
        var start = new IndustryStart(noLabour);
        Assert.Throws<ArgumentException>(() => new IndustrySystem(new Table(1), noLabour, start, new IndustryParams(), rig.In, 1, new CbCultureRule()));
        _ = new IndustrySystem(new Table(1), noLabour, start, new IndustryParams(), rig.In, 1, new V23CultureRule()); // ablation needs no labour
    }

    [Fact]
    public void EachMonth_ProcE_Plus_Rest_EqualsTheBudgetOfTheActor()
    {
        var rig = new Rig();
        for (int month = 0; month < 6; month++)
        {
            rig.In.RestSeen.Clear();
            rig.RunThroughMonth(month);
            // Actor 0 owns sites 0, 1: E of both sites plus rest_a (the same for both) = D_0 (per actor, not per site).
            Fx6 e = Fx6.Zero;
            for (int s = 0; s < 2; s++)
            {
                for (int r = 0; r < rig.Layout.ProcessCount; r++)
                    e += rig.System.ProcE(s, r);
            }
            Assert.Equal(rig.In.Budget0, e + rig.System.LastRest(0));
            Assert.Equal(rig.System.LastRest(0), rig.System.LastRest(1));
            // Actor 1 owns site 2 with a zero budget.
            Assert.Equal(Fx6.Zero, rig.System.LastRest(2));
            for (int r = 0; r < rig.Layout.ProcessCount; r++)
                Assert.Equal(Fx6.Zero, rig.System.ProcE(2, r));
            // ToolingInvest saw the rest of the site's actor for every cell.
            Assert.All(rig.In.RestSeen, x => Assert.Equal(rig.System.LastRest(x.Site), x.RestA));
            Assert.Equal(3 * 2 * 3, rig.In.RestSeen.Count);
        }
    }

    [Fact]
    public void FocusAndBudget_ReachProcE_WithinTheCaps()
    {
        var rig = new Rig();
        rig.In.Focus = [39, -1, -1];
        rig.RunThroughMonth(1);
        int r7 = rig.Layout.RowOf(39);
        // Month 1: month 0 had output (proc_v = 10), so E_useful > 0; the focus process takes what E_useful allows.
        Assert.True(rig.System.ProcE(0, r7) > Fx6.Zero);
        // Cap of the focus: E_{p} ≤ E_full (k* ≤ 1) at every site.
        for (int r = 0; r < rig.Layout.ProcessCount; r++)
        {
            Assert.True(rig.System.ProcE(0, r) >= Fx6.Zero);
            Assert.True(rig.System.ProcE(0, r) <= new IndustryParams().EFull);
        }
    }

    [Fact]
    public void ProcE_OfMonthT_IsReadByTheCultureOfMonthTPlus1()
    {
        var rig = new Rig();
        rig.RunThroughMonth(0);
        // Month 0 ran with proc_E = 0 (column empty at start): the cell drive was zero for everything.
        for (int c = 0; c < 2; c++)
        {
            for (int d = 0; d < 3; d++)
                Assert.Equal(Fx6.Zero, rig.System.LastCulture(0, c, (Dimension)d).Drive);
        }
        Assert.Contains(Enumerable.Range(0, rig.Layout.ProcessCount), r => rig.System.ProcE(0, r) > Fx6.Zero);
        rig.RunThroughMonth(1);
        Assert.Contains(Enumerable.Range(0, 2).SelectMany(c => Enumerable.Range(0, 3).Select(d => rig.System.LastCulture(0, c, (Dimension)d).Drive)),
            drive => drive > Fx6.Zero);
    }

    [Fact]
    public void Money_RaisesCultureAboveTheSameRunWithoutMoney()
    {
        var with = new Rig();
        var without = new Rig();
        without.In.Budget0 = Fx6.Zero;
        with.RunThroughMonth(24);
        without.RunThroughMonth(24);
        Fx6 sumWith = Fx6.Zero, sumWithout = Fx6.Zero;
        for (int c = 0; c < 2; c++)
        {
            for (int d = 0; d < 3; d++)
            {
                sumWith += with.System.CultureH(0, c, (Dimension)d);
                sumWithout += without.System.CultureH(0, c, (Dimension)d);
            }
        }
        Assert.True(sumWith > sumWithout);
    }

    [Fact]
    public void V23Ablation_TakesNoMoney_WholeItemGoesToTheTooling()
    {
        var rig = new Rig(new V23CultureRule());
        rig.RunThroughMonth(3);
        for (int s = 0; s < 3; s++)
        {
            for (int r = 0; r < rig.Layout.ProcessCount; r++)
                Assert.Equal(Fx6.Zero, rig.System.ProcE(s, r));
        }
        Assert.Equal(rig.In.Budget0, rig.System.LastRest(0));
        Assert.Equal("R0 v2.3", rig.System.Rule.Name);
    }

    [Fact]
    public void ControlModes_DoNotSpendOnProcessDevelopment()
    {
        var rig = new Rig(mode: IndustryMode.ConstLc0);
        rig.RunThroughMonth(2);
        for (int r = 0; r < rig.Layout.ProcessCount; r++)
            Assert.Equal(Fx6.Zero, rig.System.ProcE(0, r));
        Assert.Empty(rig.In.RestSeen);
    }

    [Fact]
    public void ExternalFunctions_EqualTheRuleEvaluatedOnTheSameSnapshot()
    {
        var rig = new Rig();
        rig.RunThroughMonth(1);
        var rule = new CbCultureRule();
        ICultureSnapshot snap = rig.System;
        foreach (long month in new[] { 1L, 2L })
        {
            for (int site = 0; site < 3; site++)
            {
                for (int r = 0; r < rig.Layout.ProcessCount; r++)
                {
                    Assert.Equal(rule.EUseful(snap, site, r, month), rig.System.EUseful(site, r, month));
                    Assert.Equal(rule.Limiter(snap, site, r, month), rig.System.Limiter(site, r, month));
                    for (int d = 0; d < 3; d++)
                    {
                        if (rig.Layout.Req[r][d] <= Fx6.Zero)
                            continue;
                        Fx6 e = rig.System.ProcE(site, r), v = rig.System.ProcV(site, r);
                        Fx6 forecast = rig.System.DHForecast(site, r, (Dimension)d, e, v, month);
                        Assert.Equal(rule.DHForecast(snap, site, r, d, e, v, month), forecast);
                        // At the current E and v the forecast is the capped growth the cell step computes.
                        Assert.Equal(rule.Step(snap, site, rig.Layout.ClusterIndex[r], d, month).DHPlusCapped, forecast);
                    }
                }
            }
        }
    }

    [Fact]
    public void Determinism_L1_SameRunSameHash_L2_ThreadsDoNotMatter()
    {
        var a = new Rig(threads: 1);
        var b = new Rig(threads: 1);
        var c = new Rig(threads: 4);
        a.RunThroughMonth(12);
        b.RunThroughMonth(12);
        c.RunThroughMonth(12);
        Assert.Equal(a.Hash(), b.Hash());
        Assert.Equal(a.Hash(), c.Hash());
    }

    [Fact]
    public void NegativeBudget_IsAnInvariantViolation()
    {
        var rig = new Rig();
        rig.In.Budget0 = F(-1);
        var ex = Assert.Throws<TickComputeException>(() => rig.RunThroughMonth(0));
        Assert.IsType<IndustryInvariantException>(ex.InnerException);
    }
}
