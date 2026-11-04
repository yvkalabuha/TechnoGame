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

    [Fact]
    public void VInf_WhenPassOneAndPassTwoCompeteForThePool_PoolFiguresEqualV0WithAnEmptyPassTwo()
    {
        // MECH.purity of site 0 needs money for admission (pass 1). MET.purity has M > H, so V0 pass 2 is empty, while VINF pass 2 has notional need there.
        ToolingPlanTests.Rig NewRig() => new((s, c, d) => c == 1 && d == 1 && s == 0 ? F(2_100_000) : F(3_400_000), 1,
            (s, c, d) => c == 0 && d == 1 ? F(2_900_000) : Fx6.FromInt(4));
        Fx6 need1 = NewRig().Plan(0, Big, Big, 0, Spec(ToolingTarget.V0)).Invest(0, 1, Dimension.Purity);
        Assert.True(need1 > Fx6.Zero);
        foreach (Fx6 rest in new[] { F(need1.Raw / 2), need1, need1 + F(10_000) })
        {
            Fx6 cap = F(5_000);
            ToolingPlanResult v0 = NewRig().Plan(0, rest, cap, 0, Spec(ToolingTarget.V0));
            ToolingPlanResult inf = NewRig().Plan(0, rest, cap, 0, Spec(ToolingTarget.VInf));
            Assert.Equal(Fx6.Zero, v0.NotionalTotal);
            Assert.True(inf.NotionalTotal > Fx6.Zero);
            Assert.Equal(v0.Total, inf.Total);
            Assert.Equal(v0.TakenRest, inf.TakenRest);
            Assert.Equal(v0.TakenCap, inf.TakenCap);
            Assert.Equal(v0.RestReturned, inf.RestReturned);
            Assert.Equal(v0.TrainingBudget, inf.TrainingBudget);
            Assert.Equal(v0.Invest(0, 1, Dimension.Purity), inf.Invest(0, 1, Dimension.Purity));
        }
    }

    // Culture rule with a known dH_forecast (everything else is the rule of v2.3).
    private sealed class FixedForecastRule(Fx6 forecast) : ICultureRule
    {
        private readonly V23CultureRule _inner = new();
        public string Name => _inner.Name;
        public CultureStep Step(ICultureSnapshot s, int site, int cluster, int dim, long month) => _inner.Step(s, site, cluster, dim, month);
        public Fx6 EUseful(ICultureSnapshot s, int site, int processRow, long month) => _inner.EUseful(s, site, processRow, month);
        public CultureLimiter Limiter(ICultureSnapshot s, int site, int processRow, long month) => _inner.Limiter(s, site, processRow, month);
        public Fx6 DHForecast(ICultureSnapshot s, int site, int processRow, int dim, Fx6 e, Fx6 v, long month) => forecast;
    }

    [Theory]
    [InlineData(2_800_000, 15_000, true)]   // M < H: M_dec 2.786 < H' = 2.915
    [InlineData(2_930_000, 21_000, true)]   // M_dec 2.9154 < H' = 2.921 (H' = H + rho_max, the largest forecast)
    [InlineData(2_930_000, 15_000, false)]  // M_dec 2.9154 >= H' = 2.915
    [InlineData(2_930_000, 0, false)]       // no growth forecast: H' = H
    public void V2_UsesHPrime_AndSkipsACellWithMDecAtOrAboveIt(long m, long forecast, bool participates)
    {
        ToolingPlanResult plan = RigWith(m, new FixedForecastRule(F(forecast))).Plan(0, Big, Big, 0, Spec(ToolingTarget.V2));
        Assert.Equal(participates, plan.Participates(0, 0, Dimension.Purity));
        Assert.True(plan.Goal(0, 0, Dimension.Purity) <= H + new IndustryParams().RhoMax);
        Assert.Equal(participates ? Need(m, H + F(forecast)) : Fx6.Zero, plan.Invest(0, 0, Dimension.Purity));
        Assert.Equal(participates ? H + F(forecast) : Fx6.Zero, plan.Goal(0, 0, Dimension.Purity));
    }

    [Fact]
    public void GoalAndParticipates_AreReportedPerCell_CappedByMCap()
    {
        ToolingPlanResult v4 = RigWith(2_800_000).Plan(0, Big, Big, 0, Spec(ToolingTarget.V4));
        Assert.True(v4.Participates(0, 0, Dimension.Purity));
        Assert.Equal(H + F(200_000), v4.Goal(0, 0, Dimension.Purity));
        Assert.False(v4.Participates(0, 1, Dimension.Purity));
        Assert.False(v4.Participates(2, 0, Dimension.Purity));
        Assert.Equal(Fx6.Zero, v4.Goal(0, 1, Dimension.Purity));
        // VINF: the goal is M_cap = 4 wherever the cell is used.
        ToolingPlanResult inf = RigWith(2_800_000).Plan(0, Big, Big, 0, Spec(ToolingTarget.VInf));
        Assert.True(inf.Participates(0, 0, Dimension.Purity));
        Assert.Equal(Fx6.FromInt(4), inf.Goal(0, 0, Dimension.Purity));
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
    [InlineData("V5", ToolingTarget.V5)]
    [InlineData("VINF", ToolingTarget.VInf)]
    public void Manifest_ParsesKnownValues(string text, ToolingTarget expected) => Assert.Equal(expected, ToolingTargetSpec.Parse(text).Variant);

    [Theory]
    [InlineData("V6")]
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
        Assert.Throws<ArgumentException>(() => (ToolingTargetSpec.Parse("V5") with { MLead = Fx6.Zero }).Validate());
        Assert.Throws<ArgumentException>(() => (new ToolingTargetSpec { Variant = (ToolingTarget)99 }).Validate());
        Assert.Throws<ArgumentException>(() => RigWith(2_800_000).Plan(0, Big, Big, 0, new ToolingTargetSpec { Variant = (ToolingTarget)99 }));
    }

    // ---- V5 (spec §11 T6e v0.21, #162): MET purity is the bottleneck (M 1.5, H 2.9, r 3); dimension 0 (r 3) is the cell under test.

    private static ToolingPlanTests.Rig V5Rig(long m0, long h0, Dims3? req = null) => new(
        (s, c, d) => c == 0 && d == 1 ? F(1_500_000) : c == 0 && d == 0 ? F(m0) : F(3_400_000), 1,
        (s, c, d) => c == 0 && d == 1 ? F(2_900_000) : c == 0 && d == 0 ? F(h0) : Fx6.FromInt(4), null, req);

    [Theory]
    [InlineData(2_400_000, 3_500_000, true)]   // (a) M < H, M < r
    [InlineData(2_800_000, 2_800_000, true)]   // (b) M = H < r
    [InlineData(3_200_000, 3_500_000, false)]  // (c) r <= M < H
    [InlineData(2_900_000, 2_800_000, false)]  // (g) M > H
    public void V5_ParticipatesWhereToolingHinders_V4OnlyAtTheBottleneck(long m0, long h0, bool v5)
    {
        ToolingPlanResult p5 = V5Rig(m0, h0).Plan(0, Big, Big, 0, Spec(ToolingTarget.V5));
        ToolingPlanResult p4 = V5Rig(m0, h0).Plan(0, Big, Big, 0, Spec(ToolingTarget.V4));
        Assert.Equal(v5, p5.Participates(0, 0, Dimension.Purity - 1));
        Assert.False(p4.Participates(0, 0, Dimension.Purity - 1));   // not the bottleneck: V4 skips it
        Assert.True(p4.Participates(0, 0, Dimension.Purity) && p5.Participates(0, 0, Dimension.Purity));
        Fx6 goal = p5.Goal(0, 0, Dimension.Purity - 1);
        Assert.Equal(v5 ? F(Math.Min(h0 + 200_000, 4_000_000)) : Fx6.Zero, goal);
        // V4 ⊆ V5 ⊆ VINF on every cell.
        ToolingPlanResult inf = V5Rig(m0, h0).Plan(0, Big, Big, 0, Spec(ToolingTarget.VInf));
        for (int s = 0; s < 3; s++)
        {
            for (int c = 0; c < 2; c++)
            {
                for (int d = 0; d < 3; d++)
                {
                    var dim = (Dimension)d;
                    Assert.True(!p4.Participates(s, c, dim) || p5.Participates(s, c, dim));
                    Assert.True(!p5.Participates(s, c, dim) || inf.Participates(s, c, dim));
                    Assert.True(p5.Goal(s, c, dim) <= Fx6.FromInt(4));
                }
            }
        }
    }

    [Fact]
    public void V5_NoParticipation_WhenVExpIsZero_TheProcessIsUnknown_OrRdIsZero()
    {
        var none = V5Rig(2_400_000, 3_500_000);
        none.Inputs.NoPlanned = true;
        Assert.False(none.Plan(0, Big, Big, 0, Spec(ToolingTarget.V5)).Participates(0, 0, Dimension.Purity - 1));
        var unknown = V5Rig(2_400_000, 3_500_000);
        unknown.Inputs.Unknown = true;
        Assert.False(unknown.Plan(0, Big, Big, 0, Spec(ToolingTarget.V5)).Participates(0, 0, Dimension.Purity - 1));
        var zero = V5Rig(2_400_000, 3_500_000, new Dims3(Fx6.Zero, Fx6.FromInt(3), F(2_500_000)));
        Assert.False(zero.Plan(0, Big, Big, 0, Spec(ToolingTarget.V5)).Participates(0, 0, Dimension.Purity - 1));
    }

    [Fact]
    public void V5_WaterfallInCanonicalOrder_PoolRunsOut_LaterCellsGetNothing()
    {
        // Purity (d1) is the first cell with need in order after dimension 0; dimension 0 comes first and takes the whole small pool.
        var rig = V5Rig(2_400_000, 3_500_000);
        Fx6 pool = F(1_000);
        ToolingPlanResult plan = rig.Plan(0, pool, Fx6.Zero, 0, Spec(ToolingTarget.V5));
        Assert.Equal(pool, plan.Total);
        Assert.Equal(pool, plan.Invest(0, 0, Dimension.Purity - 1));
        Assert.Equal(Fx6.Zero, plan.Invest(0, 0, Dimension.Purity));
        Assert.Equal(Fx6.Zero, plan.Invest(1, 0, Dimension.Purity - 1));
        ToolingPlanResult same = V5Rig(2_400_000, 3_500_000).Plan(0, pool, Fx6.Zero, 0, Spec(ToolingTarget.V5));
        Assert.Equal(plan.Total, same.Total);
        Fx6 rest = F(500), cap = F(700);
        ToolingPlanResult two = V5Rig(2_400_000, 3_500_000).Plan(0, rest, cap, 0, Spec(ToolingTarget.V5));
        Assert.True(two.Total <= rest + cap);
        Assert.True(two.TakenRest <= rest);
        Assert.Equal(rest + cap, two.Total + two.RestReturned + two.TrainingBudget);
    }
}
