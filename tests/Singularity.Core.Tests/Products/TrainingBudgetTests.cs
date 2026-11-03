using Singularity.Core.Products;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>Contract v2.4 r3 §4.4d step 7 and F15: the training money is spent after the common money left, on its line only.</summary>
public class TrainingBudgetTests
{
    private const int Usa = 0, G1Sup = 0;

    private static ProductsHarness Run(Func<int, int, long, Fx6> training, long budgetRaw = 50_000)
    {
        var h = new ProductsHarness
        {
            Launch = (a, d, _) => a == Usa && d == G1Sup,
            Rate = (_, _, _) => Fx6.FromInt(50),
            Budget = (_, _) => Fx6.FromRaw(budgetRaw),
            Training = training,
        };
        h.RunThroughMonth(0);
        return h;
    }

    [Fact]
    public void ZeroTrainingBudget_GivesTheFormerStep7()
    {
        ProductsHarness h = Run((_, _, _) => Fx6.Zero);
        Assert.Equal(20_877_659, h.Products.Rate(Usa, G1Sup).Raw);   // as Series_IsLimitedByMoneyLeftAfterUpkeep
        Assert.Equal(Fx6.Zero, h.Products.TrainingSpent(Usa));
    }

    [Fact]
    public void TrainingMoney_ExtendsTheRate_AfterTheCommonLeft_AndStaysOutOfSpent()
    {
        ProductsHarness h = Run((a, d, _) => a == Usa && d == G1Sup ? Fx6.FromRaw(20_000) : Fx6.Zero);
        ProductsSystem p = h.Products;
        // (47 100 + 20 000)/2256 units: the common money is spent first, the rest comes from the training money.
        long r = (47_100 + 20_000) * Fx6.Scale / 2256;
        Assert.Equal(r, p.Rate(Usa, G1Sup).Raw);
        long pay = r * 2256 / Fx6.Scale;
        Assert.Equal(2900 + 47_100, p.Spent(Usa).Raw);
        Assert.Equal(pay - 47_100, p.TrainingSpent(Usa).Raw);
        Assert.True(p.TrainingSpent(Usa).Raw <= 20_000);
        Assert.True(p.Spent(Usa) <= Fx6.FromRaw(50_000));
    }

    [Fact]
    public void TrainingMoney_IsNotTouched_WhenTheCommonMoneyCoversTheLine()
    {
        ProductsHarness h = Run((_, _, _) => Fx6.FromInt(1_000), budgetRaw: 440_000);
        Assert.Equal(Fx6.FromInt(50), h.Products.Rate(Usa, G1Sup));
        Assert.Equal(Fx6.Zero, h.Products.TrainingSpent(Usa));
    }

    [Fact]
    public void TrainingMoney_GoesToItsLineOnly()
    {
        ProductsHarness h = Run((a, d, _) => a == Usa && d != G1Sup ? Fx6.FromInt(1_000_000) : Fx6.Zero);
        Assert.Equal(20_877_659, h.Products.Rate(Usa, G1Sup).Raw);
        Assert.Equal(Fx6.Zero, h.Products.TrainingSpent(Usa));
    }

    [Fact]
    public void NegativeTrainingBudget_Throws()
    {
        var h = new ProductsHarness
        {
            Launch = (a, d, _) => a == Usa && d == G1Sup,
            Rate = (_, _, _) => Fx6.FromInt(50),
            Training = (_, _, _) => Fx6.FromRaw(-1),
        };
        Exception e = Assert.ThrowsAny<Exception>(() => h.RunThroughMonth(0));
        Assert.IsType<ProductsInvariantException>(e.InnerException ?? e);
    }
}
