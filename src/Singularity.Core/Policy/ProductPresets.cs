using System.Collections.Immutable;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Core.Policy;

/// <summary>Variants of a <c>Requirement</c> (observation of the print, SLICE_COMPOSITION §13.5); <c>Producible</c> and <c>FvProducible</c> are empty only when <c>Count &lt; 2</c>.</summary>
public readonly record struct PresetVariants(int Gen, int Count, ImmutableArray<int> Superior, ImmutableArray<int> Producible, Fx6 FvSuperior, Fx6? FvProducible);

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
        if (!DesignRules.Superior(products.Layout, gen, n => products.Knows(actor, n, month), alts))
            return false;
        if (profile == PolicyProfile.Quality)
            return true;
        Span<int> superiorAlts = stackalloc int[products.Layout.SlotCount];
        alts.CopyTo(superiorAlts);
        Span<int> producible = stackalloc int[products.Layout.SlotCount];
        if (!TryProducible(products, p, actor, gen, month, producible))
            return true;
        if (profile == PolicyProfile.Mass)
        {
            producible.CopyTo(alts);
        }
        else
        {
            Fx6 superior = products.CandidateValue(actor, alts, p.FvBudget, month);
            Fx6 other = products.CandidateValue(actor, producible, p.FvBudget, month);
            if (other > superior)
                producible.CopyTo(alts);
        }
        // The pilot choice is a decision of the preset taken: if it is «producible» and does not grow the fleet's generation, go back to «superior».
        if (p.PilotChoice != PilotChoice.Off && PilotKeepsSuperior(products, p, actor, month, alts, superiorAlts))
            superiorAlts.CopyTo(alts);
        return true;
    }

    /// <summary>
    /// Pilot choice (sketch §7.3 В2, no new numbers). The generation <c>g</c> of a composition is the highest node generation among the processes of its
    /// <c>prc_set</c>; the actor's current <c>g</c> is that of its live designs. True when the chosen (producible) composition does not exceed the current
    /// <c>g</c> and the superior one does. (<c>gen(d)</c> and slot ranks do not separate the two: the engine is the same in both.)
    /// With <see cref="PilotChoice.OnAdmitted"/> every process of the superior <c>prc_set</c> must also have <c>yield &gt; 0</c>.
    /// </summary>
    private static bool PilotKeepsSuperior(IPolicyProducts products, PolicyParams p, int actor, long month, ReadOnlySpan<int> chosen, ReadOnlySpan<int> superior)
    {
        ProductsLayout l = products.Layout;
        bool rank = p.PilotChoice == PilotChoice.Rank;
        int current = 0;
        for (int d = 0; d < l.Designs; d++)
        {
            if (products.Design(actor, d, month) is { Cancelled: false, Stage: DesignStage.Project or DesignStage.Prototype or DesignStage.Series or DesignStage.InService })
            {
                int[] own = products.Alternatives(actor, d);
                current = Math.Max(current, rank ? (own[l.AlloySlot] >= 0 ? l.AltRank[own[l.AlloySlot]] : 0) : ProcessGen(l, own));
            }
        }
        // RANK: the hot-section rank (the alloy slot) replaces g; the superior one must be strictly higher than the producible one.
        if (rank)
            return l.AltRank[chosen[l.AlloySlot]] <= current && l.AltRank[superior[l.AlloySlot]] > l.AltRank[chosen[l.AlloySlot]];
        if (ProcessGen(l, chosen) > current || ProcessGen(l, superior) <= current)
            return false;
        if (p.PilotChoice == PilotChoice.OnAdmitted)
        {
            foreach (int prc in DesignRules.PrcSet(l, superior))
            {
                if (products.AdmittedYield(actor, prc, month) <= Fx6.Zero)
                    return false;
            }
        }
        return true;
    }

    private static int ProcessGen(ProductsLayout l, ReadOnlySpan<int> alts)
    {
        int g = 0;
        foreach (int prc in DesignRules.PrcSet(l, alts))
            g = Math.Max(g, l.NodeGen[prc]);
        return g;
    }

    /// <summary>
    /// The <c>producible</c> candidate (P2's preset): false if there is none or it would equal <c>superior</c> (F-PC3). The one step that
    /// <see cref="Compose"/> and <see cref="Variants"/> share, so the choice of a profile and its print cannot drift apart.
    /// </summary>
    private static bool TryProducible(IPolicyProducts products, PolicyParams p, int actor, int gen, long month, Span<int> producible)
    {
        ProductsLayout l = products.Layout;
        Fx6 need = Fx6.FromInt(p.NServiceMin) / Fx6.FromInt(p.TField);
        Fx6 supply = products.AlloySupply(actor, month);
        return DesignRules.Producible(l, gen, n => products.Knows(actor, n, month), q => products.AdmittedYield(actor, q, month), p.YProd,
            hot => l.AltAlloyFree[hot] || supply / l.AltAlloyKg[hot] >= need, producible);
    }

    /// <summary>
    /// The variants of the <c>Requirement</c> for the generation <paramref name="gen"/> (spec §5.5, §7 S1) with <c>FV</c> of both, whatever the
    /// profile: a pure function of the snapshot. <c>Count</c> is 0 if even <c>superior</c> cannot be built, 1 for <c>superior</c> alone
    /// (F-PC3: a <c>producible</c> equal to it is not a second variant), 2 for both.
    /// </summary>
    public static PresetVariants Variants(IPolicyProducts products, PolicyParams p, int actor, int gen, long month)
    {
        int slots = products.Layout.SlotCount;
        var superior = new int[slots];
        if (!DesignRules.Superior(products.Layout, gen, n => products.Knows(actor, n, month), superior))
            return new PresetVariants(gen, 0, [], [], Fx6.Zero, null);
        Fx6 fvSuperior = products.CandidateValue(actor, superior, p.FvBudget, month);
        var producible = new int[slots];
        if (!TryProducible(products, p, actor, gen, month, producible))
            return new PresetVariants(gen, 1, [.. superior], [], fvSuperior, null);
        return new PresetVariants(gen, 2, [.. superior], [.. producible], fvSuperior, products.CandidateValue(actor, producible, p.FvBudget, month));
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
        Span<int> full = stackalloc int[l.SlotCount];
        if (!Compose(profile, products, p, actor, products.Design(actor, parent, month).PlatformGen, month, full))
            return false;
        // The engine of the preset the profile actually uses (spec v0.14 §1.4: max_eng_gen counted) must outrank the parent's.
        if (l.AltRank[full[l.BaseSlot]] < l.AltRank[own[l.BaseSlot]] + 1)
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
