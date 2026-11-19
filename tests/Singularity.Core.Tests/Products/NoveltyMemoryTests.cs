using Singularity.Core.Products;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>TASK-0110 (DEBT-0054, spec C10): the actor remembers the alternatives that reached О; slot reuse does not make them new again.</summary>
public class NoveltyMemoryTests
{
    private const int Gbr = 1;

    private static readonly string[] AIds = ["eng_g2", "hot_sa2_forged", "comp_axial", "af_swept", "ab_on"];
    private static readonly string[] BIds = ["eng_g1", "hot_steel_g0", "comp_axial", "af_swept", "ab_on"];

    private static DesignCommand Cmd(string[] ids) => Compose(Probe, 2, ids);

    private static int[] Alts(string[] ids) => [.. ids.Select(id => Alt(Probe, id))];

    private static DesignCommand Cancel(int slot, int generation) => new(DesignCommandKind.Cancel, slot, [], Generation: generation);

    private static ProductsHarness Stand(int threads, int chunkSize, Func<ProductsSystem, long, IReadOnlyList<DesignCommand>> script, int maxDesigns = 3)
    {
        ProductsHarness? self = null;
        var h = new ProductsHarness(threads, chunkSize, startLine: false, maxDesigns: maxDesigns)
        {
            Knows = (a, n) => a == Gbr && Probe.KnownUpTo(2)(n),
            Commands = (a, t) => a == Gbr ? script(self!.Products, t) : [],
        };
        self = h;
        return h;
    }

    // (a) A is composed in slot 1, taken to О and cancelled there; B then retakes slot 1.
    [Fact]
    public void WithdrawnInO_ThenSlotReuse_NoveltyIsUnchanged()
    {
        bool reuse = false;
        ProductsHarness h = Stand(1, 1, (p, t) =>
        {
            if (t == 0)
                return [Cmd(AIds)];
            if (p.Stage(Gbr, 1) == DesignStage.Prototype)
                return [Cancel(1, 1)];
            if (reuse && p.Stage(Gbr, 1) == DesignStage.Withdrawn)
                return [Cmd(BIds)];
            return [];
        });
        ProductsSystem p = h.Products;
        Fx6 before = p.Novelty(Gbr, Alts(AIds));
        Assert.True(before > Fx6.Zero);   // the g2 alternatives are new at the start
        long t = 0;
        for (; t < 80 && p.Stage(Gbr, 1) != DesignStage.Withdrawn; t++)
            h.RunThroughMonth(t);
        Assert.Equal(DesignStage.Withdrawn, p.Stage(Gbr, 1));
        Assert.True(p.StageOMonth(Gbr, 1) >= 0, "A must have reached О");
        Fx6 afterWithdraw = p.Novelty(Gbr, Alts(AIds));
        Assert.Equal(Fx6.Zero, afterWithdraw);
        reuse = true;
        h.RunThroughMonth(t);   // B takes the freed slot 1
        Assert.Equal(2, p.Generation(Gbr, 1));
        Assert.Equal(-1, p.StageOMonth(Gbr, 1));
        Assert.Equal(Fx6.Zero, p.Novelty(Gbr, Alts(AIds)));   // the slot was overwritten, the memory was not
    }

    // (b) Cancelled in П: nothing was tested, the alternatives stay new, also after the slot is retaken (and cancelled in П again).
    [Fact]
    public void WithdrawnInP_AlternativesStayNew_AlsoAfterReuse()
    {
        ProductsHarness h = Stand(1, 1, (_, t) => t switch
        {
            0 => [Cmd(AIds)],
            1 => [Cancel(1, 1)],
            3 => [Cmd(BIds)],
            4 => [Cancel(1, 2)],
            _ => [],
        });
        ProductsSystem p = h.Products;
        Fx6 before = p.Novelty(Gbr, Alts(AIds));
        Assert.True(before > Fx6.Zero);
        h.RunThroughMonth(0);
        Assert.Equal(DesignStage.Project, p.Stage(Gbr, 1));
        h.RunThroughMonth(1);
        Assert.Equal(DesignStage.Withdrawn, p.Stage(Gbr, 1));
        Assert.Equal(-1, p.StageOMonth(Gbr, 1));
        Assert.Equal(before, p.Novelty(Gbr, Alts(AIds)));
        h.RunThroughMonth(2);
        h.RunThroughMonth(3);
        Assert.Equal(2, p.Generation(Gbr, 1));
        h.RunThroughMonth(4);
        Assert.Equal(DesignStage.Withdrawn, p.Stage(Gbr, 1));
        Assert.Equal(before, p.Novelty(Gbr, Alts(AIds)));
        for (int alt = 0; alt < Probe.AltStart[Probe.SlotCount]; alt++)
            Assert.False(p.AltReached(Gbr, alt) && !p.Alternatives(Gbr, 0).Contains(alt), $"alternative {alt} must not be marked");
    }

