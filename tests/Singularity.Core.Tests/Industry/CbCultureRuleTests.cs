using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Industry;

/// <summary>Culture rule C-B (contract v2.4 §4.1, §4.1a): invariants of the cell step and of the rule functions.</summary>
public class CbCultureRuleTests
{
    private const int Prec = 0, Pur = 1;

    private static Fx6 D(long u) => Fx6.FromInt(u);

    private static Fx6 F(long raw) => Fx6.FromRaw(raw);

    private static readonly CbCultureRule Rule = new();

    private static Dims3 Req(long prec, long pur, long qc) => new(F(prec), F(pur), F(qc));

    // ---- G4-1: the effort of a process acts in every active dimension

    [Fact]
    public void Effort_ActsInAllActiveDimensions_NotOnlyWhereTheProcessLeads()
    {
        // p (10): precision 4, purity 3.5. q (11): purity 4.5 only, so q leads the purity cell, p leads precision.
        IndustryLayout l = CbFake.MakeLayout([0], (10, Cluster.MET, Req(4_000_000, 3_500_000, 0)), (11, Cluster.MET, Req(0, 4_500_000, 0)));
        var s = new CbFake(l);
        int p = s.P(10), q = s.P(11);
        s.Vs[p] = D(10);
        s.Vs[q] = D(10);
        Assert.Equal(q, CbCultureRule.Lead(s, 0, 0, Pur, 0));
        Assert.Equal(p, CbCultureRule.Lead(s, 0, 0, Prec, 0));

        Fx6 noMoney = Rule.Step(s, 0, 0, Pur, 0).DHPlus;
        s.Es[p] = s.Params.EFull;
        CultureStep withMoney = Rule.Step(s, 0, 0, Pur, 0);
        Assert.True(withMoney.DHPlus > noMoney, "money of p must grow the purity cell it does not lead");
        Assert.True(withMoney.Drive > Fx6.Zero);
        Assert.True(Rule.Step(s, 0, 0, Prec, 0).DHPlus > Fx6.Zero);
    }

    [Fact]
    public void Effort_NoLeadInCell_StillGrowsFromMoneyOfAProcessLeadingElsewhere()
    {
        // The purity cell of p has no eligible EFFORT candidate when p is the only process and purity is saturated by Hcap.
        IndustryLayout l = CbFake.MakeLayout([0], (10, Cluster.MET, Req(4_000_000, 3_500_000, 0)));
        var s = new CbFake(l);
        s.HcapByDim[Pur] = F(3_500_000); // SATURATION in purity: no lead there
        int p = s.P(10);
        s.Vs[p] = D(10);
        s.Es[p] = s.Params.EFull;
        Assert.Equal(-1, CbCultureRule.Lead(s, 0, 0, Pur, 0));
        Assert.True(Rule.Step(s, 0, 0, Pur, 0).DHPlus > Fx6.Zero);
    }

    [Fact]
    public void WithoutMoney_GrowthIsOnlySpill_AndMaintenanceDoesNotDependOnMoney()
    {
        IndustryLayout l = CbFake.MakeLayout([0], (10, Cluster.MET, Req(4_000_000, 3_500_000, 0)));
        var s = new CbFake(l);
        s.Vs[s.P(10)] = D(7);
        CultureStep off = Rule.Step(s, 0, 0, Prec, 0);
        Assert.Equal(Fx6.Zero, off.DHPlus); // one cluster: no spill
        s.Es[s.P(10)] = s.Params.EFull;
        CultureStep on = Rule.Step(s, 0, 0, Prec, 0);
        Assert.Equal(off.Maint, on.Maint);
        Assert.True(on.DHPlus > Fx6.Zero);
    }

    [Fact]
    public void Spill_IsUnchangedFromV23_AndDoesNotNeedMoney()
    {
        IndustryLayout l = CbFake.MakeLayout([0], (10, Cluster.MET, Req(4_000_000, 0, 0)), (12, Cluster.CHEM, Req(4_000_000, 0, 0)));
        var s = new CbFake(l);
        s.SetH(0, 1, Prec, D(4)); // CHEM is ahead of MET in precision (clusters ascending: MET, CHEM)
        CultureStep st = Rule.Step(s, 0, 0, Prec, 0);
        Fx6 expected = s.Params.Spill(Cluster.MET, Cluster.CHEM) * (D(4) - D(2));
        Assert.Equal(expected, st.Spill);
        Assert.True(st.DHPlus > Fx6.Zero);
    }

