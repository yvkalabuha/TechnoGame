using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Policy;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Policy;

/// <summary>
/// TASK-0029 (D2, spec §11 T6e v0.19 r1): pass 2 of the tooling plan under <c>TOOLING_TARGET</c> V0…V4, V3b, VINF. The fixture of
/// <see cref="ToolingPlanTests"/> with MET.purity of the MET process (req 3, v_exp 10) as the only bottleneck cell: M_cap = 4, cost by the tier of M.
/// </summary>
public class ToolingTargetTests
{
    private static Fx6 F(long raw) => Fx6.FromRaw(raw);
    private static readonly Fx6 Big = Fx6.FromInt(100);

    private static ToolingTargetSpec Spec(ToolingTarget v) => new() { Variant = v };

    // H of MET.purity is 2.9 (below req 3, so H limits the process); M is given. Everything else comfortable.
    private static ToolingPlanTests.Rig RigWith(long mRaw, ICultureRule? rule = null) => new(
        (s, c, d) => c == 0 && d == 1 ? F(mRaw) : F(3_400_000), 1, (s, c, d) => c == 0 && d == 1 ? F(2_900_000) : Fx6.FromInt(4), rule);

    private static Fx6 Need(long mRaw, Fx6 goal)
    {
        var p = new IndustryParams();
        Fx6 m = F(mRaw), dec = m - p.DeltaM * m;
        return (goal - dec) * p.CostM(IndustryMath.CostTier(m));
    }

    private static readonly Fx6 H = F(2_900_000);

    [Fact]
    public void CellBelowH_AllVariantsTakePart_WithTheirOwnTarget()
    {
        const long m = 2_800_000;
        Fx6 Invest(ToolingTarget v) => RigWith(m).Plan(0, Big, Big, 0, Spec(v)).Invest(0, 0, Dimension.Purity);
        Assert.Equal(Need(m, H), Invest(ToolingTarget.V0));
        Assert.Equal(Need(m, H + F(21_000)), Invest(ToolingTarget.V1));
        Assert.Equal(Need(m, H + F(63_000)), Invest(ToolingTarget.V3));
        Assert.Equal(Need(m, H + F(200_000)), Invest(ToolingTarget.V3b));
        Assert.Equal(Need(m, H + F(200_000)), Invest(ToolingTarget.V4));
        // V2: the target is H' = H + dH_forecast, at most H + rho_max.
        Fx6 v2 = Invest(ToolingTarget.V2);
        Assert.InRange(v2.Raw, Need(m, H).Raw, Need(m, H + F(21_000)).Raw);
    }

    [Fact]
    public void CellJustAboveH_OnlyV3AndV2MayTakePart()
    {
        // M_dec = 2.9154 is in [H, H + h_lo): M > H, so V0, V1 and V4 do not take part.
        const long m = 2_930_000;
        Fx6 Invest(ToolingTarget v) => RigWith(m).Plan(0, Big, Big, 0, Spec(v)).Invest(0, 0, Dimension.Purity);
        Assert.Equal(Fx6.Zero, Invest(ToolingTarget.V0));
        Assert.Equal(Fx6.Zero, Invest(ToolingTarget.V1));
        Assert.Equal(Fx6.Zero, Invest(ToolingTarget.V4));
        Assert.Equal(Need(m, H + F(63_000)), Invest(ToolingTarget.V3));
        Assert.Equal(Need(m, H + F(200_000)), Invest(ToolingTarget.V3b));
        Assert.True(Invest(ToolingTarget.V2) >= Fx6.Zero);
    }

    [Fact]
    public void CellAtH_V4TakesPart_V0AndV1DoNot()
    {
        const long m = 2_900_000;
        Fx6 Invest(ToolingTarget v) => RigWith(m).Plan(0, Big, Big, 0, Spec(v)).Invest(0, 0, Dimension.Purity);
        Assert.Equal(Fx6.Zero, Invest(ToolingTarget.V0));
        Assert.Equal(Fx6.Zero, Invest(ToolingTarget.V1));
        Assert.Equal(Need(m, H + F(200_000)), Invest(ToolingTarget.V4));
    }

    [Fact]
    public void CellFarAboveH_NoVariantTakesPart()
    {
        const long m = 3_300_000;
        foreach (ToolingTarget v in new[] { ToolingTarget.V0, ToolingTarget.V1, ToolingTarget.V3, ToolingTarget.V3b, ToolingTarget.V4 })
            Assert.Equal(Fx6.Zero, RigWith(m).Plan(0, Big, Big, 0, Spec(v)).Invest(0, 0, Dimension.Purity));
    }

