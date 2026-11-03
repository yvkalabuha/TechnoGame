using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Industry;

/// <summary>
/// Property tests of the C-B rule and the effort allocation on random snapshots (seeded test-side RNG; contract v2.4 r2 §4.1,
/// §4.1a, §4.1b): <c>D_a = ΣE + rest</c>, <c>rest ≥ 0</c>, <c>E ≤ E_useful</c>, only the lead of a cell with <c>limiter₀ = EFFORT</c> takes
/// money, effort acts in every active dimension of the process, and the whole thing is monotone and deterministic.
/// </summary>
public class CbEffortPropertyTests
{
    private static readonly CbCultureRule Rule = new();
    private static readonly int[] NoFocus = [-1, -1, -1];
    private static readonly Cluster[] Pool = [Cluster.MET, Cluster.CHEM, Cluster.MECH, Cluster.AERO];

    private static Fx6 F(long raw) => Fx6.FromRaw(raw);

    private static CbFake Random(Random rng, out int actors)
    {
        int nClusters = rng.Next(1, 4), nProcs = rng.Next(1, 9);
        var clusters = Pool.OrderBy(_ => rng.Next()).Take(nClusters).Order().ToArray();
        var procs = new List<IndustryProcessSpec>();
        for (int i = 0; i < nProcs; i++)
        {
            Cluster c = clusters[rng.Next(clusters.Length)];
            long Dim(int chance) => rng.Next(100) < chance ? 0 : rng.Next(500_000, 5_000_000);
            var req = new Dims3(F(Dim(40)), F(Dim(20)), F(Dim(40)));
            if (req.Precision == Fx6.Zero && req.Purity == Fx6.Zero && req.QualityControl == Fx6.Zero)
                req = new Dims3(F(rng.Next(3_000_000, 4_000_000)), Fx6.Zero, Fx6.Zero);
            Fx6 key = F(rng.Next(3_000_000, 5_000_000));   // a key dimension r_d >= tier 3 (spec A25)
            int kd = rng.Next(3);
            req = new Dims3(kd == 0 ? key : req.Precision, kd == 1 ? key : req.Purity, kd == 2 ? key : req.QualityControl);
            procs.Add(new IndustryProcessSpec(10 + i, c, 0, 3, req, F(rng.Next(500_000, 2_000_000))));
        }
        IndustryProcessSpec[] ordered = [.. procs.OrderBy(x => x.Number)];
        int[] owners = rng.Next(2) == 0 ? [0] : [0, 0, 1];
        actors = owners.Max() + 1;
        IndustryLayout l = IndustryLayout.Create(clusters, ordered, owners, 2, 1, 4);
        var s = new CbFake(l, new IndustryParams());
        for (int d = 0; d < 3; d++)
            s.HcapByDim[d] = F(rng.Next(2_000_000, 6_000_000));
        for (int i = 0; i < s.Hs.Length; i++)
        {
            s.Hs[i] = F(rng.Next(0, 5_000_000));
            s.Ms[i] = F(rng.Next(0, 6_000_000));
        }
        for (int i = 0; i < s.Vs.Length; i++)
        {
            s.Vs[i] = rng.Next(3) == 0 ? Fx6.Zero : F(rng.Next(0, 20_000_000));
            s.Es[i] = rng.Next(3) == 0 ? F(rng.Next(0, 300_000)) : Fx6.Zero;
            s.Vexp[i] = F(rng.Next(0, 15_000_000));
            s.Known[i] = rng.Next(8) != 0;
            s.PlannedF[i] = rng.Next(2) == 0;
            s.Lic[i] = rng.Next(4) == 0;
            s.Lead[i] = rng.Next(4) == 0;
        }
        s.ActValue = F(rng.Next(0, 1_000_001));
        s.Qp = rng.Next(4) == 0;
        return s;
    }

    private static int[] RandomFocus(Random rng, IndustryLayout l)
    {
        var focus = new int[] { -1, -1, -1 };
        var pick = Enumerable.Range(0, l.ProcessCount).OrderBy(_ => rng.Next()).ToArray();
        for (int k = 0; k < 3; k++)
            focus[k] = rng.Next(3) == 0 || k >= pick.Length ? -1 : l.Number[pick[k]];
        return focus;
    }

