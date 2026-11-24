using Singularity.Core.Industry;
using Singularity.Kernel;

namespace Singularity.Core.Products;

/// <summary>
/// Alloy of an actor against its running lines in one month. <see cref="Supply"/> is kg after the disruption cut; <see cref="Need"/> is the alloy the admitted lines
/// asked for in that month's step (alloy_kg · asked rate, before the alloy cut); <see cref="DeficitFraction"/> is the policy cut 0..1.
/// </summary>
public readonly record struct AlloyCoverage(Fx6 Supply, Fx6 Need, Fx6 DeficitFraction)
{
    /// <summary>Supply over the asked need, capped at 1 (the supply is shared by the lines, so the minimum over lines is the same ratio); 1 when no line needs alloy.</summary>
    public Fx6 Ratio => Need <= Fx6.Zero || Supply >= Need ? Fx6.One : Supply / Need;

    /// <summary>
    /// The need is the ask of the last step 7 (<see cref="ProductsSystem.LastAlloyAsk"/>), not the produced rate: the produced rate is already cut by the alloy,
    /// so a line stopped for lack of alloy would drop out. <paramref name="month"/> is the month of that step; before the first step there is no ask and the need is 0.
    /// A pure read, no heap allocation.
    /// </summary>
    public static AlloyCoverage Of(ProductsSystem p, IndustrySystem industry, Fx6 deficitFraction, int actor, long month) =>
        new(industry.Alloy(actor, month).Supply, p.LastAlloyAsk(actor), deficitFraction);
}