    [Fact]
    public void NoVariant_PutsMAboveTheCapOrTheRoom()
    {
        var p = new IndustryParams();
        foreach (ToolingTarget v in Enum.GetValues<ToolingTarget>())
        {
            ToolingPlanResult plan = RigWith(2_800_000).Plan(0, Big, Big, 0, Spec(v));
            for (int s = 0; s < 2; s++)
            {
                for (int c = 0; c < 2; c++)
                {
                    for (int d = 0; d < 3; d++)
                    {
                        Fx6 m = c == 0 && d == 1 ? F(2_800_000) : F(3_400_000), dec = m - p.DeltaM * m;
                        Fx6 room = (Fx6.FromInt(4) - dec) * p.CostM(IndustryMath.CostTier(m));
                        Assert.True(plan.Applied(s, c, (Dimension)d) <= room, $"{v} cell {s}/{c}/{d}");
                    }
                }
            }
        }
    }

    [Fact]
    public void A39Balance_HoldsInEveryVariant_ForManyPools()
    {
        var rig = RigWith(2_800_000);
        ulong x = 99;
        foreach (ToolingTarget v in Enum.GetValues<ToolingTarget>())
        {
            for (int i = 0; i < 60; i++)
            {
                x = x * 6364136223846793005UL + 1442695040888963407UL;
                Fx6 rest = F((long)(x >> 44) % 400_000), cap = F((long)((x >> 20) & 0xFFFFF) % 600_000);
                ToolingPlanResult plan = rig.Plan(0, rest, cap, 0, Spec(v));
                Assert.True(plan.Total <= rest + cap, v.ToString());
                Assert.Equal(plan.Total, plan.TakenRest + plan.TakenCap);
                Assert.Equal(rest + cap, plan.Total + plan.RestReturned + plan.TrainingBudget);
                Assert.Equal(Fx6.Zero, plan.Invest(2, 0, Dimension.Purity));
            }
        }
    }

    [Fact]
    public void VInf_MReachesTheCap_ButTheMoneyIsNotTakenFromThePool()
    {
        const long m = 2_800_000;
        var rig = RigWith(m);
        Fx6 rest = F(70_000), cap = F(30_000);
        ToolingPlanResult inf = rig.Plan(0, rest, cap, 0, Spec(ToolingTarget.VInf));
        // No pass 1 here, so a zero pass 2 takes nothing: the pool figures are those of an untouched pool, bit for bit.
        Assert.Equal(Fx6.Zero, inf.Total);
        Assert.Equal(Fx6.Zero, inf.TakenRest);
        Assert.Equal(Fx6.Zero, inf.TakenCap);
        Assert.Equal(rest, inf.RestReturned);
        Assert.Equal(cap, inf.TrainingBudget);
        Assert.Equal(Fx6.Zero, inf.Invest(0, 0, Dimension.Purity));
        // The cell receives (M_cap − M_dec)·cost_M, i.e. M = M_cap after ΔM; every cell of a process with v_exp > 0 and r_d > 0 (MET cluster).
        Fx6 expectedCell = Need(m, Fx6.FromInt(4));
        Assert.Equal(expectedCell, inf.Notional(0, 0, Dimension.Purity));
        Assert.Equal(expectedCell, inf.Applied(0, 0, Dimension.Purity));
        Fx6 sum = Fx6.Zero;
        for (int s = 0; s < 3; s++)
        {
            for (int c = 0; c < 2; c++)
            {
                for (int d = 0; d < 3; d++)
                    sum += inf.Notional(s, c, (Dimension)d);
            }
        }
        Assert.Equal(sum, inf.NotionalTotal);
        Assert.True(inf.NotionalTotal > Fx6.Zero);
        // MECH has no planned volume, the other actor's site is not the actor's.
        Assert.Equal(Fx6.Zero, inf.Notional(0, 1, Dimension.Purity));
        Assert.Equal(Fx6.Zero, inf.Notional(2, 0, Dimension.Purity));
    }

    [Fact]
    public void VInf_ExemptFromTheBoundSumInvestAtMostPool_OtherInvariantsHold()
    {
        // The exception named: Σ applied money may exceed rest_a + B_cap on VINF only; the pool balance of every other arm keeps the bound.
        var rig = RigWith(2_800_000);
        Fx6 rest = F(1_000), cap = F(1_000);
        ToolingPlanResult inf = rig.Plan(0, rest, cap, 0, Spec(ToolingTarget.VInf));
        Fx6 applied = Fx6.Zero;
        for (int s = 0; s < 3; s++)
        {
            for (int c = 0; c < 2; c++)
            {
                for (int d = 0; d < 3; d++)
                    applied += inf.Applied(s, c, (Dimension)d);
            }
        }
        Assert.True(applied > rest + cap);
        Assert.True(inf.Total <= rest + cap);
        Assert.Equal(rest + cap, inf.Total + inf.RestReturned + inf.TrainingBudget);
        ToolingPlanResult v4 = rig.Plan(0, rest, cap, 0, Spec(ToolingTarget.V4));
        Assert.True(v4.Total <= rest + cap);
        Assert.Equal(Fx6.Zero, v4.NotionalTotal);
    }

