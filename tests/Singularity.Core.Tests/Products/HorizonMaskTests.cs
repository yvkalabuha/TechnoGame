using Singularity.Core.Products;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>TASK-0121 (#423): the <c>q</c> mask in 62-bit blocks for cohort horizons beyond 62 years.</summary>
public class HorizonMaskTests
{
    private const int Gbr = 1;

    [Fact]
    public void Layout_YearsBeyondMaskBlocks_Throws()
    {
        Assert.Equal(124, MakeLayout(out _, 124, Scenario.MaxDesigns).Years);
        Assert.Throws<ArgumentOutOfRangeException>(() => MakeLayout(out _, 125, Scenario.MaxDesigns));
    }

    [Fact]
    public void QMask_YearIndex64_GoesToBlock1()
    {
        // Cohorts at year indices 5 (block 0), 61 and 62 (the block border), 64 (block 1); all mastered at t0.
        var h = new ProductsHarness(startLine: false, years: 70, isMastered: (_, _) => true, tweakStart: s =>
        {
            ProductsLayout l = s.Layout;
            var alts = new int[l.SlotCount];
            Assert.True(DesignRules.Superior(l, 2, l.KnownUpTo(2), alts));
            int row = Gbr * l.Designs + 1;
            s.Stage[row] = DesignStage.InService;
            s.PlatformGen[row] = 2;
            for (int k = 0; k < alts.Length; k++)
                s.Alts[row * l.SlotCount + k] = alts[k];
            foreach (int y in new[] { 5, 61, 62, 64 })
                s.Cohort[row * l.Years + y] = Fx6.FromInt(60);
        });
        ProductsSystem p = h.Products;
        foreach (int y in new[] { 5, 61, 62, 64 })
            Assert.True(p.CohortMastered(Gbr, 1, y), $"year index {y}");
        foreach (int y in new[] { 0, 4, 6, 60, 63, 65, 69 })
            Assert.False(p.CohortMastered(Gbr, 1, y), $"year index {y}");
        // Block 0 holds bits 5 and 61 only; block 1 holds bits 0 (index 62) and 2 (index 64).
        Assert.Equal((1L << 5) | (1L << 61), Raw(h, "prd.d1.q"));
        Assert.Equal((1L << 0) | (1L << 2), Raw(h, "prd.d1.q.1"));
    }

    private static long Raw(ProductsHarness h, string name) =>
        ((LongColumn)h.Table.Columns.Single(c => c.Name == name)).Current[Gbr];
}
