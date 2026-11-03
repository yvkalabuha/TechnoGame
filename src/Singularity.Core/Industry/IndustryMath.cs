using Singularity.Core.Data;
using Singularity.Kernel;

namespace Singularity.Core.Industry;

/// <summary>Which stock limits <c>c_d = min(culture_H, tooling_M)</c>; a tie <c>H = M</c> is <see cref="H"/> (spec §3.5 A5).</summary>
public enum CapabilitySource { H, M }

/// <summary>What limits the bottleneck process (contract §4.2, spec §3.5 A5).</summary>
public enum BottleneckSource
{
    H,
    M,

    /// <summary>All <c>g_d = 1</c>: only <c>ramp</c> limits the process (dim = null).</summary>
    Ramp,

    /// <summary>L_C / L_C0 controls: the yield comes from a curve or a constant, not from stocks.</summary>
    Control,
}

/// <summary>
/// <c>yield_p</c> with its parts (contract §4.2). Only dimensions with <c>r_d &gt; 0</c> are active; for an
/// inactive dimension <c>C</c>, <c>G</c> are 0 and meaningless. <c>QP</c> = <c>q_p</c> (A4).
/// </summary>
public readonly record struct ProcessYield(int ActiveMask, Dims3 C, Dims3 G, int MSourceMask, Fx6 XHalfEff, Fx6 Ramp, Fx6 Yield, Fx6 QP)
{
    public bool IsActive(int dim) => (ActiveMask & (1 << dim)) != 0;

    public CapabilitySource Source(int dim) => (MSourceMask & (1 << dim)) != 0 ? CapabilitySource.M : CapabilitySource.H;

    /// <summary>A yield set directly by a control (L_C, L_C0): no dimensions, no ramp.</summary>
    public static ProcessYield Control(Fx6 yield) => new(0, default, default, 0, Fx6.Zero, Fx6.Zero, yield, Fx6.One);
}

/// <summary>One process of a <c>prc_set</c> for <see cref="IndustryMath.Profile"/>.</summary>
public readonly record struct ProfileItem(int ProcessNumber, ProcessYield Yield, bool Known, Fx6 N, Fx6 BaseCost, Fx6 ControlUnitCost = default);

/// <summary><c>production_profile(site, prc_set)</c> (contract §4.2, §5); <see cref="BottleneckDim"/> is null for ramp and controls.</summary>
public readonly record struct ProductionProfile(bool Admitted, Fx6 UnitCostMult, Fx6 Q, int BottleneckProcess,
    Dimension? BottleneckDim, BottleneckSource BottleneckSource);

/// <summary>One process of a cluster in the <c>culture_H</c> step: <c>v_p</c> of month t−1, <c>r_d(p)</c>, <c>world_lead</c>.</summary>
public readonly record struct CultureProcess(int ProcessNumber, Fx6 V, Fx6 R, bool WorldLead);

/// <summary>Spill-in from another cluster c' of the site: <c>R[c,c']</c> and the snapshot <c>H[c',d]</c>.</summary>
public readonly record struct CultureSpill(Fx6 R, Fx6 HOther);

/// <summary>The cell <c>(site, c, d)</c> before the step; all snapshot values.</summary>
public readonly record struct CultureCell(Fx6 H, Fx6 Hcap, Fx6 Act, Fx6 W, bool QualityBoost);

/// <summary>One <c>culture_H</c> step with every intermediate value of contract §4.1.</summary>
public readonly record struct CultureStep(Fx6 VC, Fx6 Drive, Fx6 Catch, Fx6 Maint, Fx6 Spill, Fx6 HFloor, Fx6 Sat,
    Fx6 AlphaEff, Fx6 DHPlus, Fx6 DHPlusCapped, Fx6 DHMinus, Fx6 HNew);

/// <summary>
/// Capability formulas of the slice in canonical Fx6 form (contract v2.3 §4.0–4.2, §4.6; spec §1.4, §3.2,
/// §3.5, §6.1): left to right, floor on every <c>·</c> and <c>/</c>, sums by ascending id. Do not reorder or
/// simplify: lab/reference/jet_slice/ checks them bit for bit. No RNG here or anywhere in the layer (#28).
/// </summary>
public static class IndustryMath
{
    private static readonly Fx6 GapOffset = Fx6.FromRaw(250_000);

