using Singularity.Core.Products;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>
/// TASK-0115 (H1): the layer resolves every handle itself, unconditionally (ADR-0005 p. 3). A stale handle of any of the six command kinds is
/// rejected with <see cref="CommandRejection.StaleHandle"/> and changes nothing but the counter; of two candidates for one slot (external, then AI)
/// the first that resolves is applied, each stale one is counted.
/// </summary>
public class ProductsHandleResolutionTests
{
    private const int Gbr = 1;

    private static DesignCommand SuperiorG2() => Compose(Probe, 2, "eng_g2", "hot_sa2_forged", "comp_axial", "af_swept", "ab_on");

    // GBR knows G2; slot 0 is its start fleet with the start line (stage С); t0 composes a G2 project into slot 1.
    private static ProductsHarness Stand(Func<int, long, IReadOnlyList<DesignCommand>>? commands = null) => new()
    {
        Knows = (a, n) => a == Gbr && Probe.KnownUpTo(2)(n),
        Commands = commands ?? ((a, t) => a == Gbr && t == 0 ? [SuperiorG2()] : []),
    };

    // Runs the month steps until slot 1 is a prototype (stage О); returns the next month.
    private static long ToPrototype(ProductsHarness h)
    {
        long t = 0;
        while (h.Products.Stage(Gbr, 1) != DesignStage.Prototype)
        {
            h.RunThroughMonth(t++);
            Assert.True(t < 120, "the project never reached stage О");
        }
        return t;
    }

    private static DesignHandle Current(ProductsHarness h, int slot) => new(slot, h.Products.Generation(Gbr, slot));

    private static DesignHandle Stale(ProductsHarness h, int slot) => new(slot, h.Products.Generation(Gbr, slot) + 1);

    [Fact]
    public void StaleHandle_OfEveryCommandKind_IsRejected_AndChangesNothingButTheCounter()
    {
        ProductsHarness control = Stand(), h = Stand();
        long at = ToPrototype(control);
        Assert.Equal(at, ToPrototype(h));
        Assert.Equal(DesignStage.Series, h.Products.Stage(Gbr, 0));
        DesignHandle s0 = Stale(h, 0), s1 = Stale(h, 1);
        var cancel = DesignCommand.Cancel(s1);
        var derive = DesignCommand.Derive(s0, [.. Enumerable.Repeat(-1, Probe.SlotCount - 1), Alt(Probe, "ab_none")]);
        h.Commands = (a, t) => a == Gbr && t == at ? [cancel, derive] : [];
        h.FleetScript = (a, t) => a == Gbr && t == at ? [new FleetCommand(FleetCommandKind.Retire, s0, 1), new FleetCommand(FleetCommandKind.Sell, s0, 1)] : [];
        h.RawStarts = (a, d, t) => a == Gbr && d == 1 && t == at ? [new SeriesStart(s1, Fx6.FromInt(5), true)] : [];
        h.RawCloses = (a, d, t) => a == Gbr && d == 0 && t == at ? [s0] : [];
        h.RunThroughMonth(at);
        control.RunThroughMonth(at);
        // Six commands, six stale handles; the state outside the rejection counters is the state of the run without them.
        Assert.Equal(6, h.Products.RejectionCount(Gbr, CommandRejection.StaleHandle));
        Assert.Equal(0, control.Products.RejectionCount(Gbr, CommandRejection.StaleHandle));
        Assert.Equal(ProductsSlotGenerationCoverageTests.StateWithoutRejections(control.Table), ProductsSlotGenerationCoverageTests.StateWithoutRejections(h.Table));
        Assert.Equal(DesignStage.Prototype, h.Products.Stage(Gbr, 1));
        Assert.Equal(DesignStage.Series, h.Products.Stage(Gbr, 0));
    }

    [Fact]
    public void ACurrentHandle_OfTheSameCommands_IsNotRejectedAsStale()
    {
        // Guards the test above against a layer that rejects everything: the same cancel with the handle read from the snapshot is accepted.
        ProductsHarness h = Stand();
        long at = ToPrototype(h);
        h.Commands = (a, t) => a == Gbr && t == at ? [DesignCommand.Cancel(Current(h, 1))] : [];
        h.RunThroughMonth(at);
        Assert.Equal(DesignStage.Withdrawn, h.Products.Stage(Gbr, 1));
        Assert.Equal(0, h.Products.RejectionCount(Gbr, CommandRejection.StaleHandle));
    }

