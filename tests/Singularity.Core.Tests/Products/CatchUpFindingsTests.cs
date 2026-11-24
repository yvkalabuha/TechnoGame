using Singularity.Core.Data;
using Singularity.Core.Products;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>TASK-0118: Codex catch-up findings on the product layer (slot reuse, novelty memory, cohort mask bounds).</summary>
public class CatchUpFindingsTests
{
    private const int Gbr = 1;

    private static readonly string[] AIds = ["eng_g2", "hot_sa2_forged", "comp_axial", "af_swept", "ab_on"];

    private static DesignCommand SuperiorG2() => Compose(Probe, 2, AIds);

    private static ProductsHarness Stand(int maxDesigns, Func<ProductsSystem, long, IReadOnlyList<DesignCommand>> script)
    {
        ProductsHarness? self = null;
        var h = new ProductsHarness(1, 1, startLine: false, maxDesigns: maxDesigns)
        {
            Knows = (a, n) => a == Gbr && Probe.KnownUpTo(2)(n),
            Commands = (a, t) => a == Gbr ? script(self!.Products, t) : [],
        };
        self = h;
        return h;
    }

    // TASK-0099: Withdraw does not bump the generation, so a freed canonical slot used to be returned as a live design until it was retaken.
    [Fact]
    public void CanonicalSlot_IsAbsent_WhileTheSlotIsFree()
    {
        ProductsHarness h = ProductsLpTests.Lp(1, 1);
        ProductsSystem p = h.Products;
        int freed = 0;
        for (long t = 0; t < 380; t++)
        {
            var seen = new List<(int Actor, int Gen, int Slot)>();
            for (int a = 0; a < h.Table.RowCount; a++)
            {
                for (int g = 1; g <= h.Layout.Generations; g++)
                {
                    if (p.CanonicalSlot(a, g) is var s and >= 0)
                        seen.Add((a, g, s));
                }
            }
            h.RunThroughMonth(t);
            foreach ((int a, int g, int slot) in seen)
            {
                if (p.Stage(a, slot) != DesignStage.Withdrawn)
                    continue;
                freed++;
                Assert.Equal(-1, p.CanonicalSlot(a, g));
            }
        }
        Assert.True(freed > 0, "no canonical slot was freed in the run");
    }

    // TASK-0099 acceptance: with every slot taken a compose is refused; after one slot is freed it succeeds and takes that slot.
    [Fact]
    public void TwelveTakenSlots_RefuseACompose_AndTheFreedSlotIsTaken()
    {
        const int Slots = 12;
        bool fill = true;
        int cancelled = -1;
        ProductsHarness h = Stand(Slots, (p, t) =>
        {
            if (fill)
                return p.FreeBureaus(Gbr) > 0 ? [SuperiorG2()] : [];
            if (cancelled < 0)
                return [DesignCommand.Cancel(new DesignHandle(5, p.Generation(Gbr, 5)))];
            return [SuperiorG2()];
        });
        ProductsSystem p = h.Products;
        static bool Taken(ProductsSystem p, int d) => p.Stage(Gbr, d) is not (DesignStage.None or DesignStage.Withdrawn);
        long t = 0;
        for (; t < 2_000 && Enumerable.Range(0, Slots).Count(d => Taken(p, d)) < Slots; t++)
            h.RunThroughMonth(t);
        Assert.Equal(Slots, Enumerable.Range(0, Slots).Count(d => Taken(p, d)));
        Assert.Equal(0, p.RejectionCount(Gbr, CommandRejection.DesignCapacity));
        fill = false;
        cancelled = -2;
        // One more compose with a bureau free would have a slot refused; the Cancel of slot 5 goes first in the next month, then a compose.
        h.RunThroughMonth(t++);   // cancel of slot 5 (a project in П or О)
        Assert.Equal(DesignStage.Withdrawn, p.Stage(Gbr, 5));
        cancelled = 5;
        int generation = p.Generation(Gbr, 5);
        h.RunThroughMonth(t++);   // compose into the freed slot
        Assert.Equal(DesignStage.Project, p.Stage(Gbr, 5));
        Assert.Equal(generation + 1, p.Generation(Gbr, 5));
        Assert.Equal(0, p.RejectionCount(Gbr, CommandRejection.DesignCapacity));
        // Every slot taken again: the next compose is refused for capacity (the first check of Create).
        h.RunThroughMonth(t);
        Assert.Equal(1, p.RejectionCount(Gbr, CommandRejection.DesignCapacity));
    }

    // TASK-0121: the monthly step sets and clears the bit of the year in the second word of the q mask; an index outside the years reads nothing.
    [Fact]
    public void CohortMastered_OutOfRangeIndex_IsFalse_NotTheMaskOfAnotherDesign()
    {
        var h = new ProductsHarness(startLine: false, isMastered: (_, _) => true);
        ProductsSystem p = h.Products;
        int years = h.Layout.Years;
        Assert.False(p.CohortMastered(Gbr, 0, years));
        Assert.False(p.CohortMastered(Gbr, 0, -1));
        Assert.False(p.CohortMastered(Gbr, 0, int.MaxValue));
    }

