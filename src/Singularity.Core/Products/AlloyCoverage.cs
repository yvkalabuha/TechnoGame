using Singularity.Core.Industry;
using Singularity.Kernel;

namespace Singularity.Core.Products;

/// <summary>
/// Alloy of an actor against its running lines. <see cref="Supply"/> is kg per month after the disruption cut; <see cref="Need"/> is the largest
/// alloy need of one running line (<c>alloy_kg · rate</c>); <see cref="DeficitFraction"/> is the policy cut 0..1.
/// </summary>
public readonly record struct AlloyCoverage(Fx6 Supply, Fx6 Need, Fx6 DeficitFraction)
{
    /// <summary>Minimum over the running lines of supply / need, capped at 1 (the supply is shared, so it is supply over the largest need); 1 when no line needs alloy.</summary>
    public Fx6 Ratio => Need <= Fx6.Zero || Supply >= Need ? Fx6.One : Supply / Need;

    /// <summary>
    /// Running lines are series-stage designs with a rate above zero and an alloy-using alternative. A pure read of the snapshot, no heap allocation.
    /// </summary>
    public static AlloyCoverage Of(ProductsSystem p, ProductsLayout pl, IndustrySystem industry, Fx6 deficitFraction, int actor, long month)
    {
        Fx6 need = Fx6.Zero;
        for (int d = 0; d < pl.Designs; d++)
        {
            if (p.Stage(actor, d) != DesignStage.Series || p.Rate(actor, d) <= Fx6.Zero)
                continue;
            int alt = p.AltAt(actor, d, pl.AlloySlot);
            if (alt < 0 || pl.AltAlloyFree[alt] || pl.AltAlloyKg[alt] <= Fx6.Zero)
                continue;
            Fx6 line = pl.AltAlloyKg[alt] * p.Rate(actor, d);
            if (line > need)
                need = line;
        }
        return new AlloyCoverage(industry.Alloy(actor, month).Supply, need, deficitFraction);
    }
}