    [Fact]
    public void StartSeries_ExternalStale_AiValid_AppliesTheAiOne_AndCountsOneStaleHandle()
    {
        ProductsHarness h = Stand();
        long at = ToPrototype(h);
        Assert.False(h.Products.GateOpen(Gbr, 1, at));   // the gate is closed: only a forced start launches
        DesignHandle stale = Stale(h, 1), valid = Current(h, 1);
        h.RawStarts = (a, d, t) => a == Gbr && d == 1 && t == at ? [new SeriesStart(stale, Fx6.FromInt(5), false), new SeriesStart(valid, Fx6.FromInt(5), true)] : [];
        h.RunThroughMonth(at);
        Assert.Equal(DesignStage.Series, h.Products.Stage(Gbr, 1));
        Assert.True(h.Products.SeriesForced(Gbr, 1));   // the second candidate (force) was the one applied
        Assert.Equal(1, h.Products.RejectionCount(Gbr, CommandRejection.StaleHandle));
    }

    [Fact]
    public void StartSeries_ExternalValid_AiStale_AppliesTheFirst_AndStillCountsTheStaleOne()
    {
        ProductsHarness h = Stand();
        long at = ToPrototype(h);
        DesignHandle stale = Stale(h, 1), valid = Current(h, 1);
        h.RawStarts = (a, d, t) => a == Gbr && d == 1 && t == at ? [new SeriesStart(valid, Fx6.FromInt(5), true), new SeriesStart(stale, Fx6.FromInt(5), false)] : [];
        h.RunThroughMonth(at);
        Assert.Equal(DesignStage.Series, h.Products.Stage(Gbr, 1));
        Assert.Equal(1, h.Products.RejectionCount(Gbr, CommandRejection.StaleHandle));
    }

    [Fact]
    public void CloseLine_ExternalValid_AiStale_ClosesTheLine_AndStillCountsTheStaleOne()
    {
        ProductsHarness h = Stand();
        long at = ToPrototype(h);
        DesignHandle stale = Stale(h, 0), valid = Current(h, 0);
        h.RawCloses = (a, d, t) => a == Gbr && d == 0 && t == at ? [valid, stale] : [];
        h.RunThroughMonth(at);
        Assert.Equal(DesignStage.InService, h.Products.Stage(Gbr, 0));
        Assert.Equal(1, h.Products.RejectionCount(Gbr, CommandRejection.StaleHandle));
    }

    [Fact]
    public void StartSeries_TwoValidCandidates_TheFirstWins_TheSecondDoesNotRescueIt()
    {
        ProductsHarness h = Stand();
        long at = ToPrototype(h);
        DesignHandle valid = Current(h, 1);
        // First: valid but not forced, the gate is closed -> SERIES_GATE; the forced second one must not rescue it.
        h.RawStarts = (a, d, t) => a == Gbr && d == 1 && t == at ? [new SeriesStart(valid, Fx6.FromInt(5), false), new SeriesStart(valid, Fx6.FromInt(5), true)] : [];
        h.RunThroughMonth(at);
        Assert.Equal(DesignStage.Prototype, h.Products.Stage(Gbr, 1));
        Assert.Equal(1, h.Products.RejectionCount(Gbr, CommandRejection.SeriesGate));
        Assert.Equal(0, h.Products.RejectionCount(Gbr, CommandRejection.StaleHandle));
    }

    [Fact]
    public void CloseLine_ExternalStale_AiValid_ClosesTheLine_AndCountsOneStaleHandle()
    {
        ProductsHarness h = Stand();
        long at = ToPrototype(h);
        DesignHandle stale = Stale(h, 0), valid = Current(h, 0);
        Assert.Equal(DesignStage.Series, h.Products.Stage(Gbr, 0));
        h.RawCloses = (a, d, t) => a == Gbr && d == 0 && t == at ? [stale, valid] : [];
        h.RunThroughMonth(at);
        Assert.Equal(DesignStage.InService, h.Products.Stage(Gbr, 0));
        Assert.Equal(1, h.Products.RejectionCount(Gbr, CommandRejection.StaleHandle));
    }

    [Fact]
    public void ADefaultHandle_NeverResolves()
    {
        ProductsHarness h = Stand();
        long at = ToPrototype(h);
        h.Commands = (a, t) => a == Gbr && t == at ? [DesignCommand.Cancel(default)] : [];
        h.RunThroughMonth(at);
        Assert.Equal(DesignStage.Prototype, h.Products.Stage(Gbr, 1));
        Assert.Equal(1, h.Products.RejectionCount(Gbr, CommandRejection.StaleHandle));
    }

