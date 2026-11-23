using Singularity.Kernel;

namespace Singularity.Core.Products;

/// <summary>Why <c>sell_cohort</c> is refused beyond the stage and count checks (contract r6 §4.4f).</summary>
public enum SaleReject
{
    None = 0,
    /// <summary><c>gap = G_front − gen(d) &lt; 1</c>: the machines are not surplus to the world front (#146).</summary>
    NotSurplus = 1,
    Quota = 2,
    RevenueCap = 3,
}

/// <summary>
/// Result of <see cref="FleetRules.Quote"/>: the one function behind the validator, the AI and the forecast (contract r6 §4.4f
/// <c>SaleQuote</c>). <see cref="Income"/> is the revenue of selling the units; the rest explains it (base · generation · age).
/// </summary>
/// <param name="BaseUnit"><c>unit_cost_floor·σ0</c>.</param>
/// <param name="Phi"><c>φ_gen(gap)</c>, 0 when <c>gap &lt; 1</c>.</param>
/// <param name="UnitMonths">Σ <c>take_i·rem_i</c> of the units sold: remaining service, unit-months, as <see cref="Fx6"/>.</param>
/// <param name="QuotaLeft"><c>⌈ς·quota_base⌉ − sold_y</c> before the sale, ≥ 0.</param>
/// <param name="CapLeft"><c>sale_cap − sale_y</c> before the sale, ≥ 0.</param>
public readonly record struct SaleQuote(SaleReject Reject, int Gap, Fx6 Income, Fx6 BaseUnit, Fx6 Phi, Fx6 UnitMonths, int QuotaLeft, Fx6 CapLeft);

/// <summary>
/// Pure rules of the fleet commands (contract r6 §4.4f, spec A42.1–A42.2): which units go first, what the revenue is, whether the sale
/// passes the quota and the ceiling. Cohorts are given by calendar year (index 0 = the first year of the layout); older first.
/// No state, no RNG; every <c>·</c> and <c>/</c> is one <see cref="Fx6"/> floor in the order of the spec.
/// </summary>
public static class FleetRules
{
    /// <summary>
    /// Takes <paramref name="n"/> units from the cohorts, the oldest year first (the fractional part of the oldest cohort goes first),
    /// into <paramref name="taken"/>; subtracts them from <paramref name="counts"/>. Exactly <c>n</c> unless the fleet is smaller.
    /// </summary>
    public static void Take(Span<Fx6> counts, Fx6 n, Span<Fx6> taken)
    {
        Fx6 rest = n;
        for (int y = 0; y < counts.Length; y++)
        {
            Fx6 take = rest < counts[y] ? rest : counts[y];
            taken[y] = take;
            counts[y] -= take;
            rest -= take;
        }
    }

    /// <summary>
    /// The dust rule (contract r6 §4.4f): if after a command <c>0 &lt; Σcount &lt; 1</c> every cohort becomes 0; returns the dust removed.
    /// </summary>
    public static Fx6 SweepDust(Span<Fx6> counts)
    {
        Fx6 sum = Sum(counts);
        if (sum <= Fx6.Zero || sum >= Fx6.One)
            return Fx6.Zero;
        counts.Clear();
        return sum;
    }

    public static Fx6 Sum(ReadOnlySpan<Fx6> counts)
    {
        Fx6 sum = Fx6.Zero;
        foreach (Fx6 c in counts)
            sum += c;
        return sum;
    }

    /// <summary>
    /// Number of leading cohorts (the oldest) whose age at <paramref name="month"/> is at least the service life: the overage (#431).
    /// <paramref name="m0"/> grows with the year, so the overage is always a prefix.
    /// </summary>
    public static int OverageCohorts(long month, ReadOnlySpan<int> m0, ProductsParams p)
    {
        int k = 0;
        while (k < m0.Length && month - m0[k] >= p.ServiceLife)
            k++;
        return k;
    }

    /// <summary>
    /// <c>n_eff = Σ count·f(m)</c> (#435): units weighed by the value of their age, <c>m = max(0, age − service_life)</c>; every product one Fx6 floor.
    /// </summary>
    public static Fx6 Effective(ReadOnlySpan<Fx6> counts, long month, ReadOnlySpan<int> m0, ProductsParams p)
    {
        Fx6 sum = Fx6.Zero;
        for (int y = 0; y < counts.Length; y++)
        {
            if (counts[y] != Fx6.Zero)
                sum += counts[y] * ProductsParams.AgeValue(month - m0[y] - p.ServiceLife);
        }
        return sum;
    }

