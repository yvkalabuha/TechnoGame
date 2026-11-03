using System.Collections.Immutable;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Tests.Data;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Industry;

/// <summary>Capability layer behaviour outside the reference trajectory: merge rules, workforce, tooling, alloy, controls, data start.</summary>
public class IndustrySystemTests
{
    private static Fx6 F(long raw) => Fx6.FromRaw(raw);

    private static Fx6 D(int units) => Fx6.FromInt(units);

    // ---- rig: 3 sites (owners 0, 0, 1), MET melts 39/40/41, one CHEM process 7, one MECH process 48 (tier 4)

    private sealed class Inputs : IIndustryInputs
    {
        public bool AllKnown = true;
        public int UnknownProcessRow = -1;
        public bool Qp;
        public Fx6 Invest;
        public Fx6 Cap = D(1000), Deficit;
        public long Avail = 10;
        public long KnownAt;

        public bool KnowsProcess(int siteRow, int processRow, long month) => AllKnown && processRow != UnknownProcessRow;

        public Fx6 W(int siteRow, int cluster, long month) => Fx6.One;

        public Fx6 Hcap(int siteRow, Dimension dim, long month) => D(5);

        public bool QualityProgramme(int siteRow, long month) => Qp;

        public Fx6 ToolingInvest(int siteRow, int cluster, Dimension dim, long month) => Invest;

        public Fx6 LicenceShare(int siteRow, int processRow, long month) => Fx6.Zero;

        public long WorkforceAvailable(int siteRow, int category, long month) => Avail;

        public Fx6 AlloyCap(int siteRow, long month) => Cap;

        public Fx6 AlloyDeficit(int siteRow, long month) => Deficit;

        public long KnownSince(int siteRow, int processRow, long month) => KnowsProcess(siteRow, processRow, month) ? KnownAt : -1;
    }

    private sealed class Sender(IndustryMailbox box) : ITickSystem
    {
        public Func<long, IEnumerable<OutputReport>> Reports = _ => [];
        public Func<long, IEnumerable<WorkforceRequest>> Requests = _ => [];

        public int SystemId => 301;

        public Cadence Cadence => Cadence.Monthly;

        public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd)
        {
            if (rowStart != 0)
                return; // one chunk posts everything: posting order must not matter
            long t = context.Calendar!.MonthIndex(context.Tick);
            foreach (OutputReport r in Reports(t))
                box.ReportOutput(chunk, r);
            foreach (WorkforceRequest q in Requests(t))
                box.ReserveWorkforce(chunk, q);
        }

        public void Merge(in TickContext context, int chunkCount) { }

