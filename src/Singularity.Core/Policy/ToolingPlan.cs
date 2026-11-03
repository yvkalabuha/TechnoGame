using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Kernel;

namespace Singularity.Core.Policy;

/// <summary>
/// The tooling plan of one actor in one month (spec §3.5 A39): the money invested into every <c>tooling_M[site, c, d]</c> and how the
/// pool <c>rest_a + B_cap</c> was used. All values are raw <see cref="Fx6"/>; cells of other actors' sites are 0.
/// </summary>
public sealed class ToolingPlanResult
{
    private readonly Fx6[] _invest;
    private readonly int _clusters;

    internal ToolingPlanResult(Fx6[] invest, int clusters, Fx6 rest, Fx6 bCap, Fx6 total)
    {
        _invest = invest;
        _clusters = clusters;
        Rest = rest;
        CapBudget = bCap;
        Total = total;
        TakenRest = total < rest ? total : rest;
        TakenCap = total - TakenRest;
    }

    /// <summary><c>rest_a</c> offered to the plan (first source).</summary>
    public Fx6 Rest { get; }

    /// <summary><c>B_cap</c> offered to the plan (second source).</summary>
    public Fx6 CapBudget { get; }

    /// <summary>Σ over all cells.</summary>
    public Fx6 Total { get; }

    /// <summary><c>taken_rest = min(rest_a, Σ invest)</c>.</summary>
    public Fx6 TakenRest { get; }

    /// <summary><c>taken_cap = Σ invest − taken_rest</c>.</summary>
    public Fx6 TakenCap { get; }

    /// <summary><c>rest_a − taken_rest</c>: the unspent residue of the month (#118).</summary>
    public Fx6 RestReturned => Rest - TakenRest;

    /// <summary><c>T_a = B_cap − taken_cap</c>: the money of the training series, or an unspent residue.</summary>
    public Fx6 TrainingBudget => CapBudget - TakenCap;

    public Fx6 Invest(int site, int cluster, Dimension dim) => _invest[(site * _clusters + cluster) * Dims3.Count + (int)dim];
}

