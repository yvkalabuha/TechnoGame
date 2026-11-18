using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Products;

/// <summary>TASK-0103 F5: a refilled book (<c>Clear</c>) is indistinguishable from a new one, so the policy may reuse one book per thread.</summary>
public class FleetBookClearTests
{
    [Fact]
    public void Clear_ReturnsEveryFieldToTheStateOfANewBook()
    {
        int[] midyears = [0, 12, 24];
        var used = new FleetBook(new ProductsParams(), midyears, 3);
        for (int d = 0; d < 3; d++)
        {
            for (int y = 0; y < 3; y++)
                used.Counts[d][y] = Fx6.FromInt(10 + d + y);
            used.Stage[d] = DesignStage.InService;
            used.Gen[d] = 2;
            used.Perf[d] = Fx6.One;
            used.Upkeep[d] = Fx6.One;
            used.UnitCostFloor[d] = Fx6.One;
            used.Bind[d] = LineBind.Money;
            used.Excess[d] = used.InDefK[d] = used.SellSlot[d] = used.RetireSlot[d] = true;
            used.Retired[d] = used.Sold[d] = Fx6.One;
            used.QuotaBase[d] = used.SoldY[d] = 5;
        }
        used.SaleY = Fx6.One;
        used.SaleCap = Fx6.One;
        used.ResetYear(Fx6.FromInt(100));
        used.Clear();

        var fresh = new FleetBook(new ProductsParams(), midyears, 3);
        Assert.Equal(fresh.Counts, used.Counts);
        Assert.Equal(fresh.Stage, used.Stage);
        Assert.Equal(fresh.Gen, used.Gen);
        Assert.Equal(fresh.Perf, used.Perf);
        Assert.Equal(fresh.Upkeep, used.Upkeep);
        Assert.Equal(fresh.UnitCostFloor, used.UnitCostFloor);
        Assert.Equal(fresh.Bind, used.Bind);
        Assert.Equal(fresh.Excess, used.Excess);
        Assert.Equal(fresh.InDefK, used.InDefK);
        Assert.Equal(fresh.Retired, used.Retired);
        Assert.Equal(fresh.Sold, used.Sold);
        Assert.Equal(fresh.QuotaBase, used.QuotaBase);
        Assert.Equal(fresh.SoldY, used.SoldY);
        Assert.Equal(fresh.SellSlot, used.SellSlot);
        Assert.Equal(fresh.RetireSlot, used.RetireSlot);
        Assert.Equal((fresh.SaleY, fresh.SaleCap, fresh.SaleIncome), (used.SaleY, used.SaleCap, used.SaleIncome));
    }
}
