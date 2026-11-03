using System.Collections.Immutable;
using Singularity.Kernel;

namespace Singularity.Core.Industry;

/// <summary>Full capability model or one of the capability-off controls (slice spec §6.1).</summary>
public enum IndustryMode
{
    Full,

    /// <summary>L_C: one median curve <c>Y_k(τ)</c>, <c>U_k(τ)</c> per tier after <c>t_known</c>; <c>q = 1</c>.</summary>
    CurveLc,

    /// <summary>L_C0: <c>yield = 1</c>, <c>unit_cost_mult = 1</c>, <c>q = 1</c> (print only).</summary>
    ConstLc0,
}

/// <summary>
/// One L_C curve (spec §6.1): knots at integer months τ (strictly ascending) with median <c>yield</c> and
/// <c>unit_cost_mult</c>. The numbers come from calibration seeds of the full model (T11/T12); this type only
/// carries and evaluates them.
/// </summary>
public sealed class CapabilityCurve
{
    public CapabilityCurve(ImmutableArray<int> tau, ImmutableArray<Fx6> yield, ImmutableArray<Fx6> unitCost)
    {
        if (tau.IsDefaultOrEmpty || tau.Length != yield.Length || tau.Length != unitCost.Length)
            throw new ArgumentException("A curve needs at least one knot and one yield and unit cost per knot.", nameof(tau));
        for (int i = 0; i < tau.Length; i++)
        {
            if (i > 0 && tau[i] <= tau[i - 1])
                throw new ArgumentException("Curve knots must be strictly ascending.", nameof(tau));
            if (yield[i] < Fx6.Zero || yield[i] > Fx6.One || unitCost[i] <= Fx6.Zero)
                throw new ArgumentException("Curve yield must be in [0; 1] and unit cost positive.", nameof(yield));
        }
        Tau = tau;
        Yield = yield;
        UnitCost = unitCost;
    }

    public ImmutableArray<int> Tau { get; }
    public ImmutableArray<Fx6> Yield { get; }
    public ImmutableArray<Fx6> UnitCost { get; }

    public Fx6 YieldAt(long tau) => IndustryMath.Interpolate(Tau.AsSpan(), Yield.AsSpan(), tau);

    public Fx6 UnitCostAt(long tau) => IndustryMath.Interpolate(Tau.AsSpan(), UnitCost.AsSpan(), tau);
}
