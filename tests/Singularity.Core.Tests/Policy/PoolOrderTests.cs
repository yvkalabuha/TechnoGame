using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Policy;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Policy;

/// <summary>
/// TASK-0032 (spec §11 «POOL_ORDER», «V5 default + why_not pool», contract r8 §3.1): the pool of pass 2 under <c>POOL_ORDER</c> CAN, REV, PROP, SEV and the
/// <c>tool_unmet</c> of the plan. Fixture of <see cref="ToolingPlanTests"/>: actor 0 owns sites 0 and 1, two clusters (MET c = 0, MECH c = 1), every process planned;
/// MET has M 2.4 (the process runs), MECH has M 1.0 (the process is not admitted, so pass 1 takes nothing) — all 12 cells hinder under V5.
/// </summary>
public class PoolOrderTests
{
    private static Fx6 F(long raw) => Fx6.FromRaw(raw);
    private static readonly Fx6 Huge = Fx6.FromInt(1000);

    private static ToolingTargetSpec Spec(PoolOrder order) => new() { Variant = ToolingTarget.V5, PoolOrder = order };

    private static ToolingPlanTests.Rig NewRig()
    {
        var rig = new ToolingPlanTests.Rig((s, c, d) => c == 0 ? F(2_400_000) : F(1_000_000), 1, (s, c, d) => F(3_500_000));
        rig.Inputs.PlanAll = true;
        return rig;
    }

    // Canonical order of the cells of actor 0 (sites 0, 1): site, cluster, dimension.
    private static readonly (int S, int C, Dimension D)[] Cells = [.. from s in new[] { 0, 1 } from c in new[] { 0, 1 } from d in new[] { 0, 1, 2 } select (s, c, (Dimension)d)];

    private static Fx6 Sum(Func<(int S, int C, Dimension D), Fx6> f) => Cells.Aggregate(Fx6.Zero, (acc, x) => acc + f(x));

    private static Fx6[] Needs() => [.. Cells.Select(x => NewRig().Plan(0, Huge, Huge, 0, Spec(PoolOrder.Can)).Invest(x.S, x.C, x.D))];

    // Expected waterfall of the needs in the given order of cell indices.
    private static Fx6[] Waterfall(int[] order, Fx6 pool)
    {
        Fx6[] need = Needs(), x = new Fx6[Cells.Length];
        foreach (int i in order)
        {
            x[i] = need[i] < pool ? need[i] : pool;
            pool -= x[i];
        }
        return x;
    }

    private static Fx6[] Invest(ToolingPlanResult p) => [.. Cells.Select(x => p.Invest(x.S, x.C, x.D))];

    private static Fx6[] Unmet(ToolingPlanResult p) => [.. Cells.Select(x => p.Unmet(x.S, x.C, x.D))];

    private static Fx6 Pool() => Fx6.FromRaw(Needs().Aggregate(0L, (a, n) => a + n.Raw) / 3);

    [Fact]
    public void Fixture_AllTwelveCellsParticipateAndNeedMoney()
    {
        ToolingPlanResult p = NewRig().Plan(0, Huge, Huge, 0, Spec(PoolOrder.Can));
        Assert.All(Cells, x => Assert.True(p.Participates(x.S, x.C, x.D) && p.Invest(x.S, x.C, x.D) > Fx6.Zero));
    }

    [Fact]
    public void PoolCoversEverything_AllRulesGiveThePlanOfCan_AndNothingIsUnmet()
    {
        Fx6[] can = Invest(NewRig().Plan(0, Huge, Fx6.Zero, 0, Spec(PoolOrder.Can)));
        foreach (PoolOrder o in new[] { PoolOrder.Rev, PoolOrder.Prop, PoolOrder.Sev })
        {
            ToolingPlanResult p = NewRig().Plan(0, Huge, Fx6.Zero, 0, Spec(o));
            Assert.Equal(can, Invest(p));
            Assert.All(Unmet(p), u => Assert.Equal(Fx6.Zero, u));
        }
    }

    [Fact]
    public void CanAndRev_AreWaterfallsInTheirOrder_Sev_ByTheSeverityOfTheBottleneck()
    {
        Fx6 pool = Pool();
        int[] canonical = [.. Enumerable.Range(0, Cells.Length)];
        int[] rev = [.. canonical.OrderBy(i => Cells[i].S).ThenByDescending(i => Cells[i].C)];
        Assert.Equal(Waterfall(canonical, pool), Invest(NewRig().Plan(0, pool, Fx6.Zero, 0, Spec(PoolOrder.Can))));
        Assert.Equal(Waterfall(rev, pool), Invest(NewRig().Plan(0, pool, Fx6.Zero, 0, Spec(PoolOrder.Rev))));
        // Severity 1 − g(M − r_d): MECH (M 1.0, gap ≤ −1.5) is as severe as it gets, MET (M 2.4) less; a tie keeps the canonical order.
        Fx6 Sev((int S, int C, Dimension D) x)
        {
            Fx6 m = x.C == 0 ? F(2_400_000) : F(1_000_000);
            Fx6 req = x.D == Dimension.QualityControl ? F(2_500_000) : Fx6.FromInt(3);
            return Fx6.One - IndustryMath.G(m - req);
        }
        int[] sev = [.. canonical.OrderByDescending(i => Sev(Cells[i]))];
        Fx6[] viaSev = Invest(NewRig().Plan(0, pool, Fx6.Zero, 0, Spec(PoolOrder.Sev)));
        Assert.Equal(Waterfall(sev, pool), viaSev);
        Assert.NotEqual(Invest(NewRig().Plan(0, pool, Fx6.Zero, 0, Spec(PoolOrder.Can))), viaSev);
    }