    [Fact]
    public void NoOutput_EffortGrowthIsBoundedByTheLaboratoryShare()
    {
        IndustryLayout l = CbFake.MakeLayout([0], (10, Cluster.MET, Req(4_000_000, 3_500_000, 0)));
        var s = new CbFake(l);
        int p = s.P(10);
        s.Es[p] = s.Params.EFull * D(10); // money is plentiful, output is 0
        CultureStep st = Rule.Step(s, 0, 0, Prec, 0);
        Fx6 gapL = D(4) - s.Params.DeltaL - D(2);
        Assert.Equal(s.Params.Ell * gapL, st.Drive); // e_L = ℓ
        // Culture at or above r_d − ΔL: the laboratory gives nothing.
        s.SetH(0, 0, Prec, D(4) - s.Params.DeltaL);
        Assert.Equal(Fx6.Zero, Rule.Step(s, 0, 0, Prec, 0).Drive);
    }

    [Fact]
    public void NoOutput_WithEllZero_NoGrowthFromMoney()
    {
        IndustryLayout l = CbFake.MakeLayout([0], (10, Cluster.MET, Req(4_000_000, 3_500_000, 0)));
        var s = new CbFake(l, new IndustryParams { Ell = Fx6.Zero });
        s.Es[s.P(10)] = s.Params.EFull * D(10);
        Assert.Equal(Fx6.Zero, Rule.Step(s, 0, 0, Prec, 0).DHPlus);
    }

    [Fact]
    public void Growth_IsMonotoneInMoneyAndInOutput()
    {
        IndustryLayout l = CbFake.MakeLayout([0], (10, Cluster.MET, Req(4_000_000, 3_500_000, 0)));
        var s = new CbFake(l);
        int p = s.P(10);
        for (long v = 0; v <= 12_000_000; v += 750_000)
        {
            Fx6 prev = Fx6.Zero;
            s.Vs[p] = F(v);
            for (long e = 0; e <= 2 * s.Params.EFull.Raw; e += 97)
            {
                s.Es[p] = F(e);
                Fx6 g = Rule.Step(s, 0, 0, Prec, 0).DHPlus;
                Assert.True(g >= prev, $"v={v} e={e}");
                prev = g;
            }
        }
        for (long e = 0; e <= s.Params.EFull.Raw; e += 229)
        {
            Fx6 prev = Fx6.Zero;
            s.Es[p] = F(e);
            for (long v = 0; v <= 12_000_000; v += 250_000)
            {
                s.Vs[p] = F(v);
                Fx6 g = Rule.Step(s, 0, 0, Prec, 0).DHPlus;
                Assert.True(g >= prev, $"e={e} v={v}");
                prev = g;
            }
        }
    }

    [Fact]
    public void Licence_NeverReducesGrowthOrMaintenance()
    {
        IndustryLayout l = CbFake.MakeLayout([0], (10, Cluster.MET, Req(4_000_000, 3_500_000, 0)));
        var s = new CbFake(l);
        int p = s.P(10);
        for (long v = 0; v <= 12_000_000; v += 500_000)
        {
            s.Vs[p] = F(v);
            s.Es[p] = s.Params.EFull;
            s.Lic[p] = false;
            CultureStep without = Rule.Step(s, 0, 0, Prec, 0);
            s.Lic[p] = true;
            CultureStep with = Rule.Step(s, 0, 0, Prec, 0);
            Assert.True(with.DHPlus >= without.DHPlus);
            Assert.True(with.Maint >= without.Maint);
        }
    }

    [Fact]
    public void Licence_CountsTwiceInOutputUnits()
    {
        // ṽ = (1 + k_tr·lic)·v: a licensed process at v = r_cap/2 has the ceiling of an unlicensed one at r_cap.
        IndustryLayout l = CbFake.MakeLayout([0], (10, Cluster.MET, Req(4_000_000, 3_500_000, 0)));
        var s = new CbFake(l);
        int p = s.P(10);
        s.Es[p] = s.Params.EFull;
        s.Vs[p] = D(10);
        Fx6 full = Rule.Step(s, 0, 0, Prec, 0).Drive;
        s.Vs[p] = D(5);
        s.Lic[p] = true;
        Assert.Equal(full, Rule.Step(s, 0, 0, Prec, 0).Drive);
    }

