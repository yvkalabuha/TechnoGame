using Singularity.Kernel;

namespace Singularity.Core.Institutes;

/// <summary>Formulas of the slot science model in canonical Fx6 form (left to right, floor on every product and quotient). Pure functions.</summary>
public static class SlotMath
{
    /// <summary><c>T_n = TimeBase · c_type · (1 + slope · gen)</c> months.</summary>
    public static Fx6 NodeMonths(SlotParams p, Fx6 costType, int gen) => p.TimeBase * costType * (Fx6.One + p.TimeGenSlope * Fx6.FromInt(gen));

    /// <summary>
    /// <c>T_eff = max(1, T_n · (1 − discount))</c>: diffusion and theft shorten the time with the same discount as the price (O-089e).
    /// </summary>
    public static Fx6 EffectiveMonths(Fx6 tn, Fx6 discount)
    {
        Fx6 te = tn * (Fx6.One - discount);
        return te < Fx6.One ? Fx6.One : te;
    }

    /// <summary>
    /// Overlap level of an institute with a node (weighted main, O-092): <c>0.5·L[main] + 0.5·ΣL[others]/(n−1)</c>, <c>L[main]</c> alone for one direction.
    /// <paramref name="levels"/> are the institute's levels in the node's directions, main first (0 where it has none).
    /// </summary>
    public static Fx6 OverlapLevel(ReadOnlySpan<int> levels, Fx6 mainWeight)
    {
        if (levels.Length == 0)
            return Fx6.Zero;
        if (levels.Length == 1)
            return Fx6.FromInt(levels[0]);
        int rest = 0;
        for (int i = 1; i < levels.Length; i++)
            rest += levels[i];
        return mainWeight * Fx6.FromInt(levels[0]) + (Fx6.One - mainWeight) * Fx6.FromInt(rest) / Fx6.FromInt(levels.Length - 1);
    }

    /// <summary>Share of the progress credited to direction <paramref name="index"/> of a node with <paramref name="count"/> directions (main first).</summary>
    public static Fx6 DirectionWeight(int index, int count, Fx6 mainWeight) =>
        count <= 1 ? Fx6.One : index == 0 ? mainWeight : (Fx6.One - mainWeight) / Fx6.FromInt(count - 1);

    /// <summary><c>M = min(cap, m_inst · s)</c> (the research_speed factor R is 1 until the data has such nodes).</summary>
    public static Fx6 Speed(SlotParams p, Fx6 instituteMultiplier, int focus)
    {
        Fx6 m = instituteMultiplier * p.FocusSpeed[focus];
        return m > p.SpeedCap ? p.SpeedCap : m;
    }

    /// <summary>Money rate of a slot per month (O-089a): <c>C · M · s^0.6 / T_eff</c>.</summary>
    public static Fx6 Rate(SlotParams p, Fx6 cEff, Fx6 speed, int focus, Fx6 teff) => cEff * speed * p.FocusMoney[focus] / teff;

    /// <summary>Coverage <c>u = min(1, B / Σ rate)</c>, equal for all slots, independent of their order.</summary>
    public static Fx6 Coverage(Fx6 budget, Fx6 total) => total <= budget || total == Fx6.Zero ? Fx6.One : budget / total;
}
