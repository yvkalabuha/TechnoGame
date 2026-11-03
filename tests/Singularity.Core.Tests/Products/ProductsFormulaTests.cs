using Singularity.Core.Products;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>
/// Product formulas (contract §4.4; spec §3.3, §3.5 A28, §5.5) against explicit values from
/// <c>Reference/products_refs.py</c> (spec formulas in Fx6 raw, PowFrac ported op by op). The reference model
/// lab/reference/jet_slice/ does not cover the product layer; simulation-engineer confirms these numbers (D-tests).
/// </summary>
public class ProductsFormulaTests
{
    private static Fx6 R(long raw) => Fx6.FromRaw(raw);

    // G1 «превосходящий», r3 data: Σ n_p·base_cost_p = 928 raw, proc_scale 0.942188 → process part 874 raw at mult 1.
    private static readonly CostCard G1 = new(Fx6.FromInt(9320), Fx6.FromInt(2330), R(125), R(217),
        ProductsMath.ProcessPart(R(928), Fx6.One, R(942_188)), 330);

    [Theory]
    [InlineData(0, 9_320_000_000L, 2256)]
    [InlineData(1, 7_920_685_880L, 2081)]
    [InlineData(10, 5_557_541_630L, 1785)]
    [InlineData(100, 3_909_621_170L, 1579)]
    [InlineData(1000, 3_084_290_900L, 1476)]
    [InlineData(10000, 2_689_251_050L, 1427)]
    public void AssemblyHoursAndUnitCost_G1(int e, long hoursRaw, long unitCostRaw)
    {
        Assert.Equal(874, G1.ProcessPart.Raw);
        Assert.Equal(hoursRaw, ProductsMath.AssemblyHours(Fx6.FromInt(e), G1.HFirst, G1.HFloor, 330).Raw);
        Assert.Equal(unitCostRaw, ProductsMath.UnitCost(G1, Fx6.FromInt(e)).Raw);
    }

    [Fact]
    public void AssemblyCost_IsOneRoundingPer1000Hours_A28()
    {
        // ⌊hours_raw·wage_kh_raw/10⁹⌋: 2330 h · 0.000125/1000 h = 0.00029125 → 291 raw (two floors would give the same here,
        // 1 h gives 0.000000125 → 0, 8 h → 1 raw).
        Assert.Equal(291, ProductsMath.AssemblyCost(Fx6.FromInt(2330), R(125)).Raw);
        Assert.Equal(0, ProductsMath.AssemblyCost(Fx6.FromInt(1), R(125)).Raw);
        Assert.Equal(1, ProductsMath.AssemblyCost(Fx6.FromInt(8), R(125)).Raw);
        Assert.Equal(1382, ProductsMath.UnitCostFloor(R(217), Fx6.FromInt(2330), R(125), R(928), R(942_188)).Raw);
    }

    [Fact]
    public void Upkeep_G1()
    {
        // ((1382·PowFrac(29, 512))·0.004)/1 = 29 raw per unit and month.
        Assert.Equal(29, ProductsMath.Upkeep(R(4_000), R(1382), 29, 512, Fx6.One).Raw);
    }

    [Theory]
    [InlineData(1, 29, 1_000_000, 11_600)]
    [InlineData(2, 37, 1_110_000, 16_428)]
    [InlineData(3, 48, 1_820_000, 34_944)]
    [InlineData(4, 49, 3_120_000, 61_152)]
    public void DesignWork_OfSuperiorDesigns_MatchesR3(int gen, int complexity, long designScaleRaw, long engineerMonths)
    {
        // product_inputs.md r3 §9.2: W = W0·complexity·design_scale at bureau_exp = 0.
        Assert.Equal(designScaleRaw, Probe.DesignScale[gen - 1].Raw);
        Assert.Equal(Fx6.FromInt(engineerMonths), ProductsMath.DesignWork(Fx6.FromInt(400), complexity, R(designScaleRaw), Fx6.Zero, Fx6.FromInt(3)));
        // bureau_exp = 3 = E_B halves the work.
        Assert.Equal(Fx6.FromInt(engineerMonths / 2), ProductsMath.DesignWork(Fx6.FromInt(400), complexity, R(designScaleRaw), Fx6.FromInt(3), Fx6.FromInt(3)));
    }

    [Fact]
    public void InitialDefects_AndDetection()
    {
        Assert.Equal(Fx6.FromInt(580), ProductsMath.InitialDefects(Fx6.FromInt(20), Fx6.One, 29, Fx6.Zero, Fx6.FromInt(3)));
        Assert.Equal(Fx6.FromInt(232), ProductsMath.InitialDefects(Fx6.FromInt(20), R(800_000), 29, Fx6.FromInt(3), Fx6.FromInt(3)));
        // λ·PowFrac(T, 512): T = 10 000 h → 5; T = 200 h → 0.707106; capped at D0.
        Assert.Equal(Fx6.FromInt(5), ProductsMath.DetectedTotal(Fx6.FromInt(10_000), Fx6.FromInt(580), R(50_000), 512));
        Assert.Equal(707_106, ProductsMath.DetectedTotal(Fx6.FromInt(200), Fx6.FromInt(580), R(50_000), 512).Raw);
        Assert.Equal(Fx6.FromInt(2), ProductsMath.DetectedTotal(Fx6.FromInt(10_000), Fx6.FromInt(2), R(50_000), 512));
        Assert.Equal(Fx6.Zero, ProductsMath.DetectedTotal(Fx6.Zero, Fx6.FromInt(580), R(50_000), 512));
    }

    [Fact]
    public void Readiness_IsPaidTimesQTimesDefectFactor()
    {
        // (min(1, 0.5)·0.9)·(1 − (0.5·290)/580) = 0.45·0.75.
        Assert.Equal(R(337_500), ProductsMath.Readiness(R(500_000), R(900_000), Fx6.FromInt(290), Fx6.FromInt(580), R(500_000)));
        Assert.Equal(R(900_000), ProductsMath.Readiness(Fx6.FromInt(2), R(900_000), Fx6.Zero, Fx6.Zero, R(500_000)));
        Assert.Equal(Fx6.One, ProductsMath.PaidRatio(Fx6.Zero, Fx6.Zero));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(175, 1)]
    [InlineData(176, 2)]
    [InlineData(18_750, 108)]
    public void People_IsCeilingOfHoursOverHpm(int hours, long people) =>
        Assert.Equal(people, ProductsMath.People(Fx6.FromInt(hours), Fx6.FromInt(175)));

    [Theory]
    [InlineData(0)]
    [InlineData(500_000)]
    [InlineData(5_000_000)]
    public void FleetCount_IsTheLargestBlockCountWithinBudget(long budgetRaw)
    {
        Fx6 budget = R(budgetRaw), upkeep = R(29);
        int n = ProductsMath.FleetCount(G1, Fx6.Zero, upkeep, 180, budget, 10, 20_000);
        Assert.Equal(0, n % 10);
        Assert.True(Cost(n) <= budget);
        Assert.True(n == 20_000 || Cost(n + 10) > budget);
        Assert.Equal(R(n * 1_000_000L) * R(500_000) * R(2_000_000), ProductsMath.FleetValue(n, R(500_000), R(2_000_000)));

        Fx6 Cost(int units)
        {
            Fx6 sum = Fx6.Zero;
            for (int k = 0; k < units; k += 10)
                sum += Fx6.FromInt(10) * ProductsMath.UnitCost(G1, Fx6.FromInt(k + 1));
            return sum + Fx6.FromInt(units) * upkeep * Fx6.FromInt(180);
        }
    }
}
