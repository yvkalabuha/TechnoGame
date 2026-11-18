namespace Singularity.Session.Tests;

/// <summary>TASK-0103 review: per-thread scratch must not carry the parameters of one world into another run on the same thread.</summary>
public class CrossWorldScratchTests
{
    private static SliceSession Make(SaleArm arm) => SliceTestData.Create(new SliceRunConfig { Seed = 1, SaleArm = arm, HashEveryTick = true });

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
}