    /// <summary><c>g(gap)</c>: 0 below −1, <c>(1 + gap)·(1 + gap)</c> on [−1; 0), 1 from 0.</summary>
    public static Fx6 G(Fx6 gap)
    {
        if (gap < -Fx6.One)
            return Fx6.Zero;
        if (gap < Fx6.Zero)
        {
            Fx6 t = Fx6.One + gap;
            return t * t;
        }
        return Fx6.One;
    }

    /// <summary><c>ramp = λ0 + ((1 − λ0)·X)/(X + X½_eff)</c> (spec §3.5).</summary>
    public static Fx6 Ramp(Fx6 x, Fx6 xHalfEff, Fx6 lambda0) => lambda0 + (Fx6.One - lambda0) * x / (x + xHalfEff);

    /// <summary><c>X½_eff = X½·(1 − l_lic)</c>; <c>l_lic = 0</c> means no licence and gives <c>X½</c> exactly.</summary>
    public static Fx6 XHalfEff(Fx6 xHalf, Fx6 lLic)
    {
        if (lLic < Fx6.Zero || lLic >= Fx6.One)
            throw new ArgumentOutOfRangeException(nameof(lLic), lLic, "l_lic must be in [0; 1).");
        return xHalf * (Fx6.One - lLic);
    }

    /// <summary>
    /// <c>yield_p = ((1·g_d1)·g_d2·…)·ramp</c> over dimensions with <c>r_d &gt; 0</c> in canonical order, ramp last;
    /// <c>q_p = g(c_qc − r_qc)</c> or 1 (A4). <paramref name="req"/> uses 0 for "no requirement".
    /// </summary>
    public static ProcessYield Yield(Dims3 req, Dims3 h, Dims3 m, Fx6 x, Fx6 xHalf, Fx6 lLic, Fx6 lambda0)
    {
        Fx6 acc = Fx6.One, qp = Fx6.One;
        Span<Fx6> c = stackalloc Fx6[Dims3.Count];
        Span<Fx6> g = stackalloc Fx6[Dims3.Count];
        int active = 0, mSource = 0;
        for (int d = 0; d < Dims3.Count; d++)
        {
            if (req[d] <= Fx6.Zero)
                continue;
            active |= 1 << d;
            bool fromM = m[d] < h[d];
            if (fromM)
                mSource |= 1 << d;
            c[d] = fromM ? m[d] : h[d];
            g[d] = G(c[d] - req[d]);
            acc *= g[d];
            if (d == (int)Dimension.QualityControl)
                qp = g[d];
        }
        Fx6 xh = XHalfEff(xHalf, lLic);
        Fx6 ramp = Ramp(x, xh, lambda0);
        return new ProcessYield(active, new Dims3(c[0], c[1], c[2]), new Dims3(g[0], g[1], g[2]), mSource, xh, ramp, acc * ramp, qp);
    }