    [Fact]
    public void Maintenance_OnlyFromOutput_FloorBuiltIn()
    {
        IndustryLayout l = CbFake.MakeLayout([0], (10, Cluster.MET, Req(4_000_000, 3_500_000, 0)));
        var s = new CbFake(l);
        int p = s.P(10);
        Fx6 floor = s.Params.PhiFloor * D(5);
        Assert.Equal(floor, Rule.Step(s, 0, 0, Prec, 0).Maint); // nobody works: the floor
        s.Es[p] = s.Params.EFull;
        Assert.Equal(floor, Rule.Step(s, 0, 0, Prec, 0).Maint); // money alone does not hold the culture
        s.Vs[p] = s.Params.RKeep; // labour 1: v = v_keep, m_p = 1
        Fx6 held = Rule.Step(s, 0, 0, Prec, 0).Maint;
        Assert.Equal(D(4) > floor ? D(4) : floor, held); // act = 1: max(r_d·a_r, floor)
        s.Vs[p] = s.Params.RKeep / D(2);
        Fx6 half = Rule.Step(s, 0, 0, Prec, 0).Maint;
        Assert.True(half > floor && half < held);
    }

    [Fact]
    public void Cell_UsesMaxOverProcesses_NotAVolumeShare()
    {
        // A tiny second process must not dilute the first one (K3, #115).
        IndustryLayout l = CbFake.MakeLayout([0], (10, Cluster.MET, Req(4_000_000, 0, 0)), (11, Cluster.MET, Req(4_000_000, 0, 0)));
        var s = new CbFake(l);
        s.Vs[s.P(10)] = D(10);
        s.Es[s.P(10)] = s.Params.EFull;
        Fx6 alone = Rule.Step(s, 0, 0, Prec, 0).Drive;
        s.Vs[s.P(11)] = F(1);
        Assert.Equal(alone, Rule.Step(s, 0, 0, Prec, 0).Drive);
    }

    // ---- G4-2: lead only among eligible processes (regression: process without output vs small output)

    [Fact]
    public void Lead_ProcessWithoutOutputAndPlanDoesNotTakeTheCellFromASmallOutput()
    {
        IndustryLayout l = CbFake.MakeLayout([0], (10, Cluster.MET, Req(4_000_000, 3_500_000, 0)), (11, Cluster.MET, Req(4_000_000, 4_500_000, 0)));
        var s = new CbFake(l);
        s.SetH(0, 0, Pur, D(3));
        s.SetH(0, 0, Prec, D(3));
        s.Ms[s.Idx(0, 0, Pur)] = s.Ms[s.Idx(0, 0, Prec)] = D(4);
        int p = s.P(10), q = s.P(11);
        s.Vs[p] = D(1); // small output
        // q: known, no output, not planned, larger r_d, so the larger t̂ — but not eligible.
        Assert.False(CbCultureRule.Eligible(s, 0, q, 0));
        Assert.Equal(LimiterKind.Effort, CbCultureRule.Limiter0(s, 0, q, Pur, 0));
        Assert.Equal(p, CbCultureRule.Lead(s, 0, 0, Pur, 0));
        Assert.Equal(Fx6.Zero, Rule.EUseful(s, 0, q, 0));
        Assert.True(Rule.EUseful(s, 0, p, 0) > Fx6.Zero);
        // Planned in a project of stage П/О: eligible, and its larger t̂ now wins.
        s.PlannedF[q] = true;
        Assert.True(CbCultureRule.Eligible(s, 0, q, 0));
        Assert.Equal(q, CbCultureRule.Lead(s, 0, 0, Pur, 0));
        // An unknown process never leads, whatever its output.
        s.Known[q] = false;
        Assert.Equal(p, CbCultureRule.Lead(s, 0, 0, Pur, 0));
    }

