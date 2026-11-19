using Singularity.Core.Data;
using Singularity.Core.Products;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>Contract r12 (I-OC, #243): the growth nominal <c>V_OC = V_C + V_O</c> of §4.1a, invariants V7 and V8 on the product layer.</summary>
public class ProductsSnapshotTests
{
    private const int Gbr = 1;

    private static ProductsHarness Gb(Func<int, long, IReadOnlyList<DesignCommand>> commands) => new(startLine: false)
    {
        Knows = (a, n) => a == Gbr && Probe.KnownUpTo(2)(n),
        Commands = commands,
    };

    private static DesignCommand SuperiorG2() => Compose(Probe, 2, "eng_g2", "hot_sa2_forged", "comp_axial", "af_swept", "ab_on");

    private static Fx6 Plan(ProductsHarness h, int prc)
    {
        int k = h.Layout.ProcIndex(prc);
        return h.Layout.RRef * h.Layout.ProcN[k] * h.Layout.ProcLabour[k];
    }

    private static long RunTo(ProductsHarness h, int design, DesignStage stage)
    {
        long t = 0;
        while (h.Products.Stage(Gbr, design) != stage)
        {
            h.RunThroughMonth(t++);
            Assert.True(t < 120, $"design {design} never reached {stage}");
        }
        return t;
    }

    [Fact]
    public void GrowthNominal_CountsEachDesignOnce()
    {
        var h = Gb((a, t) => a == Gbr && t == 0 ? [SuperiorG2()] : []);
        ProductsSystem p = h.Products;
        h.RunThroughMonth(0);
        Assert.Equal(DesignStage.Project, p.Stage(Gbr, 1));
        int[] prcs = p.PrcSet(Gbr, 1);
        Assert.NotEmpty(prcs);
        // Gate review: the data of the slice has no process in two slots, so the «one plan per process» case is not constructible from it; prc_set is a set by
        // construction (DesignRules.PrcSet). If a slot pair ever shares a process this equality fails, and the fixture must then hold that case.
        int[] alts = p.Alternatives(Gbr, 1);
        Assert.Equal(prcs.Length, alts.Sum(a => h.Layout.PrcOf(a).Length));
        foreach (int prc in prcs)
            Assert.Equal(Fx6.Zero, p.GrowthNominal(Gbr, prc, true));              // П: a paper project gives nothing
        long t = RunTo(h, 1, DesignStage.Prototype);
        foreach (int prc in prcs)
        {
            Assert.Equal(Plan(h, prc), p.GrowthNominal(Gbr, prc, true));         // О: one plan(d, p) per process, however many slots bring it
            Assert.Equal(Fx6.Zero, p.GrowthNominal(Gbr, prc, false));            // not on home
            Assert.True(p.GrowthNominal(Gbr, prc, true) <= p.ExpectedVolume(Gbr, prc, Fx6.Zero, true));   // V8
        }
        Assert.Equal(Fx6.Zero, p.GrowthNominal(Gbr, 9999, true));
        // О → С: the same value (the design is in V_O before, in V_C after, never in both).
        long launch = t;
        h.Series = (a, d, tt) => a == Gbr && d == 1 && tt == launch ? new ScriptedStart(Fx6.FromInt(5), true) : null;
        h.RunThroughMonth(t);
        Assert.Equal(DesignStage.Series, p.Stage(Gbr, 1));
        foreach (int prc in prcs)
            Assert.Equal(Plan(h, prc), p.GrowthNominal(Gbr, prc, true));
    }

    [Fact]
    public void GrowthNominal_TwoProjectsInPrototypeSum_AndAWithdrawnOneIsZero()
    {
        long cancelAt = long.MaxValue;
        var h = Gb((a, t) => a != Gbr ? [] : t == 0 ? [SuperiorG2(), SuperiorG2()] : t == cancelAt ? [DesignCommand.Cancel(ProductsHarness.Now(1))] : []);
        ProductsSystem p = h.Products;
        long t = 0;
        while (p.Stage(Gbr, 1) != DesignStage.Prototype || p.Stage(Gbr, 2) != DesignStage.Prototype)
        {
            h.RunThroughMonth(t++);
            Assert.True(t < 200, "both projects never stood in О together");
        }
        foreach (int prc in p.PrcSet(Gbr, 1).Intersect(p.PrcSet(Gbr, 2)))
        {
            Assert.Equal(Plan(h, prc) + Plan(h, prc), p.GrowthNominal(Gbr, prc, true));
            Assert.True(p.GrowthNominal(Gbr, prc, true) <= p.ExpectedVolume(Gbr, prc, Fx6.Zero, true));
        }
        cancelAt = t;
        h.RunThroughMonth(t);
        Assert.Equal(DesignStage.Withdrawn, p.Stage(Gbr, 1));
        foreach (int prc in p.PrcSet(Gbr, 2))
            Assert.Equal(Plan(h, prc), p.GrowthNominal(Gbr, prc, true));         // «снят» weighs 0, the other project once
    }
}