    // Property (note §6, H1): handles taken from a random PAST view of the slot never touch a project of another generation. Run A: a script of composes and
    // current-handle cancels that reuses slot 1 again and again. Run B: the same plus, every month, random commands of all six kinds on random slots
    // whose handle generation is one the slot had in some earlier month and differs from the one it holds when the command is applied.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Property_HandlesFromARandomPastView_NeverChangeAProjectOfAnotherGeneration(int seed)
    {
        const int months = 40;
        var rng = new Random(seed);
        // Dice drawn up front: (kind, slot, pick of a past generation).
        var dice = new List<(int Kind, int Slot, int Pick)>[months];
        for (int m = 0; m < months; m++)
        {
            dice[m] = [];
            for (int n = rng.Next(0, 4); n > 0; n--)
                dice[m].Add((rng.Next(6), rng.Next(0, 3), rng.Next(0, 1000)));
        }
        var seen = new List<int>[3];
        for (int d = 0; d < 3; d++)
            seen[d] = [0];   // generations a slot had in earlier months; grows with the run (never the live one of a step that composes)

        IReadOnlyList<DesignCommand> Base(int a, long t) => a != Gbr ? [] : (t % 3) switch
        {
            0 => [SuperiorG2()],
            2 => [DesignCommand.Cancel(new DesignHandle(1, (int)(t / 3) + 1))],
            _ => [],
        };
        ProductsHarness clean = new(maxDesigns: 3) { Knows = (a, n) => a == Gbr && Probe.KnownUpTo(2)(n), Commands = Base };
        ProductsHarness noisy = null!;
        // A past generation of the slot that is not the one it holds now (moved far above when it happens to be the same).
        DesignHandle PastHandle((int Kind, int Slot, int Pick) die)
        {
            int now = noisy.Products.Generation(Gbr, die.Slot);
            int g = seen[die.Slot][die.Pick % seen[die.Slot].Count];
            return new DesignHandle(die.Slot, g == now ? now + 1000 : g);   // far above: a compose of the same step may bump the live generation by one
        }
        IEnumerable<(int Kind, int Slot, int Pick)> Of(long t, params int[] kinds) => dice[t].Where(x => kinds.Contains(x.Kind));
        noisy = new ProductsHarness(maxDesigns: 3)
        {
            Knows = (a, n) => a == Gbr && Probe.KnownUpTo(2)(n),
            Commands = (a, t) => a != Gbr ? [] : [.. Base(a, t), .. Of(t, 0, 1).Select(x => x.Kind == 0
                ? DesignCommand.Cancel(PastHandle(x))
                : DesignCommand.Derive(PastHandle(x), [.. Enumerable.Repeat(-1, Probe.SlotCount - 1), Alt(Probe, "ab_none")]))],
            FleetScript = (a, t) => a != Gbr ? [] : [.. Of(t, 2, 3).Select(x => new FleetCommand(x.Kind == 2 ? FleetCommandKind.Retire : FleetCommandKind.Sell, PastHandle(x), 1))],
            RawStarts = (a, d, t) => a != Gbr ? [] : [.. Of(t, 4).Where(x => x.Slot == d).Select(x => new SeriesStart(PastHandle(x), Fx6.FromInt(5), true))],
            RawCloses = (a, d, t) => a != Gbr ? [] : [.. Of(t, 5).Where(x => x.Slot == d).Select(x => PastHandle(x))],
        };
        long expected = 0;
        for (long t = 0; t < months; t++)
        {
            expected += dice[t].Count;
            clean.RunThroughMonth(t);
            noisy.RunThroughMonth(t);
            for (int d = 0; d < 3; d++)
            {
                if (!seen[d].Contains(noisy.Products.Generation(Gbr, d)))
                    seen[d].Add(noisy.Products.Generation(Gbr, d));
            }
            Assert.Equal(ProductsSlotGenerationCoverageTests.StateWithoutRejections(clean.Table), ProductsSlotGenerationCoverageTests.StateWithoutRejections(noisy.Table));
            Assert.True(expected == noisy.Products.RejectionCount(Gbr, CommandRejection.StaleHandle), $"month {t}: dice [{string.Join(";", dice[t])}] expected {expected} got {noisy.Products.RejectionCount(Gbr, CommandRejection.StaleHandle)}; gens {string.Join(",", Enumerable.Range(0, 3).Select(d => noisy.Products.Generation(Gbr, d)))}");
        }
        Assert.Equal(0, clean.Products.RejectionCount(Gbr, CommandRejection.StaleHandle));
        Assert.True(noisy.Products.Generation(Gbr, 1) >= 5, "the script must reuse slot 1 several times");
    }
}
