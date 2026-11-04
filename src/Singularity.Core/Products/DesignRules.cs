using Singularity.Core.Industry;
using Singularity.Kernel;

namespace Singularity.Core.Products;

/// <summary>
/// Choice of alternatives per slot (spec §1.4, contract §4.4a; COMPONENT_SELECTION §6; product_class.md) and the derived
/// design numbers. Deterministic (#28): pure functions of what the actor knows and of snapshot yields. A design is an array
/// of one alternative index (layout-global) per slot. There is no generation cap on components (#81): the project
/// generation is <c>gen = min(g_p, eng_gen(engine) + 1)</c> (#70). The presets <c>superior</c> and <c>producible</c> here
/// are AI-policy helpers (they choose a composition for a <c>compose_design</c> command; the layer itself only validates
/// the command): ties are resolved by rank, which is unique per slot. <c>adaptive</c> needs <c>FV</c> and lives with the AI.
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

    /// <summary>The airframe rank is at or above <c>platform_min_rank[g*]</c> (contract §4.4a validator).</summary>
    public static bool MeetsPlatform(ProductsLayout l, int airframeAlt, int targetGen) =>
        l.AltRank[airframeAlt] >= l.PlatformMinRank[targetGen - 1];

    /// <summary>
    /// <c>g_p</c> (contract §4.4a): the largest generation g whose APP node the actor knows and whose threshold the airframe
    /// meets, at least 1. Fixed at design creation.
    /// </summary>
    public static int PlatformGen(ProductsLayout l, ReadOnlySpan<int> alts, Func<int, bool> knows)
    {
        int gp = 1;
        for (int g = 1; g <= l.Generations; g++)
        {
            if (knows(l.GenApp[g - 1]) && MeetsPlatform(l, alts[l.AirframeSlot], g))
                gp = g;
        }
        return gp;
    }

    /// <summary><c>gen(d) = min(g_p, eng_gen(engine) + 1)</c> (contract §4.4a, #70, G4-9).</summary>
    public static int Gen(ProductsLayout l, int platformGen, ReadOnlySpan<int> alts) =>
        Math.Min(platformGen, l.AltEngGen[alts[l.BaseSlot]] + 1);

    /// <summary>
    /// <c>max_eng_gen</c> (spec v0.14 §1.4), the first cross-slot condition: the alternative <paramref name="alt"/> is allowed with the engine
    /// alternative <paramref name="engineAlt"/> of the same composition when it has no limit or <c>eng_gen(engine) ≤ max_eng_gen</c>.
    /// </summary>
    public static bool EngineAllows(ProductsLayout l, int alt, int engineAlt) =>
        l.AltMaxEngGen[alt] == 0 || l.AltEngGen[engineAlt] <= l.AltMaxEngGen[alt];

    /// <summary><c>avail(alt | composition)</c>: <see cref="Available"/> and <see cref="EngineAllows"/> for the engine of the composition.</summary>
    public static bool AvailableWith(ProductsLayout l, int alt, int engineAlt, Func<int, bool> knows) =>
        EngineAllows(l, alt, engineAlt) && Available(l, alt, knows);

    /// <summary>Every alternative of the composition respects its <c>max_eng_gen</c> (validation of <c>compose_design</c> and <c>derive</c>).</summary>
    public static bool EngineLimitsHold(ProductsLayout l, ReadOnlySpan<int> alts)
    {
        foreach (int a in alts)
        {
            if (!EngineAllows(l, a, alts[l.BaseSlot]))
                return false;
        }
        return true;
    }

    /// <summary>The alloy slot alternative of the composition needs no alloy (<c>alloy_free</c>): no line capacity, no alloy limit.</summary>
    public static bool AlloyFree(ProductsLayout l, ReadOnlySpan<int> alts) => l.AltAlloyFree[alts[l.AlloySlot]];

    /// <summary><c>m(d) = Π upkeep_mult</c> of the chosen alternatives in slot order (1 when none sets it).</summary>
    public static Fx6 UpkeepMult(ProductsLayout l, ReadOnlySpan<int> alts)
    {
        Fx6 m = Fx6.One;
        for (int s = 0; s < l.SlotCount; s++)
            m *= l.AltUpkeepMult[alts[s]];
        return m;
    }

    /// <summary>
    /// «superior»: in every slot the highest-rank available alternative (alloy and yield ignored: the quality doctrine);
    /// false if a slot has none or the best airframe is below the platform threshold of the target generation
    /// <paramref name="targetGen"/> (<c>g*</c>). The engines are tried from the highest rank down and the first one with an alternative
    /// in every other slot (<c>max_eng_gen</c> counted) is taken (spec v0.14 §1.4): knowing a newer engine never leaves the actor without a project.
    /// </summary>
    public static bool Superior(ProductsLayout l, int targetGen, Func<int, bool> knows, Span<int> alts)
    {
        for (int e = l.AltStart[l.BaseSlot + 1] - 1; e >= l.AltStart[l.BaseSlot]; e--)
        {
            if (Available(l, e, knows) && TrySuperior(l, e, targetGen, knows, alts))
                return true;
        }
        for (int s = 0; s < l.SlotCount; s++)
            alts[s] = -1;
        return false;
    }

    private static bool TrySuperior(ProductsLayout l, int engine, int targetGen, Func<int, bool> knows, Span<int> alts)
    {
        for (int s = 0; s < l.SlotCount; s++)
        {
            alts[s] = -1;
            if (s == l.BaseSlot)
            {
                alts[s] = engine;
                continue;
            }
            for (int a = l.AltStart[s + 1] - 1; a >= l.AltStart[s] && alts[s] < 0; a--)
            {
                if (AvailableWith(l, a, engine, knows))
                    alts[s] = a;
            }
            if (alts[s] < 0)
                return false;
        }
        return MeetsPlatform(l, alts[l.AirframeSlot], targetGen);
    }

    /// <summary>
    /// «producible» (COMPONENT_SELECTION §6, spec §1.4, A32): in every slot the highest-rank available alternative whose
    /// bottleneck process yields ≥ <paramref name="yProd"/> on the own site (an empty <c>prc_set</c> passes) and, in the alloy slot,
    /// that is <c>alloy_free</c> or satisfies <paramref name="alloyOk"/> (null: always); none — the lightest available one (in the engine slot, #140: the newest one; the airframe not below the
    /// platform threshold of <paramref name="targetGen"/>). A substitute takes part in the main rule; the lightest-one fallback and the
    /// lightening below skip it when the slot has a non-substitute alternative (spec v0.14 §1.4). The engine is chosen by the same rule (none passes: the newest) and,
    /// if some other slot then has no alternative (<c>max_eng_gen</c>), the next lower engine is tried (spec v0.15 §1.4, #140). If the <c>prc_set</c> then equals the
    /// superior one, the non-base slot with the highest tier (ties by lighten order, F-PC2) that has a lower admissible alternative takes the next
    /// lower one; if none can, the preset equals «superior» (F-PC3) and the result is false. With <paramref name="admitted"/> only alternatives
    /// it accepts are candidates and a slot without one makes the result false; there is no «superior» twin to differ from
    /// (the canonical project of L_P, spec §6.1).
    /// </summary>
    public static bool Producible(ProductsLayout l, int targetGen, Func<int, bool> knows, Func<int, Fx6> yieldOf, Fx6 yProd,
        Func<int, bool>? alloyOk, Span<int> alts, Func<int, bool>? admitted = null)
    {
        Span<int> sup = stackalloc int[l.SlotCount];
        if (!Superior(l, targetGen, knows, sup) && admitted is null)
            return false;
        bool Candidate(int s, int a, int engine) => AvailableWith(l, a, engine, knows) && (admitted?.Invoke(a) ?? true)
            && (s != l.AirframeSlot || MeetsPlatform(l, a, targetGen));
        bool Passes(int s, int a) => BottleneckYield(l, a, yieldOf) >= yProd
            && (s != l.AlloySlot || l.AltAlloyFree[a] || (alloyOk?.Invoke(a) ?? true));

        // The engine by the slot rule (#140: when none passes y_prod the newest one, not the lightest), then the lower ones only while
        // the other slots cannot be filled (availability only: yield, alloy_ok and FV take no part in the step down).
        int preferred = -1, newestEngine = -1;
        for (int a = l.AltStart[l.BaseSlot + 1] - 1; a >= l.AltStart[l.BaseSlot]; a--)
        {
            if (!Candidate(l.BaseSlot, a, a))
                continue;
            if (newestEngine < 0)
                newestEngine = a;
            if (preferred < 0 && Passes(l.BaseSlot, a))
                preferred = a;
        }
        if (preferred < 0)
            preferred = newestEngine;
        bool built = false;
        for (int e = preferred; e >= l.AltStart[l.BaseSlot] && !built; e--)
        {
            if (Candidate(l.BaseSlot, e, e))
                built = TryProducible(l, e, Candidate, Passes, alts);
        }
        if (!built)
        {
            for (int s = 0; s < l.SlotCount; s++)
                alts[s] = -1;
            return false;
        }
        if (admitted is not null)
            return true;
        if (!SamePrcSet(l, alts, sup))
            return true;
        int engineAlt = alts[l.BaseSlot];
        int best = -1, bestTier = -1, bestLower = -1;
        foreach (int s in l.LightenOrder)
        {
            int lower = -1;
            bool hasPlain = HasPlainCandidate(l, s, engineAlt, Candidate);
            for (int a = alts[s] - 1; a >= l.AltStart[s] && lower < 0; a--)
            {
                if (Candidate(s, a, engineAlt) && !(hasPlain && l.AltSubstitute[a]))
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

    // One composition for the engine: per slot the highest alternative that passes, else the lightest candidate (a substitute only when
    // the slot has nothing else); false if some slot has no candidate.
    private static bool TryProducible(ProductsLayout l, int engine, Func<int, int, int, bool> candidate, Func<int, int, bool> passes, Span<int> alts)
    {
        for (int s = 0; s < l.SlotCount; s++)
        {
            alts[s] = -1;
            if (s == l.BaseSlot)
            {
                alts[s] = engine;
                continue;
            }
            int lowestPlain = -1, lowestAny = -1;
            for (int a = l.AltStart[s + 1] - 1; a >= l.AltStart[s]; a--)
            {
                if (!candidate(s, a, engine))
                    continue;
                lowestAny = a;
                if (!l.AltSubstitute[a])
                    lowestPlain = a;
                if (alts[s] < 0 && passes(s, a))
                    alts[s] = a;
            }
            if (alts[s] < 0)
                alts[s] = lowestPlain >= 0 ? lowestPlain : lowestAny;
            if (alts[s] < 0)
                return false;
        }
        return true;
    }

    private static bool HasPlainCandidate(ProductsLayout l, int slot, int engine, Func<int, int, int, bool> candidate)
    {
        for (int a = l.AltStart[slot]; a < l.AltStart[slot + 1]; a++)
        {
            if (!l.AltSubstitute[a] && candidate(slot, a, engine))
                return true;
        }
        return false;
    }

    /// <summary><c>r_PU</c>: the maximum rank over the PU slots (every slot except the airframe), contract §4.4a.</summary>
    public static int PuRank(ProductsLayout l, ReadOnlySpan<int> alts)
    {
        int r = 1;
        for (int s = 0; s < l.SlotCount; s++)
        {
            if (s != l.AirframeSlot && l.AltRank[alts[s]] > r)
                r = l.AltRank[alts[s]];
        }
        return r;
    }

    /// <summary><c>af_class</c> of the airframe alternative of the design.</summary>
    public static int AfClass(ProductsLayout l, ReadOnlySpan<int> alts) => l.AltAfClass[alts[l.AirframeSlot]];

    /// <summary>The processes of <paramref name="prcSet"/> in the AF block (<paramref name="airframe"/>) or in the PU block.</summary>
    public static int[] Block(ProductsLayout l, ReadOnlySpan<int> prcSet, bool airframe)
    {
        var block = new List<int>();
        foreach (int p in prcSet)
        {
            if (l.ProcInAirframe[l.ProcIndex(p)] == airframe)
                block.Add(p);
        }
        return [.. block];
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
