using Singularity.Core.Products;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>
/// Property tests of the product formulas (contract v2.3 §4.4, spec §3.3, §5.5) on seeded random inputs and on every
/// design the core data can produce: <c>unit_cost</c> non-increasing in <c>line_E</c> and never under its floor; upkeep
/// positive and non-decreasing in complexity; <c>readiness</c> in [0; 1] and non-increasing in unfixed defects; detection
/// bounded by <c>D0</c> and monotone in hours; <c>n_v</c> of <c>fleet_value</c> monotone in budget and upkeep.
/// </summary>
public class ProductsPropertyTests
{
    private static Fx6 F(long raw) => Fx6.FromRaw(raw);

    private static Fx6 Rnd(Random r, long minRaw, long maxRaw) => F(r.NextInt64(minRaw, maxRaw + 1));

    /// <summary>Every («превосходящий», «технологичный» at yields 1) design of every generation with all nodes up to it known.</summary>
    private static IEnumerable<(int Gen, int[] Prc, int Af, int Pu)> DataDesigns()
    {
        ProductsLayout l = Probe;
        var alts = new int[l.SlotCount];
        for (int g = 1; g <= l.Generations; g++)
        {
            for (int k = g; k <= l.Generations; k++)
            {
                if (DesignRules.Superior(l, g, l.KnownUpTo(k), alts))
                    yield return (g, DesignRules.PrcSet(l, alts), DesignRules.AfClass(l, alts), DesignRules.PuRank(l, alts));
                if (DesignRules.Producible(l, g, l.KnownUpTo(k), _ => Fx6.One, F(500_000), null, alts))
                    yield return (g, DesignRules.PrcSet(l, alts), DesignRules.AfClass(l, alts), DesignRules.PuRank(l, alts));
            }
        }
    }

    private static Fx6 Part(int[] prc, int af, int pu, Fx6 mult)
    {
        int[] puPrc = DesignRules.Block(Probe, prc, airframe: false), afPrc = DesignRules.Block(Probe, prc, airframe: true);
        return ProductsMath.ProcessPart(DesignRules.BaseCostSum(Probe, puPrc), mult, Probe.ProcScalePu[pu - 1],
            DesignRules.BaseCostSum(Probe, afPrc), mult, Probe.ProcScaleAf[af - 1]);
    }

    private static CostCard CardOf(int af, int pu, int[] prc, Fx6 mult) =>
        new(Probe.HFirst[af - 1], Probe.HFloor[af - 1], Probe.WageKh, Probe.MaterialsFloor[af - 1], Part(prc, af, pu, mult), new ProductsParams().LearningQ10);

    private static Fx6 FloorOf(int af, int pu, int[] prc) =>
        ProductsMath.UnitCostFloor(Probe.MaterialsFloor[af - 1], Probe.HFloor[af - 1], Probe.WageKh,
            DesignRules.BaseCostSum(Probe, DesignRules.Block(Probe, prc, airframe: false)), Probe.ProcScalePu[pu - 1],
            DesignRules.BaseCostSum(Probe, DesignRules.Block(Probe, prc, airframe: true)), Probe.ProcScaleAf[af - 1]);

    [Fact]
    public void DataDesigns_CoverEveryGeneration()
    {
        var gens = DataDesigns().Select(x => x.Gen).Distinct().Order().ToArray();
        Assert.Equal(Enumerable.Range(1, Probe.Generations), gens);
    }

    [Fact]
    public void UnitCost_NonIncreasingInLineExperience_NeverBelowFloor_DataDesigns()
    {
        var rnd = new Random(44);
        foreach ((int g, int[] prc, int af, int pu) in DataDesigns())
        {
            foreach (Fx6 mult in new[] { Fx6.One, F(1_250_000), Rnd(rnd, 1_000_000, 20_000_000) })
            {
                CostCard card = CardOf(af, pu, prc, mult);
                Fx6 floor = card.MaterialsFloor + ProductsMath.AssemblyCost(card.HFloor, card.WageKh) + card.ProcessPart;
                Fx6 prev = ProductsMath.UnitCost(card, Fx6.Zero);
                long e = 0;
                while (e < 2_000_000_000_000L) // up to E = 2·10⁶ units
                {
                    e += 1 + e / 3 + rnd.NextInt64(0, 400_000);
                    Fx6 cost = ProductsMath.UnitCost(card, F(e));
                    Assert.True(cost <= prev, $"G{g}: unit_cost rose at E raw {e}: {prev} → {cost}");
                    Assert.True(cost >= floor, $"G{g}: unit_cost {cost} under its floor {floor} at E raw {e}");
                    prev = cost;
                }
            }
            // unit_cost_floor of the design (yield 1, E → ∞) equals the card floor at unit_cost_mult = 1.
            CostCard one = CardOf(af, pu, prc, Fx6.One);
            Assert.Equal(one.MaterialsFloor + ProductsMath.AssemblyCost(one.HFloor, one.WageKh) + one.ProcessPart,
                FloorOf(af, pu, prc));
        }
    }