    [Fact]
    public void Allocation_Random_ConservesBudget_RestNonNegative_OnlyEffortLeadsGetMoney()
    {
        var rng = new Random(20261103);
        int withMoney = 0, withRest = 0;
        for (int i = 0; i < 400; i++)
        {
            CbFake s = Random(rng, out int actors);
            IndustryLayout l = s.Layout;
            for (int a = 0; a < actors; a++)
            {
                Fx6 budget = F(rng.Next(0, 4) == 0 ? 0 : rng.Next(0, 2_500_000));
                int[] focus = RandomFocus(rng, l);
                EffortAllocation x = EffortAllocator.Allocate(Rule, s, a, 0, budget, focus);
                EffortAllocation y = EffortAllocator.Allocate(Rule, s, a, 0, budget, focus);
                Assert.Equal(x.E, y.E);               // pure function of the snapshot
                Assert.Equal(x.Rest, y.Rest);
                Fx6 sum = x.E.Aggregate(Fx6.Zero, (p, q) => p + q);
                Assert.True(x.Rest >= Fx6.Zero, $"case {i}: negative rest {x.Rest}");
                Assert.Equal(budget, sum + x.Rest);   // D_a = ΣE + returned/for tooling
                for (int site = 0; site < l.SiteCount; site++)
                {
                    foreach (int r in l.Order)
                    {
                        Fx6 e = x.E[site * l.ProcessCount + r];
                        Assert.True(e >= Fx6.Zero);
                        if (l.SiteOwner[site] != a)
                        {
                            Assert.Equal(Fx6.Zero, e);
                            continue;
                        }
                        Assert.True(e <= Rule.EUseful(s, site, r, 0), $"case {i}: E above E_useful");
                        if (e == Fx6.Zero)
                            continue;
                        withMoney++;
                        // Money only to the lead of a cell it leads, with limiter₀ = EFFORT in d* (G4-2).
                        int d = CbCultureRule.DStar(s, site, r, 0);
                        Assert.True(d >= 0);
                        int c = l.ClusterIndex[r];
                        Assert.Equal(r, CbCultureRule.Lead(s, site, c, d, 0));
                        Assert.Equal(LimiterKind.Effort, CbCultureRule.Limiter0(s, site, r, d, 0));
                        Assert.True(CbCultureRule.Eligible(s, site, r, 0));
                    }
                }
                if (x.Rest > Fx6.Zero)
                    withRest++;
            }
        }
        Assert.True(withMoney > 100 && withRest > 100, $"degenerate generator: money {withMoney}, rest {withRest}");
    }

    [Fact]
    public void Lead_Random_IsEligibleEffortCandidate_AndUniquePerCell()
    {
        var rng = new Random(77);
        int leads = 0;
        for (int i = 0; i < 400; i++)
        {
            CbFake s = Random(rng, out _);
            IndustryLayout l = s.Layout;
            for (int site = 0; site < l.SiteCount; site++)
            {
                for (int c = 0; c < l.Clusters.Length; c++)
                {
                    for (int d = 0; d < 3; d++)
                    {
                        int lead = CbCultureRule.Lead(s, site, c, d, 0);
                        Assert.Equal(lead, CbCultureRule.Lead(s, site, c, d, 0));
                        if (lead < 0)
                            continue;
                        leads++;
                        Assert.Equal(c, l.ClusterIndex[lead]);
                        Assert.True(l.Req[lead][d] > Fx6.Zero);
                        Assert.True(CbCultureRule.Eligible(s, site, lead, 0));
                        Assert.Equal(LimiterKind.Effort, CbCultureRule.Limiter0(s, site, lead, d, 0));
                        // Ties and order: no other candidate has the same row.
                        int led = l.Order.Count(r => l.ClusterIndex[r] == c && CbCultureRule.Lead(s, site, c, d, 0) == r);
                        Assert.Equal(1, led);
                    }
                }
            }
        }
        Assert.True(leads > 200, $"degenerate generator: {leads} leads");
    }

