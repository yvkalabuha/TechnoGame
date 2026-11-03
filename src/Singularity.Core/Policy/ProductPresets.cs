using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Core.Policy;

/// <summary>
/// The compositions of the AI (spec §1.4, COMPONENT_SELECTION §6): <c>superior</c> (P1), <c>producible</c> (P2) and <c>adaptive</c> (P3, the
/// larger <c>FV</c> of the two; a tie goes to <c>superior</c>, the higher rank). Pure functions of the snapshot, no RNG (#28). The first two
/// are <see cref="DesignRules"/>; this class adds what the policy knows: the alloy test <c>alloy_ok</c> and <c>FV</c>.
/// </summary>
public static class ProductPresets
{
    /// <summary>
    /// Composition of the profile for the target generation <paramref name="gen"/> into <paramref name="alts"/>; false if even
    /// <c>superior</c> cannot be built (an unavailable slot, or no airframe above the platform threshold). F-PC3: a <c>producible</c> that
    /// would equal <c>superior</c> is not a second design — the result is <c>superior</c>.
    /// </summary>
    public static bool Compose(PolicyProfile profile, IPolicyProducts products, PolicyParams p, int actor, int gen, long month, Span<int> alts)
    {
        ProductsLayout l = products.Layout;
        bool Knows(int n) => products.Knows(actor, n, month);
        if (!DesignRules.Superior(l, gen, Knows, alts))
            return false;
        if (profile == PolicyProfile.Quality)
            return true;
        Fx6 need = Fx6.FromInt(p.NServiceMin) / Fx6.FromInt(p.TField);
        Fx6 supply = products.AlloySupply(actor, month);
        Span<int> producible = stackalloc int[l.SlotCount];
        if (!DesignRules.Producible(l, gen, Knows, q => products.AdmittedYield(actor, q, month), p.YProd,
                hot => l.AltAlloyFree[hot] || supply / l.AltAlloyKg[hot] >= need, producible))
            return true;
        if (profile == PolicyProfile.Mass)
        {
            producible.CopyTo(alts);
            return true;
        }
        Fx6 superior = products.CandidateValue(actor, alts, p.FvBudget, month);
        Fx6 other = products.CandidateValue(actor, producible, p.FvBudget, month);
        if (other > superior)
            producible.CopyTo(alts);
        return true;
    }

    /// <summary>
    /// The <c>derive</c> of the arm DERIV (spec §6.8): the profile's preset for the parent's platform generation, as −1 («unchanged») in every
    /// slot where it gives the parent's alternative and in the airframe slot; false unless the engine the preset gives (the highest one the other slots
    /// admit, <c>max_eng_gen</c> counted) is at least the parent's engine rank + 1 and some slot changes.
    /// </summary>
    public static bool Derive(PolicyProfile profile, IPolicyProducts products, PolicyParams p, int actor, int parent, long month, Span<int> alts)
    {
        ProductsLayout l = products.Layout;
        int[] own = products.Alternatives(actor, parent);
        // The engine the preset gives (spec v0.14 §1.4): with max_eng_gen the highest known engine may be out of reach of the other slots.
        Span<int> top = stackalloc int[l.SlotCount];
        if (!DesignRules.Superior(l, products.Design(actor, parent, month).PlatformGen, n => products.Knows(actor, n, month), top)
            || l.AltRank[top[l.BaseSlot]] < l.AltRank[own[l.BaseSlot]] + 1)
            return false;
        Span<int> full = stackalloc int[l.SlotCount];
        if (!Compose(profile, products, p, actor, products.Design(actor, parent, month).PlatformGen, month, full))
            return false;
        bool changed = false;
        for (int s = 0; s < l.SlotCount; s++)
        {
            bool keep = s == l.AirframeSlot || full[s] == own[s];
            alts[s] = keep ? -1 : full[s];
            changed |= !keep;
        }
        return changed;
    }
}
