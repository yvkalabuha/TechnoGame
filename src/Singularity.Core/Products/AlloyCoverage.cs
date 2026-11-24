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
    /// The coverage of the step of <paramref name="month"/>. The need is the ask of that step 7 (<see cref="ProductsSystem.LastAlloyAsk"/>, read by the caller: it is a scratch
    /// trace), not the produced rate, which is already cut by the alloy, so a line stopped for lack of alloy would drop out. Before the first step the ask is 0.
    /// </summary>
    public static AlloyCoverage Of(IndustrySystem industry, Fx6 ask, Fx6 deficitFraction, int actor, long month) =>
        new(industry.Alloy(actor, month).Supply, ask, deficitFraction);
}
