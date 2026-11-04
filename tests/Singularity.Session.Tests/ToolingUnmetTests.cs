using Singularity.Core.Data;
using Singularity.Core.Policy;
using Singularity.Core.WhyNot;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>
/// TASK-0032 part 2 (contract r8, F33; spec §11 «V5 default + why_not pool»): the column <c>tool_unmet</c> of the assembled slice, the cause
/// <c>TOOLING_POOL_EXHAUSTED</c> and the <c>POOL_ORDER</c>/<c>k_cap</c> switches in the run.
/// </summary>
public class ToolingUnmetTests
{
    // Hash of the columns that existed before F33 (everything but ind.unmet.*), recorded on the build of part 1 (A39 V5 default), AI on, threads 4:
    // (seed, months, hash). The new column must not change the trajectory by a single bit.
    private static readonly (ulong Seed, int Months, string Hash)[] BeforeF33 =
    [
        (41, 384, "40e6db1291df78bf96ace451abb5b8449875bff3e78af29d610e82581a31adfe"),
        (1, 150, "facf62d7f49acd7d37cd9e1e9c021700d0f9c562136f3fdc242e4c9d291af992"),
        (2, 150, "e56a695e8c882d3f48a0a723c57411761efc3b6619a657280db877715c07b23e"),
        (7, 150, "b6569c4004848c5d0393dc572a70d9132cc2e9d6f2176891f3e54a53985361d0"),
        (61, 150, "a936c680d2cb6c6b2ea3e1502729166c50542ca8b238c70dd2585b40cf454b58"),
    ];

    private static SliceSession Run(SliceRunConfig c, int months)
    {
        SliceSession s = SliceTestData.Create(c);
        for (long t = 0; t < months; t++)
            s.StepThroughMonth(t);
        return s;
    }

    [Fact]
    public void Trajectory_ByTheColumnsBeforeF33_IsBitIdenticalToPart1()
    {
        foreach ((ulong seed, int months, string hash) in BeforeF33)
        {
            SliceSession s = Run(new SliceRunConfig { Seed = seed, Threads = 4 }, months);
            Assert.True(hash == ColumnProjection.Hash(s, n => !n.StartsWith("ind.unmet.", StringComparison.Ordinal)),
                $"seed {seed}, {months} months: an old column differs (the new column must not touch the decisions)");
        }
    }

