using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>TASK-0129 (#431): L1 and L2 over the whole horizon, where the overage lives and leaves against the replacement credit.</summary>
public class FleetAgeingRunTests
{
    private const long Months = 1020;

    private static (string Hash, long Credit, long Overage) Run(int threads, int chunkSize)
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 3, Threads = threads, ChunkSize = chunkSize });
        long credit = 0, overage = 0;
        for (long t = 0; t < Months; t++)
        {
            s.StepThroughMonth(t);
            if (t % 12 != 11)
                continue;
            for (int a = 0; a < s.World.Scenario.ActorCount; a++)
            {
                for (int d = 0; d < s.World.ProductsLayout.Designs; d++)
                {
                    credit += s.World.Products.ReplacementCredit(a, d).Raw;
                    // Units of cohorts past the service life that are still in the fleet.
                    for (int y = 0; y < s.World.ProductsLayout.Years; y++)
                    {
                        if (t - s.World.Products.CohortMidyear(y) >= s.World.Products.Parameters.ServiceLife)
                            overage += s.World.Products.Cohorts(a, d)[y].Raw;
                    }
                }
            }
        }
        return (s.LastHash, credit, overage);
    }

    [Fact]
    public void TheFullHorizon_IsTheSameInTwoRuns_AndForOneAndFourThreads_AndExercisesTheOverage()
    {
        var one = Run(1, 1);
        var again = Run(1, 1);
        var four = Run(4, 3);
        HashAssert.Equal(one.Hash, again.Hash, "L1: two runs of 1020 months");
        HashAssert.Equal(one.Hash, four.Hash, "L2: threads 1 vs 4 over 1020 months");
        Assert.True(one.Overage > 0, "the run never held units past their service life: the ageing is not exercised");
        Assert.True(one.Credit > 0, "no replacement credit was ever earned: the replacement is not exercised");
    }
}
