using Singularity.Core.Industry;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session;

/// <summary>Inputs of the AI policy: pure reads of the product and capability layers (snapshot t−1). No RNG.</summary>
internal sealed class PolicyInputsAdapter(SliceWorld world) : IPolicyInputs
{
    public DesignHandle? NewestSeriesDesign(int actorRow, long month)
    {
        // F17b (A41): with two lines the training series is the candidate (the larger g_p); with ADMIT or without the product AI (a scripted player) the newest by id, as before.
        int best = -1;
        for (int d = world.ProductsLayout.Designs - 1; d >= 0; d--)
        {
            if (world.Products.Stage(actorRow, d) != DesignStage.Series)
                continue;
            if (world.Policy.LineClose == LineCloseRule.Admit || !world.Config.ProductAi)
                return HandleOf(actorRow, d);
            if (best < 0 || world.Products.PlatformGen(actorRow, d) > world.Products.PlatformGen(actorRow, best))
                best = d;
        }
        return best < 0 ? null : HandleOf(actorRow, best);
    }

    private DesignHandle HandleOf(int actorRow, int design) => new(design, world.Products.Generation(actorRow, design));

    public BottleneckLimit? Bottleneck(int actorRow, int design, long month)
    {
        ProductionProfile profile = ProfileOf(actorRow, design, month);
        int row = world.IndustryLayout.RowOf(profile.BottleneckProcess);
        // Spec A27: in the slice a site is an actor, so the site row is the actor row (one site per actor; the table has one row per actor).
        int site = actorRow;
        return row < 0 ? null : new BottleneckLimit(profile.BottleneckSource, world.Industry.Limiter(site, row, month).Value,
            world.Industry.ProcV(site, row) > Fx6.Zero, profile.BottleneckProcess);
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
        world.Industry.Profile(actorRow, world.Products.PrcItems(actorRow, design), month);
}