        public void Post(in TickContext context) { }
    }

    private sealed class Rig
    {
        public Rig(IndustryMode mode = IndustryMode.Full, ImmutableArray<CapabilityCurve> curves = default, IndustryParams? p = null, int threads = 1)
        {
            Layout = IndustryLayout.Create([Cluster.MET, Cluster.CHEM, Cluster.MECH],
            [
                new IndustryProcessSpec(39, Cluster.MET, 0, 4, new Dims3(Fx6.Zero, D(4), F(3_500_000))),
                new IndustryProcessSpec(40, Cluster.MET, 0, 4, new Dims3(Fx6.Zero, F(4_500_000), D(4))),
                new IndustryProcessSpec(41, Cluster.MET, 0, 5, new Dims3(Fx6.Zero, D(5), F(4_500_000))),
                new IndustryProcessSpec(7, Cluster.CHEM, 1, 3, new Dims3(D(3), D(3), F(2_500_000))),
                new IndustryProcessSpec(48, Cluster.MECH, 2, 4, new Dims3(D(4), Fx6.Zero, F(3_500_000))),
            ], [0, 0, 1], workforceCategories: 2, skilledCategory: 1, requesters: 6, alloyGrades: IndustryLayout.SliceAlloyGrades);
            Start = new IndustryStart(Layout);
            for (int s = 0; s < 3; s++)
            {
                for (int c = 0; c < 3; c++)
                {
                    Fx6[] h = [D(4), F(3_800_000), F(3_700_000)];
                    for (int d = 0; d < 3; d++)
                    {
                        Start.H[Start.Cell(s, c, d)] = h[d];
                        Start.M[Start.Cell(s, c, d)] = h[d] + F(200_000);
                    }
                }
                for (int r = 0; r < Layout.ProcessCount; r++)
                    Start.X[s * Layout.ProcessCount + r] = D(1000);
            }
            Table = new Table(3);
            Calendar = new TickCalendar(new CivilDate(1945, 9, 1));
            P = p ?? new IndustryParams();
            System = new IndustrySystem(Table, Layout, Start, P, In, TickRunner.ChunkCountFor(3, 1), new V23CultureRule(), mode, curves);
            Sender = new Sender(System.CreateMailbox(3, Cadence.Monthly));
            Runner = new TickRunner(Table, 1, [System, Sender], 1, threads, Calendar);
        }

        public Inputs In { get; } = new();
        public IndustryLayout Layout { get; }
        public IndustryStart Start { get; }
        public IndustryParams P { get; }
        public Table Table { get; }
        public TickCalendar Calendar { get; }
        public IndustrySystem System { get; }
        public Sender Sender { get; }
        public TickRunner Runner { get; }

        public int Row(int number) => Layout.RowOf(number);

        public void RunThroughMonth(long month)
        {
            long last = Calendar.TickOfMonth(month);
            while (Runner.Tick <= last)
                Runner.RunTick();
        }
    }

    // ---- report_output

    [Fact]
    public void Merge_ReportAddsExperience_NoReportForgets()
    {
        var rig = new Rig();
        rig.Sender.Reports = t => [new OutputReport(1, 7, D(5), D(250))];
        rig.RunThroughMonth(0);
        IndustrySystem s = rig.System;
        Assert.Equal(D(1005), s.ProcX(1, rig.Row(7)));
        Assert.Equal(D(250), s.ProcV(1, rig.Row(7)));
        // No report: proc_v = 0 and X − δ_X·X (contract §4.0 p. 6).
        Assert.Equal(D(1000) - rig.P.DeltaX * D(1000), s.ProcX(0, rig.Row(7)));
        Assert.Equal(Fx6.Zero, s.ProcV(0, rig.Row(7)));
        rig.Sender.Reports = _ => [];
        rig.RunThroughMonth(1);
        Assert.Equal(D(1005) - rig.P.DeltaX * D(1005), s.ProcX(1, rig.Row(7)));
        Assert.Equal(Fx6.Zero, s.ProcV(1, rig.Row(7)));
    }

    [Fact]
    public void Merge_SortsBySiteAndProcess_SumsDuplicates_IndependentOfPostingOrder()
    {
        OutputReport[] reports = [new(2, 7, D(1), D(10)), new(0, 39, D(2), D(20)), new(0, 7, D(3), D(30)), new(2, 7, D(4), D(40))];
        var a = new Rig();
        var b = new Rig();
        a.Sender.Reports = _ => reports;
        b.Sender.Reports = _ => reports.Reverse();
        a.RunThroughMonth(0);
        b.RunThroughMonth(0);
        OutputReport[] expected = [new(0, 7, D(3), D(30)), new(0, 39, D(2), D(20)), new(2, 7, D(5), D(50))];
        Assert.Equal(expected, a.System.LastReports);
        Assert.Equal(expected, b.System.LastReports);
        Assert.Equal(StateHash.ComputeHex(a.Table, 1, 0), StateHash.ComputeHex(b.Table, 1, 0));
        Assert.Equal(D(50), a.System.ProcV(2, a.Row(7)));
    }

    [Fact]
    public void Merge_ReportWithZeroYield_IsAnInvariantViolation()
    {
        // I3: yield = 0 ⇒ no report_output. An unknown process has proc_yield = 0.
        var rig = new Rig();
        rig.In.UnknownProcessRow = rig.Row(40);
        rig.Sender.Reports = _ => [new OutputReport(0, 40, D(1), D(1))];
        Assert.Throws<IndustryInvariantException>(() => rig.RunThroughMonth(0));
    }

    [Fact]
    public void ProcYield_IsZeroForUnknown_YieldQueryStillReportsIt()
    {
        var rig = new Rig();
        rig.In.UnknownProcessRow = rig.Row(7);
        rig.RunThroughMonth(0);
        Assert.Equal(Fx6.Zero, rig.System.ProcYield(0, rig.Row(7)));
        Assert.True(rig.System.LastYield(0, rig.Row(7)).Yield > Fx6.Zero);
        ProductionProfile p = rig.System.Profile(0, [new PrcSetItem(7, Fx6.One, Fx6.One)], 1);
        Assert.False(p.Admitted); // yield > 0 but not known (contract §4.2)
    }

    // ---- reserve_workforce

    [Fact]
    public void Workforce_GrantedVisibleAfterTheMonth_SplitByRequester_ZeroWithoutRequest()
    {
        var rig = new Rig();
        rig.In.Avail = 7;
        rig.Sender.Requests = t => t == 0 ? [new WorkforceRequest(0, 1, 2, 1), new WorkforceRequest(0, 1, 1, 10), new WorkforceRequest(1, 0, 0, 3)] : [];
        rig.RunThroughMonth(0);
        IndustrySystem s = rig.System;
        // Short: floor(10·7/11) = 6, floor(1·7/11) = 0, rest 1 goes to requester 1 first (ascending id), then 2 gets nothing.
        Assert.Equal(7, s.Granted(0, 1, 1));
        Assert.Equal(0, s.Granted(0, 1, 2));
        Assert.Equal(10, s.Requested(0, 1, 1));
        Assert.Equal(3, s.Granted(1, 0, 0));
        rig.RunThroughMonth(1);
        Assert.Equal(0, s.Granted(0, 1, 1));
        Assert.Equal(0, s.Requested(1, 0, 0));
    }

    [Fact]
    public void Workforce_DuplicateRequester_Throws()
    {
        var rig = new Rig();
        rig.Sender.Requests = _ => [new WorkforceRequest(0, 1, 2, 1), new WorkforceRequest(0, 1, 2, 4)];
        Assert.Throws<ArgumentException>(() => rig.RunThroughMonth(0));
    }

    [Fact]
    public void SplitWorkforce_NeverExceedsRequestOrAvailable()
    {
        var granted = new long[3];
        for (long avail = 0; avail < 30; avail++)
        {
            long[] n = [avail % 7, 5, 11];
            IndustryMath.SplitWorkforce(avail, n, granted);
            Assert.True(granted.Sum() <= avail);
            Assert.Equal(Math.Min(avail, n.Sum()), granted.Sum()); // nobody is left idle while someone is short
            for (int i = 0; i < 3; i++)
                Assert.InRange(granted[i], 0, n[i]);
        }
    }

    // ---- tooling_M (spec §3.5 A11)

    [Fact]
    public void CostM_DerivedFromCm_ByFloorOfMClampedTo3To5()
    {
        var p = new IndustryParams();
        Assert.Equal([3, 3, 4, 5, 5], new[] { F(2_000_000), F(3_999_999), D(4), D(5), D(9) }.Select(IndustryMath.CostTier));
        Assert.Equal([F(100_000), F(200_000), F(400_000)], new[] { 3, 4, 5 }.Select(p.CostM));
        Assert.Equal([F(30_000), F(60_000), F(120_000)], new[] { 3, 4, 5 }.Select((new IndustryParams { CM = F(30_000) }).CostM));
        Assert.Throws<ArgumentException>(() => new IndustryParams { CM = Fx6.Zero }.Validate());
    }

    [Fact]
    public void Tooling_InvestOverCostMinusWear_UnderMechCap()
    {
        var rig = new Rig();
        rig.In.Invest = F(10_000);
        rig.RunThroughMonth(0);
        // MECH 48 (tier 4) is admitted: M_cap = 5. MET precision M = 4.2 → k = 4, cost 0.2: 4.2 − 0.021 + 0.05 = 4.229.
        Assert.Equal(F(4_229_000), rig.System.ToolingM(0, 0, Dimension.Precision));
        Assert.Equal(D(5), rig.System.LastTooling(0, 0, Dimension.Precision).MCap);
        // quality_control M = 3.9 → k = 3, cost 0.1: 3.9 − 0.0195 + 0.1 = 3.9805.
        Assert.Equal(F(3_980_500), rig.System.ToolingM(0, 0, Dimension.QualityControl));
    }

    [Fact]
    public void Tooling_WithoutAdmittedMech_CapIsFour_GainCutWearKept()
    {
        // A22: no admitted MECH process → M_cap = 1 + 3 = 4. M = 4.2 above the cap only wears; 3.9 grows to the cap.
        var rig = new Rig();
        rig.In.Invest = F(10_000);
        rig.In.UnknownProcessRow = rig.Row(48);
        rig.RunThroughMonth(0);
        Assert.Equal(D(4), rig.System.LastTooling(0, 0, Dimension.Precision).MCap);
        Assert.Equal(F(4_179_000), rig.System.ToolingM(0, 0, Dimension.Precision));
        Assert.Equal(F(3_980_500), rig.System.ToolingM(0, 0, Dimension.QualityControl));
        rig.In.Invest = F(50_000);
        rig.RunThroughMonth(1);
        Assert.Equal(D(4), rig.System.ToolingM(0, 0, Dimension.QualityControl)); // 3.9606 + 0.5 capped at 4
    }

    // ---- culture_H inputs

    [Fact]
    public void QualityProgramme_BoostsOnlyQualityControl()
    {
        var on = new Rig();
        var off = new Rig();
        on.In.Qp = true;
        on.Sender.Reports = off.Sender.Reports = _ => [new OutputReport(0, 39, D(1), D(100))];
        on.RunThroughMonth(1);
        off.RunThroughMonth(1);
        Assert.Equal(on.System.LastCulture(0, 0, Dimension.Purity), off.System.LastCulture(0, 0, Dimension.Purity));
        CultureStep a = on.System.LastCulture(0, 0, Dimension.QualityControl), b = off.System.LastCulture(0, 0, Dimension.QualityControl);
        Assert.Equal(on.P.Alpha * (Fx6.One + on.P.KQp), a.AlphaEff);
        Assert.True(a.DHPlus > b.DHPlus);
    }

    [Fact]
    public void WorldLead_OnlyAnotherActorsSiteWithYieldAtLeast07()
    {
        var rig = new Rig();
        rig.Sender.Reports = _ => [new OutputReport(2, 7, D(1), D(10)), new OutputReport(1, 39, D(1), D(10))];
        rig.RunThroughMonth(0);
        int p7 = rig.Row(7), p39 = rig.Row(39);
        Assert.True(rig.System.ProcYield(2, p7) >= F(700_000));
        Assert.True(rig.System.WorldLead(0, p7));   // owner 1 ran it, site 0 belongs to actor 0
        Assert.True(rig.System.WorldLead(1, p7));
        Assert.False(rig.System.WorldLead(2, p7));  // own run does not count
        Assert.False(rig.System.WorldLead(0, p39)); // site 1 has the same owner as site 0
    }

    // ---- alloy (spec §1.4, §3.5 A23)

    [Fact]
    public void Alloy_AdmittedMeltWithHighestYield_SupplyIsCapTimesYield_NoReportNeeded()
    {
        var rig = new Rig();
        rig.In.Cap = D(30);
        IndustrySystem s = rig.System;
        Fx6 y39 = s.Yield(0, rig.Row(39), 0).Yield, y40 = s.Yield(0, rig.Row(40), 0).Yield;
        Assert.True(y39 > y40 && y40 > Fx6.Zero);
        Assert.Equal(Fx6.Zero, s.Yield(0, rig.Row(41), 0).Yield); // not admitted: yield 0
        Assert.Equal(new AlloyAvailability(39, D(30) * y39, false), s.Alloy(0, 0));
        rig.In.Deficit = F(500_000);
        Assert.Equal(new AlloyAvailability(39, D(15) * y39, true), s.Alloy(0, 0));
        Assert.Equal(D(15) * y39 / D(3), s.LineCapacity(0, D(3), 0));
        rig.In.UnknownProcessRow = rig.Row(39);
        Assert.Equal(40, s.Alloy(0, 0).GradeNumber);
        rig.In.AllKnown = false;
        Assert.Equal(new AlloyAvailability(-1, Fx6.Zero, true), s.Alloy(0, 0));
        var granted = new Fx6[3];
        IndustryMath.SplitAlloy(D(5), [D(2), D(4), D(1)], granted);
        Assert.Equal([D(2), D(3), Fx6.Zero], granted);
    }

    // ---- act (spec §3.5 A26), capability_tier (A25)

    [Fact]
    public void Act_FromSkilledGrantsOfTheCluster_VisibleNextMonth()
    {
        var rig = new Rig();
        rig.In.Avail = 6;
        // C = 3 clusters: requester 0 and 3 → MET, 1 → CHEM; category 0 (engineer) does not count.
        rig.Sender.Requests = t => t == 0
            ? [new WorkforceRequest(0, 1, 0, 6), new WorkforceRequest(0, 1, 3, 2), new WorkforceRequest(0, 1, 1, 1), new WorkforceRequest(0, 0, 2, 50)]
            : [];
        rig.RunThroughMonth(0);
        // Skilled pool short: 9 asked for 6 → floors 4 (id 0), 0 (id 1), 1 (id 3), rest 1 to id 0 → 5, 0, 1.
        Assert.Equal((5L, 0L, 1L), (rig.System.Granted(0, 1, 0), rig.System.Granted(0, 1, 1), rig.System.Granted(0, 1, 3)));
        Assert.Equal(F(750_000), rig.System.Act(0, 0)); // MET: (5 + 1) / (6 + 2)
        Assert.Equal(Fx6.Zero, rig.System.Act(0, 1));  // CHEM: 0 / 1
        Assert.Equal(Fx6.One, rig.System.Act(0, 2)); // MECH: no request
        Assert.Equal(Fx6.One, rig.System.Act(1, 0)); // other site
        rig.RunThroughMonth(1);
        Assert.Equal(Fx6.One, rig.System.Act(0, 0)); // month 1 made no request
    }

    [Fact]
    public void CapabilityTier_MinOverClusterKeyDims()
    {
        var rig = new Rig();
        // MET: 39 (tier 4) purity; 40 (tier 4) purity, qc; 41 (tier 5) purity → {purity, qc}.
        Assert.Equal(0b110, rig.Layout.ClusterKeyDims[0]);
        Assert.Equal(0b011, rig.Layout.ClusterKeyDims[1]); // CHEM 7 (tier 3): precision 3, purity 3; qc 2.5 < 3
        Assert.Equal(0b001, rig.Layout.ClusterKeyDims[2]); // MECH 48 (tier 4): precision 4
        Assert.Null(IndustryMath.CapabilityTier(default, default, 0)); // cluster without PRC
        Assert.Equal(F(3_700_000), rig.System.CapabilityTier(0, 0)); // min over purity 3.8, qc 3.7 (H ≤ M)
    }

    // ---- controls (spec §6.1)

    [Fact]
    public void Lc0_YieldAndCostOne_CultureAndToolingFrozen()
    {
        var rig = new Rig(IndustryMode.ConstLc0);
        rig.In.Invest = F(10_000);
        rig.In.UnknownProcessRow = rig.Row(41);
        rig.Sender.Reports = _ => [new OutputReport(0, 39, D(1), D(100))];
        rig.RunThroughMonth(3);
        Assert.Equal(Fx6.One, rig.System.ProcYield(0, rig.Row(39)));
        Assert.Equal(Fx6.Zero, rig.System.ProcYield(0, rig.Row(41)));
        Assert.Equal(rig.Start.H[rig.Start.Cell(0, 0, 0)], rig.System.CultureH(0, 0, Dimension.Precision));
        Assert.Equal(rig.Start.M[rig.Start.Cell(0, 0, 0)], rig.System.ToolingM(0, 0, Dimension.Precision));
        ProductionProfile p = rig.System.Profile(0, [new PrcSetItem(39, D(2), D(3)), new PrcSetItem(41, D(1), D(1))], 4);
        Assert.Equal(new ProductionProfile(false, Fx6.One, Fx6.One, 39, null, BottleneckSource.Control), p);
    }

    [Fact]
    public void Lc_CurveByTierSinceKnown_WeightedUnitCost()
    {
        var t4 = new CapabilityCurve([0, 10], [F(100_000), F(600_000)], [D(5), D(2)]);
        var t5 = new CapabilityCurve([0, 20], [Fx6.Zero, F(400_000)], [D(9), D(3)]);
        var t3 = new CapabilityCurve([0], [Fx6.One], [Fx6.One]);
        var rig = new Rig(IndustryMode.CurveLc, [t3, t4, t5]);
        rig.In.KnownAt = 2;
        rig.RunThroughMonth(6);
        // month 6: τ = 4 → tier 4: 0.1 + (0.5·4)/10 = 0.3; tier 5: (0.4·4)/20 = 0.08.
        Assert.Equal(F(300_000), rig.System.ProcYield(0, rig.Row(39)));
        Assert.Equal(F(80_000), rig.System.ProcYield(0, rig.Row(41)));
        Assert.Equal(Fx6.One, rig.System.ProcYield(0, rig.Row(7)));
        ProductionProfile p = rig.System.Profile(0, [new PrcSetItem(39, Fx6.One, Fx6.One), new PrcSetItem(41, Fx6.One, Fx6.One)], 6);
        // U: tier 4 5 + (−3·4)/10 = 3.8; tier 5 9 + (−6·4)/20 = 7.8; weights equal → 5.8.
        Assert.Equal(new ProductionProfile(true, F(5_800_000), Fx6.One, 41, null, BottleneckSource.Control), p);
        Assert.Equal(F(600_000), t4.YieldAt(400)); // clamped after the last knot
    }

    [Fact]
    public void Lc_WithoutCurves_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Rig(IndustryMode.CurveLc));
    }

    // ---- profile through the system

    [Fact]
    public void Profile_FromSnapshot_MatchesMath()
    {
        var rig = new Rig();
        rig.RunThroughMonth(0);
        IndustrySystem s = rig.System;
        ProductionProfile p = s.Profile(0, [new PrcSetItem(40, D(2), D(3)), new PrcSetItem(39, D(1), D(5))], 1);
        ProductionProfile expected = IndustryMath.Profile(
            [new ProfileItem(39, s.Yield(0, rig.Row(39), 1), true, D(1), D(5)), new ProfileItem(40, s.Yield(0, rig.Row(40), 1), true, D(2), D(3))],
            rig.P.Eps);
        Assert.Equal(expected, p);
        Assert.Equal(40, p.BottleneckProcess);
        Assert.Equal(Dimension.Purity, p.BottleneckDim);
    }

    [Fact]
    public void Layout_RejectsUnknownClusterDuplicateNumberNoKeyDimAndBadSkilled()
    {
        Assert.Throws<ArgumentException>(() => IndustryLayout.Create([Cluster.MET],
            [new IndustryProcessSpec(1, Cluster.CHEM, 0, 3, new Dims3(D(3), Fx6.Zero, Fx6.Zero))], [0], 1, 0, 0));
        Assert.Throws<ArgumentException>(() => IndustryLayout.Create([Cluster.MET],
            [new IndustryProcessSpec(1, Cluster.MET, 0, 3, new Dims3(D(3), Fx6.Zero, Fx6.Zero)), new IndustryProcessSpec(1, Cluster.MET, 0, 3, new Dims3(D(3), Fx6.Zero, Fx6.Zero))], [0], 1, 0, 0));
        // A25: a process needs a key dimension (r_d ≥ tier).
        Assert.Throws<ArgumentException>(() => IndustryLayout.Create([Cluster.MET],
            [new IndustryProcessSpec(1, Cluster.MET, 0, 4, new Dims3(F(3_500_000), Fx6.Zero, Fx6.Zero))], [0], 1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => IndustryLayout.Create([Cluster.MET],
            [new IndustryProcessSpec(1, Cluster.MET, 0, 3, new Dims3(D(3), Fx6.Zero, Fx6.Zero))], [0], 1, 1, 0));
    }

    // ---- slice data

    [Fact]
    public void SliceStart_FromData()
    {
        GameData data = ModLoader.Load(TestMods.CoreDir);
        ScenarioData sc = data.Scenario("jet_slice_1945");
        var p = new IndustryParams();
        IndustryLayout l = IndustryLayout.FromData(data.Nodes, sc, workforceCategories: 2, requesters: 15);
        Assert.Equal(20, l.ProcessCount);
        Assert.Equal(sc.ActorCount, l.SiteCount);
        Assert.Equal([39, 40, 41], l.AlloyGrades.Select(r => l.Number[r]));
        IndustryStart st = IndustryStart.FromScenario(sc, data.Nodes, l, p);
        int usa = sc.ActorIndex("USA"), met = l.Clusters.IndexOf(Cluster.MET);
        Assert.Equal(sc.CultureOf(usa, Cluster.MET, Dimension.Purity), st.H[st.Cell(usa, met, 1)]);
        Assert.Equal(sc.ToolingOf(usa, Cluster.MET, Dimension.QualityControl), st.M[st.Cell(usa, met, 2)]);
        Assert.Equal(F(3_900_000), st.M[st.Cell(usa, met, 2)]); // A20: H_qc 3.7 + 0.2, per cell
        int melt = l.RowOf(39); // nicr_alloy_melt: mastered by USA, tier 4 → 3·800
        Assert.Equal(D(2400), st.X[usa * l.ProcessCount + melt]);
        Assert.Equal(Fx6.Zero, st.X[sc.ActorIndex("SWE") * l.ProcessCount + melt]);
    }
}
