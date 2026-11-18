using Singularity.Core.Products;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>ADR-0005 p. 3 (rev 3, #388): slots are reused with a generation counter; a handle is (slot, generation).</summary>
public class ProductsSlotGenerationTests
{
    private const int Gbr = 1;

    private static DesignCommand SuperiorG2() => Compose(Probe, 2, "eng_g2", "hot_sa2_forged", "comp_axial", "af_swept", "ab_on");

    private static ProductsHarness Stand(int threads, int chunkSize, int maxDesigns, Func<int, long, IReadOnlyList<DesignCommand>> commands) =>
        new(threads, chunkSize, startLine: false, maxDesigns: maxDesigns)
        {
            Knows = (a, n) => a == Gbr && Probe.KnownUpTo(2)(n),
            Commands = commands,
        };

    // Start fleet in slot 0; t0 compose -> slot 1; t2 cancel (generation 1); t3 compose -> the freed slot 1 again;
    // t5 cancel with the old handle; t6 derive from slot 0 with generation 0; t7 retire with the old handle of slot 0.
    private static IReadOnlyList<DesignCommand> Script(int a, long t)
    {
        if (a != Gbr)
            return [];
        return t switch
        {
            0 or 3 => [SuperiorG2()],
            2 => [new DesignCommand(DesignCommandKind.Cancel, 1, [], Generation: 1)],
            5 => [new DesignCommand(DesignCommandKind.Cancel, 1, [], Generation: 1)],
            6 => [new DesignCommand(DesignCommandKind.Derive, 0, [.. Enumerable.Repeat(-1, Probe.SlotCount - 1), Alt(Probe, "ab_none")], Generation: 0)],
            _ => [],
        };
    }

    private static ProductsHarness ScriptedRun(int threads = 1, int chunkSize = 1, int months = 8)
    {
        ProductsHarness h = Stand(threads, chunkSize, 3, Script);
        h.FleetScript = (a, t) => a == Gbr && t == 7 ? [new FleetCommand(FleetCommandKind.Retire, 0, 1, Generation: 0)] : [];
        for (long t = 0; t < months; t++)
            h.RunThroughMonth(t);
        return h;
    }

    [Fact]
    public void FreedSlot_IsTheLowestFree_AndRetakenWithGenerationPlusOne()
    {
        ProductsHarness h = Stand(1, 1, 3, Script);
        ProductsSystem p = h.Products;
        Assert.Equal(1, p.Generation(Gbr, 0));   // the scenario start counts as the first taking
        Assert.Equal(0, p.Generation(Gbr, 1));
        h.RunThroughMonth(0);
        Assert.Equal(DesignStage.Project, p.Stage(Gbr, 1));
        Assert.Equal(1, p.Generation(Gbr, 1));
        for (long t = 1; t <= 2; t++)
            h.RunThroughMonth(t);
        Assert.Equal(DesignStage.Withdrawn, p.Stage(Gbr, 1));
        Assert.Equal(1, p.Generation(Gbr, 1));   // freeing does not change the generation
        Assert.False(p.Resolves(Gbr, 1, 1));     // a withdrawn slot resolves to nothing
        h.RunThroughMonth(3);
        Assert.Equal(DesignStage.Project, p.Stage(Gbr, 1));   // lowest free, not slot 2
        Assert.Equal(2, p.Generation(Gbr, 1));
        Assert.Equal(DesignStage.None, p.Stage(Gbr, 2));
        Assert.Equal(0, p.Generation(Gbr, 2));
        Assert.True(p.Resolves(Gbr, 1, 2));
        Assert.False(p.Resolves(Gbr, 1, 1));
        // The retaken slot carries nothing of the first design.
        Assert.False(p.Cancelled(Gbr, 1));
    }

    [Fact]
    public void StaleHandle_IsRejectedWithWhyNot_AndChangesNothing()
    {
        ProductsHarness h = ScriptedRun(months: 6);
        ProductsSystem p = h.Products;
        Assert.Equal(DesignStage.Project, p.Stage(Gbr, 1));   // the cancel of month 5 named generation 1; the slot holds generation 2
        Assert.Equal(1, p.RejectionCount(Gbr, CommandRejection.StaleHandle));
        Assert.Equal(0, p.RejectionCount(Gbr, CommandRejection.BadCancel));
        h.RunThroughMonth(6);   // derive with a stale handle of slot 0
        Assert.Equal(2, p.RejectionCount(Gbr, CommandRejection.StaleHandle));
        Assert.Equal(0, p.RejectionCount(Gbr, CommandRejection.BadDerive));
        Assert.Equal(DesignStage.None, p.Stage(Gbr, 2));
        h.RunThroughMonth(7);   // fleet command with a stale handle
        Assert.Equal(3, p.RejectionCount(Gbr, CommandRejection.StaleHandle));
        Assert.Equal(0, p.RejectionCount(Gbr, CommandRejection.BadRetireStage));
    }

    [Fact]
    public void CurrentHandle_IsAccepted()
    {
        ProductsHarness h = Stand(1, 1, 3, (a, t) => a == Gbr && t == 0 ? [SuperiorG2()] : a == Gbr && t == 1
            ? [new DesignCommand(DesignCommandKind.Cancel, 1, [], Generation: 1)] : []);
        for (long t = 0; t <= 1; t++)
            h.RunThroughMonth(t);
        Assert.Equal(DesignStage.Withdrawn, h.Products.Stage(Gbr, 1));
        Assert.Equal(0, h.Products.RejectionCount(Gbr, CommandRejection.StaleHandle));
    }

    [Fact]
    public void Creation_IsRejectedOnlyWhenEverySlotIsTaken()
    {
        // max_designs = 2: slot 0 is the start fleet. t0 takes slot 1; t1 finds none; t2 cancels it; t3 finds the freed slot.
        ProductsHarness h = Stand(1, 1, 2, (a, t) => a != Gbr ? [] : t switch
        {
            0 or 1 or 3 => [SuperiorG2()],
            2 => [new DesignCommand(DesignCommandKind.Cancel, 1, [], Generation: 1)],
            _ => [],
        });
        ProductsSystem p = h.Products;
        h.RunThroughMonth(0);
        h.RunThroughMonth(1);
        Assert.Equal(1, p.RejectionCount(Gbr, CommandRejection.DesignCapacity));
        Assert.Equal(1, p.Generation(Gbr, 1));
        h.RunThroughMonth(2);
        h.RunThroughMonth(3);
        Assert.Equal(1, p.RejectionCount(Gbr, CommandRejection.DesignCapacity));   // not rejected again
        Assert.Equal(DesignStage.Project, p.Stage(Gbr, 1));
        Assert.Equal(2, p.Generation(Gbr, 1));
    }

    [Fact]
    public void Generation_IsInTheStateHash_AndTheTable()
    {
        ProductsHarness withReuse = ScriptedRun(months: 4);
        Assert.Contains(withReuse.Table.Columns, c => c.Name == "prd.d1.generation");
        string before = withReuse.Hash();
        // Same state except the generation of slot 1: a different hash.
        ProductsHarness other = ScriptedRun(months: 4);
        Assert.Equal(before, other.Hash());
        ProductsHarness noReuse = Stand(1, 1, 3, (a, t) => a == Gbr && t == 0 ? [SuperiorG2()] : []);
        for (long t = 0; t < 4; t++)
            noReuse.RunThroughMonth(t);
        Assert.NotEqual(before, noReuse.Hash());
    }

    [Fact]
    public void L1_TwoRuns_GiveTheSameHashEveryMonth_AndTheSameHandles()
    {
        ProductsHarness a = Stand(1, 1, 3, Script), b = Stand(1, 1, 3, Script);
        for (long t = 0; t < 8; t++)
        {
            a.RunThroughMonth(t);
            b.RunThroughMonth(t);
            Assert.Equal(a.Hash(), b.Hash());
            for (int d = 0; d < 3; d++)
                Assert.Equal(a.Products.Generation(Gbr, d), b.Products.Generation(Gbr, d));
        }
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(4, 2)]
    [InlineData(8, 3)]
    public void L2_ThreadsAndChunks_GiveTheSameHashEveryMonth(int threads, int chunk)
    {
        ProductsHarness one = Stand(1, 1, 3, Script), many = Stand(threads, chunk, 3, Script);
        for (long t = 0; t < 8; t++)
        {
            one.RunThroughMonth(t);
            many.RunThroughMonth(t);
            Assert.Equal(one.Hash(), many.Hash());
        }
    }
}