    // (c) The scenario fleet counts as tried without any project.
    [Fact]
    public void StartFleet_AlternativesAreNotNew_WithoutAnyProject()
    {
        ProductsHarness h = Stand(1, 1, (_, _) => []);
        ProductsSystem p = h.Products;
        int[] fleet = [.. p.Alternatives(Gbr, 0).ToArray()];
        Assert.Equal(Fx6.Zero, p.Novelty(Gbr, fleet));
        Assert.All(fleet, alt => Assert.True(p.AltReached(Gbr, alt)));
        Assert.True(p.Novelty(Gbr, Alts(AIds)) > Fx6.Zero);
        h.RunThroughMonth(0);
        Assert.Equal(Fx6.Zero, p.Novelty(Gbr, fleet));
    }

    // Two projects reach О and are cancelled in either order; the slots are then retaken. Novelty must not depend on the order.
    private static ProductsHarness TwoProjects(bool cancelAFirst, int threads = 1, int chunk = 1) =>
        Stand(threads, chunk, (p, t) =>
        {
            var cmds = new List<DesignCommand>();
            if (t == 0)
                cmds.Add(Cmd(AIds));
            if (t == 1)
                cmds.Add(Cmd(BIds));
            if (p.Stage(Gbr, 1) == DesignStage.Prototype && p.Stage(Gbr, 2) == DesignStage.Prototype)
            {
                cmds.Add(cancelAFirst ? Cancel(1, 1) : Cancel(2, 1));
                cmds.Add(cancelAFirst ? Cancel(2, 1) : Cancel(1, 1));
            }
            return cmds;
        }, maxDesigns: 4);

    // (d)
    [Fact]
    public void SlotFreeingOrder_DoesNotChangeNovelty()
    {
        ProductsHarness x = TwoProjects(true), y = TwoProjects(false);
        for (long t = 0; t < 60; t++)
        {
            x.RunThroughMonth(t);
            y.RunThroughMonth(t);
        }
        foreach (ProductsHarness h in new[] { x, y })
        {
            Assert.Equal(DesignStage.Withdrawn, h.Products.Stage(Gbr, 1));
            Assert.Equal(DesignStage.Withdrawn, h.Products.Stage(Gbr, 2));
        }
        foreach (string[] ids in new[] { AIds, BIds })
        {
            Assert.Equal(Fx6.Zero, x.Products.Novelty(Gbr, Alts(ids)));
            Assert.Equal(x.Products.Novelty(Gbr, Alts(ids)), y.Products.Novelty(Gbr, Alts(ids)));
        }
        for (int alt = 0; alt < Probe.AltStart[Probe.SlotCount]; alt++)
            Assert.Equal(x.Products.AltReached(Gbr, alt), y.Products.AltReached(Gbr, alt));
    }

    // (e)
    [Theory]
    [InlineData(2, 1)]
    [InlineData(4, 2)]
    [InlineData(8, 3)]
    public void L1_L2_ThreadsAndChunks_GiveTheSameHashEveryMonth(int threads, int chunk)
    {
        ProductsHarness one = TwoProjects(true), again = TwoProjects(true), many = TwoProjects(true, threads, chunk);
        for (long t = 0; t < 40; t++)
        {
            one.RunThroughMonth(t);
            again.RunThroughMonth(t);
            many.RunThroughMonth(t);
            Assert.Equal(one.Hash(), again.Hash());
            Assert.Equal(one.Hash(), many.Hash());
        }
    }

    private static void CopyState(Table from, Table to)
    {
        for (int i = 0; i < from.Columns.Count; i++)
        {
            switch (from.Columns[i])
            {
                case IntColumn c: ((IntColumn)to.Columns[i]).Load(c.Current); break;
                case LongColumn c: ((LongColumn)to.Columns[i]).Load(c.Current); break;
            }
        }
    }

    [Fact]
    public void Memory_SurvivesSaveLoad_AndIsPartOfTheHash()
    {
        ProductsHarness src = TwoProjects(true);
        for (long t = 0; t < 60; t++)
            src.RunThroughMonth(t);
        Assert.Contains(src.Table.Columns, c => c.Name.StartsWith("prd.altReached.", StringComparison.Ordinal));
        Assert.True(src.Products.AltReached(Gbr, Alt(Probe, "eng_g2")));
        ProductsHarness loaded = Stand(1, 1, (_, _) => [], 4);
        CopyState(src.Table, loaded.Table);
        Assert.Equal(src.Hash(), loaded.Hash());
        Assert.Equal(Fx6.Zero, loaded.Products.Novelty(Gbr, Alts(AIds)));
        // Forgetting one alternative is a different state.
        ProductsHarness tampered = Stand(1, 1, (_, _) => [], 4);
        CopyState(src.Table, tampered.Table);
        IntColumn col = tampered.Table.GetInt($"prd.altReached.{Alt(Probe, "eng_g2")}");
        int[] v = col.Current.ToArray();
        v[Gbr] = 0;
        col.Load(v);
        Assert.NotEqual(src.Hash(), tampered.Hash());
        Assert.True(tampered.Products.Novelty(Gbr, Alts(AIds)) > Fx6.Zero);
    }
}
