using Singularity.Kernel;

namespace Singularity.Core.Industry;

/// <summary>
/// Result of the process-development allocation of one actor in one month: <c>E_{s,p}</c> in the local layout of the actor
/// (<c>k·P + processRow</c>, <c>k</c> = <see cref="IndustryLayout.LocalSiteIndex"/> of the site; only the sites of the actor are stored),
/// <c>rest_a</c> for the tooling step and the number of fill passes. With a scratch the array may be longer than <c>sites·P</c> and
/// lives until the next use of the scratch.
/// </summary>
/// <param name="Upkeep">Money of <c>D</c> the mastery programmes spent beyond their <c>E</c> (<c>(upkeep_mult − 1)·E</c>): grows nothing; <c>ΣE + Rest + Upkeep = D</c>.</param>
public readonly record struct EffortAllocation(Fx6[] E, Fx6 Rest, int Passes, Fx6 Upkeep = default);

/// <summary>
/// Working buffers of <see cref="EffortAllocator"/>: filled and cleared by each call, never part of the state. One per thread or per
/// owner; the result of a call that used it is valid until the next call with the same scratch.
/// </summary>
public sealed class EffortScratch
{
    [ThreadStatic] private static EffortScratch? _forThread;

    internal Fx6[] Eu = [], EuActor = [], E = [], Weight = [], Share = [], Result = [], SiteShare = [];
    internal int[] Cand = [], Rest = [];

    /// <summary>The scratch of the calling thread (for code that runs on any thread of a parallel loop).</summary>
    public static EffortScratch ForThread => _forThread ??= new EffortScratch();

    internal void Prepare(int n, int actorSites)
    {
        Size(ref Eu, actorSites * n);
        Size(ref Result, actorSites * n);
        Size(ref EuActor, n);
        Size(ref E, n);
        Size(ref Weight, n);
        Size(ref Share, n);
        Size(ref SiteShare, actorSites);
        if (Cand.Length < n)
        {
            Cand = new int[n];
            Rest = new int[n];
        }
    }

    // Grows only; the used prefix is zeroed so a call never sees the values of an earlier one.
    private static void Size(ref Fx6[] a, int len)
    {
        if (a.Length < len)
            a = new Fx6[len];
        else
            a.AsSpan(0, len).Clear();
    }
}

/// <summary>
/// Distribution of the process-development item <c>D_a</c> (contract v2.4 §4.1b, G4-5, G4-6): a pure,
/// deterministic function of the snapshot, the budget and the player's focus. No RNG, no state.
/// </summary>
public static class EffortAllocator
{
    /// <summary>Allocation with fresh buffers: the result is independent of any later call.</summary>
    public static EffortAllocation Allocate(ICultureRule rule, ICultureSnapshot s, int actor, long month, Fx6 budget, ReadOnlySpan<int> focus) =>
        Allocate(rule, s, actor, month, budget, focus, new EffortScratch());

    /// <param name="focus">Process numbers of <c>effort_focus</c> in order, −1 = empty; a repeat is rejected.</param>
    /// <param name="scratch">Working buffers; the result is valid until the next call with the same scratch.</param>
    public static EffortAllocation Allocate(ICultureRule rule, ICultureSnapshot s, int actor, long month, Fx6 budget, ReadOnlySpan<int> focus, EffortScratch scratch)
    {
        IndustryLayout l = s.Layout;
        int n = l.ProcessCount;
        ReadOnlySpan<int> sites = l.SitesOf(actor);
        if (budget < Fx6.Zero)
            throw new ArgumentOutOfRangeException(nameof(budget), budget, "The effort budget cannot be negative.");
        Span<int> focusRows = stackalloc int[focus.Length];
        for (int k = 0; k < focus.Length; k++)
        {
            focusRows[k] = focus[k] < 0 ? -1 : l.RowOf(focus[k]);
            if (focus[k] >= 0 && focusRows[k] < 0)
                throw new ArgumentException($"effort_focus names unknown process {focus[k]}.", nameof(focus));
            for (int j = 0; j < k; j++)
            {
                if (focus[k] >= 0 && focus[k] == focus[j])
                    throw new ArgumentException($"effort_focus repeats process {focus[k]}.", nameof(focus));
            }
        }

        scratch.Prepare(n, sites.Length);
        // E_useful per site (local layout) and per actor (sum over the actor's sites).
        Fx6[] eu = scratch.Eu, euActor = scratch.EuActor;
        for (int k = 0; k < sites.Length; k++)
        {
            rule.EUsefulAll(s, sites[k], month, eu.AsSpan(k * n, n));
            foreach (int r in l.Order)
                euActor[r] += eu[k * n + r];
        }

        Fx6[] e = scratch.E;
        Fx6 left = budget;
        // Step -1 (mastery programme, production-mastery.md §2 p. 5): the programme processes first, the oldest programme first, each up to
        // E_useful; the upkeep beyond E is taken from D and grows nothing.
        Fx6 upkeep = Fx6.Zero;
        if (sites.Length > 0)
            left = ProgrammeFirst(s, sites[0], l, month, euActor, e, left, ref upkeep);
        // Step 0 (player): focus processes first, in order.
        foreach (int r in focusRows)
        {
            if (r < 0)
                continue;
            Fx6 room = euActor[r] - e[r];
            Fx6 give = left < room ? left : room;
            e[r] += give;
            left -= give;
        }

        // Steps 1-3: candidates and the fill passes (every share of a pass is taken from D at the pass start).
        int[] cand = scratch.Cand, rest = scratch.Rest;
        int candCount = 0;
        foreach (int r in l.Order)
        {
            if (euActor[r] > Fx6.Zero && e[r] < euActor[r])
                cand[candCount++] = r;
        }
        Fx6[] weight = scratch.Weight, share = scratch.Share;
        for (int i = 0; i < candCount; i++)
            weight[cand[i]] = Weight(s, actor, cand[i], month);
        int limit = candCount, passes = 0;
        while (left > Fx6.Zero && candCount > 0 && passes < limit)
        {
            passes++;
            Fx6 d0 = left, total = Fx6.Zero;
            for (int i = 0; i < candCount; i++)
                total += weight[cand[i]];
            if (total == Fx6.Zero)
                break;
            for (int i = 0; i < candCount; i++)
                share[cand[i]] = d0 * weight[cand[i]] / total;
            int restCount = 0;
            for (int i = 0; i < candCount; i++)
            {
                int r = cand[i];
                Fx6 room = euActor[r] - e[r];
                Fx6 give = share[r] < room ? share[r] : room;
                e[r] += give;
                left -= give;
                if (e[r] < euActor[r])
                    rest[restCount++] = r;
            }
            bool anyCapped = restCount < candCount;
            (cand, rest) = (rest, cand);
            candCount = restCount;
            if (!anyCapped)
                break;
        }

        // Step 4: split by site, capped by E_useful(s, p); the excess goes to the tooling rest.
        Fx6[] result = scratch.Result;
        Fx6 excess = Fx6.Zero;
        foreach (int r in l.Order)
        {
            if (e[r] <= Fx6.Zero)
                continue;
            excess += SplitBySite(s, l, sites, r, e[r], eu, result, scratch.SiteShare);
        }
        return new EffortAllocation(result, left + excess, passes, upkeep);
    }

