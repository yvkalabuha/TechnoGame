using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Industry;

/// <summary>TASK-0068 F1 and F6: the dense row table and the per-actor site index equal the linear scans they replace.</summary>
public class SitesOfAndRowOfTests
{
    private static IndustryLayout Layout(int[] siteOwner, int[] numbers)
    {
        Dims3 req = new(Fx6.FromInt(3), Fx6.FromInt(3), Fx6.FromInt(3));
        var specs = numbers.Select(n => new IndustryProcessSpec(n, Cluster.MET, 0, 3, req, Fx6.One)).ToList();
        return IndustryLayout.Create([Cluster.MET], specs, siteOwner, 2, 1, 2);
    }

    [Fact]
    public void RowOf_EqualsLinearScan_ForEveryNumberAroundTheRange()
    {
        int[] numbers = [40, 7, 65535, 12, 1, 300];
        IndustryLayout l = Layout([0, 1], numbers);
        for (int n = -2; n <= 65540; n++)
            Assert.Equal(Array.IndexOf(numbers, n), l.RowOf(n));
    }

    [Fact]
    public void SitesOf_EqualsFilterOfSiteOwner_AscendingAndEmptyForUnknownActors()
    {
        int[] owner = [2, 0, 2, 1, 0, 2, -1, 5];
        IndustryLayout l = Layout(owner, [3, 4]);
        for (int a = 0; a < 9; a++)
        {
            int[] expected = [.. Enumerable.Range(0, owner.Length).Where(s => owner[s] == a)];
            Assert.Equal(expected, l.SitesOf(a).ToArray());
        }
        Assert.Empty(l.SitesOf(-1).ToArray());
    }
}
