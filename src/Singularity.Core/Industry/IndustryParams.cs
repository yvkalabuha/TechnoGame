using System.Collections.Immutable;
using Singularity.Core.Data;
using Singularity.Kernel;

namespace Singularity.Core.Industry;

/// <summary>
/// Capability-layer parameters of the jet slice (spec docs/simulation/slice-jet-aviation.md §8.1, all per
/// month). Defaults are the spec values; for a range ("К") the value used by the reference fixture is the
/// default, calibration replaces it later.
/// </summary>
public sealed record IndustryParams
{
    private static Fx6 F(long raw) => Fx6.FromRaw(raw);

    /// <summary><c>α</c>, range 0.02–0.08 (К); 0.05 as in the reference fixture.</summary>
    public Fx6 Alpha { get; init; } = F(50_000);

    /// <summary><c>δ_H</c>, range 0.0017–0.0043 (К); 0.003 as in the reference fixture.</summary>
    public Fx6 DeltaH { get; init; } = F(3_000);
    public Fx6 RhoMax { get; init; } = F(21_000);

    /// <summary><c>k_catch = 0.5·α</c> (С).</summary>
    public Fx6 KCatch { get; init; } = F(25_000);
    public Fx6 Sigma { get; init; } = F(5_000);
    public Fx6 PhiFloor { get; init; } = F(600_000);
    public Fx6 KQp { get; init; } = F(500_000);

    /// <summary><c>act_norm</c> (spec §3.2).</summary>
    public Fx6 ActNorm { get; init; } = Fx6.One;
    public Fx6 Lambda0 { get; init; } = F(300_000);

    /// <summary><c>X½</c> by complexity tier, index = tier − 3: t3 200, t4 800, t5 2500 units (К).</summary>
    public ImmutableArray<Fx6> XHalfByTier { get; init; } = [Fx6.FromInt(200), Fx6.FromInt(800), Fx6.FromInt(2500)];
    public Fx6 DeltaX { get; init; } = F(4_000);
    public Fx6 DeltaM { get; init; } = F(5_000);

    /// <summary>
    /// <c>c_M</c> (spec §3.5 A11, A21): <c>cost_M(k) = c_M·2^(k−3)</c> for k = 3, 4, 5, money per unit of
    /// <c>M</c>; 0.1 (К). The table by k is derived, never set on its own, so the 1:2:4 ratio always holds.
    /// </summary>
    public Fx6 CM { get; init; } = F(100_000);

    /// <summary><c>cost_M(k)</c>, k = 3…5 (spec §3.5 A11).</summary>
    public Fx6 CostM(int k)
    {
        if (k < 3 || k > 5)
            throw new ArgumentOutOfRangeException(nameof(k), k, "cost_M is defined for k = 3…5.");
        return CM * Fx6.FromInt(1L << (k - 3));
    }

    // ---- culture rule C-B (contract v2.4 §4.1, decisions #111, #115)

    /// <summary><c>r_ref</c>: mature-line rate, units of a process per month (reference line, spec §6.4).</summary>
    public Fx6 RRef { get; init; } = Fx6.FromInt(25);

    /// <summary><c>r_cap = r_ref/2.5</c> (10): output rate at which money from output is fully useful.</summary>
    public Fx6 RCap => RRef / F(2_500_000);

    /// <summary><c>r_keep = r_ref/4</c> (6.25): output rate that fully holds the culture.</summary>
    public Fx6 RKeep => RRef / Fx6.FromInt(4);

    /// <summary><c>k_tr</c>: licence multiplier on the output channel, <c>ṽ = (1 + k_tr·lic)·v</c> (#104, #107).</summary>
    public Fx6 KTr { get; init; } = Fx6.One;

    /// <summary><c>E_full</c>: a raw literal of the manifest (1838 raw), never recomputed during a run (G4-4).</summary>
    public Fx6 EFull { get; init; } = F(1_838);

    /// <summary><c>ℓ</c>: laboratory share of money that works without output (#115); 0 only for the ablation.</summary>
    public Fx6 Ell { get; init; } = F(100_000);

