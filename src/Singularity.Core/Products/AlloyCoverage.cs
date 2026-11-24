using Singularity.Core.Industry;
using Singularity.Kernel;

namespace Singularity.Core.Products;

/// <summary>What the alloy of an actor is doing in a monthly step, from its <see cref="AlloyCoverage"/> (decided here, not by a reader).</summary>
public enum AlloyState
{
    /// <summary>No step has run in this session (or a restored snapshot): nothing to show.</summary>
    Unknown = 0,
    /// <summary>Supply covers what the lines asked for and no policy cut is on.</summary>
    Normal,
    /// <summary>A policy disruption cuts the supply; the supply still covers the ask (or no line asks).</summary>
    Disrupted,
    /// <summary>The supply is below what the running lines asked for.</summary>
    Shortage,
    /// <summary>No supply at all: no admitted melt, or a full cut.</summary>
    NoSmelting,
}

/// <summary>
/// Alloy of an actor against its running lines in one monthly step. <see cref="Supply"/> is the kg the step started from (after the disruption cut);
/// <see cref="Need"/> is the alloy the admitted lines asked for in the same step (alloy_kg · asked rate, before the alloy cut, saturated);
/// <see cref="DeficitFraction"/> is the policy cut 0..1. <see cref="Known"/> is false when no step has run in this session (a restored snapshot, a view before the first step):
/// then there is nothing to show, and a reader must not read it as «enough».
/// </summary>
public readonly record struct AlloyCoverage(Fx6 Supply, Fx6 Need, Fx6 DeficitFraction, bool Known = true)
{
    /// <summary>The state of the alloy this month. No supply wins over a shortage (nothing to count a need against), a shortage over a disruption.</summary>
    public AlloyState State =>
        !Known ? AlloyState.Unknown
        : Supply <= Fx6.Zero ? AlloyState.NoSmelting
        : Need > Fx6.Zero && Supply < Need ? AlloyState.Shortage
        : DeficitFraction > Fx6.Zero ? AlloyState.Disrupted
        : AlloyState.Normal;

    /// <summary>Supply over the asked need, capped at 1 (the supply is shared by the lines, so the minimum over lines is the same ratio); 1 when no line needs alloy.</summary>
    public Fx6 Ratio => Need <= Fx6.Zero || Supply >= Need ? Fx6.One : Supply / Need;
}