    /// <summary>
    /// <c>production_profile</c> (contract §4.2): admitted, <c>unit_cost_mult</c> with <c>yc_p = max(yield_p, ε)</c>,
    /// <c>q = min q_p</c>, bottleneck (A5, A6; ties by process number, then canonical dimension order). Items
    /// may come in any order; sums run by ascending process number. Under a control (L_C, L_C0;
    /// <paramref name="control"/>) the per-process multiplier is <see cref="ProfileItem.ControlUnitCost"/>
    /// instead of <c>1/yc_p</c>, weighted the same way, and the bottleneck source is <see cref="BottleneckSource.Control"/>.
    /// </summary>
    public static ProductionProfile Profile(ReadOnlySpan<ProfileItem> items, Fx6 eps, bool control = false)
    {
        if (items.IsEmpty)
            throw new ArgumentException("A prc_set needs at least one process.", nameof(items));
        ProfileItem[] sorted = items.ToArray();
        Array.Sort(sorted, (a, b) => a.ProcessNumber.CompareTo(b.ProcessNumber));
        bool admitted = true;
        Fx6 num = Fx6.Zero, den = Fx6.Zero, q = Fx6.One;
        int bn = 0;
        for (int i = 0; i < sorted.Length; i++)
        {
            ProfileItem p = sorted[i];
            if (i > 0 && p.ProcessNumber == sorted[i - 1].ProcessNumber)
                throw new ArgumentException($"Process {p.ProcessNumber} is twice in the prc_set.", nameof(items));
            admitted &= p.Yield.Yield > Fx6.Zero && p.Known;
            Fx6 nb = p.N * p.BaseCost;
            if (control)
                num += nb * p.ControlUnitCost;
            else
                num += nb / (p.Yield.Yield > eps ? p.Yield.Yield : eps);
            den += nb;
            if (p.Yield.QP < q)
                q = p.Yield.QP;
            if (p.Yield.Yield < sorted[bn].Yield.Yield)
                bn = i;
        }
        ProcessYield y = sorted[bn].Yield;
        Dimension? dim = null;
        BottleneckSource src = control ? BottleneckSource.Control : BottleneckSource.Ramp;
        Fx6 minG = Fx6.One;
        for (int d = 0; d < Dims3.Count; d++)
        {
            if (y.IsActive(d) && y.G[d] < minG)
            {
                minG = y.G[d];
                dim = (Dimension)d;
                src = y.Source(d) == CapabilitySource.M ? BottleneckSource.M : BottleneckSource.H;
            }
        }
        return new ProductionProfile(admitted, num / den, q, sorted[bn].ProcessNumber, dim, src);
    }

    /// <summary>
    /// One <c>culture_H</c> cell step (contract §4.1, canonical form). <paramref name="processes"/> are the
    /// cluster's processes with <c>v_p &gt; 0</c> of month t−1 in ascending number; <paramref name="spill"/>
    /// the other clusters of the site. Process order is checked, not repaired.
    /// </summary>
    public static CultureStep Culture(CultureCell cell, ReadOnlySpan<CultureProcess> processes, ReadOnlySpan<CultureSpill> spill, IndustryParams p)
    {
        Fx6 h = cell.H;
        Fx6 vc = Fx6.Zero;
        for (int i = 0; i < processes.Length; i++)
        {
            if (i > 0 && processes[i].ProcessNumber <= processes[i - 1].ProcessNumber)
                throw new ArgumentException("Culture processes must be in strictly ascending number.", nameof(processes));
            vc += processes[i].V;
        }
        Fx6 drive = Fx6.Zero, catchUp = Fx6.Zero, maint = Fx6.Zero;
        if (vc != Fx6.Zero)
        {
            Fx6 actRatio = cell.Act / p.ActNorm;
            if (actRatio > Fx6.One)
                actRatio = Fx6.One;
            foreach (CultureProcess proc in processes)
            {
                Fx6 s = proc.V / vc;
                Fx6 above = Max0(proc.R + GapOffset - h);
                drive += s * above;
                if (proc.WorldLead)
                    catchUp += s * above;
                maint += s * proc.R * actRatio;
            }
        }
        Fx6 spillSum = Fx6.Zero;
        foreach (CultureSpill sp in spill)
            spillSum += sp.R * Max0(sp.HOther - h);
        Fx6 hFloor = p.PhiFloor * cell.Hcap;
        Fx6 sat = Max0(Fx6.One - h / cell.Hcap);
        Fx6 alphaEff = cell.QualityBoost ? p.Alpha * (Fx6.One + p.KQp) : p.Alpha;
        Fx6 inner = alphaEff * cell.Act * cell.W * drive + p.KCatch * catchUp + p.Sigma * spillSum;
        Fx6 dPlus = sat * inner;
        Fx6 capped = dPlus < p.RhoMax ? dPlus : p.RhoMax;
        Fx6 dMinus = p.DeltaH * Max0(h - (maint > hFloor ? maint : hFloor));
        return new CultureStep(vc, drive, catchUp, maint, spillSum, hFloor, sat, alphaEff, dPlus, capped, dMinus, h + capped - dMinus);
    }

    /// <summary><c>cost_M(k) = c_M·2^(k−3)</c>, <c>k = clamp(⌊M⌋, 3, 5)</c> of the cell before investment (spec §3.5 A11).</summary>
    public static Fx6 CostM(Fx6 m, IndustryParams p)
    {
        long k = m.Raw < 3 * Fx6.Scale ? 3 : m.Raw >= 5 * Fx6.Scale ? 5 : m.Raw / Fx6.Scale;
        return p.CostMByTier[(int)k - 3];
    }