    [Fact]
    public void Prop_IsFloorOfNeedTimesPoolOverSum_RemainderIsNotHandedOut()
    {
        Fx6 pool = Pool();
        Fx6[] need = Needs();
        Int128 n = need.Aggregate((Int128)0, (a, x) => a + x.Raw);
        ToolingPlanResult p = NewRig().Plan(0, pool, Fx6.Zero, 0, Spec(PoolOrder.Prop));
        Fx6[] x = Invest(p);
        for (int i = 0; i < x.Length; i++)
            Assert.Equal((long)((Int128)need[i].Raw * pool.Raw / n), x[i].Raw);
        Fx6 sum = Sum(c => p.Invest(c.S, c.C, c.D));
        Assert.True(sum <= pool && pool.Raw - sum.Raw < Cells.Length);
        Assert.Equal(sum, p.Total);
        Assert.Equal(pool - sum, p.RestReturned);   // the remainder goes back to the actor
    }

    [Fact]
    public void Unmet_IsNeedMinusInvest_WhereThePoolRanOut_ForEveryRule()
    {
        Fx6 pool = Pool();
        Fx6[] need = Needs();
        foreach (PoolOrder o in Enum.GetValues<PoolOrder>())
        {
            ToolingPlanResult p = NewRig().Plan(0, pool, Fx6.Zero, 0, Spec(o));
            Fx6[] x = Invest(p), u = Unmet(p);
            for (int i = 0; i < x.Length; i++)
                Assert.Equal(need[i], x[i] + u[i]);
            Assert.True(Sum(c => p.Unmet(c.S, c.C, c.D)) > Fx6.Zero, o.ToString());
            Assert.Equal(Fx6.Zero, p.Unmet(2, 0, Dimension.Precision));   // a cell of another actor's site
        }
    }

    [Fact]
    public void Unmet_IsZeroWhenThePoolIsAmple_AndInVInfPass2()
    {
        Assert.All(Unmet(NewRig().Plan(0, Huge, Fx6.Zero, 0, Spec(PoolOrder.Can))), u => Assert.Equal(Fx6.Zero, u));
        ToolingPlanResult inf = NewRig().Plan(0, Fx6.Zero, Fx6.Zero, 0, new ToolingTargetSpec { Variant = ToolingTarget.VInf });
        Assert.All(Unmet(inf), u => Assert.Equal(Fx6.Zero, u));
        Assert.True(inf.NotionalTotal > Fx6.Zero);
    }

    [Fact]
    public void PoolAccounting_HoldsUnderEveryRule()
    {
        Fx6 rest = F(400_000), cap = F(300_000);
        foreach (PoolOrder o in Enum.GetValues<PoolOrder>())
        {
            ToolingPlanResult p = NewRig().Plan(0, rest, cap, 0, Spec(o));
            Assert.True(p.Total <= rest + cap);
            Assert.True(p.TakenRest <= rest);
            Assert.Equal(rest + cap, p.Total + p.RestReturned + p.TrainingBudget);
            Fx6[] room = Invest(p);
            Assert.All(room, x => Assert.True(x >= Fx6.Zero));
            // The plan is the same in every call of the month.
            Assert.Equal(Invest(p), Invest(NewRig().Plan(0, rest, cap, 0, Spec(o))));
        }
    }

    [Theory]
    [InlineData("CAN", PoolOrder.Can)]
    [InlineData("REV", PoolOrder.Rev)]
    [InlineData("PROP", PoolOrder.Prop)]
    [InlineData("SEV", PoolOrder.Sev)]
    public void Manifest_ParsesPoolOrder(string text, PoolOrder expected) => Assert.Equal(expected, ToolingTargetSpec.ParsePoolOrder(text));

    [Theory]
    [InlineData("")]
    [InlineData("can")]
    [InlineData("SEVERITY")]
    public void Manifest_UnknownPoolOrderIsAnError(string text) => Assert.Throws<ArgumentException>(() => ToolingTargetSpec.ParsePoolOrder(text));

    [Fact]
    public void Manifest_KCapMustBeInTheHalfOpenUnitInterval_AndSevNeedsV5()
    {
        ToolingTargetSpec ok = ToolingTargetSpec.Parse("V5");
        ok.Validate();
        (ok with { KCap = F(500_000) }).Validate();
        Assert.Throws<ArgumentException>(() => (ok with { KCap = Fx6.Zero }).Validate());
        Assert.Throws<ArgumentException>(() => (ok with { KCap = F(-1) }).Validate());
        Assert.Throws<ArgumentException>(() => (ok with { KCap = F(1_000_001) }).Validate());
        Assert.Throws<ArgumentException>(() => (ok with { PoolOrder = (PoolOrder)9 }).Validate());
        Assert.Throws<ArgumentException>(() => (ToolingTargetSpec.Parse("V0") with { PoolOrder = PoolOrder.Sev }).Validate());
        (ToolingTargetSpec.Parse("V0") with { PoolOrder = PoolOrder.Prop }).Validate();
        Assert.Equal(PoolOrder.Can, new ToolingTargetSpec().PoolOrder);
        Assert.Equal(Fx6.One, new ToolingTargetSpec().KCap);
    }
}