    [Fact]
    public void UnitCost_NonIncreasingInLineExperience_RandomCards()
    {
        var rnd = new Random(45);
        for (int i = 0; i < 2000; i++)
        {
            Fx6 hFloor = Rnd(rnd, 0, 50_000_000_000);
            var card = new CostCard(hFloor + Rnd(rnd, 0, 100_000_000_000), hFloor, Rnd(rnd, 1, 2_000), Rnd(rnd, 0, 5_000_000),
                Rnd(rnd, 0, 50_000_000), rnd.Next(1, 1024));
            Fx6 e1 = Rnd(rnd, 0, 1_000_000_000_000), e2 = e1 + Rnd(rnd, 0, 1_000_000_000_000);
            Assert.True(ProductsMath.UnitCost(card, e2) <= ProductsMath.UnitCost(card, e1), $"case {i}: {card} E {e1} → {e2}");
        }
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProductsMath.UnitCost(new CostCard(Fx6.One, Fx6.One, Fx6.One, Fx6.One, Fx6.One, 330), -Fx6.One));
    }

    [Fact]
    public void Upkeep_Positive_ForEveryDataDesign_AndNonDecreasingInComplexity()
    {
        var p = new ProductsParams();
        foreach ((int g, int[] prc, int af, int pu) in DataDesigns())
        {
            Fx6 floor = FloorOf(af, pu, prc);
            int c = DesignRules.Complexity(Probe, prc);
            Fx6 upkeep = ProductsMath.Upkeep(p.UpkeepShare, floor, c, p.UpkeepQ10, Probe.Maintainability);
            Assert.True(upkeep > Fx6.Zero, $"G{g} complexity {c}: upkeep {upkeep}");
            // Upkeep is a small share of the floor price per month (§8.1: 0.004·unit_cost·√complexity).
            Assert.True(upkeep < floor, $"G{g}: upkeep {upkeep} ≥ floor {floor}");
        }
    }

    [Fact]
    public void Upkeep_RandomInputs_PositiveAndMonotone()
    {
        var rnd = new Random(46);
        var p = new ProductsParams();
        for (int i = 0; i < 3000; i++)
        {
            // Floors from the smallest data design (≈ 1400 raw) up; complexity 1…30; maintainability 0.5…2.
            Fx6 floor = Rnd(rnd, 1_000, 2_000_000_000);
            int c = rnd.Next(1, 31);
            Fx6 m = Rnd(rnd, 500_000, 2_000_000);
            Fx6 u = ProductsMath.Upkeep(p.UpkeepShare, floor, c, p.UpkeepQ10, m);
            Assert.True(u > Fx6.Zero, $"case {i}: floor {floor} c {c} m {m}");
            Assert.True(ProductsMath.Upkeep(p.UpkeepShare, floor, c + 1, p.UpkeepQ10, m) >= u, $"case {i}: complexity");
            Assert.True(ProductsMath.Upkeep(p.UpkeepShare, floor + Rnd(rnd, 0, 1_000_000), c, p.UpkeepQ10, m) >= u, $"case {i}: floor");
        }
    }

    [Fact]
    public void Readiness_InUnitRange_NonIncreasingInDefects()
    {
        var rnd = new Random(47);
        for (int i = 0; i < 5000; i++)
        {
            Fx6 paid = Rnd(rnd, 0, 1_500_000); // a paid ratio above 1 is capped
            Fx6 q = Rnd(rnd, 0, 1_000_000);
            Fx6 psi = Rnd(rnd, 0, 1_000_000);
            Fx6 d0 = Rnd(rnd, 1, 500_000_000); // D_ref
            Fx6 dA = Rnd(rnd, 0, d0.Raw), dB = Rnd(rnd, dA.Raw, d0.Raw);
            Fx6 rA = ProductsMath.Readiness(paid, q, dA, d0, psi), rB = ProductsMath.Readiness(paid, q, dB, d0, psi);
            Assert.InRange(rA.Raw, 0, Fx6.Scale);
            Assert.InRange(rB.Raw, 0, Fx6.Scale);
            Assert.True(rB <= rA, $"case {i}: readiness rose with defects {dA} → {dB}");
            // No unfixed defects: the defect factor is 1.
            Fx6 capped = paid < Fx6.One ? paid : Fx6.One;
            Assert.Equal(capped * q, ProductsMath.Readiness(paid, q, Fx6.Zero, d0, psi));
            // D_ref scales the defect factor (contract §4.4b): a larger D_ref never lowers readiness.
            Assert.True(ProductsMath.Readiness(paid, q, dB, d0 + Rnd(rnd, 0, 500_000_000), psi) >= rB);
            // Monotone in paid ratio too.
            Assert.True(ProductsMath.Readiness(paid + Rnd(rnd, 0, 500_000), q, dA, d0, psi) >= rA);
        }
    }

    [Fact]
    public void PaidRatio_IsOneWithoutNeed_AndAtMostOneWhenPaidLeqNeed()
    {
        var rnd = new Random(48);
        Assert.Equal(Fx6.One, ProductsMath.PaidRatio(Fx6.Zero, Fx6.Zero));
        for (int i = 0; i < 2000; i++)
        {
            Fx6 need = Rnd(rnd, 1, 10_000_000_000), paid = Rnd(rnd, 0, need.Raw);
            Assert.InRange(ProductsMath.PaidRatio(paid, need).Raw, 0, Fx6.Scale);
        }
    }

    [Fact]
    public void DetectedTotal_BoundedByD0_NonDecreasingInHours()
    {
        var rnd = new Random(49);
        var p = new ProductsParams();
        for (int i = 0; i < 3000; i++)
        {
            Fx6 d0 = Rnd(rnd, 0, 1_000_000_000);
            Fx6 h1 = Rnd(rnd, 0, 1_000_000_000_000), h2 = h1 + Rnd(rnd, 0, 100_000_000_000);
            Fx6 f1 = ProductsMath.DetectedTotal(h1, d0, p.LambdaD, p.BetaQ10), f2 = ProductsMath.DetectedTotal(h2, d0, p.LambdaD, p.BetaQ10);
            Assert.InRange(f1.Raw, 0, d0.Raw);
            Assert.InRange(f2.Raw, 0, d0.Raw);
            Assert.True(f2 >= f1, $"case {i}: detection fell with hours");
        }
        Assert.Equal(Fx6.Zero, ProductsMath.DetectedTotal(Fx6.Zero, Fx6.FromInt(10), p.LambdaD, p.BetaQ10));
    }

    [Fact]
    public void InitialDefectsAndDesignWork_FallWithBureauExperience()
    {
        var rnd = new Random(50);
        var p = new ProductsParams();
        for (int i = 0; i < 2000; i++)
        {
            int c = rnd.Next(1, 30);
            Fx6 novelty = Rnd(rnd, 0, 1_000_000), scale = Rnd(rnd, 100_000, 3_000_000);
            Fx6 e1 = Rnd(rnd, 0, 20_000_000), e2 = e1 + Rnd(rnd, 0, 5_000_000);
            Fx6 d1 = ProductsMath.InitialDefects(p.D0Factor, novelty, c, e1, p.EB), d2 = ProductsMath.InitialDefects(p.D0Factor, novelty, c, e2, p.EB);
            Assert.True(d1 >= Fx6.Zero && d2 <= d1, $"case {i}: D0");
            Fx6 w1 = ProductsMath.DesignWork(p.W0, c, scale, e1, p.EB), w2 = ProductsMath.DesignWork(p.W0, c, scale, e2, p.EB);
            Assert.True(w1 > Fx6.Zero && w2 <= w1, $"case {i}: W");
        }
    }

    [Fact]
    public void FleetCount_MultipleOfStep_MonotoneInBudgetAndUpkeep()
    {
        var rnd = new Random(51);
        var p = new ProductsParams();
        (int g, int[] prc, int af, int pu)[] designs = [.. DataDesigns()];
        for (int i = 0; i < 300; i++)
        {
            (int g, int[] prc, int af, int pu) = designs[rnd.Next(designs.Length)];
            CostCard card = CardOf(af, pu, prc, Rnd(rnd, 1_000_000, 3_000_000));
            Fx6 e = Rnd(rnd, 0, 500_000_000);
            Fx6 upkeep = Rnd(rnd, 1, 100);
            Fx6 b1 = Rnd(rnd, 0, 2_000_000), b2 = b1 + Rnd(rnd, 0, 1_000_000);
            int n1 = ProductsMath.FleetCount(card, e, upkeep, p.FvHorizon, b1, p.FvStep, 2_000);
            int n2 = ProductsMath.FleetCount(card, e, upkeep, p.FvHorizon, b2, p.FvStep, 2_000);
            int n3 = ProductsMath.FleetCount(card, e, upkeep + Rnd(rnd, 0, 50), p.FvHorizon, b1, p.FvStep, 2_000);
            Assert.Equal(0, n1 % p.FvStep);
            Assert.InRange(n1, 0, 2_000);
            Assert.True(n2 >= n1, $"case {i}: n fell with budget");
            Assert.True(n3 <= n1, $"case {i}: n rose with upkeep");
            Assert.Equal(Fx6.Zero, ProductsMath.FleetValue(0, Fx6.One, Fx6.One, 512));
        }
    }

    [Fact]
    public void People_IsSmallestHeadcountCoveringTheHours()
    {
        var rnd = new Random(52);
        Fx6 hpm = Probe.HPm;
        for (int i = 0; i < 3000; i++)
        {
            Fx6 hours = Rnd(rnd, 0, 100_000_000_000);
            long n = ProductsMath.People(hours, hpm);
            Assert.True(Fx6.FromInt((int)n) * hpm >= hours);
            Assert.True(n == 0 || Fx6.FromInt((int)(n - 1)) * hpm < hours);
        }
        Assert.Equal(0, ProductsMath.People(-Fx6.One, hpm));
    }
}
