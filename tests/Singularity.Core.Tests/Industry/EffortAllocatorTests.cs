using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Industry;

/// <summary>Distribution of the process-development item (contract v2.4 §4.1b, G4-5, G4-6, §4.1a site split).</summary>
public class EffortAllocatorTests
{
    private static readonly CbCultureRule Rule = new();

    private static Fx6 D(long u) => Fx6.FromInt(u);

    private static Fx6 F(long raw) => Fx6.FromRaw(raw);

    private static Dims3 Req(long prec, long pur, long qc) => new(F(prec), F(pur), F(qc));

    private static readonly int[] NoFocus = [-1, -1, -1];

    // Three independent clusters' worth of processes, each the sole process of its cell (purity r_d 3.5, 4.0, 4.5).
    private static CbFake ThreeProcesses(int[]? owners = null)
    {
        IndustryLayout l = CbFake.MakeLayout(owners ?? [0],
            (10, Cluster.MET, Req(0, 3_500_000, 0)), (11, Cluster.CHEM, Req(0, 4_000_000, 0)), (12, Cluster.MECH, Req(0, 4_500_000, 0)));
        var s = new CbFake(l);
        for (int site = 0; site < l.SiteCount; site++)
        {
            foreach (int r in l.Order)
            {
                s.Vs[site * l.ProcessCount + r] = D(10);
                s.Vexp[site * l.ProcessCount + r] = D(10);
                s.PlannedF[site * l.ProcessCount + r] = true;
            }
        }
        return s;
    }

    private static Fx6 Sum(EffortAllocation a) => a.E.Aggregate(Fx6.Zero, (x, y) => x + y);

    [Fact]
    public void Budget_IsConservedExactly_AndEveryShareIsWithinEUseful()
    {
        var rng = new Random(31);
        for (int i = 0; i < 300; i++)
        {
            CbFake s = ThreeProcesses();
            foreach (int r in s.Layout.Order)
                s.SetH(0, s.Layout.ClusterIndex[r], 1, F(rng.Next(0, 4_300_000)));
            Fx6 budget = F(rng.Next(0, 3 * (int)s.Params.EFull.Raw + 500));
            EffortAllocation a = EffortAllocator.Allocate(Rule, s, 0, 0, budget, NoFocus);
            Assert.Equal(budget, Sum(a) + a.Rest);
            foreach (int r in s.Layout.Order)
            {
                Assert.True(a.E[r] >= Fx6.Zero);
                Assert.True(a.E[r] <= Rule.EUseful(s, 0, r, 0));
            }
            int candidates = s.Layout.Order.Count(r => Rule.EUseful(s, 0, r, 0) > Fx6.Zero);
            Assert.True(a.Passes <= candidates);
        }
    }

    [Fact]
    public void MultP_DoublesTheWeightOfItsProcess_LastFactor_AndOneChangesNothing()
    {
        CbFake s = ThreeProcesses();
        foreach (int r in s.Layout.Order)
            s.SetH(0, s.Layout.ClusterIndex[r], 1, Fx6.Zero);   // the same gap in the three cells (a gap makes a weight), a budget below the sum of E_useful
        Fx6 budget = Rule.EUseful(s, 0, s.Layout.Order[0], 0) / Fx6.FromInt(2);
        EffortAllocation plain = EffortAllocator.Allocate(Rule, s, 0, 0, budget, NoFocus);
        s.Mult = _ => Fx6.One;
        Assert.Equal(plain.E, EffortAllocator.Allocate(Rule, s, 0, 0, budget, NoFocus).E);
        int first = s.Layout.Order[0];
        s.Mult = r => r == first ? Fx6.FromInt(2) : Fx6.One;
        EffortAllocation boosted = EffortAllocator.Allocate(Rule, s, 0, 0, budget, NoFocus);
        Assert.Equal(budget, Sum(boosted) + boosted.Rest);
        Assert.True(boosted.E[first] > plain.E[first]);
        foreach (int r in s.Layout.Order.Skip(1))
            Assert.True(boosted.E[r] <= plain.E[r]);
    }

