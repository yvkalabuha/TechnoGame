using System.Runtime.InteropServices;
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
    // Local layout: only the sites of the actor, cell (k, c, d) at (k·C + c)·3 + d with k = IndustryLayout.LocalSiteIndex (F3, TASK-0103).
    // The arrays may be longer than the cells in use; a result that was built into a reused object lives until its next use.
    private Fx6[] _invest = [], _notional = [], _goal = [], _unmet = [];
    private bool[] _participates = [];
    private IndustryLayout _layout = null!;
    private int _actor, _clusters;

    public ToolingPlanResult()
    {
    }

    internal Fx6[] InvestCells => _invest;

    internal Fx6[] NotionalCells => _notional;

    internal Fx6[] GoalCells => _goal;

    internal Fx6[] UnmetCells => _unmet;

    internal bool[] ParticipatesCells => _participates;

    /// <summary>Sizes and zeroes the cell arrays for the actor and binds the layout; values follow with <see cref="Finish"/>.</summary>
    internal void Begin(IndustryLayout layout, int actor, int cells)
    {
        _layout = layout;
        _actor = actor;
        _clusters = layout.Clusters.Length;
        Size(ref _invest, cells);
        Size(ref _notional, cells);
        Size(ref _goal, cells);
        Size(ref _unmet, cells);
        if (_participates.Length < cells)
            _participates = new bool[cells];
        else
            Array.Clear(_participates, 0, cells);
    }

    private static void Size(ref Fx6[] a, int len)
    {
        if (a.Length < len)
            a = new Fx6[len];
        else
            a.AsSpan(0, len).Clear();
    }

    internal void Finish(Fx6 rest, Fx6 bCap, Fx6 total, Fx6 notionalTotal)
    {
        Rest = rest;
        CapBudget = bCap;
        Total = total;
        TakenRest = total < rest ? total : rest;
        TakenCap = total - TakenRest;
        NotionalTotal = notionalTotal;
    }

    // Local cell index of a global site, or −1 for a site of another actor (its cells are 0).
    private int Cell(int site, int cluster, Dimension dim)
    {
        if ((uint)site >= (uint)_layout.SiteCount || _layout.SiteOwner[site] != _actor)
            return -1;
        return (_layout.LocalSiteIndex(site) * _clusters + cluster) * Dims3.Count + (int)dim;
    }

    /// <summary><c>rest_a</c> offered to the plan (first source).</summary>
    public Fx6 Rest { get; private set; }

    /// <summary><c>B_cap</c> offered to the plan (second source).</summary>
    public Fx6 CapBudget { get; private set; }

    /// <summary>Σ over all cells.</summary>
    public Fx6 Total { get; private set; }

    /// <summary><c>taken_rest = min(rest_a, Σ invest)</c>.</summary>
    public Fx6 TakenRest { get; private set; }

    /// <summary><c>taken_cap = Σ invest − taken_rest</c>.</summary>
    public Fx6 TakenCap { get; private set; }

    /// <summary><c>rest_a − taken_rest</c>: the unspent residue of the month (#118).</summary>
    public Fx6 RestReturned => Rest - TakenRest;

    /// <summary><c>T_a = B_cap − taken_cap</c>: the money of the training series, or an unspent residue.</summary>
    public Fx6 TrainingBudget => CapBudget - TakenCap;

    /// <summary><c>Σ need∞</c> of the reference arm VINF (ledger column <c>invest_notional</c>); 0 in every other arm. Not part of <see cref="Total"/>.</summary>
    public Fx6 NotionalTotal { get; private set; }

    /// <summary>Notional money of the cell (VINF only), not taken from the pool.</summary>
    public Fx6 Notional(int site, int cluster, Dimension dim) => Cell(site, cluster, dim) is var i and >= 0 ? _notional[i] : Fx6.Zero;

    /// <summary>What <c>tooling_M</c> receives (spec §3.2): the pool money <see cref="Invest"/> plus the notional money of VINF.</summary>
    public Fx6 Applied(int site, int cluster, Dimension dim) => Invest(site, cluster, dim) + Notional(site, cluster, dim);

    /// <summary>Pass 2 took the cell into account under the arm's participation rule (ledger of M1(г)); false for cells of other actors.</summary>
    public bool Participates(int site, int cluster, Dimension dim) => Cell(site, cluster, dim) is var i and >= 0 && _participates[i];

    /// <summary>The pass-2 target of a participating cell, already capped by <c>M_cap</c> (VINF: <c>M_cap</c>); 0 otherwise.</summary>
    public Fx6 Goal(int site, int cluster, Dimension dim) => Cell(site, cluster, dim) is var i and >= 0 ? _goal[i] : Fx6.Zero;

    /// <summary>
    /// <c>tool_unmet</c> (contract r8 §3.1): what the cell was short only because the pool ran out, <c>Σ (min(need, room − invest_before) − x)</c> over
    /// both passes; the <c>room</c> ceiling is not counted, nor is the notional pass 2 of VINF.
    /// </summary>
    public Fx6 Unmet(int site, int cluster, Dimension dim) => Cell(site, cluster, dim) is var i and >= 0 ? _unmet[i] : Fx6.Zero;

    public Fx6 Invest(int site, int cluster, Dimension dim) => Cell(site, cluster, dim) is var i and >= 0 ? _invest[i] : Fx6.Zero;
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
    // Working buffers of one call, per thread: Compute does not nest and never hands them out.
    private sealed class Temp
    {
        public Fx6[] Room = [], MDec = [], Cost = [], MCap = [], Wants = [];
        public int[] PStar = [];
        public ProcessYield[] Yields = [];
        public readonly List<Pass2Cell> Cells = new();
    }

    [ThreadStatic] private static Temp? _temp;

    /// <param name="reuse">A result object to fill (a memo slot): valid until its next use. Null = a fresh result.</param>
    public static ToolingPlanResult Compute(IndustrySystem industry, IIndustryInputs inputs, Fx6 mKeep, int actor, long month, Fx6 rest, Fx6 bCap,
        ToolingTargetSpec? target = null, ToolingPlanResult? reuse = null)
    {
        target ??= new ToolingTargetSpec();
        target.Validate();
        if (rest < Fx6.Zero || bCap < Fx6.Zero)
            throw new ArgumentOutOfRangeException(nameof(rest), "rest_a and B_cap must not be negative.");
        IndustryLayout l = industry.Layout;
        IndustryParams p = industry.Parameters;
        int clusters = l.Clusters.Length, n = l.ProcessCount;
        int mech = l.Clusters.IndexOf(Cluster.MECH);
        // Local layout: only the sites of the actor, cell (k, c, d) at (k·C + c)·3 + d, k = position of the site among SitesOf(actor).
        ReadOnlySpan<int> sites = l.SitesOf(actor);
        int cellCount = sites.Length * clusters * Dims3.Count;
        ToolingPlanResult result = reuse ?? new ToolingPlanResult();
        result.Begin(l, actor, cellCount);
        Fx6[] invest = result.InvestCells, notional = result.NotionalCells, goals = result.GoalCells, unmet = result.UnmetCells;
        bool[] takes = result.ParticipatesCells;
        Temp t = _temp ??= new Temp();
        Fx6[] room = Fit(ref t.Room, cellCount), mDec = Fit(ref t.MDec, cellCount), cost = Fit(ref t.Cost, cellCount), mCap = Fit(ref t.MCap, sites.Length);
        int[] pStar = t.PStar.Length < sites.Length ? t.PStar = new int[sites.Length] : t.PStar;
        ProcessYield[] yields = t.Yields.Length < sites.Length * n ? t.Yields = new ProcessYield[sites.Length * n] : t.Yields;
        for (int k = 0; k < sites.Length; k++)
        {
            int s = sites[k];
            int tier = 0;
            pStar[k] = -1;
            for (int r = 0; r < n; r++)
                yields[k * n + r] = industry.Yield(s, r, month);
            // p*: ties on the tier go to the lower process number, so the scan runs in number order (Order), not in row order.
            foreach (int r in l.Order)
            {
                bool admitted = inputs.KnowsProcess(s, r, month) && yields[k * n + r].Yield > Fx6.Zero;
                if (admitted && l.Clusters[l.ClusterIndex[r]] == Cluster.MECH && l.Tier[r] > tier)
                {
                    tier = l.Tier[r];
                    pStar[k] = r;
                }
            }
            mCap[k] = Fx6.FromInt(1 + (tier > 3 ? tier : 3));
            for (int c = 0; c < clusters; c++)
            {
                for (int d = 0; d < Dims3.Count; d++)
                {
                    int i = (k * clusters + c) * Dims3.Count + d;
                    Fx6 m = industry.ToolingM(s, c, (Dimension)d);
                    cost[i] = p.CostM(IndustryMath.CostTier(m));
                    mDec[i] = m - p.DeltaM * m;
                    room[i] = mDec[i] < mCap[k] ? (mCap[k] - mDec[i]) * cost[i] : Fx6.Zero;
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
            for (int k = 0; k < sites.Length; k++)
            {
                if (pStar[k] < 0)
                    continue;
                Dims3 req = l.Req[pStar[k]];
                for (int d = 0; d < Dims3.Count; d++)
                {
                    if (req[d] <= Fx6.Zero)
                        continue;
                    int i = (k * clusters + mech) * Dims3.Count + d;
                    Give(i, (req[d] - mKeep - mDec[i]) * cost[i]);
                }
            }
        }

        Fx6 notionalTotal = Fx6.Zero;
        if (target.Variant == ToolingTarget.VInf)
        {
            // Reference arm: pass 2 is replaced; M reaches M_cap wherever a known process with v_exp > 0 and r_d > 0 uses the cell. The money is
            // notional: the pool, taken_rest, taken_cap and T_a stay as with a zero pass 2.
            for (int k = 0; k < sites.Length; k++)
            {
                for (int c = 0; c < clusters; c++)
                {
                    for (int d = 0; d < Dims3.Count; d++)
                    {
                        int i = (k * clusters + c) * Dims3.Count + d;
                        if (!(mDec[i] < mCap[k]) || !UsedCell(l, inputs, sites[k], c, d, month))
                            continue;
                        takes[i] = true;
                        goals[i] = mCap[k];
                        Fx6 need = (mCap[k] - mDec[i]) * cost[i] - invest[i];
                        if (need <= Fx6.Zero)
                            continue;
                        notional[i] = need;
                        notionalTotal += need;
                    }
                }
            }
            result.Finish(rest, bCap, total, notionalTotal);
            return result;
        }

        // Pass 2 (TOOLING_TARGET, spec §11 T6e v0.19): the bottleneck cells of the processes the actor knows and plans to run, in the canonical order.
        List<Pass2Cell> cells = t.Cells;
        cells.Clear();
        for (int k = 0; k < sites.Length; k++)
        {
            int s = sites[k];
            ReadOnlySpan<ProcessYield> siteYields = yields.AsSpan(k * n, n);
            for (int c = 0; c < clusters; c++)
            {
                for (int d = 0; d < Dims3.Count; d++)
                {
                    int i = (k * clusters + c) * Dims3.Count + d;
                    Fx6 h = industry.CultureH(s, c, (Dimension)d);
                    Fx6 m = industry.ToolingM(s, c, (Dimension)d);
                    if (!CellTarget(target, industry, l, inputs, siteYields, s, c, d, month, h, m, mDec[i], out Fx6 goal, out Fx6 severity))
                        continue;
                    if (goal > mCap[k])
                        goal = mCap[k];
                    takes[i] = true;
                    goals[i] = goal;
                    // Site = local index k: ascending like the site id, so the sorts below order the same way.
                    cells.Add(new Pass2Cell(i, k, c, goal, severity, cells.Count));
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
                CollectionsMarshal.AsSpan(cells).Sort(static (a, b) =>
                    a.Site != b.Site ? a.Site.CompareTo(b.Site) : a.Cluster != b.Cluster ? b.Cluster.CompareTo(a.Cluster) : a.Position.CompareTo(b.Position));
                foreach (Pass2Cell cell in cells)
                    Give(cell.Index, (cell.Goal - mDec[cell.Index]) * cost[cell.Index] - invest[cell.Index]);
                break;
            case PoolOrder.Sev:
                // Severity descending; a tie goes to the canonical order (the sort is stable over the canonical list).
                CollectionsMarshal.AsSpan(cells).Sort(static (a, b) => a.Severity != b.Severity ? b.Severity.CompareTo(a.Severity) : a.Position.CompareTo(b.Position));
                foreach (Pass2Cell cell in cells)
                    Give(cell.Index, (cell.Goal - mDec[cell.Index]) * cost[cell.Index] - invest[cell.Index]);
                break;
            case PoolOrder.Prop:
                // x_i = ⌊need'_i·P/N⌋ when the pool does not cover N = Σ need'_i; else every need in full. The remainder is not handed out.
                Fx6[] wants = Fit(ref t.Wants, cells.Count);
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
        result.Finish(rest, bCap, total, notionalTotal);
        return result;
    }

    // Grows only; the used prefix is zeroed.
    private static Fx6[] Fit(ref Fx6[] a, int len)
    {
        if (a.Length < len)
            a = new Fx6[len];
        else
            a.AsSpan(0, len).Clear();
        return a;
    }

    // Position = index in the canonical list: the tie-break that makes the in-place span sort behave as a stable one (DEBT-0021).
    private readonly record struct Pass2Cell(int Index, int Site, int Cluster, Fx6 Goal, Fx6 Severity, int Position);

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
    private static bool CellTarget(ToolingTargetSpec t, IndustrySystem industry, IndustryLayout l, IIndustryInputs inputs, ReadOnlySpan<ProcessYield> yields,
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