    [Fact]
    public void VInf_PassOneMoneyStaysInTheCellAndInThePool()
    {
        // Pass 1 (A30) feeds MECH.purity of site 0; MECH has no planned volume, so VINF adds nothing there and Applied = invest₁.
        var rig = new ToolingPlanTests.Rig((s, c, d) => c == 1 && d == 1 && s == 0 ? F(2_100_000) : c == 0 && d == 1 ? F(2_800_000) : F(3_400_000));
        ToolingPlanResult inf = rig.Plan(0, F(500_000), Fx6.Zero, 0, Spec(ToolingTarget.VInf));
        Assert.True(inf.Invest(0, 1, Dimension.Purity) > Fx6.Zero);
        Assert.Equal(inf.Invest(0, 1, Dimension.Purity), inf.Applied(0, 1, Dimension.Purity));
        Assert.Equal(Fx6.Zero, inf.Notional(0, 1, Dimension.Purity));
        // The pool pays pass 1 only.
        Assert.Equal(inf.Invest(0, 1, Dimension.Purity), inf.Total);
    }

    private sealed class CountingRule(ICultureRule inner) : ICultureRule
    {
        public int Forecasts;
        public string Name => inner.Name;
        public CultureStep Step(ICultureSnapshot s, int site, int cluster, int dim, long month) => inner.Step(s, site, cluster, dim, month);
        public Fx6 EUseful(ICultureSnapshot s, int site, int processRow, long month) => inner.EUseful(s, site, processRow, month);
        public CultureLimiter Limiter(ICultureSnapshot s, int site, int processRow, long month) => inner.Limiter(s, site, processRow, month);

        public Fx6 DHForecast(ICultureSnapshot s, int site, int processRow, int dim, Fx6 e, Fx6 v, long month)
        {
            Forecasts++;
            return inner.DHForecast(s, site, processRow, dim, e, v, month);
        }
    }

    [Fact]
    public void DhForecast_IsReadOnlyUnderV2()
    {
        foreach (ToolingTarget v in Enum.GetValues<ToolingTarget>())
        {
            var rule = new CountingRule(new V23CultureRule());
            RigWith(2_800_000, rule).Plan(0, Big, Big, 0, Spec(v));
            Assert.True(v == ToolingTarget.V2 ? rule.Forecasts > 0 : rule.Forecasts == 0, v.ToString());
        }
    }

    [Fact]
    public void DefaultPlan_IsV0()
    {
        var rig = RigWith(2_800_000);
        ToolingPlanResult none = rig.Plan(0, Big, Big);
        ToolingPlanResult v0 = rig.Plan(0, Big, Big, 0, Spec(ToolingTarget.V0));
        Assert.Equal(v0.Total, none.Total);
        Assert.Equal(v0.Invest(0, 0, Dimension.Purity), none.Invest(0, 0, Dimension.Purity));
    }

    [Theory]
    [InlineData("V0", ToolingTarget.V0)]
    [InlineData("V1", ToolingTarget.V1)]
    [InlineData("V2", ToolingTarget.V2)]
    [InlineData("V3", ToolingTarget.V3)]
    [InlineData("V3b", ToolingTarget.V3b)]
    [InlineData("V4", ToolingTarget.V4)]
    [InlineData("VINF", ToolingTarget.VInf)]
    public void Manifest_ParsesKnownValues(string text, ToolingTarget expected) => Assert.Equal(expected, ToolingTargetSpec.Parse(text).Variant);

    [Theory]
    [InlineData("V5")]
    [InlineData("v0")]
    [InlineData("")]
    public void Manifest_UnknownValueIsAnError(string text) => Assert.Throws<ArgumentException>(() => ToolingTargetSpec.Parse(text));

    [Fact]
    public void Manifest_MissingNumberOrUnknownVariantIsAnError()
    {
        Assert.Throws<ArgumentException>(() => (ToolingTargetSpec.Parse("V1") with { RhoMax = Fx6.Zero }).Validate());
        Assert.Throws<ArgumentException>(() => (ToolingTargetSpec.Parse("V3") with { HHi = Fx6.Zero }).Validate());
        Assert.Throws<ArgumentException>(() => (ToolingTargetSpec.Parse("V3b") with { HHiControl = Fx6.Zero }).Validate());
        Assert.Throws<ArgumentException>(() => (ToolingTargetSpec.Parse("V4") with { MLead = Fx6.Zero }).Validate());
        Assert.Throws<ArgumentException>(() => (new ToolingTargetSpec { Variant = (ToolingTarget)99 }).Validate());
        Assert.Throws<ArgumentException>(() => RigWith(2_800_000).Plan(0, Big, Big, 0, new ToolingTargetSpec { Variant = (ToolingTarget)99 }));
    }
}
