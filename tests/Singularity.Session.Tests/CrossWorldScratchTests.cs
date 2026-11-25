using Singularity.Core.Products;

namespace Singularity.Session.Tests;

/// <summary>TASK-0103 review: per-thread scratch must not carry the parameters of one world into another run on the same thread.</summary>
public class CrossWorldScratchTests
{
    private static SliceSession Make(SaleArm arm) => SliceTestData.Create(SliceRunConfig.Legacy with { Seed = 1, SaleArm = arm, HashEveryTick = true });

    [Fact]
    public void TwoWorldsWithDifferentSaleArm_RunAlternatelyOnOneThread_GiveTheHashesOfTheirOwnRuns()
    {
        const int months = 30;
        string Alone(SaleArm arm)
        {
            SliceSession s = Make(arm);
            s.StepThroughMonth(months);
            return s.LastHash;
        }
        string a0 = Alone(SaleArm.None), b0 = Alone(SaleArm.ChiOff);
        SliceSession a = Make(SaleArm.None), b = Make(SaleArm.ChiOff);
        for (long m = 0; m <= months; m++)
        {
            a.StepThroughMonth(m);
            b.StepThroughMonth(m);
        }
        HashAssert.Equal(a0, a.LastHash);
        HashAssert.Equal(b0, b.LastHash);
    }

    [Fact]
    public void SnapshotBook_RefillsABookOfItsOwnWorld_ButNeverOneOfAnotherWorld()
    {
        SliceWorld w1 = Make(SaleArm.None).World, w2 = Make(SaleArm.SigmaLo).World;
        FleetBook own = w1.Products.SnapshotBook(0, 3);
        FleetBook other = w2.Products.SnapshotBook(0, 3);
        Assert.NotEqual(own.Parameters, other.Parameters);                       // the arm reaches the parameters of the book
        Assert.Same(own, w1.Products.SnapshotBook(0, 3, own));                   // same world: refilled in place
        FleetBook result = w2.Products.SnapshotBook(0, 3, own);
        Assert.NotSame(own, result);                                             // another world's book is not reused
        Assert.Same(other.Parameters, result.Parameters);                        // and the result carries the parameters of its own world
    }
}