    [Fact]
    public void BudgetAboveAllEUseful_TheRemainderGoesToTooling()
    {
        CbFake s = ThreeProcesses();
        Fx6 total = s.Layout.Order.Aggregate(Fx6.Zero, (x, r) => x + Rule.EUseful(s, 0, r, 0));
        Assert.True(total > Fx6.Zero);
        EffortAllocation a = EffortAllocator.Allocate(Rule, s, 0, 0, total + D(5), NoFocus);
        Assert.Equal(total, Sum(a));
        Assert.Equal(D(5), a.Rest);
    }

    [Fact]
    public void Passes_RedistributeWhatACappedProcessCannotTake()
    {
        CbFake s = ThreeProcesses();
        // Process 10 has little output (small E_useful) but a large planned volume (large weight): it caps in pass 1.
        s.Vs[s.P(10)] = D(3);
        s.Vexp[s.P(10)] = D(1000);
        Fx6 eu10 = Rule.EUseful(s, 0, s.P(10), 0);
        Fx6 eu11 = Rule.EUseful(s, 0, s.P(11), 0), eu12 = Rule.EUseful(s, 0, s.P(12), 0);
        Assert.True(eu10 > Fx6.Zero && eu10 < eu11);
        Fx6 budget = eu10 + eu11 + eu12 - F(10);
        EffortAllocation a = EffortAllocator.Allocate(Rule, s, 0, 0, budget, NoFocus);
        Assert.Equal(eu10, a.E[s.P(10)]);
        Assert.True(a.Passes >= 2);
        // Everything but floor crumbs is out of the tooling rest.
        Assert.True(a.Rest.Raw < 4);
    }

    [Fact]
    public void Pass_SharesAreTakenFromTheBudgetAtTheStartOfThePass_OrderInsideDoesNotMatter()
    {
        // Same snapshot with the process storage reversed gives the same E per process number.
        CbFake s = ThreeProcesses();
        foreach (int r in s.Layout.Order)
            s.SetH(0, s.Layout.ClusterIndex[r], 1, F(2_000_000 + 400_000 * r));
        Fx6 budget = F(2_000);
        EffortAllocation a = EffortAllocator.Allocate(Rule, s, 0, 0, budget, NoFocus);

        IndustryLayout rev = IndustryLayout.Create([.. s.Layout.Clusters],
            [.. s.Layout.Order.Reverse().Select(r => new IndustryProcessSpec(s.Layout.Number[r], s.Layout.Clusters[s.Layout.ClusterIndex[r]], 0, 3, s.Layout.Req[r], Fx6.One))],
            [0], 2, 1, 4);
        var s2 = new CbFake(rev);
        for (int i = 0; i < s2.Hs.Length; i++)
            s2.Hs[i] = s.Hs[i];
        foreach (int r in s.Layout.Order)
        {
            int r2 = rev.RowOf(s.Layout.Number[r]);
            s2.Vs[r2] = s.Vs[r];
            s2.Vexp[r2] = s.Vexp[r];
            s2.PlannedF[r2] = true;
        }
        EffortAllocation b = EffortAllocator.Allocate(Rule, s2, 0, 0, budget, NoFocus);
        foreach (int r in s.Layout.Order)
            Assert.Equal(a.E[r], b.E[rev.RowOf(s.Layout.Number[r])]);
        Assert.Equal(a.Rest, b.Rest);
    }

    [Fact]
    public void Allocation_IsAPureFunction_AndIgnoresLastMonthsProcE()
    {
        CbFake s = ThreeProcesses();
        EffortAllocation a = EffortAllocator.Allocate(Rule, s, 0, 0, F(1_000), NoFocus);
        EffortAllocation again = EffortAllocator.Allocate(Rule, s, 0, 0, F(1_000), NoFocus);
        Assert.Equal(a.E, again.E);
        for (int r = 0; r < s.Layout.ProcessCount; r++)
            s.Es[r] = F(12_345 + r);
        EffortAllocation changed = EffortAllocator.Allocate(Rule, s, 0, 0, F(1_000), NoFocus);
        Assert.Equal(a.E, changed.E);
        Assert.Equal(a.Rest, changed.Rest);
    }

