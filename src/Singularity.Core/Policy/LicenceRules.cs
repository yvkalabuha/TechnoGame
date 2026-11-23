using Singularity.Kernel;

namespace Singularity.Core.Policy;

/// <summary>Reasons of <c>BAD_LICENCE</c> (contract §5 «Валидатор take_licence», #154).</summary>
public enum BadLicence
{
    None = 0,

    /// <summary><c>s = b</c>.</summary>
    Self = 1,

    /// <summary><c>gen ≤ 0</c> or an empty mask or a negative price.</summary>
    Empty = 2,

    /// <summary>The buyer already has an active licence of this generation or a newer one.</summary>
    Gen = 3,

    /// <summary><c>ProcurementBase(b, t) &lt; price</c>: the buyer's procurement budget would go negative.</summary>
    Money = 4,
}

/// <summary>
/// Pure rules of the licence and of the alloy disruption (spec §3.5 A43): price, mask, the validator of <c>take_licence</c>, the draws. No state;
/// the draws take the stream value, so the same keys give the same events at any thread count.
/// </summary>
public static class LicenceRules
{
    /// <summary>Bit of the process with node number <paramref name="processNumber"/> in a licence mask (1…127; anything else throws).</summary>
    public static LicenceMask Bit(int processNumber) => LicenceMask.Of(processNumber);

    /// <summary>Whether the mask holds the node; a number above the capacity of the mask (63 for a mask read from a one-word policy, else 127) or below 1 throws (it used to answer <c>false</c> silently, H13).</summary>
    public static bool Has(LicenceMask mask, int processNumber) => mask.Has(processNumber);

    /// <summary><c>lic_price = ((unit_cost_floor·n_service_min)·lic_share)</c>, a floor at each product (spec A43.3; G1 of E1: 20 730 raw).</summary>
    public static Fx6 Price(Fx6 unitCostFloor, int nServiceMin, Fx6 licShare) => unitCostFloor * Fx6.FromInt(nServiceMin) * licShare;

    /// <summary>
    /// The validator of <c>take_licence</c> (contract §5): <c>s ≠ b</c>; <c>gen &gt; 0</c>, <c>mask ≠ 0</c>, <c>price ≥ 0</c>; <c>gen &gt; lic_gen(b)</c> or the buyer's
    /// licence is not active; <c>ProcurementBase(b, t) ≥ price</c>. The first failed check names the reason.
    /// </summary>
    public static BadLicence Validate(int buyer, int seller, int gen, LicenceMask mask, Fx6 price, int buyerLicGen, bool buyerLicActive, Fx6 procurementBase)
    {
        if (seller == buyer)
            return BadLicence.Self;
        if (gen <= 0 || mask.IsEmpty || price < Fx6.Zero)
            return BadLicence.Empty;
        if (buyerLicActive && gen <= buyerLicGen)
            return BadLicence.Gen;
        return procurementBase < price ? BadLicence.Money : BadLicence.None;
    }

    /// <summary><c>p_dis_a = p_dis·exposure_a</c> (<see cref="Fx6"/> floor; 8 333 · 0,5 = 4 166 raw).</summary>
    public static Fx6 DisruptionChance(Fx6 pDis, Fx6 exposure) => pDis * exposure;
}