/// <summary>
/// <c>ToolingPlan(a, t, rest_a)</c> (spec §3.5 A39, A30; DEBT-0015): a pure function of the snapshot and the month. One pool for all the
/// sites of the actor, <c>rest_a</c> first and then <c>B_cap</c>; pass 1 keeps the MECH process <c>p*</c> admitted (A30), pass 2 raises
/// <c>M</c> at the bottleneck cells; a waterfall in the canonical order (site row, cluster in layout order, dimension), each cell capped
/// by <c>(M_cap − M_dec)·cost_M</c>. Raw <see cref="Fx6"/>, products floored, no division, so nothing is lost to rounding. No state, no RNG;
/// every call of the month recomputes the same plan (no cache to keep in step with the snapshot).
/// </summary>
public static class ToolingPlan
{
    public static ToolingPlanResult Compute(IndustrySystem industry, IIndustryInputs inputs, Fx6 mKeep, int actor, long month, Fx6 rest, Fx6 bCap)
    {
        if (rest < Fx6.Zero || bCap < Fx6.Zero)
            throw new ArgumentOutOfRangeException(nameof(rest), "rest_a and B_cap must not be negative.");
        IndustryLayout l = industry.Layout;
        IndustryParams p = industry.Parameters;
        int clusters = l.Clusters.Length, n = l.ProcessCount;
        int mech = l.Clusters.IndexOf(Cluster.MECH);
        var invest = new Fx6[l.SiteCount * clusters * Dims3.Count];
        var room = new Fx6[invest.Length];
        var mDec = new Fx6[invest.Length];
        var cost = new Fx6[invest.Length];
        var mCap = new Fx6[l.SiteCount];
        var sites = new List<int>();
        var yields = new ProcessYield[l.SiteCount][];
        var pStar = new int[l.SiteCount];
        for (int s = 0; s < l.SiteCount; s++)
        {
            if (l.SiteOwner[s] != actor)
                continue;
            sites.Add(s);
            yields[s] = new ProcessYield[n];
            int tier = 0;
            pStar[s] = -1;
            for (int r = 0; r < n; r++)
                yields[s][r] = industry.Yield(s, r, month);
            // p*: ties on the tier go to the lower process number, so the scan runs in number order (Order), not in row order.
            foreach (int r in l.Order)
            {
                bool admitted = inputs.KnowsProcess(s, r, month) && yields[s][r].Yield > Fx6.Zero;
                if (admitted && l.Clusters[l.ClusterIndex[r]] == Cluster.MECH && l.Tier[r] > tier)
                {
                    tier = l.Tier[r];
                    pStar[s] = r;
                }
            }
            mCap[s] = Fx6.FromInt(1 + (tier > 3 ? tier : 3));
            for (int c = 0; c < clusters; c++)
            {
                for (int d = 0; d < Dims3.Count; d++)
                {
                    int i = (s * clusters + c) * Dims3.Count + d;
                    Fx6 m = industry.ToolingM(s, c, (Dimension)d);
                    cost[i] = p.CostM(IndustryMath.CostTier(m));
                    mDec[i] = m - p.DeltaM * m;
                    room[i] = mDec[i] < mCap[s] ? (mCap[s] - mDec[i]) * cost[i] : Fx6.Zero;
                }
            }
        }

        Fx6 poolLeft = rest + bCap, total = Fx6.Zero;
        void Give(int i, Fx6 need)
        {
            if (need <= Fx6.Zero)
                return;
            Fx6 x = need;
            Fx6 free = room[i] - invest[i];
            if (free < x)
                x = free;
            if (poolLeft < x)
                x = poolLeft;
            if (x <= Fx6.Zero)
                return;
            invest[i] += x;
            poolLeft -= x;
            total += x;
        }

        // Pass 1 (A30): keep the dimensions p* needs above the admission threshold.
        if (mech >= 0)
        {
            foreach (int s in sites)
            {
                if (pStar[s] < 0)
                    continue;
                Dims3 req = l.Req[pStar[s]];
                for (int d = 0; d < Dims3.Count; d++)
                {
                    if (req[d] <= Fx6.Zero)
                        continue;
                    int i = (s * clusters + mech) * Dims3.Count + d;
                    Give(i, (req[d] - mKeep - mDec[i]) * cost[i]);
                }
            }
        }

        // Pass 2: the bottleneck cells of M of the processes the actor knows and plans to run.
        foreach (int s in sites)
        {
            for (int c = 0; c < clusters; c++)
            {
                for (int d = 0; d < Dims3.Count; d++)
                {
                    if (!IsMBottleneck(l, inputs, yields[s], s, c, d, month))
                        continue;
                    int i = (s * clusters + c) * Dims3.Count + d;
                    Fx6 h = industry.CultureH(s, c, (Dimension)d);
                    Fx6 target = h < mCap[s] ? h : mCap[s];
                    Give(i, (target - mDec[i]) * cost[i] - invest[i]);
                }
            }
        }
        return new ToolingPlanResult(invest, clusters, rest, bCap, total);
    }

    // A5, A6: the bottleneck dimension of a process is its first dimension of minimal g below 1; its source is M when M < H strictly.
    private static bool IsMBottleneck(IndustryLayout l, IIndustryInputs inputs, ProcessYield[] yields, int site, int cluster, int dim, long month)
    {
        for (int r = 0; r < l.ProcessCount; r++)
        {
            if (l.ClusterIndex[r] != cluster || !inputs.KnowsProcess(site, r, month) || inputs.PlannedVolume(site, r, month) <= Fx6.Zero)
                continue;
            ProcessYield y = yields[r];
            Fx6 minG = Fx6.One;
            int bottleneck = -1;
            for (int d = 0; d < Dims3.Count; d++)
            {
                if (y.IsActive(d) && y.G[d] < minG)
                {
                    minG = y.G[d];
                    bottleneck = d;
                }
            }
            if (bottleneck == dim && y.Source(dim) == CapabilitySource.M)
                return true;
        }
        return false;
    }
}