    [Fact]
    public void Lead_NeighbourWithSaturationOrNothingToLearnDoesNotTakeTheCell()
    {
        // FRA×40 / SUN×40 shape (contract §4.1b): the neighbour with a larger r_d has limiter₀ SATURATION, the target
        // is a plain EFFORT process; in v2.4 r0 the neighbour led the cell and the target was never learnt.
        IndustryLayout l = CbFake.MakeLayout([0], (10, Cluster.MET, Req(0, 3_500_000, 3_000_000)), (11, Cluster.MET, Req(0, 4_500_000, 3_000_000)));
        var s = new CbFake(l);
        s.HcapByDim[Pur] = D(4); // Hcap ≤ 4.5: SATURATION for the neighbour only
        s.SetH(0, 0, Pur, F(2_500_000));
        s.Ms[s.Idx(0, 0, Pur)] = D(3);
        int target = s.P(10), nb = s.P(11);
        s.Vs[target] = D(10);
        s.Vs[nb] = D(10);
        Assert.Equal(LimiterKind.Saturation, CbCultureRule.Limiter0(s, 0, nb, Pur, 0));
        Assert.Equal(LimiterKind.Effort, CbCultureRule.Limiter0(s, 0, target, Pur, 0));
        Assert.Equal(target, CbCultureRule.Lead(s, 0, 0, Pur, 0));
        Assert.True(Rule.EUseful(s, 0, target, 0) > Fx6.Zero);
        Assert.Equal(Fx6.Zero, Rule.EUseful(s, 0, nb, 0));

        // Neighbour with no gap (NONE) is no candidate either.
        var s2 = new CbFake(CbFake.MakeLayout([0], (10, Cluster.MET, Req(0, 3_500_000, 3_000_000)), (11, Cluster.MET, Req(4_000_000, 1_000_000, 0))));
        s2.SetH(0, 0, Pur, F(2_500_000));
        s2.Vs[s2.P(10)] = D(10);
        s2.Vs[s2.P(11)] = D(10);
        Assert.Equal(LimiterKind.None, CbCultureRule.Limiter0(s2, 0, s2.P(11), Pur, 0));
        Assert.Equal(s2.P(10), CbCultureRule.Lead(s2, 0, 0, Pur, 0));
    }

    // ---- limiter values and subjects

    [Fact]
    public void Limiter_EveryValueOccurs_AndNonLeadShowsTheLeadWithItsSubject()
    {
        IndustryLayout l = CbFake.MakeLayout([0], (10, Cluster.MET, Req(0, 3_500_000, 3_000_000)), (11, Cluster.MET, Req(0, 3_000_000, 3_500_000)));
        var s = new CbFake(l);
        int a = s.P(10), b = s.P(11);
        s.SetH(0, 0, Pur, F(2_500_000));
        s.Vs[a] = D(10);
        s.Vs[b] = D(10);

        // EFFORT: lead with E = 0; the non-lead shows the lead's limiter with the lead as subject.
        int lead = CbCultureRule.Lead(s, 0, 0, CbCultureRule.DStar(s, 0, a, 0), 0);
        Assert.True(lead >= 0);
        CultureLimiter la = Rule.Limiter(s, 0, a, 0);
        Assert.Equal(LimiterKind.Effort, la.Value);

        // VOLUME: money at E_useful, output below r_cap.
        s.Vs[a] = D(2);
        s.Vs[b] = Fx6.Zero;
        int led = CbCultureRule.Lead(s, 0, 0, Pur, 0);
        Assert.Equal(a, led);
        s.Es[a] = Rule.EUseful(s, 0, a, 0);
        Assert.Equal(new CultureLimiter(LimiterKind.Volume, 10), Rule.Limiter(s, 0, a, 0));
        // b leads nothing in purity? it has no output and is not planned: not eligible; it shows the lead.
        Assert.Equal(new CultureLimiter(LimiterKind.Volume, 10), Rule.Limiter(s, 0, b, 0));

        // The remaining values are set on both dimensions so that the shown dimension d* does not matter.
        void AllDims(Action<int> f)
        {
            f(Pur);
            f(2);
        }
        // TOOLING
        AllDims(d => s.Ms[s.Idx(0, 0, d)] = D(1));
        Assert.Equal(LimiterKind.Tooling, Rule.Limiter(s, 0, a, 0).Value);
        AllDims(d => s.Ms[s.Idx(0, 0, d)] = D(3));
        // SATURATION
        AllDims(d => s.HcapByDim[d] = D(3));
        Assert.Equal(LimiterKind.Saturation, Rule.Limiter(s, 0, a, 0).Value);
        AllDims(d => s.HcapByDim[d] = D(5));
        // LAB_CEILING: no output, no plan, culture at or above r_d − ΔL
        s.Vs[a] = Fx6.Zero;
        AllDims(d =>
        {
            s.SetH(0, 0, d, F(3_300_000));
            s.Ms[s.Idx(0, 0, d)] = D(4);
        });
        Assert.Equal(LimiterKind.LabCeiling, Rule.Limiter(s, 0, a, 0).Value);
        // NONE
        AllDims(d =>
        {
            s.SetH(0, 0, d, D(4));
            s.Ms[s.Idx(0, 0, d)] = D(5);
        });
        Assert.Equal(LimiterKind.None, Rule.Limiter(s, 0, a, 0).Value);
    }