    // TASK-0121: cohort years 62 and 63 live in the second word of the q mask. Month 4 is January of cohort index 1, so cohort 62 spans months
    // 736..747 and its last write is the compute of month 748 (it reads mastery of month 747).
    [Fact]
    public void QMask_SecondWord_BitIsSetAndCleared_ByTheMonthlyStep()
    {
        bool Mastered(bool lostInLastWrite, out ProductsSystem products)
        {
            long now = 0;
            var h = new ProductsHarness(years: 70, isMastered: (_, _) => !(lostInLastWrite && now == 748))
            {
                Rate = (_, _, _) => Fx6.FromInt(20),
            };
            for (long m = 0; m < 760; m++)
            {
                now = m;
                h.RunThroughMonth(m);
            }
            products = h.Products;
            Assert.Equal(DesignStage.Series, products.Stage(0, 0));
            return products.CohortMastered(0, 0, 62);
        }
        Assert.True(Mastered(lostInLastWrite: false, out ProductsSystem control));
        Assert.True(control.CohortMastered(0, 0, 61));
        Assert.False(Mastered(lostInLastWrite: true, out ProductsSystem lost));   // set by earlier months of the year, cleared by the last
        Assert.True(lost.CohortMastered(0, 0, 61));       // the neighbour in the first word is untouched
        Assert.True(lost.CohortMastered(0, 0, 63));       // and the next bit of the second word is set by its own first write
    }

    // TASK-0110 / spec C10: the memory of an actor includes the start fleet even after it is written off, and project creation applies it.
    [Fact]
    public void CreateAppliesTheRemembered_Novelty_AfterTheSlotWasReused()
    {
        string[] bIds = ["eng_g1", "hot_steel_g0", "comp_axial", "af_swept", "ab_on"];
        ProductsHarness h = Stand(3, (p, t) =>
        {
            if (t == 0)
                return [SuperiorG2()];
            if (p.Stage(Gbr, 1) == DesignStage.Prototype)
                return [DesignCommand.Cancel(new DesignHandle(1, p.Generation(Gbr, 1)))];
            if (t > 3 && p.Stage(Gbr, 1) == DesignStage.Withdrawn && p.Generation(Gbr, 1) == 1)
                return [Compose(Probe, 2, bIds)];
            if (p.Generation(Gbr, 1) == 2 && p.Stage(Gbr, 2) == DesignStage.None && p.Stage(Gbr, 1) == DesignStage.Project)
                return [SuperiorG2()];   // A again, after its first slot was overwritten by B
            return [];
        });
        ProductsSystem p = h.Products;
        long t = 0;
        for (; t < 200 && !(p.Generation(Gbr, 1) == 2 && p.Stage(Gbr, 2) == DesignStage.Project); t++)
            h.RunThroughMonth(t);
        Assert.Equal(DesignStage.Project, p.Stage(Gbr, 2));
        Assert.Equal(Fx6.Zero, p.Novelty(Gbr, [.. AIds.Select(id => Alt(Probe, id))]));
        // A has novelty 0 (it reached О before), so its D0 is that of an experienced design: less than a design of 5 new alternatives.
        Assert.True(p.InitialDefects(Gbr, 2) < p.InitialDefects(Gbr, 1) || p.InitialDefects(Gbr, 2) == Fx6.Zero);
    }

    // C10: a fully written-off start fleet stays remembered, so the novelty of its alternatives does not come back before any slot is reused.
    [Fact]
    public void RetiredStartFleet_StaysRemembered_BeforeAnySlotIsReused()
    {
        ProductsSystem? sys = null;
        var h = new ProductsHarness(startLine: false) { Knows = (a, n) => a == Gbr && Probe.KnownUpTo(2)(n) };
        sys = h.Products;
        h.FleetScript = (a, t) => a == Gbr && t == 1 && sys.Stage(Gbr, 0) == DesignStage.InService
            ? [new FleetCommand(FleetCommandKind.Retire, new DesignHandle(0, 1), sys.InService(Gbr, 0).Raw / Fx6.Scale)] : [];
        ProductsSystem p = h.Products;
        int[] fleet = [.. p.Alternatives(Gbr, 0).ToArray()];
        Assert.Equal(Fx6.Zero, p.Novelty(Gbr, fleet));
        for (long t = 0; t < 12 && p.Stage(Gbr, 0) != DesignStage.Withdrawn; t++)
            h.RunThroughMonth(t);
        Assert.Equal(DesignStage.Withdrawn, p.Stage(Gbr, 0));
        Assert.Equal(1, p.Generation(Gbr, 0));
        Assert.Equal(Fx6.Zero, p.Novelty(Gbr, fleet));
    }
}
