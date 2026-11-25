using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Knowledge;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>
/// TASK-0068 (tick profile M0, F1-F3, F5): every fast path equals the old one on the slice, month after month.
/// The golden hashes of the other suites pin that the whole run did not change.
/// </summary>
public class TickHotSpotEquivalenceTests
{
    private const long Months = 14;

    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    public void F1_RowOfTables_EqualLinearScans(ulong seed)
    {
        SliceWorld w = SliceTestData.Create(SliceRunConfig.Legacy with { Seed = seed }).World;
        KnowledgeGraph g = w.Graph;
        IndustryLayout l = w.IndustryLayout;
        for (int n = 0; n <= KnowledgeRng.MaxNodeNumber + 1; n++)
        {
            Assert.Equal(g.Number.IndexOf(n), g.RowOf(n));
            Assert.Equal(l.Number.IndexOf(n), l.RowOf(n));
        }
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    public void F2_F3_F5_EachMonth_FastPathsEqualTheOldOnes(ulong seed)
    {
        SliceSession session = SliceTestData.Create(SliceRunConfig.Legacy with { Seed = seed });
        SliceWorld w = session.World;
        IndustryLayout l = w.IndustryLayout;
        ICultureSnapshot snap = w.Industry;
        ICultureRule rule = w.Industry.Rule;
        var adapter = new IndustryInputsAdapter(w);
        int cells = l.Clusters.Length * Dims3.Count;
        for (long month = 0; month <= Months; month++)
        {
            if (month > 0)
                session.StepThroughMonth(month);

            // F2: the memoised prc_set equals the recomputation from the alternatives.
            for (int a = 0; a < l.SiteCount; a++)
            {
                for (int d = 0; d < w.ProductsLayout.Designs; d++)
                {
                    int[] expected = w.Products.Stage(a, d) == DesignStage.None ? [] : DesignRules.PrcSet(w.ProductsLayout, w.Products.Alternatives(a, d));
                    Assert.Equal(expected, w.Products.PrcSet(a, d));
                }
            }

            for (int site = 0; site < l.SiteCount; site++)
            {
                // F3: E_useful of all processes in one pass equals the one-by-one call.
                var all = new Fx6[l.ProcessCount];
                rule.EUsefulAll(snap, site, month, all);
                for (int r = 0; r < l.ProcessCount; r++)
                    Assert.Equal(rule.EUseful(snap, site, r, month), all[r]);

                // F5: the plan of the site in one call equals the cell-by-cell calls.
                foreach (Fx6 rest in new[] { Fx6.Zero, Fx6.FromInt(3) })
                {
                    var invest = new Fx6[cells];
                    var unmet = new Fx6[cells];
                    adapter.ToolingInvestSite(site, month, rest, invest, unmet);
                    for (int i = 0; i < cells; i++)
                    {
                        Fx6 one = adapter.ToolingInvest(site, i / Dims3.Count, (Dimension)(i % Dims3.Count), month, rest, out Fx6 u);
                        Assert.Equal(one, invest[i]);
                        Assert.Equal(u, unmet[i]);
                    }
                }
            }
        }
    }
}