    [Fact]
    public void Effort_ActsInEveryActiveDimension_ForAProcessWithoutOutput()
    {
        // One process, v = 0, E > 0: the laboratory part ℓ·gapL (G-5, #99) drives every active dimension with gapL > 0 and
        // no inactive one; without money the drive is 0 (v = 0, E = 0 → the process is not in P(c,d) at all).
        var rng = new Random(5);
        int tested = 0;
        for (int i = 0; i < 300; i++)
        {
            Dims3 req = new(F(rng.Next(0, 3) == 0 ? 0 : rng.Next(1_000_000, 5_000_000)), F(rng.Next(0, 3) == 0 ? 0 : rng.Next(1_000_000, 5_000_000)),
                F(rng.Next(0, 3) == 0 ? 0 : rng.Next(1_000_000, 5_000_000)));
            int kd = rng.Next(3);
            Fx6 key = F(rng.Next(3_000_000, 5_000_000));
            req = new Dims3(kd == 0 ? key : req.Precision, kd == 1 ? key : req.Purity, kd == 2 ? key : req.QualityControl);
            IndustryLayout l = CbFake.MakeLayout([0], (10, Cluster.MET, req));
            var s = new CbFake(l);
            IndustryParams p = s.Params;
            for (int d = 0; d < 3; d++)
                s.Hs[s.Idx(0, 0, d)] = F(rng.Next(0, 5_000_000));
            s.Es[0] = F(rng.Next(1, 1_000_000)) + p.EFull;     // E ≥ E_full → x_E = 1, min(x_E, ℓ) = ℓ
            for (int d = 0; d < 3; d++)
            {
                CultureStep withE = Rule.Step(s, 0, 0, d, 0);
                Fx6 e = s.Es[0];
                s.Es[0] = Fx6.Zero;
                CultureStep without = Rule.Step(s, 0, 0, d, 0);
                s.Es[0] = e;
                Assert.Equal(Fx6.Zero, without.Drive);
                bool active = req[d] > Fx6.Zero;
                Fx6 gapL = req[d] - p.DeltaL - s.Hs[s.Idx(0, 0, d)];
                if (!active)
                {
                    Assert.Equal(Fx6.Zero, withE.Drive);
                    continue;
                }
                tested++;
                Assert.Equal(p.Ell > Fx6.Zero && gapL > Fx6.Zero, withE.Drive > Fx6.Zero);
            }
        }
        Assert.True(tested > 200);
    }

    [Fact]
    public void Drive_IsMonotoneInEffort_AndInVolume_Random()
    {
        var rng = new Random(9);
        for (int i = 0; i < 300; i++)
        {
            CbFake s = Random(rng, out _);
            IndustryLayout l = s.Layout;
            int r = rng.Next(l.ProcessCount), c = l.ClusterIndex[r];
            for (int d = 0; d < 3; d++)
            {
                if (l.Req[r][d] <= Fx6.Zero)
                    continue;
                Fx6 prev = Fx6.Zero;
                for (int k = 0; k <= 6; k++)
                {
                    Fx6 dh = Rule.DHForecast(s, 0, r, d, F(k * s.Params.EFull.Raw / 6), s.Vs[r], 0);
                    Assert.True(dh >= prev, $"case {i}: dH_forecast falls with E");
                    prev = dh;
                }
                Fx6 prevV = Fx6.Zero;
                for (int k = 0; k <= 6; k++)
                {
                    Fx6 dh = Rule.DHForecast(s, 0, r, d, F(s.Params.EFull.Raw / 2), F(k * 4_000_000), 0);
                    Assert.True(dh >= prevV, $"case {i}: dH_forecast falls with v");
                    prevV = dh;
                }
                CultureStep st = Rule.Step(s, 0, c, d, 0);
                Assert.True(st.HNew >= Fx6.Zero && st.DHPlusCapped <= s.Params.RhoMax && st.DHPlusCapped >= Fx6.Zero);
            }
        }
    }
}
