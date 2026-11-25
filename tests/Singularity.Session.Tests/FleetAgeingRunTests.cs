using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>TASK-0129 (#431): L1 and L2 over the whole horizon, where the overage lives and leaves against the replacement credit.</summary>
public class FleetAgeingRunTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private const long Months = 1020;

    private static (string Hash, long Credit, long Overage, long Retire, long Sell) Run(int threads, int chunkSize)
    {
        SliceSession s = SliceTestData.Create(SliceRunConfig.Legacy with { Seed = 3, Threads = threads, ChunkSize = chunkSize });
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
        // Refusals of the layer to the AI's fleet commands (BAD_RETIRE*, BAD_SELL*): the projection may miss the credit of the month's own output.
        long retire = 0, sell = 0;
        for (int a = 0; a < s.World.Scenario.ActorCount; a++)
        {
            foreach (CommandRejection r in Enum.GetValues<CommandRejection>())
            {
                string name = r.ToString();
                if (name.StartsWith("BadRetire", StringComparison.Ordinal))
                    retire += s.World.Products.RejectionCount(a, r);
                else if (name.StartsWith("BadSell", StringComparison.Ordinal))
                    sell += s.World.Products.RejectionCount(a, r);
            }
        }
        return (s.LastHash, credit, overage, retire, sell);
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
        output.WriteLine($"AI fleet commands refused over {Months} months: retire {one.Retire}, sell {one.Sell}");
        // The AI's projection does not see the credit that the month's own output earns (it never saw the output), so the layer may refuse a retire
        // whose overage the credit has already taken (BadRetireCount, no state change). Seed 3 gives 109; a sale promised by the projection must never be refused.
        Assert.Equal(0, one.Sell);
        Assert.True(one.Retire <= 220, $"{one.Retire} refused retire commands: the projection drifted from the layer");
        Assert.True(one.Credit > 0, "no replacement credit was ever earned: the replacement is not exercised");
    }
}