    /// <summary><c>M' = M + invest/cost_M − δ_M·M</c> (spec §3.2, §3.5 A11).</summary>
    public static Fx6 Tooling(Fx6 m, Fx6 invest, Fx6 costM, Fx6 deltaM) => m + invest / costM - deltaM * m;

    /// <summary>Forgetting <c>x − δ·x</c> (contract §4.0 p. 6).</summary>
    public static Fx6 Forget(Fx6 x, Fx6 delta) => x - delta * x;

    /// <summary>
    /// <c>reserve_workforce</c> split of one (site, cat) (contract §4.6): requests in ascending
    /// <c>requester_id</c>; all granted if they fit, else <c>⌊n_i·avail/Σn⌋</c> and the rest one by one in
    /// requester order. Whole people.
    /// </summary>
    public static void SplitWorkforce(long avail, ReadOnlySpan<long> requested, Span<long> granted)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(avail);
        if (granted.Length != requested.Length)
            throw new ArgumentException("One granted slot per request.", nameof(granted));
        long total = 0;
        foreach (long n in requested)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(n, nameof(requested));
            total = checked(total + n);
        }
        if (total <= avail)
        {
            requested.CopyTo(granted);
            return;
        }
        long rest = avail;
        for (int i = 0; i < requested.Length; i++)
        {
            granted[i] = (long)((Int128)requested[i] * avail / total);
            rest -= granted[i];
        }
        for (int i = 0; i < requested.Length && rest > 0; i++)
        {
            if (granted[i] < requested[i])
            {
                granted[i]++;
                rest--;
            }
        }
    }

    /// <summary>Alloy supply of a month: <c>min(chain output, alloy_cap·(1 − deficit))</c> (spec §1.4).</summary>
    public static Fx6 AlloySupply(Fx6 chainOutput, Fx6 cap, Fx6 deficit)
    {
        Fx6 limit = cap * (Fx6.One - deficit);
        return chainOutput < limit ? chainOutput : limit;
    }

    /// <summary><c>line_capacity = supply / alloy_kg[gen]</c> (spec §1.4).</summary>
    public static Fx6 LineCapacity(Fx6 supply, Fx6 alloyPerEngine) => supply / alloyPerEngine;

    /// <summary>The alloy limit shared by lines in ascending <c>design_id</c> (spec §1.4): each takes what it needs while it lasts.</summary>
    public static void SplitAlloy(Fx6 capacity, ReadOnlySpan<Fx6> demandByDesign, Span<Fx6> granted)
    {
        if (granted.Length != demandByDesign.Length)
            throw new ArgumentException("One granted slot per line.", nameof(granted));
        Fx6 left = capacity;
        for (int i = 0; i < demandByDesign.Length; i++)
        {
            if (demandByDesign[i] < Fx6.Zero)
                throw new ArgumentOutOfRangeException(nameof(demandByDesign), demandByDesign[i], "Demand cannot be negative.");
            granted[i] = demandByDesign[i] < left ? demandByDesign[i] : left;
            left -= granted[i];
        }
    }

    /// <summary>
    /// Piecewise-linear curve over integer months (L_C, spec §6.1): clamped at both ends; inside a segment
    /// <c>v_i + ((v_{i+1} − v_i)·(τ − τ_i))/(τ_{i+1} − τ_i)</c>.
    /// </summary>
    public static Fx6 Interpolate(ReadOnlySpan<int> tau, ReadOnlySpan<Fx6> value, long t)
    {
        if (t <= tau[0])
            return value[0];
        int last = tau.Length - 1;
        if (t >= tau[last])
            return value[last];
        int i = 0;
        while (tau[i + 1] <= t)
            i++;
        return value[i] + (value[i + 1] - value[i]) * Fx6.FromInt(t - tau[i]) / Fx6.FromInt(tau[i + 1] - tau[i]);
    }

    private static Fx6 Max0(Fx6 x) => x > Fx6.Zero ? x : Fx6.Zero;
}
