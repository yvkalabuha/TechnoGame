using Singularity.Core.Policy;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>Data of the slice that the A41 condition <c>prc(sup[s]) \ prc(prod[s]) ≠ ∅</c> relies on (contract §4.4a r12, R4).</summary>
public class SliceDataTests
{
    [Fact]
    public void HotSection_HigherRankAlwaysAddsAProcess()
    {
        var l = Probe;
        int start = l.AltStart[l.AlloySlot], end = l.AltStart[l.AlloySlot + 1], pairs = 0;
        for (int a = start; a < end; a++)
        {
            for (int b = start; b < end; b++)
            {
                if (l.AltRank[a] <= l.AltRank[b])
                    continue;
                pairs++;
                Assert.True(ProductPresets.BringsNewProcess(l, a, b), $"{l.AltId[a]} (rank {l.AltRank[a]}) has no process outside {l.AltId[b]} (rank {l.AltRank[b]})");
            }
        }
        Assert.True(pairs > 0);
    }
}