    /// <summary>
    /// Step 0 of the replacement (#431, memo §2.3): of the overage cohorts <c>min(whole overage units, ⌊credit⌋)</c> units leave, the oldest first,
    /// the credit falls by that number, and the credit never exceeds the whole units left. The dust rule applies to what is left.
    /// </summary>
    public static void ExpendCredit(Span<Fx6> counts, ref Fx6 credit, long month, ReadOnlySpan<int> m0, ProductsParams p)
    {
        int over = OverageCohorts(month, m0, p);
        long leave = Math.Min(Whole(counts[..over]), credit.Raw / Fx6.Scale);
        if (leave > 0)
        {
            Span<Fx6> taken = stackalloc Fx6[over];
            Take(counts[..over], Fx6.FromInt(leave), taken);
            credit -= Fx6.FromInt(leave);
            SweepDust(counts);
        }
        Fx6 whole = Fx6.FromInt(Whole(counts));
        if (credit > whole)
            credit = whole;
    }

    /// <summary>
    /// The replacement credit of units that arrive (#431, memo §2.3): <paramref name="arrived"/> new units of project <paramref name="e"/> are
    /// credited to the projects they replace (<see cref="Replaces"/>), the highest key <c>(gen, perf, slot)</c> first, each up to its room
    /// <c>whole − credit</c>; what is left over is lost. <paramref name="whole"/> is whole units per slot; <paramref name="done"/> is scratch.
    /// </summary>
    public static void Accrue(Fx6 arrived, int e, ReadOnlySpan<int> gen, ReadOnlySpan<Fx6> perf, ReadOnlySpan<long> whole, Span<Fx6> credit,
        Span<bool> done, ProductsParams p)
    {
        done.Clear();
        Fx6 rest = arrived;
        while (rest > Fx6.Zero)
        {
            int best = -1;
            for (int d = 0; d < gen.Length; d++)
            {
                if (d == e || done[d] || whole[d] <= 0 || !Replaces(gen[e], perf[e], gen[d], perf[d], p))
                    continue;
                if (best < 0 || gen[d] > gen[best] || (gen[d] == gen[best] && perf[d] >= perf[best]))
                    best = d;
            }
            if (best < 0)
                return;
            done[best] = true;
            Fx6 room = Fx6.FromInt(whole[best]) - credit[best];
            if (room <= Fx6.Zero)
                continue;
            Fx6 give = rest < room ? rest : room;
            credit[best] += give;
            rest -= give;
        }
    }

    /// <summary>Project e replaces project d (memo §2.1): a higher generation, or the same one with <c>perf_e ≥ 1,05·perf_d</c>.</summary>
    public static bool Replaces(int genE, Fx6 perfE, int genD, Fx6 perfD, ProductsParams p) =>
        genE > genD || (genE == genD && perfE >= perfD * p.ReplacePerfRatio);

    /// <summary><c>⌊Σcount⌋</c> as an integer.</summary>
    public static long Whole(ReadOnlySpan<Fx6> counts) => Sum(counts).Raw / Fx6.Scale;

    /// <summary>
    /// Price of a unit of the cohort of age <paramref name="age"/> months at the sale (spec A42.2, canonical order):
    /// <c>((((floor·σ0)·φ)·rem)/service_life)</c>, <c>rem = max(0, service_life − age)</c>.
    /// </summary>
    public static Fx6 Price(Fx6 unitCostFloor, ProductsParams p, int gap, long age)
    {
        long rem = Math.Max(0, p.ServiceLife - age);
        return (((unitCostFloor * p.SaleSigma) * p.PhiGen(gap)) * Fx6.FromInt(rem)) / Fx6.FromInt(p.ServiceLife);
    }

