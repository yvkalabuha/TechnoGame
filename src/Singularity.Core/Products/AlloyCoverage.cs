using Singularity.Core.Industry;
using Singularity.Kernel;

namespace Singularity.Core.Products;

/// <summary>
/// Alloy of an actor against its running lines in one monthly step. <see cref="Supply"/> is the kg the step started from (after the disruption cut);
/// <see cref="Need"/> is the alloy the admitted lines asked for in the same step (alloy_kg · asked rate, before the alloy cut, saturated);
/// <see cref="DeficitFraction"/> is the policy cut 0..1. <see cref="Known"/> is false when no step has run in this session (a restored snapshot, a view before the first step):
/// then there is nothing to show, and a reader must not read it as «enough».
/// </summary>
public readonly record struct AlloyCoverage(Fx6 Supply, Fx6 Need, Fx6 DeficitFraction, bool Known = true)
{
    /// <summary>Supply over the asked need, capped at 1 (the supply is shared by the lines, so the minimum over lines is the same ratio); 1 when no line needs alloy.</summary>
    public Fx6 Ratio => Need <= Fx6.Zero || Supply >= Need ? Fx6.One : Supply / Need;
}