    /// <summary><c>ΔL</c>: how far below <c>r_d</c> the laboratory alone can lift the culture (#115).</summary>
    public Fx6 DeltaL { get; init; } = F(500_000);

    public Fx6 Eps { get; init; } = F(10_000);

    /// <summary><c>yield ≥ 0.7</c> of another actor makes <c>world_lead</c> (contract §4.1).</summary>
    public Fx6 WorldLeadYield { get; init; } = F(700_000);

    /// <summary>Months in a row a process must hold <c>yield ≥ <see cref="MasteredYield"/></c> and <c>proc_v ≥ v_min</c> to count as mastered (contract r6 §9.1).</summary>
    public const int MasteredMonths = 6;

    /// <summary><c>yield_p ≥ 0,7</c> of the mastered condition (contract r6 §9.1).</summary>
    public Fx6 MasteredYield { get; init; } = F(700_000);

    /// <summary>
    /// <c>v_min</c>: labour hours a month of the process below which it does not count as mastered (contract r6 §9.1, spec §8.1 «З»): 5% of the
    /// output of a mature line, <c>V_ref</c> = 18 750 h (<c>ProductsLayout.ReferenceVRef</c>, the lower median of the G2 project).
    /// </summary>
    public Fx6 VMin { get; init; } = F(937_500_000);

    /// <summary><c>yield ≥ 0.3</c> admits <c>v_p</c> into <c>discipline_volume</c> (contract §4.3).</summary>
    public Fx6 DisciplineYield { get; init; } = F(300_000);

    /// <summary>
    /// <c>R[c,c']</c> of spec §8.1: MET–CHEM 0.3, MET–MECH 0.2, MECH–AERO 0.3, CHEM–AERO 0.1, other pairs 0.05,
    /// symmetric, 0 on the diagonal.
    /// </summary>
    public Fx6 Spill(Cluster a, Cluster b)
    {
        if (a == b)
            return Fx6.Zero;
        foreach ((Cluster x, Cluster y, Fx6 r) in SpillPairs)
        {
            if ((x == a && y == b) || (x == b && y == a))
                return r;
        }
        return SpillDefault;
    }

    public ImmutableArray<(Cluster A, Cluster B, Fx6 R)> SpillPairs { get; init; } =
    [
        (Cluster.MET, Cluster.CHEM, F(300_000)), (Cluster.MET, Cluster.MECH, F(200_000)),
        (Cluster.MECH, Cluster.AERO, F(300_000)), (Cluster.CHEM, Cluster.AERO, F(100_000)),
    ];

    public Fx6 SpillDefault { get; init; } = F(50_000);

    /// <summary>X½ of a tier (3…5).</summary>
    public Fx6 XHalf(int tier)
    {
        if (tier < 3 || tier - 3 >= XHalfByTier.Length)
            throw new ArgumentOutOfRangeException(nameof(tier), tier, "No X½ for this tier.");
        return XHalfByTier[tier - 3];
    }

    /// <summary>Throws if a value would break a formula (division by zero, l_lic and caps out of range).</summary>
    public void Validate()
    {
        if (ActNorm <= Fx6.Zero || Lambda0 < Fx6.Zero || Lambda0 > Fx6.One || Eps <= Fx6.Zero)
            throw new ArgumentException("act_norm and ε must be positive, λ0 in [0; 1].");
        if (CM <= Fx6.Zero)
            throw new ArgumentException("c_M must be positive.");
        if (RRef <= Fx6.Zero || KTr < Fx6.Zero || EFull < Fx6.Zero || DeltaL < Fx6.Zero || Ell < Fx6.Zero || Ell > Fx6.One)
            throw new ArgumentException("r_ref must be positive, k_tr, E_full, ΔL non-negative, ℓ in [0; 1].");
        foreach (Fx6 x in XHalfByTier)
        {
            if (x <= Fx6.Zero)
                throw new ArgumentException("X½ must be positive.");
        }
    }
}
