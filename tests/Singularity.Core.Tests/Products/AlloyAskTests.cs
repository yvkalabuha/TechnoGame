using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Products;

public class AlloyAskTests
{
    [Fact]
    public void AnOrdinaryAsk_IsTheProduct() =>
        Assert.Equal(Fx6.FromInt(2900).Raw, ProductsMath.AlloyAskRaw(Fx6.FromInt(10), Fx6.FromInt(290), long.MaxValue));

    [Fact]
    public void AHugeRate_SaturatesAtTheCap_InsteadOfOverflowing()
    {
        Fx6 trillion = Fx6.FromInt(1_000_000_000_000);
        Assert.Throws<OverflowException>(() => (trillion * Fx6.FromInt(290)).Raw);   // the plain product is what overflowed
        Assert.Equal(1_000_000L, ProductsMath.AlloyAskRaw(trillion, Fx6.FromInt(290), 1_000_000L));
        Assert.Equal(long.MaxValue / 8, ProductsMath.AlloyAskRaw(Fx6.FromRaw(long.MaxValue), Fx6.FromInt(290), long.MaxValue / 8));
    }
}