    [Fact]
    public void Limiter_NoLead_ProcessNotEligible_IsLabCeilingWithItselfAsSubject()
    {
        IndustryLayout l = CbFake.MakeLayout([0], (10, Cluster.MET, Req(4_000_000, 3_500_000, 0)));
        var s = new CbFake(l);
        // known, no output, not planned: nobody leads, the process is shown as "stand only"
        Assert.Equal(new CultureLimiter(LimiterKind.LabCeiling, 10), Rule.Limiter(s, 0, s.P(10), 0));
    }

    // ---- E_useful (G4-4) and dH_forecast

    [Fact]
    public void EUseful_IsTheCeilingOfTheKink_AtEUsefulTheEffortIsFullyUsed()
    {
        var rng = new Random(3);
        for (int i = 0; i < 400; i++)
        {
            IndustryLayout l = CbFake.MakeLayout([0], (10, Cluster.MET, Req(4_000_000, 3_500_000, 0)));
            var p = new IndustryParams { EFull = F(rng.Next(1, 5_000_000)) };
            var s = new CbFake(l, p);
            int r = s.P(10);
            s.Vs[r] = F(rng.Next(0, 14_000_000));
            s.SetH(0, 0, Prec, F(rng.Next(0, 4_400_000)));
            s.Ms[s.Idx(0, 0, Prec)] = D(5);
            Fx6 eu = Rule.EUseful(s, 0, r, 0);
            if (eu == Fx6.Zero)
                continue;
            Assert.True(eu <= p.EFull);
            int d = CbCultureRule.DStar(s, 0, r, 0);
            Fx6 k = CbCultureRule.KStar(s, 0, r, d, 0);
            Fx6 xE = eu / p.EFull;
            Assert.True(xE >= k, $"floor(E_useful/E_full) must reach k*: {xE} < {k}");
            Assert.Equal(Rule.DHForecast(s, 0, r, d, p.EFull, s.Vs[r], 0), Rule.DHForecast(s, 0, r, d, eu, s.Vs[r], 0));
            Assert.True(Rule.DHForecast(s, 0, r, d, F(Math.Max(0, eu.Raw - 1) / 2), s.Vs[r], 0) <= Rule.DHForecast(s, 0, r, d, eu, s.Vs[r], 0));
        }
    }

    [Fact]
    public void DHForecast_AtCurrentEAndV_EqualsTheActualCappedGrowth()
    {
        var rng = new Random(5);
        for (int i = 0; i < 300; i++)
        {
            CbFake s = RandomSnapshot(rng, out _);
            for (int site = 0; site < s.Layout.SiteCount; site++)
            {
                foreach (int r in s.Layout.Order)
                {
                    int c = s.Layout.ClusterIndex[r];
                    for (int d = 0; d < 3; d++)
                    {
                        if (s.Layout.Req[r][d] <= Fx6.Zero)
                            continue;
                        Fx6 actual = Rule.Step(s, site, c, d, 0).DHPlusCapped;
                        Assert.Equal(actual, Rule.DHForecast(s, site, r, d, s.E(site, r), s.V(site, r), 0));
                    }
                }
            }
        }
    }

    // ---- structural invariants on random snapshots

