using Singularity.Kernel;

namespace Singularity.Core.Industry;

/// <summary>
/// Result of the process-development allocation of one actor in one month: <c>E_{s,p}</c> (site-major,
/// <c>site·P + processRow</c>, zero for sites of other actors), <c>rest_a</c> for the tooling step and the
/// number of fill passes.
/// </summary>
public sealed class EffortAllocation(Fx6[] e, Fx6 rest, int passes)
{
    public Fx6[] E { get; } = e;

    public Fx6 Rest { get; } = rest;

    public int Passes { get; } = passes;
}

/// <summary>
/// Distribution of the process-development item <c>D_a</c> (contract v2.4 §4.1b, G4-5, G4-6): a pure,
/// deterministic function of the snapshot, the budget and the player's focus. No RNG, no state.
/// </summary>
public static class EffortAllocator
{
    /// <param name="focus">Process numbers of <c>effort_focus</c> in order, −1 = empty; a repeat is rejected.</param>
    public static EffortAllocation Allocate(ICultureRule rule, ICultureSnapshot s, int actor, long month, Fx6 budget, ReadOnlySpan<int> focus)
    {
        IndustryLayout l = s.Layout;
        IndustryParams p = s.Params;
        int n = l.ProcessCount, sites = l.SiteCount;
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

        // E_useful per site and per actor (sum over the actor's sites).
        var eu = new Fx6[sites * n];
        var euActor = new Fx6[n];
        for (int site = 0; site < sites; site++)
        {
            if (l.SiteOwner[site] != actor)
                continue;
            foreach (int r in l.Order)
            {
                eu[site * n + r] = rule.EUseful(s, site, r, month);
                euActor[r] += eu[site * n + r];
            }
        }

        var e = new Fx6[n];
        Fx6 left = budget;
        // Step 0 (player): focus processes first, in order.
        foreach (int r in focusRows)
        {
            if (r < 0)
                continue;
            Fx6 give = left < euActor[r] ? left : euActor[r];
            e[r] += give;
            left -= give;
        }

        // Steps 1-3: candidates and the fill passes (every share of a pass is taken from D at the pass start).
        var cand = new List<int>();
        foreach (int r in l.Order)
        {
            if (euActor[r] > Fx6.Zero && e[r] < euActor[r])
                cand.Add(r);
        }
        var weight = new Fx6[n];
        foreach (int r in cand)
            weight[r] = Weight(s, actor, r, month);
        int limit = cand.Count, passes = 0;
        var share = new Fx6[n];
        while (left > Fx6.Zero && cand.Count > 0 && passes < limit)
        {
            passes++;
            Fx6 d0 = left, total = Fx6.Zero;
            foreach (int r in cand)
                total += weight[r];
            if (total == Fx6.Zero)
                break;
            foreach (int r in cand)
                share[r] = d0 * weight[r] / total;
            var rest = new List<int>();
            foreach (int r in cand)
            {
                Fx6 room = euActor[r] - e[r];
                Fx6 give = share[r] < room ? share[r] : room;
                e[r] += give;
                left -= give;
                if (e[r] < euActor[r])
                    rest.Add(r);
            }
            bool anyCapped = rest.Count < cand.Count;
            cand = rest;
            if (!anyCapped)
                break;
        }

        // Step 4: split by site, capped by E_useful(s, p); the excess goes to the tooling rest.
        var result = new Fx6[sites * n];
        Fx6 excess = Fx6.Zero;
        foreach (int r in l.Order)
        {
            if (e[r] <= Fx6.Zero)
                continue;
            excess += SplitBySite(s, l, actor, r, e[r], eu, result);
        }
        return new EffortAllocation(result, left + excess, passes);
    }

    // w_p = (((v_exp / (r_ref·labour_p))·gap_H)·mult_P) (R2-10, A37); v_exp summed and gap_H maximised over the actor's sites.
    private static Fx6 Weight(ICultureSnapshot s, int actor, int r, long month)
    {
        IndustryLayout l = s.Layout;
        int c = l.ClusterIndex[r];
        Fx6 vExp = Fx6.Zero, gapH = Fx6.Zero;
        for (int site = 0; site < l.SiteCount; site++)
        {
            if (l.SiteOwner[site] != actor)
                continue;
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
    // Σv = 0: everything to the lowest site. E_{s,p} = min(share_s, E_useful(s,p)). Returns the excess.
    private static Fx6 SplitBySite(ICultureSnapshot s, IndustryLayout l, int actor, int r, Fx6 total, Fx6[] eu, Fx6[] result)
    {
        int n = l.ProcessCount;
        Fx6 sumV = Fx6.Zero;
        int first = -1, top = -1;
        for (int site = 0; site < l.SiteCount; site++)
        {
            if (l.SiteOwner[site] != actor)
                continue;
            Fx6 v = s.V(site, r);
            sumV += v;
            if (first < 0)
                first = site;
            if (top < 0 || v > s.V(top, r))
                top = site;
        }
        if (first < 0)
            return total;
        var share = new Fx6[l.SiteCount];
        if (sumV == Fx6.Zero)
        {
            share[first] = total;
        }
        else
        {
            Fx6 given = Fx6.Zero;
            for (int site = 0; site < l.SiteCount; site++)
            {
                if (l.SiteOwner[site] != actor)
                    continue;
                share[site] = total * s.V(site, r) / sumV;
                given += share[site];
            }
            share[top] += total - given;
        }
        Fx6 excess = Fx6.Zero;
        for (int site = 0; site < l.SiteCount; site++)
        {
            if (l.SiteOwner[site] != actor)
                continue;
            Fx6 cap = eu[site * n + r];
            Fx6 give = share[site] < cap ? share[site] : cap;
            result[site * n + r] = give;
            excess += share[site] - give;
        }
        return excess;
    }
}
