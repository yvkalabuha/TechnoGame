using Singularity.Core.Industry;
using Singularity.Kernel;

namespace Singularity.Core.Products;

/// <summary>
/// Choice of alternatives per slot (spec §1.4, contract §4.4; product_class.md F-PC1–3) and the derived design
/// numbers. Deterministic (#28): pure functions of what the actor knows and of snapshot yields. A design is an array
/// of one alternative index (layout-global) per slot. The engine slot's rank is its generation: a design of
/// generation g needs an engine of rank ≥ g (APP g requires an engine ≥ g).
/// A32 отменён (#81): no generation cap — a project takes the best known components of any generation; стоимость по
/// фактическим компонентам — ожидает спецификации (cost, alloy and W still come from the ordered generation g).
/// </summary>
public static class DesignRules
{
    /// <summary>An alternative is available when all its <c>requires_nodes</c> groups and all its processes are known.</summary>
    public static bool Available(ProductsLayout l, int alt, Func<int, bool> knows)
    {
        for (int g = l.AltGroupStart[alt]; g < l.AltGroupStart[alt + 1]; g++)
        {
            bool any = false;
            for (int m = l.GroupMemberStart[g]; m < l.GroupMemberStart[g + 1] && !any; m++)
                any = knows(l.GroupMembers[m]);
            if (!any)
                return false;
        }
        foreach (int p in l.PrcOf(alt))
        {
            if (!knows(p))
                return false;
        }
        return true;
    }

    /// <summary>«превосходящий»: the best (highest-rank) available alternative in every slot; false if a slot has none or the engine is below the generation.</summary>
    public static bool Superior(ProductsLayout l, int gen, Func<int, bool> knows, Span<int> alts)
    {
        for (int s = 0; s < l.SlotCount; s++)
        {
            alts[s] = -1;
            for (int a = l.AltStart[s + 1] - 1; a >= l.AltStart[s] && alts[s] < 0; a--)
            {
                if (Available(l, a, knows))
                    alts[s] = a;
            }
            if (alts[s] < 0)
                return false;
        }
        return l.AltRank[alts[l.BaseSlot]] >= gen;
    }

    /// <summary>
    /// «технологичный» (spec §1.4): the engine as in «превосходящий» (never lightened); in every other slot the best available
    /// alternative whose bottleneck process yields ≥ <paramref name="yProd"/> on the own site (an empty <c>prc_set</c> passes),
    /// else the lowest available one [Предл.: the spec does not say what to take when none passes]. If the <c>prc_set</c> then
    /// equals the superior one, the slot with the highest tier (ties by lighten order, F-PC2) that has a lower available
    /// alternative takes the next lower one; if no slot can, the variant equals «превосходящий» (F-PC3) and the result is false.
    /// </summary>
    public static bool Producible(ProductsLayout l, int gen, Func<int, bool> knows, Func<int, Fx6> yieldOf, Fx6 yProd, Span<int> alts)
    {
        Span<int> sup = stackalloc int[l.SlotCount];
        if (!Superior(l, gen, knows, sup))
            return false;
        for (int s = 0; s < l.SlotCount; s++)
        {
            if (s == l.BaseSlot)
            {
                alts[s] = sup[s];
                continue;
            }
            alts[s] = -1;
            int lowest = -1;
            for (int a = l.AltStart[s + 1] - 1; a >= l.AltStart[s]; a--)
            {
                if (!Available(l, a, knows))
                    continue;
                lowest = a;
                if (alts[s] < 0 && BottleneckYield(l, a, yieldOf) >= yProd)
                    alts[s] = a;
            }
            if (alts[s] < 0)
                alts[s] = lowest;
        }
        if (!SamePrcSet(l, alts, sup))
            return true;
        int best = -1, bestTier = -1, bestLower = -1;
        foreach (int s in l.LightenOrder)
        {
            int lower = -1;
            for (int a = alts[s] - 1; a >= l.AltStart[s] && lower < 0; a--)
            {
                if (Available(l, a, knows))
                    lower = a;
            }
            if (lower >= 0 && l.AltTier[alts[s]] > bestTier)
            {
                best = s;
                bestTier = l.AltTier[alts[s]];
                bestLower = lower;
            }
        }
        if (best < 0)
        {
            sup.CopyTo(alts);
            return false;
        }
        alts[best] = bestLower;
        return true;
    }