    private static CbFake RandomSnapshot(Random rng, out IndustryLayout layout)
    {
        Cluster[] cl = [Cluster.MET, Cluster.CHEM];
        var procs = new List<(int, Cluster, Dims3)>();
        for (int k = 0; k < 6; k++)
        {
            long a = rng.Next(0, 3) == 0 ? 0 : rng.Next(500_000, 5_000_000);
            long b = rng.Next(0, 3) == 0 ? 0 : rng.Next(500_000, 5_000_000);
            long c = rng.Next(0, 3) == 0 ? 0 : rng.Next(500_000, 5_000_000);
            if (a < 3_000_000 && b < 3_000_000 && c < 3_000_000)
                a = 3_500_000; // a key dimension is required by the layout
            procs.Add((10 + k, cl[k % 2], Req(a, b, c)));
        }
        layout = CbFake.MakeLayout([0], [.. procs]);
        var s = new CbFake(layout);
        for (int i = 0; i < s.Hs.Length; i++)
        {
            s.Hs[i] = F(rng.Next(0, 5_500_000));
            s.Ms[i] = F(rng.Next(0, 6_000_000));
        }
        for (int d = 0; d < 3; d++)
            s.HcapByDim[d] = F(rng.Next(3_000_000, 6_000_000));
        for (int r = 0; r < layout.ProcessCount; r++)
        {
            s.Vs[r] = rng.Next(0, 3) == 0 ? Fx6.Zero : F(rng.Next(0, 14_000_000));
            s.Es[r] = rng.Next(0, 3) == 0 ? Fx6.Zero : F(rng.Next(0, 2_500));
            s.Known[r] = rng.Next(0, 6) != 0;
            s.PlannedF[r] = rng.Next(0, 3) == 0;
            s.Lic[r] = rng.Next(0, 4) == 0;
            s.Lead[r] = rng.Next(0, 4) == 0;
        }
        return s;
    }

    [Fact]
    public void RuleInvariants_HoldOnRandomSnapshots()
    {
        var rng = new Random(17);
        for (int iter = 0; iter < 400; iter++)
        {
            CbFake s = RandomSnapshot(rng, out IndustryLayout l);
            for (int c = 0; c < l.Clusters.Length; c++)
            {
                for (int d = 0; d < 3; d++)
                {
                    int lead = CbCultureRule.Lead(s, 0, c, d, 0);
                    bool anyEligibleEffort = false;
                    foreach (int r in l.Order)
                    {
                        if (l.ClusterIndex[r] == c && l.Req[r][d] > Fx6.Zero && CbCultureRule.Eligible(s, 0, r, 0)
                            && CbCultureRule.Limiter0(s, 0, r, d, 0) == LimiterKind.Effort)
                            anyEligibleEffort = true;
                    }
                    Assert.Equal(anyEligibleEffort, lead >= 0);
                    if (lead >= 0)
                    {
                        Assert.True(CbCultureRule.Eligible(s, 0, lead, 0));
                        Assert.Equal(LimiterKind.Effort, CbCultureRule.Limiter0(s, 0, lead, d, 0));
                    }
                }
            }
            foreach (int r in l.Order)
            {
                Fx6 eu = Rule.EUseful(s, 0, r, 0);
                Assert.True(eu >= Fx6.Zero && eu <= s.Params.EFull);
                int d = CbCultureRule.DStar(s, 0, r, 0);
                Assert.True(d >= 0);
                if (eu > Fx6.Zero)
                {
                    Assert.Equal(r, CbCultureRule.Lead(s, 0, l.ClusterIndex[r], d, 0));
                    Assert.Equal(LimiterKind.Effort, CbCultureRule.Limiter0(s, 0, r, d, 0));
                    Assert.True(CbCultureRule.Eligible(s, 0, r, 0));
                }
                Assert.Contains(Rule.Limiter(s, 0, r, 0).SubjectProcess, l.Number);
            }
        }
    }

    [Fact]
    public void LeadAndLimiter0_DoNotDependOnProcE()
    {
        var rng = new Random(23);
        for (int iter = 0; iter < 200; iter++)
        {
            CbFake s = RandomSnapshot(rng, out IndustryLayout l);
            var leads = new List<int>();
            var lim0 = new List<LimiterKind>();
            var eu = new List<Fx6>();
            void Snap()
            {
                leads.Clear();
                lim0.Clear();
                eu.Clear();
                for (int c = 0; c < l.Clusters.Length; c++)
                {
                    for (int d = 0; d < 3; d++)
                        leads.Add(CbCultureRule.Lead(s, 0, c, d, 0));
                }
                foreach (int r in l.Order)
                {
                    lim0.Add(CbCultureRule.Limiter0(s, 0, r, CbCultureRule.DStar(s, 0, r, 0), 0));
                    eu.Add(Rule.EUseful(s, 0, r, 0));
                }
            }
            Snap();
            int[] l1 = [.. leads];
            LimiterKind[] k1 = [.. lim0];
            Fx6[] e1 = [.. eu];
            for (int r = 0; r < l.ProcessCount; r++)
                s.Es[r] = F(rng.Next(0, 4_000));
            Snap();
            Assert.Equal(l1, leads);
            Assert.Equal(k1, lim0);
            Assert.Equal(e1, eu);
        }
    }
}
