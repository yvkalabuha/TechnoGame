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
    // t5 cancel with the old handle; t6 derive from slot 0 with a generation it never had; t7 retire with the old handle of slot 0.
    private static IReadOnlyList<DesignCommand> Script(int a, long t)
    {
        if (a != Gbr)
            return [];
        return t switch
        {
            0 or 3 => [SuperiorG2()],
            2 => [DesignCommand.Cancel(new DesignHandle(1, 1))],
            5 => [DesignCommand.Cancel(new DesignHandle(1, 1))],
            6 => [DesignCommand.Derive(new DesignHandle(0, 7), [.. Enumerable.Repeat(-1, Probe.SlotCount - 1), Alt(Probe, "ab_none")])],
            _ => [],
        };
    }

    private static ProductsHarness ScriptedRun(int threads = 1, int chunkSize = 1, int months = 8)
    {
        ProductsHarness h = Stand(threads, chunkSize, 3, Script);
        h.FleetScript = (a, t) => a == Gbr && t == 7 ? [new FleetCommand(FleetCommandKind.Retire, new DesignHandle(0, 7), 1)] : [];
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
        Assert.False(p.Resolves(Gbr, new DesignHandle(1, 1)));     // a withdrawn slot resolves to nothing
        h.RunThroughMonth(3);
        Assert.Equal(DesignStage.Project, p.Stage(Gbr, 1));   // lowest free, not slot 2
        Assert.Equal(2, p.Generation(Gbr, 1));
        Assert.Equal(DesignStage.None, p.Stage(Gbr, 2));
        Assert.Equal(0, p.Generation(Gbr, 2));
        Assert.True(p.Resolves(Gbr, new DesignHandle(1, 2)));
        Assert.False(p.Resolves(Gbr, new DesignHandle(1, 1)));
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
            ? [DesignCommand.Cancel(new DesignHandle(1, 1))] : []);
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
            2 => [DesignCommand.Cancel(new DesignHandle(1, 1))],
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
        Assert.Contains(ScriptedRun(months: 4).Table.Columns, c => c.Name == "prd.d1.generation");
        // Two states that differ only in the generation of the never used slot 2: the hash must differ.
        IReadOnlyList<DesignCommand> Commands(int a, long t) => a == Gbr && t == 0 ? [SuperiorG2()] : [];
        bool bumped = false;
        ProductsHarness plain = Stand(1, 1, 3, Commands);
        var changed = new ProductsHarness(1, 1, startLine: false, maxDesigns: 3, corruptInMerge: t =>
        {
            if (bumped)
                return;
            bumped = true;
            t.GetInt("prd.d2.generation").Next[Gbr] = 7;
        })
        {
            Knows = (a, n) => a == Gbr && Probe.KnownUpTo(2)(n),
            Commands = Commands,
        };
        for (long t = 0; t < 3; t++)
        {
            plain.RunThroughMonth(t);
            changed.RunThroughMonth(t);
        }
        Assert.Equal(DesignStage.None, changed.Products.Stage(Gbr, 2));
        Assert.Equal(7, changed.Products.Generation(Gbr, 2));
        Assert.NotEqual(plain.Hash(), changed.Hash());
    }

    // [Cancel d, Compose, Cancel d] in one step: slot choice reads Current only, so the new design takes the next None slot and the second
    // cancel does not touch it (before: it silently cancelled the new design in the freed slot).
    [Fact]
    public void CancelThenCompose_InOneStep_DoesNotReuseTheSlotUntilTheNextStep()
    {
        ProductsHarness h = Stand(1, 1, 4, (a, t) => a != Gbr ? [] : t switch
        {
            0 => [SuperiorG2()],
            2 => [DesignCommand.Cancel(new DesignHandle(1, 1)), SuperiorG2(),
                  DesignCommand.Cancel(new DesignHandle(1, 1))],
            _ => [],
        });
        ProductsSystem p = h.Products;
        for (long t = 0; t <= 2; t++)
            h.RunThroughMonth(t);
        Assert.Equal(DesignStage.Withdrawn, p.Stage(Gbr, 1));
        Assert.Equal(1, p.Generation(Gbr, 1));
        Assert.Equal(DesignStage.Project, p.Stage(Gbr, 2));   // the next None slot, not the slot freed in the same step
        Assert.Equal(1, p.Generation(Gbr, 2));
        Assert.False(p.Cancelled(Gbr, 2));
        Assert.Equal(1, p.RejectionCount(Gbr, CommandRejection.StaleHandle));
    }

    private static long ColumnValue(ProductsHarness h, string name) =>
        h.Table.Columns.Single(c => c.Name == name) switch
        {
            LongColumn l => l.Current[Gbr],
            IntColumn i => i.Current[Gbr],
            _ => throw new InvalidOperationException(name),
        };

    [Fact]
    public void ReuseAfterService_EveryPerDesignColumnStartsFromAnEmptySlot()
    {
        // Slot 0 (the start fleet, stage В) is retired to nothing, becomes «снят», and is retaken by the next project.
        ProductsHarness h = new(1, 1, startLine: false, maxDesigns: 2)
        {
            Knows = (a, n) => a == Gbr && Probe.KnownUpTo(2)(n),
            Commands = (a, t) => a == Gbr && t == 3 ? [SuperiorG2()] : [],
        };
        ProductsSystem p = h.Products;
        long whole = p.InService(Gbr, 0).Raw / Fx6.Scale;
        Assert.True(whole > 0);
        h.FleetScript = (a, t) => a == Gbr && t == 0 ? [new FleetCommand(FleetCommandKind.Retire, new DesignHandle(0, 1), whole)] : [];
        for (long t = 0; t <= 2; t++)
            h.RunThroughMonth(t);
        Assert.Equal(DesignStage.Withdrawn, p.Stage(Gbr, 0));
        Assert.Equal(Fx6.Zero, p.InService(Gbr, 0));
        Assert.True(p.Retired(Gbr, 0) > Fx6.Zero);
        Assert.Equal(1, p.Generation(Gbr, 0));
        h.RunThroughMonth(3);
        Assert.Equal(DesignStage.Project, p.Stage(Gbr, 0));
        Assert.Equal(2, p.Generation(Gbr, 0));
        // Slot 1 was never used: its state is what a freshly taken slot must hold in every column the take (not the order) sets.
        string[] suffixes = ["retired", "sold", "soldY", "quotaBase", "lossAcc", "hours", "lineE", "rate", "rateCmd", "lost", "inService0", "q",
            "cancelled", "builtAny", "idleC", "lineBind", "retroPool", "dGate", "open", "found", "parentGen"];
        foreach (string suffix in suffixes)
            Assert.True(ColumnValue(h, $"prd.d1.{suffix}") == ColumnValue(h, $"prd.d0.{suffix}"), $"{suffix} of the retaken slot differs from an empty slot");
        for (int year = h.Layout.StartYear; year < h.Layout.StartYear + h.Layout.Years; year++)
            Assert.Equal(Fx6.Zero, p.Cohort(Gbr, 0, year));
        Assert.Equal(-1, p.Parent(Gbr, 0));
        Assert.Equal(-1, p.StageOMonth(Gbr, 0));
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
