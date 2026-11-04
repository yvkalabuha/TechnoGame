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
    private readonly Fx6[] _invest, _notional, _goal, _unmet;
    private readonly bool[] _participates;
    private readonly int _clusters;

    internal ToolingPlanResult(Fx6[] invest, Fx6[] notional, Fx6[] goal, Fx6[] unmet, bool[] participates, int clusters, Fx6 rest, Fx6 bCap, Fx6 total)
    {
        _unmet = unmet;
        _invest = invest;
        _notional = notional;
        _goal = goal;
        _participates = participates;
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

    /// <summary><c>Σ need∞</c> of the reference arm VINF (ledger column <c>invest_notional</c>); 0 in every other arm. Not part of <see cref="Total"/>.</summary>
    public Fx6 NotionalTotal { get; internal init; }

    /// <summary>Notional money of the cell (VINF only), not taken from the pool.</summary>
    public Fx6 Notional(int site, int cluster, Dimension dim) => _notional[(site * _clusters + cluster) * Dims3.Count + (int)dim];

    /// <summary>What <c>tooling_M</c> receives (spec §3.2): the pool money <see cref="Invest"/> plus the notional money of VINF.</summary>
    public Fx6 Applied(int site, int cluster, Dimension dim) => Invest(site, cluster, dim) + Notional(site, cluster, dim);

    /// <summary>Pass 2 took the cell into account under the arm's participation rule (ledger of M1(г)); false for cells of other actors.</summary>
    public bool Participates(int site, int cluster, Dimension dim) => _participates[(site * _clusters + cluster) * Dims3.Count + (int)dim];

    /// <summary>The pass-2 target of a participating cell, already capped by <c>M_cap</c> (VINF: <c>M_cap</c>); 0 otherwise.</summary>
    public Fx6 Goal(int site, int cluster, Dimension dim) => _goal[(site * _clusters + cluster) * Dims3.Count + (int)dim];

    /// <summary>
    /// <c>tool_unmet</c> (contract r8 §3.1): what the cell was short only because the pool ran out, <c>Σ (min(need, room − invest_before) − x)</c> over
    /// both passes; the <c>room</c> ceiling is not counted, nor is the notional pass 2 of VINF.
    /// </summary>
    public Fx6 Unmet(int site, int cluster, Dimension dim) => _unmet[(site * _clusters + cluster) * Dims3.Count + (int)dim];

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
    public static ToolingPlanResult Compute(IndustrySystem industry, IIndustryInputs inputs, Fx6 mKeep, int actor, long month, Fx6 rest, Fx6 bCap,
        ToolingTargetSpec? target = null)
    {
        target ??= new ToolingTargetSpec();
        target.Validate();
        if (rest < Fx6.Zero || bCap < Fx6.Zero)
            throw new ArgumentOutOfRangeException(nameof(rest), "rest_a and B_cap must not be negative.");
        IndustryLayout l = industry.Layout;
        IndustryParams p = industry.Parameters;
        int clusters = l.Clusters.Length, n = l.ProcessCount;
        int mech = l.Clusters.IndexOf(Cluster.MECH);
        var invest = new Fx6[l.SiteCount * clusters * Dims3.Count];
        var notional = new Fx6[invest.Length];
        var goals = new Fx6[invest.Length];
        var unmet = new Fx6[invest.Length];
        var takes = new bool[invest.Length];
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
        // The step «Issue» (A39): the cell takes min(need, room − invest_before, pool); what the pool alone withheld goes to tool_unmet.
        void Give(int i, Fx6 need)
        {
            Fx6 want = Capped(i, need);
            if (want <= Fx6.Zero)
                return;
            Fx6 x = poolLeft < want ? poolLeft : want;
            invest[i] += x;
            poolLeft -= x;
            total += x;
            unmet[i] += want - x;
        }

        // min(need, room − invest_before): the need the pool is asked for; ≤ 0 when there is nothing to give.
        Fx6 Capped(int i, Fx6 need)
        {
            if (need <= Fx6.Zero)
                return Fx6.Zero;
            Fx6 free = room[i] - invest[i];
            return free < need ? free : need;
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

        Fx6 notionalTotal = Fx6.Zero;
        if (target.Variant == ToolingTarget.VInf)
        {
            // Reference arm: pass 2 is replaced; M reaches M_cap wherever a known process with v_exp > 0 and r_d > 0 uses the cell. The money is
            // notional: the pool, taken_rest, taken_cap and T_a stay as with a zero pass 2.
            foreach (int s in sites)
            {
                for (int c = 0; c < clusters; c++)
                {
                    for (int d = 0; d < Dims3.Count; d++)
                    {
                        int i = (s * clusters + c) * Dims3.Count + d;
                        if (!(mDec[i] < mCap[s]) || !UsedCell(l, inputs, s, c, d, month))
                            continue;
                        takes[i] = true;
                        goals[i] = mCap[s];
                        Fx6 need = (mCap[s] - mDec[i]) * cost[i] - invest[i];
                        if (need <= Fx6.Zero)
                            continue;
                        notional[i] = need;
                        notionalTotal += need;
                    }
                }
            }
            return new ToolingPlanResult(invest, notional, goals, unmet, takes, clusters, rest, bCap, total) { NotionalTotal = notionalTotal };
        }

        // Pass 2 (TOOLING_TARGET, spec §11 T6e v0.19): the bottleneck cells of the processes the actor knows and plans to run, in the canonical order.
        var cells = new List<Pass2Cell>();
        foreach (int s in sites)
        {
            for (int c = 0; c < clusters; c++)
            {
                for (int d = 0; d < Dims3.Count; d++)
                {
                    int i = (s * clusters + c) * Dims3.Count + d;
                    Fx6 h = industry.CultureH(s, c, (Dimension)d);
                    Fx6 m = industry.ToolingM(s, c, (Dimension)d);
                    if (!CellTarget(target, industry, l, inputs, yields[s], s, c, d, month, h, m, mDec[i], out Fx6 goal, out Fx6 severity))
                        continue;
                    if (goal > mCap[s])
                        goal = mCap[s];
                    takes[i] = true;
                    goals[i] = goal;
                    cells.Add(new Pass2Cell(i, c, goal, severity));
                }
            }
        }

        switch (target.PoolOrder)
        {
            case PoolOrder.Can:
                foreach (Pass2Cell cell in cells)
                    Give(cell.Index, (cell.Goal - mDec[cell.Index]) * cost[cell.Index] - invest[cell.Index]);
                break;
            case PoolOrder.Rev:
                // Clusters in reverse layout order; sites and dimensions as in the canonical order (the sort is stable).
                foreach (Pass2Cell cell in cells.OrderBy(x => x.Index / Dims3.Count / clusters).ThenByDescending(x => x.Cluster))
                    Give(cell.Index, (cell.Goal - mDec[cell.Index]) * cost[cell.Index] - invest[cell.Index]);
                break;
            case PoolOrder.Sev:
                // Severity descending; a tie goes to the canonical order (the sort is stable over the canonical list).
                foreach (Pass2Cell cell in cells.OrderByDescending(x => x.Severity))
                    Give(cell.Index, (cell.Goal - mDec[cell.Index]) * cost[cell.Index] - invest[cell.Index]);
                break;
            case PoolOrder.Prop:
                // x_i = ⌊need'_i·P/N⌋ when the pool does not cover N = Σ need'_i; else every need in full. The remainder is not handed out.
                var wants = new Fx6[cells.Count];
                Int128 sum = 0;
                for (int k = 0; k < cells.Count; k++)
                {
                    int i = cells[k].Index;
                    wants[k] = Capped(i, (cells[k].Goal - mDec[i]) * cost[i] - invest[i]);
                    sum += wants[k].Raw > 0 ? wants[k].Raw : 0;
                }
                for (int k = 0; k < cells.Count; k++)
                {
                    int i = cells[k].Index;
                    if (wants[k] <= Fx6.Zero)
                        continue;
                    Fx6 x = sum <= poolLeft.Raw ? wants[k] : Fx6.FromRaw((long)((Int128)wants[k].Raw * poolLeft.Raw / sum));
                    invest[i] += x;
                    total += x;
                    unmet[i] += wants[k] - x;
                }
                // The pool is not reduced cell by cell: P stays the month's pool for every cell, and nothing follows pass 2.
                break;
        }
        return new ToolingPlanResult(invest, notional, goals, unmet, takes, clusters, rest, bCap, total);
    }

    private readonly record struct Pass2Cell(int Index, int Cluster, Fx6 Goal, Fx6 Severity);

    // VINF: a known process of the cluster with v_exp > 0 and r_d > 0 (the bottleneck and the source are not checked).
    private static bool UsedCell(IndustryLayout l, IIndustryInputs inputs, int site, int cluster, int dim, long month)
    {
        for (int r = 0; r < l.ProcessCount; r++)
        {
            if (l.ClusterIndex[r] == cluster && l.Req[r][dim] > Fx6.Zero && inputs.KnowsProcess(site, r, month)
                && inputs.PlannedVolume(site, r, month) > Fx6.Zero)
                return true;
        }
        return false;
    }

    // The cell takes part and has a target when a process of the cluster that the actor knows and plans to run (v_exp > 0) has this dimension as its
    // bottleneck (A5, A6: first dimension of minimal g below 1). V0 and V1 also need the source M (M < H strictly); V2…V4 take M or H as the source
    // and add their own condition on M. With several processes the target is the maximum over them (V2: the maximum H').
    private static bool CellTarget(ToolingTargetSpec t, IndustrySystem industry, IndustryLayout l, IIndustryInputs inputs, ProcessYield[] yields,
        int site, int cluster, int dim, long month, Fx6 h, Fx6 m, Fx6 mDec, out Fx6 goal, out Fx6 severity)
    {
        goal = Fx6.Zero;
        severity = Fx6.Zero;
        bool any = false;
        for (int r = 0; r < l.ProcessCount; r++)
        {
            if (l.ClusterIndex[r] != cluster || !inputs.KnowsProcess(site, r, month))
                continue;
            Fx6 vExp = inputs.PlannedVolume(site, r, month);
            if (vExp <= Fx6.Zero)
                continue;
            ProcessYield y = yields[r];
            // V5 (v0.21): any dimension the process uses where the tooling hinders; the bottleneck is not checked.
            if (t.Variant == ToolingTarget.V5)
            {
                if (l.Req[r][dim] > Fx6.Zero && m <= h && m < l.Req[r][dim])
                {
                    if (!any || h + t.MLead > goal)
                        goal = h + t.MLead;
                    // sev = 1 − g(min(H, M) − r_d) with M ≤ H; the largest over the processes that put the cell into pass 2 (POOL_ORDER SEV).
                    Fx6 sev = Fx6.One - IndustryMath.G(m - l.Req[r][dim]);
                    if (sev > severity)
                        severity = sev;
                    any = true;
                }
                continue;
            }
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
            if (bottleneck != dim)
                continue;
            Fx6 candidate;
            bool fromM = y.Source(dim) == CapabilitySource.M;
            switch (t.Variant)
            {
                case ToolingTarget.V0 when fromM:
                    candidate = h;
                    break;
                case ToolingTarget.V1 when fromM:
                    candidate = h + t.RhoMax;
                    break;
                case ToolingTarget.V2:
                    // dH_forecast is read only under this arm (F18).
                    Fx6 forecast = h + industry.DHForecast(site, r, (Dimension)dim, industry.EUseful(site, r, month), vExp, month);
                    if (!(mDec < forecast))
                        continue;
                    candidate = forecast;
                    break;
                case ToolingTarget.V3 when mDec < h + t.HLo:
                    candidate = h + t.HHi;
                    break;
                case ToolingTarget.V3b when mDec < h + t.HLo:
                    candidate = h + t.HHiControl;
                    break;
                case ToolingTarget.V4 when m <= h:
                    candidate = h + t.MLead;
                    break;
                default:
                    continue;
            }
            if (!any || candidate > goal)
                goal = candidate;
            any = true;
        }
        return any;
    }
}
