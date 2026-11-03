using Singularity.Kernel;

namespace Singularity.Core.Knowledge;

/// <summary>Result of the hazard check of one node (spec §3.1).</summary>
public readonly record struct HazardStep(bool Active, Fx6 Y, Fx6 H);

/// <summary>
/// Knowledge formulas of the slice in their canonical Fx6 form: left to right, floor on every
/// <c>·</c> and <c>/</c> (contract v2.3 §4.0, spec §3.5). Do not reorder or simplify: the reference
/// lab/reference/jet_slice/ checks them bit for bit.
/// </summary>
public static class KnowledgeMath
{
    private static readonly Fx6 Twelve = Fx6.FromInt(12);
    private static readonly Fx6 YieldFloor = Fx6.FromRaw(300_000);

    /// <summary><c>c_money = c_n·(12·S_ref)</c> (§3.5 A9).</summary>
    public static Fx6 CMoney(Fx6 cN, Fx6 sRef) => cN * (Twelve * sRef);

    /// <summary>
    /// <c>c_eff = (c_money·(1 − k_diff·known_elsewhere)) / ((1 + (k_K·Kmix)/K_ref)·(1 + Σ w_i))</c>,
    /// <paramref name="informsSum"/> = Σ w_i over known informing nodes (ascending node order).
    /// </summary>
    public static Fx6 CEff(Fx6 cMoney, Fx6 kDiff, bool knownElsewhere, Fx6 kK, Fx6 kmix, Fx6 kRef, Fx6 informsSum)
    {
        Fx6 num = cMoney * (Fx6.One - kDiff * (knownElsewhere ? Fx6.One : Fx6.Zero));
        Fx6 den = (Fx6.One + kK * kmix / kRef) * (Fx6.One + informsSum);
        return num / den;
    }

    /// <summary><c>h = (h_max·y)/(y + y_half)</c>, active only when <c>prog ≥ θ_start</c>.</summary>
    public static HazardStep Hazard(Fx6 prog, Fx6 thetaStart, Fx6 hMax, Fx6 yHalf)
    {
        if (prog < thetaStart)
            return new HazardStep(false, Fx6.Zero, Fx6.Zero);
        Fx6 y = prog - thetaStart;
        return new HazardStep(true, y, hMax * y / (y + yHalf));
    }

    /// <summary><c>ΔK_d = (μ_K·amount_d)/S_ref + k_node·opened_d</c> (§3.1, A13).</summary>
    public static Fx6 KdDelta(Fx6 amount, int opened, Fx6 muK, Fx6 sRef, Fx6 kNode) =>
        muK * amount / sRef + kNode * Fx6.FromInt(opened);

    /// <summary>
    /// One <c>K_ind</c> step (contract §4.3). At <c>K_d = 0</c> there is no growth (v2.1); the same
    /// holds when <c>θ_ind·K_d</c> floors to 0 raw, where the formula would divide by zero.
    /// </summary>
    public static Fx6 KIndStep(Fx6 kInd, Fx6 kd, Fx6 volume, Fx6 eta, Fx6 vRef, Fx6 thetaInd, Fx6 deltaInd)
    {
        if (volume == Fx6.Zero)
            return kInd - deltaInd * kInd;
        Fx6 cap = thetaInd * kd;
        if (kd == Fx6.Zero || cap == Fx6.Zero)
            return kInd;
        Fx6 frac = eta * volume / (volume + vRef);
        Fx6 room = Fx6.One - kInd / cap;
        return kInd + frac * (room > Fx6.Zero ? room : Fx6.Zero);
    }

    /// <summary>
    /// <c>V_d</c> = Σ <c>v_p</c> over processes of discipline d with <c>yield_p ≥ 0.3</c> (contract §4.3);
    /// inputs are the month t−1 columns <c>proc_v</c>, <c>proc_yield</c> in ascending process order.
    /// </summary>
    public static Fx6 DisciplineVolume(ReadOnlySpan<Fx6> procV, ReadOnlySpan<Fx6> procYield, ReadOnlySpan<int> procDiscipline, int discipline)
    {
        Fx6 v = Fx6.Zero;
        for (int p = 0; p < procV.Length; p++)
        {
            if (procDiscipline[p] == discipline && procYield[p] >= YieldFloor)
                v += procV[p];
        }
        return v;
    }
}
