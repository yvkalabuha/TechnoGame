using Singularity.Core.Commands;
using Singularity.Core.Products;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>AdoptLicensedDesign (TASK-0152, #158, #477): a licence becomes a project in stage О, and its lineage stays licensed through a derivative.</summary>
public class ProductsLicenceTests
{
    private const int Gbr = 1;

    private static ProductsHarness Rig(bool market)
    {
        var h = new ProductsHarness(startLine: false, parameters: new ProductsParams { LicenceMarket = market })
        {
            Knows = (a, n) => a == Gbr && Probe.KnownUpTo(2)(n),
        };
        return h;
    }

    private static (int Seller, int Gen) FindSerial(ProductsHarness h)
    {
        for (int s = 0; s < h.Table.RowCount; s++)
        {
            for (int g = 1; g <= Probe.Generations; g++)
            {
                if (s != Gbr && h.Products.SerialOwnDesign(s, g) >= 0)
                    return (s, g);
            }
        }
        throw new InvalidOperationException("no serial project in the start data");
    }

    private static int LicensedSlot(ProductsHarness h) =>
        Enumerable.Range(0, Probe.Designs).First(d => h.Products.LicensedFrom(Gbr, d) >= 0);

    [Fact]
    public void ALicenceBecomesAProjectInStageO_WithoutABureau_AndItsDerivativeIsStillLicensed()
    {
        ProductsHarness h = Rig(true);
        (int seller, int gen) = FindSerial(h);
        h.Licences = (a, t) => a == Gbr && t == 1 ? (seller, gen) : null;
        ProductsSystem p = h.Products;
        h.RunThroughMonth(0);
        Assert.All(Enumerable.Range(0, Probe.Designs), d => Assert.Equal(-1, p.LicensedFrom(Gbr, d)));
        h.RunThroughMonth(1);
        int d0 = LicensedSlot(h);
        Assert.Equal(seller, p.LicensedFrom(Gbr, d0));
        Assert.Equal(DesignStage.Prototype, p.Stage(Gbr, d0));
        Assert.Equal(1, p.StageOMonth(Gbr, d0));
        Assert.Equal(-1, p.DesignBureau(Gbr, d0));
        Assert.Equal(Fx6.Zero, p.WorkLeft(Gbr, d0));
        // Series, then a derivative of the licensed parent: the lineage is kept (#477).
        long t = 2;
        h.Series = (a, d, tt) => a == Gbr && d == d0 && tt == 2 ? new ScriptedStart(Fx6.FromInt(5), true) : null;
        h.RunThroughMonth(t++);
        Assert.Equal(DesignStage.Series, p.Stage(Gbr, d0));
        Assert.NotEqual(d0, p.SerialOwnDesign(Gbr, gen));   // a bought project in series is never the subject of a licence (#477)
        int[] alts = p.Alternatives(Gbr, d0);
        int slot = Enumerable.Range(0, Probe.SlotCount).Single(s => Probe.AltStart[s] <= alts[s] && alts[s] < Probe.AltStart[s + 1] && CoreData.ProductClasses.SlotId[s] == "compressor");
        string other = alts[slot] == Alt(Probe, "comp_centrifugal") ? "comp_axial" : "comp_centrifugal";
        var change = new int[Probe.SlotCount];
        Array.Fill(change, -1);
        change[slot] = Alt(Probe, other);
        long deriveAt = t;
        h.Commands = (a, tt) => a == Gbr && tt == deriveAt ? [DesignCommand.Derive(ProductsHarness.Now(d0), [.. change])] : [];
        h.RunThroughMonth(t++);
        int derived = Enumerable.Range(0, Probe.Designs).First(d => d != d0 && p.Stage(Gbr, d) == DesignStage.Project);
        Assert.Equal(seller, p.LicensedFrom(Gbr, derived));
        while (p.Stage(Gbr, derived) != DesignStage.Prototype)
        {
            h.RunThroughMonth(t++);
            Assert.True(t < 200, "the derived design never reached stage О");
        }
        long launch = t;
        h.Series = (a, d, tt) => a == Gbr && d == derived && tt == launch ? new ScriptedStart(Fx6.FromInt(5), true) : null;
        h.RunThroughMonth(t++);
        Assert.Equal(DesignStage.Series, p.Stage(Gbr, derived));
        Assert.NotEqual(derived, p.SerialOwnDesign(Gbr, Probe.AltEngGen[p.Alternatives(Gbr, derived)[Probe.BaseSlot]]));   // the derivative is not an own design either
    }

    [Fact]
    public void WithTheMarketOff_NoColumnExists_AndALicenceInputIsIgnored()
    {
        ProductsHarness h = Rig(false);
        (int seller, int gen) = FindSerial(h);
        h.Licences = (a, t) => a == Gbr && t == 1 ? (seller, gen) : null;
        h.RunThroughMonth(1);
        Assert.All(Enumerable.Range(0, Probe.Designs), d => Assert.Equal(-1, h.Products.LicensedFrom(Gbr, d)));
        Assert.DoesNotContain(h.Table.Columns, c => c.Name.EndsWith(".licFrom", StringComparison.Ordinal));
    }
}
