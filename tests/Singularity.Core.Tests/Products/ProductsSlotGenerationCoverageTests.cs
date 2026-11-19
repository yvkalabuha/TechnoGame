using Singularity.Core.Products;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>TASK-0099 coverage: many reuse cycles, generation in the saved columns, stale handles after a restore, stale handles never mutate.</summary>
public class ProductsSlotGenerationCoverageTests
{
    private const int Gbr = 1;

    private static DesignCommand SuperiorG2() => Compose(Probe, 2, "eng_g2", "hot_sa2_forged", "comp_axial", "af_swept", "ab_on");

    private static ProductsHarness Stand(int threads, int chunkSize, int maxDesigns, Func<int, long, IReadOnlyList<DesignCommand>> commands) =>
        new(threads, chunkSize, startLine: false, maxDesigns: maxDesigns)
        {
            Knows = (a, n) => a == Gbr && Probe.KnownUpTo(2)(n),
            Commands = commands,
        };

    // Period 3 on slot 1: compose, idle, cancel with the handle the slot is expected to hold (cycle k -> generation k + 1).
    private static IReadOnlyList<DesignCommand> CycleScript(int a, long t) => a != Gbr ? [] : (t % 3) switch
    {
        0 => [SuperiorG2()],
        2 => [DesignCommand.Cancel(new DesignHandle(1, (int)(t / 3) + 1))],
        _ => [],
    };

    [Fact]
    public void ManyReuseCycles_OneSlot_GenerationCountsEveryTaking_NothingLeaks()
    {
        const int cycles = 25;
        ProductsHarness h = Stand(1, 1, 2, CycleScript), shadow = Stand(4, 8, 2, CycleScript);
        ProductsSystem p = h.Products;
        for (long t = 0; t < cycles * 3; t++)
        {
            h.RunThroughMonth(t);
            shadow.RunThroughMonth(t);
            Assert.Equal(h.Hash(), shadow.Hash());
            int k = (int)(t / 3);
            switch (t % 3)
            {
                case 0:
                    Assert.Equal(DesignStage.Project, p.Stage(Gbr, 1));
                    Assert.Equal(k + 1, p.Generation(Gbr, 1));
                    Assert.False(p.Cancelled(Gbr, 1), $"cycle {k}: the retaken slot carries the cancel mark");
                    Assert.False(p.BuiltAny(Gbr, 1));
                    Assert.Equal(0, p.IdleMonths(Gbr, 1));
                    Assert.Equal(Fx6.Zero, p.DefectsFound(Gbr, 1));
                    break;
                case 2:
                    Assert.Equal(DesignStage.Withdrawn, p.Stage(Gbr, 1));
                    Assert.Equal(k + 1, p.Generation(Gbr, 1));
                    break;
            }
        }
        Assert.Equal(cycles, p.Generation(Gbr, 1));
        Assert.Equal(1, p.Generation(Gbr, 0));   // the start fleet slot never changed
        Assert.Equal(0, p.RejectionCount(Gbr, CommandRejection.StaleHandle));
        Assert.Equal(0, p.RejectionCount(Gbr, CommandRejection.DesignCapacity));
        Assert.Equal(0, p.RejectionCount(Gbr, CommandRejection.BadCancel));
    }

    // Copies the whole state (the saved form of the model is its columns) of one harness into the fresh table of another.
    private static void CopyState(Table from, Table to)
    {
        Assert.Equal(from.Columns.Count, to.Columns.Count);
        for (int i = 0; i < from.Columns.Count; i++)
        {
            Assert.Equal(from.Columns[i].Name, to.Columns[i].Name);
            switch (from.Columns[i])
            {
                case IntColumn c: ((IntColumn)to.Columns[i]).Load(c.Current); break;
                case LongColumn c: ((LongColumn)to.Columns[i]).Load(c.Current); break;
                default: throw new InvalidOperationException(from.Columns[i].Name);
            }
        }
    }

