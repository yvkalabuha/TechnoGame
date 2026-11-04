using Singularity.Kernel;

namespace Singularity.Core.Policy;

/// <summary>The target of pass 2 of the tooling plan (spec §11 T6e v0.19, #156). <see cref="V5"/> is the default (#163); V0 is the ablation arm.</summary>
public enum ToolingTarget
{
    /// <summary>Source <c>M</c> (<c>M &lt; H</c>); target <c>min(H, M_cap)</c>.</summary>
    V0 = 0,

    /// <summary>Source <c>M</c>; target <c>min(H + ρ_max, M_cap)</c>.</summary>
    V1 = 1,

    /// <summary><c>M_dec &lt; H'</c>, <c>H' = H + dH_forecast(…)</c>, source <c>M</c> or <c>H</c>; target <c>min(H', M_cap)</c>.</summary>
    V2 = 2,

    /// <summary><c>M_dec &lt; H + h_lo</c>; target <c>min(H + h_hi, M_cap)</c>.</summary>
    V3 = 3,

    /// <summary><c>M ≤ H</c> (snapshot); target <c>min(H + m_lead, M_cap)</c>.</summary>
    V4 = 4,

    /// <summary>Control (print only): V3 with <c>h_hi</c> = <see cref="ToolingTargetSpec.HHiControl"/>.</summary>
    V3b = 5,

    /// <summary>
    /// Lab reference, not selectable by the owner: pass 2 is replaced, <c>M</c> after the plan is <c>M_cap</c> in every cell with a known process with
    /// <c>v_exp &gt; 0</c> and <c>r_d &gt; 0</c>; the money (<c>need∞</c>) is notional and is not taken from the pool.
    /// </summary>
    VInf = 6,

    /// <summary>
    /// Every dimension a known process with <c>v_exp &gt; 0</c> uses (<c>r_d &gt; 0</c>), where the tooling hinders: <c>M ≤ H ∧ M &lt; r_d</c> (snapshot, raw);
    /// target <c>min(H + m_lead, M_cap)</c>. The bottleneck is not required (spec §11 T6e v0.21, #162).
    /// </summary>
    V5 = 7,
}

/// <summary>
/// The <c>TOOLING_TARGET</c> switch of the manifest with its numbers (spec §11 T6e v0.19). The numbers are manifest literals, declared
/// before the run ([Предп.] «З») and part of the run's hash; a variant whose number is missing (≤ 0) is a manifest error.
/// </summary>
public sealed record ToolingTargetSpec
{
    public ToolingTarget Variant { get; init; } = ToolingTarget.V5;

    /// <summary><c>ρ_max</c> of V1: 0,021.</summary>
    public Fx6 RhoMax { get; init; } = Fx6.FromRaw(21_000);

    /// <summary><c>h_lo</c> of V3: 0,021.</summary>
    public Fx6 HLo { get; init; } = Fx6.FromRaw(21_000);

    /// <summary><c>h_hi</c> of V3: 0,063.</summary>
    public Fx6 HHi { get; init; } = Fx6.FromRaw(63_000);

    /// <summary><c>m_lead</c> of V4 and V5: 0,20.</summary>
    public Fx6 MLead { get; init; } = Fx6.FromRaw(200_000);

    /// <summary><c>h_hi</c> of the control V3b: 0,20.</summary>
    public Fx6 HHiControl { get; init; } = Fx6.FromRaw(200_000);

    /// <summary>The variant of a manifest value "V0"…"V5"; anything else is a manifest error.</summary>
    public static ToolingTargetSpec Parse(string value) => value switch
    {
        "V0" => new ToolingTargetSpec { Variant = ToolingTarget.V0 },
        "V1" => new ToolingTargetSpec { Variant = ToolingTarget.V1 },
        "V2" => new ToolingTargetSpec { Variant = ToolingTarget.V2 },
        "V3" => new ToolingTargetSpec { Variant = ToolingTarget.V3 },
        "V3b" => new ToolingTargetSpec { Variant = ToolingTarget.V3b },
        "V4" => new ToolingTargetSpec { Variant = ToolingTarget.V4 },
        "V5" => new ToolingTargetSpec { Variant = ToolingTarget.V5 },
        "VINF" => new ToolingTargetSpec { Variant = ToolingTarget.VInf },
        _ => throw new ArgumentException($"TOOLING_TARGET '{value}' is not one of V0, V1, V2, V3, V3b, V4, V5, VINF.", nameof(value)),
    };

    /// <summary>Manifest check: a known variant and a positive number for every number the variant uses.</summary>
    public void Validate()
    {
        switch (Variant)
        {
            case ToolingTarget.V0 or ToolingTarget.V2 or ToolingTarget.VInf:
                return;
            case ToolingTarget.V1 when RhoMax > Fx6.Zero:
            case ToolingTarget.V3 when HLo > Fx6.Zero && HHi > Fx6.Zero:
            case ToolingTarget.V3b when HLo > Fx6.Zero && HHiControl > Fx6.Zero:
            case ToolingTarget.V4 or ToolingTarget.V5 when MLead > Fx6.Zero:
                return;
            case ToolingTarget.V1 or ToolingTarget.V3 or ToolingTarget.V3b or ToolingTarget.V4 or ToolingTarget.V5:
                throw new ArgumentException($"TOOLING_TARGET {Variant}: a number of the variant is missing or not positive.");
            default:
                throw new ArgumentException($"TOOLING_TARGET {(int)Variant} is not a known variant.");
        }
    }
}
