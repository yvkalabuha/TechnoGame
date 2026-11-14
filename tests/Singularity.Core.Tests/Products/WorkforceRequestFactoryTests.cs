using Singularity.Core.Industry;
using Singularity.Core.Products;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>DEBT-0012: the two factories are the only creators of a WorkforceRequest in Core (the architecture test), so these two facts are the whole rule.</summary>
public class WorkforceRequestFactoryTests
{
    [Fact]
    public void SkilledRequest_NamesTheSkilledCategory_AndCarriesSkilledRequester()
    {
        ProductsLayout l = MakeLayout(out _, Years, Scenario.MaxDesigns);
        for (int line = 0; line < 2; line++)
        {
            for (int c = 0; c < l.ClusterCount; c++)
            {
                WorkforceRequest r = l.SkilledRequest(5, line, c, 7);
                Assert.Equal(new WorkforceRequest(5, l.SkilledCategory, l.SkilledRequester(line, c), 7), r);
                Assert.Equal(line * l.ClusterCount + c, r.RequesterId);
            }
        }
    }

    [Fact]
    public void EngineerRequest_NamesTheEngineerCategory_AndKeepsTheId()
    {
        Assert.Equal(new WorkforceRequest(2, ProductsLayout.EngineerCategory, 9, 4), ProductsLayout.EngineerRequest(2, 9, 4));
    }
}
