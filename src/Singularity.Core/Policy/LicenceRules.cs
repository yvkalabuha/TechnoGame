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

    /// <summary>Market only: the buyer is outside the seller's circle (bloc, friends, all; O-056) or the seller's catalogue is closed.</summary>
    Circle = 5,

    /// <summary>Market only: the buyer has no free project slot for the licensed project (checked before any payment).</summary>
    Capacity = 6,

    /// <summary>Market only: the seller's licence income of the year would pass the yearly cap (O-062).</summary>
    Cap = 7,

    /// <summary>Market only: an AI buyer's deal reached settlement above its ceiling <c>k × ref</c> (the seller raised the price after the decision).</summary>
    Price = 8,
}

/// <summary>Why the AI buyer does not take an offer of the market (the first failed rule of licence-prices.md §4); an observation.</summary>
public enum AiDecline
{
    None = 0,
    Circle,
    Profile,
    PriceAboveK,
    OwnEtaShort,
    Reserve,
    Held,
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

    /// <summary>
    /// The validator of the market (flag LicenceMarket): the checks of <see cref="Validate"/> in the order Self, Empty, Gen (the key (seller, gen) is already held
    /// and active), Money (<paramref name="funds"/> against the price; <paramref name="limit"/> is the stock limit L of an AI buyer), Circle, Capacity, Cap. The first failed check names the reason.
    /// </summary>
    public static BadLicence ValidateMarket(int buyer, int seller, int gen, bool offered, bool keyHeld, Fx6 price, Fx6 funds, Fx6 limit, bool inCircle, bool hasSlot, bool overCap)
    {
        if (seller == buyer)
            return BadLicence.Self;
        if (gen <= 0 || !offered || price < Fx6.Zero)
            return BadLicence.Empty;
        if (keyHeld)
            return BadLicence.Gen;
        if (funds < price || limit < price)
            return BadLicence.Money;
        if (!inCircle)
            return BadLicence.Circle;
        if (!hasSlot)
            return BadLicence.Capacity;
        return overCap ? BadLicence.Cap : BadLicence.None;
    }

    /// <summary>Whether a seller's multiplier is inside the declared range (O-062: [0,5; 2]).</summary>
    public static bool MultiplierInRange(Fx6 m, Fx6 mMin, Fx6 mMax) => m >= mMin && m <= mMax;

    /// <summary>
    /// The buying rule of the AI after the circle and profile checks (licence-prices.md §4 items 3, 5, 6): the price not above <c>k × ref</c>, the own ETA to the
    /// generation not below the minimum (null = no estimate = far away), the price within the stock limit <c>L</c>. The first failed rule is returned.
    /// </summary>
    public static AiDecline AiRule(Fx6 price, Fx6 reference, Fx6 k, int? ownEtaMonths, int minEtaMonths, Fx6 limit)
    {
        if (price > reference * k)
            return AiDecline.PriceAboveK;
        if (ownEtaMonths is { } eta && eta < minEtaMonths)
            return AiDecline.OwnEtaShort;
        return price > limit ? AiDecline.Reserve : AiDecline.None;
    }

    /// <summary>
    /// <c>WorldAvg</c> (licence-prices.md §5): the mean of the asking prices of the AI catalogues of one generation, or null while there are fewer than
    /// <paramref name="minSellers"/> of them (the screen then shows the reference price and «мало предложений»).
    /// </summary>
    public static Fx6? WorldAverage(ReadOnlySpan<Fx6> prices, int minSellers)
    {
        if (prices.Length < minSellers || prices.Length == 0)
            return null;
        Fx6 sum = Fx6.Zero;
        foreach (Fx6 p in prices)
            sum += p;
        return sum / Fx6.FromInt(prices.Length);
    }

    /// <summary><c>p_dis_a = p_dis·exposure_a</c> (<see cref="Fx6"/> floor; 8 333 · 0,5 = 4 166 raw).</summary>
    public static Fx6 DisruptionChance(Fx6 pDis, Fx6 exposure) => pDis * exposure;
}
