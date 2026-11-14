using Singularity.Core.Industry;
using Singularity.Core.Products;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>DEBT-0012: the two factories are the only creators of a WorkforceRequest in Core (the architecture test), so these two facts are the whole rule.</summary>
public class WorkforceRequestFactoryTests
{
    [Fact]
    public void ReserveSkilled_OnePerBusyCluster_WithSkilledRequesterAndCeilingPeople()
    {
        ProductsLayout l = MakeLayout(out _, Years, Scenario.MaxDesigns);
        var box = new IndustryMailbox(1);
        long[] hours = new long[l.ClusterCount];
        for (int c = 0; c < hours.Length; c++)
            hours[c] = c % 2 == 0 ? 0 : Fx6.FromInt(1000).Raw;
        l.ReserveSkilled(box, 0, 5, 1, hours);
        var reports = new List<OutputReport>();
        var requests = new List<WorkforceRequest>();
        box.Drain(reports, requests);
        Assert.Equal(hours.Count(h => h > 0), requests.Count);
        foreach (WorkforceRequest r in requests)
        {
            Assert.Equal(5, r.Site);
            Assert.Equal(l.SkilledCategory, r.Category);
            Assert.Equal(1 * l.ClusterCount + r.RequesterId % l.ClusterCount, r.RequesterId);
            Assert.True(hours[r.RequesterId % l.ClusterCount] > 0);
            Assert.Equal(ProductsMath.People(Fx6.FromRaw(hours[r.RequesterId % l.ClusterCount]), l.HPm), r.N);
        }
    }

    [Fact]
    public void ReserveSkilled_RejectsAWrongClusterCountOrLine()
    {
        ProductsLayout l = MakeLayout(out _, Years, Scenario.MaxDesigns);
        var box = new IndustryMailbox(1);
        Assert.Throws<ArgumentException>(() => l.ReserveSkilled(box, 0, 0, 0, new long[l.ClusterCount + 1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => l.ReserveSkilled(box, 0, 0, l.Designs, new long[l.ClusterCount]));
        Assert.Throws<ArgumentOutOfRangeException>(() => l.ReserveSkilled(box, 0, 0, -1, new long[l.ClusterCount]));
    }

    [Fact]
    public void EngineerRequest_NamesTheEngineerCategory_AndKeepsTheId()
    {
        Assert.Equal(new WorkforceRequest(2, ProductsLayout.EngineerCategory, 9, 4), ProductsLayout.EngineerRequest(2, 9, 4));
    }
}