    /// <summary>
    /// Canonical project of the L_P control (spec §6.1): in every slot the best alternative that is available and whose
    /// processes are all admitted on the site (yield &gt; 0), else the next lower; false if a slot has none or the engine is
    /// below the generation.
    /// </summary>
    public static bool Canonical(ProductsLayout l, int gen, Func<int, bool> knows, Func<int, Fx6> yieldOf, Span<int> alts)
    {
        for (int s = 0; s < l.SlotCount; s++)
        {
            alts[s] = -1;
            for (int a = l.AltStart[s + 1] - 1; a >= l.AltStart[s] && alts[s] < 0; a--)
            {
                if (Available(l, a, knows) && (l.PrcOf(a).IsEmpty || BottleneckYield(l, a, yieldOf) > Fx6.Zero))
                    alts[s] = a;
            }
            if (alts[s] < 0)
                return false;
        }
        return l.AltRank[alts[l.BaseSlot]] >= gen;
    }

    /// <summary><c>prc_set</c>: union of the alternatives' processes, ascending numbers.</summary>
    public static int[] PrcSet(ProductsLayout l, ReadOnlySpan<int> alts)
    {
        var set = new List<int>();
        foreach (int a in alts)
        {
            foreach (int p in l.PrcOf(a))
            {
                if (!set.Contains(p))
                    set.Add(p);
            }
        }
        set.Sort();
        return [.. set];
    }

    /// <summary><c>complexity</c> = Σ tier over the <c>prc_set</c>.</summary>
    public static int Complexity(ProductsLayout l, ReadOnlySpan<int> prcSet)
    {
        int sum = 0;
        foreach (int p in prcSet)
            sum += l.ProcTier[l.ProcIndex(p)];
        return sum;
    }

    /// <summary><c>perf</c> = engine base × Π slot multipliers, in slot data order (spec §1.4).</summary>
    public static Fx6 Perf(ProductsLayout l, ReadOnlySpan<int> alts)
    {
        Fx6 perf = l.AltPerf[alts[l.BaseSlot]];
        for (int s = 0; s < l.SlotCount; s++)
        {
            if (s != l.BaseSlot)
                perf *= l.AltPerf[alts[s]];
        }
        return perf;
    }

    /// <summary>The <c>prc_set</c> with <c>n_p</c> and <c>base_cost_p</c> for <c>production_profile</c>.</summary>
    public static PrcSetItem[] Items(ProductsLayout l, ReadOnlySpan<int> prcSet)
    {
        var items = new PrcSetItem[prcSet.Length];
        for (int i = 0; i < prcSet.Length; i++)
        {
            int k = l.ProcIndex(prcSet[i]);
            items[i] = new PrcSetItem(prcSet[i], l.ProcN[k], l.ProcBaseCost[k]);
        }
        return items;
    }

    /// <summary><c>Σ n_p·base_cost_p</c> over the <c>prc_set</c>, ascending numbers.</summary>
    public static Fx6 BaseCostSum(ProductsLayout l, ReadOnlySpan<int> prcSet)
    {
        Fx6 sum = Fx6.Zero;
        foreach (int p in prcSet)
        {
            int k = l.ProcIndex(p);
            sum += l.ProcN[k] * l.ProcBaseCost[k];
        }
        return sum;
    }

    /// <summary>
    /// <c>novelty</c> (spec §3.3): share of slots whose alternative the actor has not used in any earlier design;
    /// <paramref name="usedAlts"/> lists the alternatives of the actor's existing designs.
    /// </summary>
    public static Fx6 Novelty(ProductsLayout l, ReadOnlySpan<int> alts, ReadOnlySpan<int> usedAlts)
    {
        int fresh = 0;
        foreach (int a in alts)
        {
            if (usedAlts.IndexOf(a) < 0)
                fresh++;
        }
        return Fx6.FromInt(fresh) / Fx6.FromInt(l.SlotCount);
    }

    private static Fx6 BottleneckYield(ProductsLayout l, int alt, Func<int, Fx6> yieldOf)
    {
        Fx6 min = Fx6.One;
        foreach (int p in l.PrcOf(alt))
        {
            Fx6 y = yieldOf(p);
            if (y < min)
                min = y;
        }
        return min;
    }

    private static bool SamePrcSet(ProductsLayout l, ReadOnlySpan<int> a, ReadOnlySpan<int> b) =>
        PrcSet(l, a).AsSpan().SequenceEqual(PrcSet(l, b));
}