    [Fact]
    public void Weights_FollowPlannedVolumeAndGap()
    {
        // Equal gaps: the process with the larger planned volume gets the larger share.
        CbFake s = ThreeProcesses();
        foreach (int r in s.Layout.Order)
            s.SetH(0, s.Layout.ClusterIndex[r], 1, D(1));
        s.Vexp[s.P(10)] = D(5);
        s.Vexp[s.P(11)] = D(20);
        EffortAllocation a = EffortAllocator.Allocate(Rule, s, 0, 0, F(300), NoFocus);
        Assert.True(a.E[s.P(11)] > a.E[s.P(10)]);
    }

    // ---- player focus

    [Fact]
    public void Focus_IsServedFirst_InOrder_BeforeTheAiFill()
    {
        CbFake s = ThreeProcesses();
        Fx6 eu12 = Rule.EUseful(s, 0, s.P(12), 0);
        Fx6 eu11 = Rule.EUseful(s, 0, s.P(11), 0);
        Fx6 budget = eu12 + eu11 / D(2);
        EffortAllocation a = EffortAllocator.Allocate(Rule, s, 0, 0, budget, [12, 11, -1]);
        Assert.Equal(eu12, a.E[s.P(12)]);
        Assert.Equal(eu11 / D(2), a.E[s.P(11)]);
        Assert.Equal(Fx6.Zero, a.E[s.P(10)]);
        Assert.Equal(Fx6.Zero, a.Rest);
    }

    [Fact]
    public void Focus_BeyondEUsefulGivesNothing_ProcessWithoutLeadGetsZero()
    {
        // Process 13 shares the purity cell with 10 but has a smaller t̂: not the lead, so focus on it yields 0.
        IndustryLayout l = CbFake.MakeLayout([0], (10, Cluster.MET, Req(0, 4_500_000, 0)), (13, Cluster.MET, Req(0, 3_500_000, 0)));
        var s = new CbFake(l);
        foreach (int r in l.Order)
        {
            s.Vs[r] = D(10);
            s.Vexp[r] = D(10);
        }
        Assert.Equal(s.P(10), CbCultureRule.Lead(s, 0, 0, 1, 0));
        EffortAllocation a = EffortAllocator.Allocate(Rule, s, 0, 0, F(1_000), [13, -1, -1]);
        Assert.Equal(Fx6.Zero, a.E[s.P(13)]);
        Assert.Equal(Fx6.Zero, Rule.EUseful(s, 0, s.P(13), 0));
        Assert.Equal(F(1_000), Sum(a) + a.Rest);
        // Focus beyond E_useful of the lead: the surplus is not taken.
        EffortAllocation b = EffortAllocator.Allocate(Rule, s, 0, 0, s.Params.EFull * D(5), [10, -1, -1]);
        Assert.Equal(Rule.EUseful(s, 0, s.P(10), 0), b.E[s.P(10)]);
        Assert.Equal(s.Params.EFull * D(5), Sum(b) + b.Rest);
    }

    [Fact]
    public void Focus_RepeatOrUnknownProcess_IsRejected()
    {
        CbFake s = ThreeProcesses();
        Assert.Throws<ArgumentException>(() => EffortAllocator.Allocate(Rule, s, 0, 0, F(10), [10, 11, 10]));
        Assert.Throws<ArgumentException>(() => EffortAllocator.Allocate(Rule, s, 0, 0, F(10), [99, -1, -1]));
        // Several empty slots are fine.
        EffortAllocator.Allocate(Rule, s, 0, 0, F(10), [-1, -1, -1]);
    }

    [Fact]
    public void Focus_Player_SameStepsAsAi_ForTheRest()
    {
        // With a focus covering a process fully, the others get what the AI would give them from the remainder.
        CbFake s = ThreeProcesses();
        Fx6 eu10 = Rule.EUseful(s, 0, s.P(10), 0);
        Fx6 budget = eu10 + F(900);
        EffortAllocation withFocus = EffortAllocator.Allocate(Rule, s, 0, 0, budget, [10, -1, -1]);
        EffortAllocation ai = EffortAllocator.Allocate(Rule, s, 0, 0, F(900), NoFocus);
        // AI on the remainder may still give process 10 something; the focus run gave 10 its full E_useful first.
        Assert.Equal(eu10, withFocus.E[s.P(10)]);
        Assert.Equal(budget, Sum(withFocus) + withFocus.Rest);
        Assert.Equal(F(900), Sum(ai) + ai.Rest);
    }