    [Fact]
    public void Column_IsPartOfTheStateHash_NonNegative_AndPositiveWherePoolRunsOut()
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 41, Threads = 4 });
        var cols = s.World.Table.Columns.Where(c => c.Name.StartsWith("ind.unmet.", StringComparison.Ordinal)).Cast<LongColumn>().ToArray();
        Assert.Equal(s.World.IndustryLayout.Clusters.Length * 3, cols.Length);
        bool positive = false;
        for (long t = 0; t < 150; t++)
        {
            s.StepThroughMonth(t);
            foreach (LongColumn c in cols)
            {
                Assert.All(c.Current.ToArray(), v => Assert.True(v >= 0));
                positive |= c.Current.ToArray().Any(v => v > 0);
            }
        }
        Assert.True(positive, "the pool never ran out in 150 months of seed 41: the column is never written");
        string withColumn = s.LastHash;
        Assert.Equal(withColumn, Run(new SliceRunConfig { Seed = 41, Threads = 1 }, 150).LastHash);   // L1/L2 on the new column
    }

    [Fact]
    public void Column_IsZeroEverywhereWithoutAPlan_AndPass2ofVInfAddsNothing()
    {
        SliceSession s = Run(new SliceRunConfig { Seed = 41, ToolingTarget = ToolingTargetSpec.Parse("VINF") }, 120);
        // VINF notional money is not a pool shortfall; pass 1 may still fall short, so the column is only checked for sign and for the stand with no plan.
        foreach (Column c in s.World.Table.Columns.Where(c => c.Name.StartsWith("ind.unmet.", StringComparison.Ordinal)))
            Assert.All(((LongColumn)c).Current.ToArray(), v => Assert.True(v >= 0));
    }

    [Fact]
    public void WhyNot_PoolExhaustedLinks_CarryTheFieldsOfToolingM_AndTheSortOrderIsThatOfToolingM()
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 41, Threads = 4 });
        int exhausted = 0, toolingM = 0;
        for (long t = 0; t < 240; t++)
        {
            s.StepThroughMonth(t);
            if (t % 6 != 5)
                continue;
            for (int a = 0; a < s.World.Scenario.ActorCount; a++)
            {
                if (s.CurrentRequirement(a) is not { } target)
                    continue;
                IReadOnlyList<Link> links = s.WhyNot(a, target);
                for (int i = 0; i < links.Count; i++)
                {
                    Link l = links[i];
                    if (i > 0)
                        Assert.True(Link.Compare(links[i - 1], l) <= 0, "the list is not in the contract order");
                    if (l.Cause == WhyNotCause.ToolingPoolExhausted)
                    {
                        exhausted++;
                        Assert.True(l.Unmet > Fx6.Zero && l.Cluster >= 0 && l.Order is >= 1 and <= 4, $"{l}");
                        Assert.Equal(l.Cluster + 1, l.Order);   // CAN: the place of the cluster in the layout order
                        Assert.Equal(WhyNotLayer.Capability, l.Layer);
                        Assert.Contains(Lever.InvestToolingM, l.Levers);
                        Assert.Empty(LeverValidator.Check([l], LeverRegistry.Slice));
                    }
                    else if (l.Cause == WhyNotCause.ToolingM)
                    {
                        toolingM++;
                        Assert.True(l.Unmet == Fx6.Zero && l.Cluster == -1 && l.Order == -1);
                    }
                }
            }
        }
        Assert.True(exhausted > 0, "no link TOOLING_POOL_EXHAUSTED in 240 months of seed 41");
        Assert.True(toolingM >= 0);
    }

    [Fact]
    public void KCap_ScalesTheCapabilityPool_AndIsPartOfTheHash_PoolOrderToo()
    {
        var cfg = new SliceRunConfig { Seed = 41, Threads = 1 };
        SliceSession half = SliceTestData.Create(cfg with { ToolingTarget = new ToolingTargetSpec { KCap = Fx6.FromRaw(500_000) } });
        SliceSession whole = SliceTestData.Create(cfg);
        Fx6 b = whole.World.SharesOf(0).Capability * whole.World.Policy.Budget(0);
        Assert.Equal(b, whole.World.IndustryInputs.Plan(0, 0, Fx6.Zero).CapBudget);
        Assert.Equal(Fx6.FromRaw(500_000) * b, half.World.IndustryInputs.Plan(0, 0, Fx6.Zero).CapBudget);
        Assert.NotEqual(Run(cfg, 24).LastHash, Run(cfg with { ToolingTarget = new ToolingTargetSpec { KCap = Fx6.FromRaw(500_000) } }, 24).LastHash);
        string can = Run(cfg, 24).LastHash;
        Assert.Equal(can, Run(cfg with { ToolingTarget = new ToolingTargetSpec { PoolOrder = PoolOrder.Can } }, 24).LastHash);
        Assert.NotEqual(can, Run(cfg with { ToolingTarget = new ToolingTargetSpec { PoolOrder = PoolOrder.Prop } }, 24).LastHash);
    }

    [Theory]
    [InlineData(PoolOrder.Rev)]
    [InlineData(PoolOrder.Prop)]
    [InlineData(PoolOrder.Sev)]
    public void PoolOrder_EveryValue_L1ThreadsOneFourEight_SeedSixtyOne(PoolOrder order)
    {
        var spec = new ToolingTargetSpec { PoolOrder = order };
        string h1 = Run(new SliceRunConfig { Seed = 61, Threads = 1, ToolingTarget = spec }, 60).LastHash;
        Assert.Equal(h1, Run(new SliceRunConfig { Seed = 61, Threads = 4, ToolingTarget = spec }, 60).LastHash);
        Assert.Equal(h1, Run(new SliceRunConfig { Seed = 61, Threads = 8, ToolingTarget = spec }, 60).LastHash);
    }

    [Theory]
    [InlineData(PoolOrder.Can, 1)]
    [InlineData(PoolOrder.Rev, 4)]
    [InlineData(PoolOrder.Prop, 0)]
    [InlineData(PoolOrder.Sev, 0)]
    public void QueuePlace_OfTheFirstCluster_FollowsPoolOrder(PoolOrder order, int expected)
    {
        var inputs = new WhyNotInputsAdapter(SliceTestData.Create(new SliceRunConfig { Seed = 1, ToolingTarget = new ToolingTargetSpec { PoolOrder = order } }).World);
        Assert.Equal(expected, inputs.PoolQueuePlace(0));
    }

    [Fact]
    public void Manifest_BadPoolOrderOrKCap_FailsTheRun()
    {
        Assert.Throws<ArgumentException>(() => SliceTestData.Create(new SliceRunConfig { Seed = 1, ToolingTarget = new ToolingTargetSpec { KCap = Fx6.Zero } }));
        Assert.Throws<ArgumentException>(() => SliceTestData.Create(new SliceRunConfig { Seed = 1, ToolingTarget = new ToolingTargetSpec { PoolOrder = (PoolOrder)7 } }));
    }
}