    [Fact]
    public void Generation_SurvivesSaveLoad_AndAStaleHandleStillResolvesToNothing()
    {
        ProductsHarness src = Stand(1, 1, 3, (a, t) => a == Gbr && (t == 0 || t == 3) ? [SuperiorG2()]
            : a == Gbr && t == 2 ? [DesignCommand.Cancel(new DesignHandle(1, 1))] : []);
        for (long t = 0; t < 5; t++)
            src.RunThroughMonth(t);
        Assert.Equal(2, src.Products.Generation(Gbr, 1));

        ProductsHarness loaded = Stand(1, 1, 3, (_, _) => []);
        CopyState(src.Table, loaded.Table);
        Assert.Equal(src.Hash(), loaded.Hash());   // the generation columns are part of the saved state hash
        for (int d = 0; d < 3; d++)
        {
            Assert.Equal(src.Products.Generation(Gbr, d), loaded.Products.Generation(Gbr, d));
            Assert.Equal(src.Products.Stage(Gbr, d), loaded.Products.Stage(Gbr, d));
        }
        Assert.False(loaded.Products.Resolves(Gbr, new DesignHandle(1, 1)));   // the stale handle of the first design
        Assert.True(loaded.Products.Resolves(Gbr, new DesignHandle(1, 2)));
        // A loaded state with a changed generation is a different state.
        ProductsHarness tampered = Stand(1, 1, 3, (_, _) => []);
        CopyState(src.Table, tampered.Table);
        IntColumn gen = tampered.Table.GetInt("prd.d1.generation");
        int[] g = gen.Current.ToArray();
        g[Gbr]++;
        gen.Load(g);
        Assert.NotEqual(src.Hash(), tampered.Hash());
    }

    // Hash of everything except the rejection counters.
    internal static string StateWithoutRejections(Table t)
    {
        var sb = new System.Text.StringBuilder();
        foreach (Column c in t.Columns)
        {
            if (c.Name.StartsWith("prd.rejected.", StringComparison.Ordinal))
                continue;
            sb.Append(c.Name).Append('=');
            switch (c)
            {
                case IntColumn i: foreach (int v in i.Current) sb.Append(v).Append(','); break;
                case LongColumn l: foreach (long v in l.Current) sb.Append(v).Append(','); break;
            }
            sb.Append(';');
        }
        return sb.ToString();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Property_StaleHandle_NeverMutatesState_AndIsCountedExactly(int seed)
    {
        // Run A: a script of composes and current-handle cancels. Run B: the same plus, every month, stale cancels/derives
        // (generation < the slot's or far in the future) of random slots. The state outside the rejection counters must be equal.
        var rng = new Random(seed);
        var extra = new Dictionary<long, List<DesignCommand>>();
        for (long t = 0; t < 40; t++)
        {
            extra[t] = [];
            for (int n = rng.Next(0, 3); n > 0; n--)
            {
                int slot = rng.Next(0, 3);
                // Placeholder generation: replaced with a stale value at the time of the month (see the noisy script below).
                extra[t].Add(rng.Next(2) == 0
                    ? DesignCommand.Cancel(new DesignHandle(slot, 0))
                    : DesignCommand.Derive(new DesignHandle(slot, 0), [.. Enumerable.Repeat(-1, Probe.SlotCount - 1), Alt(Probe, "ab_none")]));
            }
        }
        IReadOnlyList<DesignCommand> Base(int a, long t) => a != Gbr ? [] : (t % 3) switch
        {
            0 => [SuperiorG2()],
            2 => [DesignCommand.Cancel(new DesignHandle(1, (int)(t / 3) + 1))],
            _ => [],
        };
        ProductsHarness clean = Stand(1, 1, 3, Base), noisy = null!;
        noisy = Stand(1, 1, 3, (a, t) =>
        {
            IReadOnlyList<DesignCommand> b = Base(a, t);
            if (a != Gbr)
                return b;
            // Stale by construction: a generation below the one the slot holds at the start of the month (gens only grow), or far above.
            IEnumerable<DesignCommand> stale = extra[t].Select(c =>
            {
                int g = noisy.Products.Generation(Gbr, c.Design.Slot);
                return c with { Design = new DesignHandle(c.Design.Slot, g >= 1 ? g - 1 : 1000) };
            });
            return [.. b, .. stale];
        });
        long expected = 0;
        for (long t = 0; t < 40; t++)
        {
            expected += extra[t].Count;
            clean.RunThroughMonth(t);
            noisy.RunThroughMonth(t);
            Assert.Equal(StateWithoutRejections(clean.Table), StateWithoutRejections(noisy.Table));
            Assert.Equal(expected, noisy.Products.RejectionCount(Gbr, CommandRejection.StaleHandle));
        }
        Assert.Equal(0, clean.Products.RejectionCount(Gbr, CommandRejection.StaleHandle));
    }
}
