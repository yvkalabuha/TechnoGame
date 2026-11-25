using Singularity.Kernel;

namespace Singularity.Core.Intelligence;

/// <summary>Intelligence formulas in Fx6 (left to right, floor on every product; no Pow, Exp or Log: ADR-0002).</summary>
public static class IntelMath
{
    private static readonly Fx6 Twelve = Fx6.FromInt(12);

    public static Fx6 Min(Fx6 a, Fx6 b) => a < b ? a : b;

    public static Fx6 Max(Fx6 a, Fx6 b) => a > b ? a : b;

    /// <summary><c>sat(x) = min(1, x)</c> for <c>x ≥ 0</c>.</summary>
    public static Fx6 Sat(Fx6 x) => x <= Fx6.Zero ? Fx6.Zero : Min(Fx6.One, x);

    /// <summary>One month of a yearly rate: a twelfth (the month is only the call frequency, ADR-0027).</summary>
    public static Fx6 PerMonth(Fx6 perYear) => perYear / Twelve;

    /// <summary>Whole part of a non-negative Fx6.</summary>
    public static int Floor(Fx6 x) => (int)(x.Raw / Fx6.Scale);

    /// <summary>Smallest integer not below a non-negative Fx6.</summary>
    public static int Ceiling(Fx6 x) => (int)((x.Raw + Fx6.Scale - 1) / Fx6.Scale);

    /// <summary>
    /// The monthly probability that compounds to a yearly one: the smallest <c>m</c> with <c>(1 − m)^12 ≤ 1 − yearly</c>, by integer bisection
    /// (the same rule as the reserve leak). 0 stays 0, 1 stays 1.
    /// </summary>
    public static Fx6 MonthlyFromYearly(Fx6 yearly)
    {
        if (yearly <= Fx6.Zero)
            return Fx6.Zero;
        if (yearly >= Fx6.One)
            return Fx6.One;
        Fx6 keep = Fx6.One - yearly;
        long lo = 0, hi = Fx6.Scale;
        while (hi - lo > 1)
        {
            long mid = lo + (hi - lo) / 2;
            Fx6 f = Fx6.One - Fx6.FromRaw(mid), p = Fx6.One;
            for (int i = 0; i < 12; i++)
                p *= f;
            if (p <= keep)
                hi = mid;
            else
                lo = mid;
        }
        return Fx6.FromRaw(hi);
    }

    /// <summary><c>conf = 1 − (1 − q)^s</c> for <paramref name="sources"/> independent sources of quality <paramref name="q"/> (maturity).</summary>
    public static Fx6 Confidence(Fx6 q, int sources)
    {
        Fx6 miss = Fx6.One;
        Fx6 one = Fx6.One - Sat(q);
        for (int i = 0; i < sources; i++)
            miss *= one;
        return Fx6.One - miss;
    }

    /// <summary><c>s_steal = min(s_max, σ·N·A)</c>: the discount of the theft channel on a node cost.</summary>
    public static Fx6 StealShare(IntelParams p, Fx6 n, Fx6 a) => Min(p.SMax, p.Sigma * n * a);

    /// <summary>Yearly exposure hazard: <c>clamp(a0·N·act·(1 + g·ci_eff), 0, cap)</c>.</summary>
    public static Fx6 HazardPerYear(IntelParams p, Fx6 n, Fx6 act, Fx6 ciEffOfTarget)
    {
        Fx6 h = p.HazardBasePerYear * n * act * (Fx6.One + p.CiHazardGain * ciEffOfTarget);
        return Min(h, p.HazardCapPerYear);
    }

    public static Fx6 Act(IntelParams p, IntelMode mode) => mode switch
    {
        IntelMode.Observe => p.ActObserve,
        IntelMode.Steal => p.ActSteal,
        IntelMode.FalseTrails => p.ActFalseTrails,
        _ => Fx6.Zero,
    };
}