    [Fact]
    public void NoMoneyToProcessesWhoseLimiterIsNotEffort()
    {
        CbFake s = ThreeProcesses();
        s.Ms[s.Idx(0, s.Layout.ClusterIndex[s.P(10)], 1)] = D(1); // TOOLING for 10
        s.HcapByDim[1] = F(4_200_000); // SATURATION for 12 (r = 4.5) only
        EffortAllocation a = EffortAllocator.Allocate(Rule, s, 0, 0, s.Params.EFull * D(5), [10, 12, -1]);
        Assert.Equal(Fx6.Zero, a.E[s.P(10)]);
        Assert.Equal(Fx6.Zero, a.E[s.P(12)]);
        Assert.True(a.E[s.P(11)] > Fx6.Zero);
    }

    // ---- site split (§4.1a): actor 0 owns sites 0 and 1, actor 1 owns site 2

    [Fact]
    public void SiteSplit_ByOutput_RemainderToTheLargestSite_CappedByEUseful_ExcessToRest()
    {
        CbFake s = ThreeProcesses([0, 0, 1]);
        int n = s.Layout.ProcessCount;
        int p = s.P(11);
        s.Vs[0 * n + p] = D(10);
        s.Vs[1 * n + p] = D(30);
        s.Vs[2 * n + p] = D(99); // another actor: never gets money of actor 0
        foreach (int r in s.Layout.Order)
        {
            if (r != p)
            {
                s.Vs[r] = Fx6.Zero;
                s.Vs[n + r] = Fx6.Zero;
                s.PlannedF[r] = false;
                s.PlannedF[n + r] = false;
            }
        }
        Fx6 euTotal = Rule.EUseful(s, 0, p, 0) + Rule.EUseful(s, 1, p, 0);
        Fx6 budget = euTotal - F(3);
        EffortAllocation a = EffortAllocator.Allocate(Rule, s, 0, 0, budget, NoFocus);
        Assert.Equal(Fx6.Zero, a.E[2 * n + p]);
        Assert.Equal(budget, Sum(a) + a.Rest);
        Assert.True(a.E[1 * n + p] > a.E[0 * n + p]);
        Assert.True(a.E[0 * n + p] <= Rule.EUseful(s, 0, p, 0));
        Assert.True(a.E[1 * n + p] <= Rule.EUseful(s, 1, p, 0));

        // Equal output: floor remainder goes to the lower site id (tie), total preserved.
        s.Vs[0 * n + p] = D(10);
        s.Vs[1 * n + p] = D(10);
        EffortAllocation b = EffortAllocator.Allocate(Rule, s, 0, 0, F(7), NoFocus);
        Assert.Equal(F(7), Sum(b) + b.Rest);
        Assert.True(b.E[0 * n + p] >= b.E[1 * n + p]);

        // Excess over a site's E_useful returns to the actor's rest instead of moving to the other site.
        s.Vs[0 * n + p] = D(1);
        s.Vs[1 * n + p] = D(1000);
        s.SetH(0, s.Layout.ClusterIndex[p], 1, F(3_950_000)); // site 0: tiny E_useful
        EffortAllocation c = EffortAllocator.Allocate(Rule, s, 0, 0, euTotal, NoFocus);
        Assert.Equal(euTotal, Sum(c) + c.Rest);
        Assert.True(c.E[0 * n + p] <= Rule.EUseful(s, 0, p, 0));
    }

    [Fact]
    public void SiteSplit_ZeroOutput_AllToTheLowestSite()
    {
        CbFake s = ThreeProcesses([0, 0, 1]);
        int n = s.Layout.ProcessCount;
        int p = s.P(11);
        foreach (int r in s.Layout.Order)
        {
            s.Vs[r] = Fx6.Zero;
            s.Vs[n + r] = Fx6.Zero;
        }
        // Planned at both sites, so both are eligible (E_useful by the laboratory part).
        s.PlannedF[p] = s.PlannedF[n + p] = true;
        EffortAllocation a = EffortAllocator.Allocate(Rule, s, 0, 0, F(50), NoFocus);
        Assert.Equal(Fx6.Zero, a.E[n + p]);
        Assert.Equal(F(50), Sum(a) + a.Rest);
    }
}