    // Programmes by (start, canonical position): repeated selection of the next one, no allocation (at most S_max programmes are active).
    private static Fx6 ProgrammeFirst(ICultureSnapshot s, int home, IndustryLayout l, long month, Fx6[] euActor, Fx6[] e, Fx6 left, ref Fx6 upkeep)
    {
        long lastStart = 0;
        int lastPos = -1;
        while (left > Fx6.Zero)
        {
            int best = -1, bestPos = -1, pos = 0;
            long bestStart = long.MaxValue;
            foreach (int r in l.Order)
            {
                long start = s.ProgrammeStart(home, r, month);
                bool after = start > lastStart || (start == lastStart && pos > lastPos);
                if (start > 0 && after && start < bestStart)
                {
                    best = r;
                    bestStart = start;
                    bestPos = pos;
                }
                pos++;
            }
            if (best < 0)
                break;
            lastStart = bestStart;
            lastPos = bestPos;
            Fx6 give = left < euActor[best] ? left : euActor[best];
            e[best] += give;
            left -= give;
            Fx6 waste = (s.Params.UpkeepMult - Fx6.One) * give;
            if (waste > left)
                waste = left;
            left -= waste;
            upkeep += waste;
        }
        return left;
    }

    // w_p = (((v_exp / (r_ref·labour_p))·gap_H)·mult_P) (R2-10, A37); v_exp summed and gap_H maximised over the actor's sites.
    private static Fx6 Weight(ICultureSnapshot s, int actor, int r, long month)
    {
        IndustryLayout l = s.Layout;
        int c = l.ClusterIndex[r];
        Fx6 vExp = Fx6.Zero, gapH = Fx6.Zero;
        foreach (int site in l.SitesOf(actor))
        {
            vExp += s.PlannedVolume(site, r, month);
            for (int d = 0; d < Dims3.Count; d++)
            {
                if (l.Req[r][d] <= Fx6.Zero)
                    continue;
                Fx6 gap = l.Req[r][d] - s.H(site, c, d);
                if (gap > gapH)
                    gapH = gap;
            }
        }
        Fx6 den = s.Params.RRef * l.Labour[r];
        // mult_P (spec A37): the doctrine factor comes last.
        return den == Fx6.Zero ? Fx6.Zero : vExp / den * gapH * s.EffortMultiplier(actor, r, month);
    }

    // share_s = (E·v_s)/Σv (floor); the floor remainder goes to the site with the largest v (tie: lower site id);
    // Σv = 0: everything to the lowest site. E_{s,p} = min(share_s, E_useful(s,p)). Returns the excess. Local site layout.
    private static Fx6 SplitBySite(ICultureSnapshot s, IndustryLayout l, ReadOnlySpan<int> sites, int r, Fx6 total, Fx6[] eu, Fx6[] result, Fx6[] share)
    {
        int n = l.ProcessCount;
        Fx6 sumV = Fx6.Zero;
        int top = -1;
        for (int k = 0; k < sites.Length; k++)
        {
            Fx6 v = s.V(sites[k], r);
            sumV += v;
            if (top < 0 || v > s.V(sites[top], r))
                top = k;
        }
        if (sites.Length == 0)
            return total;
        share.AsSpan(0, sites.Length).Clear();
        if (sumV == Fx6.Zero)
        {
            share[0] = total;
        }
        else
        {
            Fx6 given = Fx6.Zero;
            for (int k = 0; k < sites.Length; k++)
            {
                share[k] = total * s.V(sites[k], r) / sumV;
                given += share[k];
            }
            share[top] += total - given;
        }
        Fx6 excess = Fx6.Zero;
        for (int k = 0; k < sites.Length; k++)
        {
            Fx6 cap = eu[k * n + r];
            Fx6 give = share[k] < cap ? share[k] : cap;
            result[k * n + r] = give;
            excess += share[k] - give;
        }
        return excess;
    }
}
