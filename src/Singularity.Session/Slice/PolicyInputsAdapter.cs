using Singularity.Core.Industry;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session;

/// <summary>Inputs of the AI policy: pure reads of the product and capability layers (snapshot t−1). No RNG.</summary>
internal sealed class PolicyInputsAdapter(SliceWorld world) : IPolicyInputs
{
    public int NewestSeriesDesign(int actorRow, long month)
    {
        for (int d = world.ProductsLayout.Designs - 1; d >= 0; d--)
        {
            if (world.Products.Stage(actorRow, d) == DesignStage.Series)
                return d;
        }
        return -1;
    }

    public BottleneckLimit? Bottleneck(int actorRow, int design, long month)
    {
        ProductionProfile profile = ProfileOf(actorRow, design, month);
        int row = world.IndustryLayout.RowOf(profile.BottleneckProcess);
        return row < 0 ? null : new BottleneckLimit(profile.BottleneckSource, world.Industry.Limiter(actorRow, row, month).Value);
    }

    public bool SeriesBelowQuality(int actorRow, Fx6 threshold, long month)
    {
        for (int d = 0; d < world.ProductsLayout.Designs; d++)
        {
            if (world.Products.Stage(actorRow, d) == DesignStage.Series && ProfileOf(actorRow, d, month).Q < threshold)
                return true;
        }
        return false;
    }

    private ProductionProfile ProfileOf(int actorRow, int design, long month) =>
        world.Industry.Profile(actorRow, DesignRules.Items(world.ProductsLayout, world.Products.PrcSet(actorRow, design)), month);
}