    /// <summary>
    /// <c>SaleQuote</c> for selling <paramref name="n"/> units at month <paramref name="month"/> from the cohorts <paramref name="counts"/>
    /// (calendar-year index) whose midyear months are <paramref name="m0"/> (age <c>t − m0(Y)</c>, spec A32). Checks in the order
    /// <c>NOT_SURPLUS, QUOTA, REVENUE_CAP</c>; the stage and count checks belong to the caller.
    /// </summary>
    /// <param name="gap"><c>G_front − gen(d)</c>.</param>
    /// <param name="quotaBase">Whole units at the start of the policy year.</param>
    /// <param name="soldY">Whole units sold since.</param>
    /// <param name="saleY">Revenue since the start of the year.</param>
    /// <param name="saleCap">Revenue ceiling of the year.</param>
    public static SaleQuote Quote(ReadOnlySpan<Fx6> counts, long n, long month, ReadOnlySpan<int> m0, Fx6 unitCostFloor, int gap, ProductsParams p,
        int quotaBase, int soldY, Fx6 saleY, Fx6 saleCap)
    {
        long quotaCap = QuotaCap(p.SaleQuota, quotaBase);
        int quotaLeft = (int)Math.Max(0, quotaCap - soldY);
        Fx6 capLeft = p.SaleChiOff ? Fx6.FromInt(long.MaxValue / Fx6.Scale / 4) : (saleCap > saleY ? saleCap - saleY : Fx6.Zero);
        Fx6 baseUnit = unitCostFloor * p.SaleSigma;
        if (gap < 1)
            return new SaleQuote(SaleReject.NotSurplus, gap, Fx6.Zero, baseUnit, Fx6.Zero, Fx6.Zero, quotaLeft, capLeft);
        Fx6 phi = p.PhiGen(gap);
        Span<Fx6> work = stackalloc Fx6[counts.Length];
        Span<Fx6> taken = stackalloc Fx6[counts.Length];
        counts.CopyTo(work);
        Take(work, Fx6.FromInt(n), taken);
        Int128 income = 0, unitMonths = 0;
        for (int y = 0; y < taken.Length; y++)
        {
            if (taken[y] == Fx6.Zero)
                continue;
            long age = month - m0[y];
            income += (taken[y] * Price(unitCostFloor, p, gap, age)).Raw;
            unitMonths += (taken[y] * Fx6.FromInt(Math.Max(0, p.ServiceLife - age))).Raw;
        }
        Fx6 inc = Fx6.FromRaw(checked((long)income));
        SaleReject reject = SaleReject.None;
        if (soldY + n > quotaCap)
            reject = SaleReject.Quota;
        else if (!p.SaleChiOff && saleY + inc > saleCap)
            reject = SaleReject.RevenueCap;
        return new SaleQuote(reject, gap, inc, baseUnit, phi, Fx6.FromRaw(checked((long)unitMonths)), quotaLeft, capLeft);
    }

    /// <summary><c>⌈ς·quota_base⌉</c> in 128 bits, one rounding up (contract r6 §4.4f).</summary>
    public static long QuotaCap(Fx6 sigma, int quotaBase)
    {
        Int128 product = (Int128)sigma.Raw * quotaBase;
        return (long)((product + Fx6.Scale - 1) / Fx6.Scale);
    }

    /// <summary>The year's revenue ceiling <c>⌊χ·(12·B_base)⌋</c> (contract r6 §4.4f).</summary>
    public static Fx6 SaleCap(Fx6 chi, Fx6 baseBudget) => chi * (Fx6.FromInt(12) * baseBudget);

    /// <summary>
    /// <c>SaleMax(d, n)</c> (spec A42.4 п. 5): the largest <c>s ≤ n</c> for which <see cref="Quote"/> gives no refusal. The quote is
    /// monotone in <c>s</c> (older units first, each adds revenue), so a binary search over whole units finds it exactly.
    /// </summary>
    public static long SaleMax(ReadOnlySpan<Fx6> counts, long n, long month, ReadOnlySpan<int> m0, Fx6 unitCostFloor, int gap, ProductsParams p,
        int quotaBase, int soldY, Fx6 saleY, Fx6 saleCap)
    {
        if (gap < 1 || n < 1)
            return 0;
        long lo = 0, hi = n;
        while (lo < hi)
        {
            long mid = lo + (hi - lo + 1) / 2;
            if (Quote(counts, mid, month, m0, unitCostFloor, gap, p, quotaBase, soldY, saleY, saleCap).Reject == SaleReject.None)
                lo = mid;
            else
                hi = mid - 1;
        }
        return lo;
    }
}
