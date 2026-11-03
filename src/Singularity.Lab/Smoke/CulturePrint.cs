using System.Globalization;
using System.Text;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Kernel;

namespace Singularity.Lab.Smoke;

/// <summary>Effort mode of a C-B stand (culture-round2.md §1.3): AI stub, MAX (<c>E_full</c> to every running process), ZERO.</summary>
internal enum CbEffort { AI, MAX, ZERO }

/// <summary>
/// Parameters of the provisional C-B rule (culture-round2.md §1.1.2, decision #111, contract v2.4 §4.1), SMOOTH form.
/// <see cref="VolShare"/> = aggregation over the processes of a cell by volume share <c>s_p = v_p/V_c</c> (contract v2.3
/// F2/A2, Appendix A) instead of <c>max_p</c>; with it the AI stub drops the <c>lead</c> condition (every process of the
/// cell contributes by its share, so its money is not wasted).
/// </summary>
internal sealed record CbParams(bool VolShare, Fx6 Ell, string Name)
{
    public Fx6 RCap { get; init; } = Fx6.FromInt(10);
    public Fx6 RKeep { get; init; } = Fx6.FromRaw(6_250_000);
    public Fx6 KTr { get; init; } = Fx6.One;
    public Fx6 EFull { get; init; } = Fx6.FromRaw(1_838);
    public Fx6 DeltaL { get; init; } = Fx6.FromRaw(500_000);
    public Fx6 RRef { get; init; } = Fx6.FromInt(25);

    public string Label => $"CB/{Name}/{(VolShare ? "share" : "max")}/l{Ell}";
}

/// <summary>
/// C-B culture step and the AI allocation stub in canonical Fx6 form (contract §4.0: left to right, floor on every
/// · and /, ascending prc_id, branches before division). Reads only the stand snapshot of month t−1.
/// </summary>
internal static class CbMath
{
    private static readonly Fx6 GapOffset = Fx6.FromRaw(250_000);
    private static readonly Fx6 Two = Fx6.FromInt(2), Three = Fx6.FromInt(3);

    private static Fx6 Min(Fx6 a, Fx6 b) => a < b ? a : b;

    private static Fx6 Max(Fx6 a, Fx6 b) => a > b ? a : b;

    private static Fx6 Max0(Fx6 x) => x > Fx6.Zero ? x : Fx6.Zero;

    /// <summary><c>ṽ_p = (1 + k_tr·lic_p)·v_p</c> (#104, #107).</summary>
    public static Fx6 VTilde(CbParams cb, Fx6 v, bool lic) => (lic ? Fx6.One + cb.KTr : Fx6.One) * v;

    /// <summary><c>c_p</c>, SMOOTH: <c>x_V = min(1, ṽ/v_cap)</c>, <c>(x·x)·(3 − 2·x)</c>.</summary>
    public static Fx6 C(CbParams cb, Fx6 vt, Fx6 labour)
    {
        Fx6 vcap = cb.RCap * labour;
        Fx6 x = vcap == Fx6.Zero ? Fx6.Zero : Min(Fx6.One, vt / vcap);
        return x * x * (Three - Two * x);
    }

    public static CultureStep Step(RuleStand s, int c, int d, CultureCell cell, ReadOnlySpan<CultureSpill> spill)
    {
        CbParams cb = s.Variant.Cb!;
        IndustryParams p = s.P;
        IndustryLayout l = s.Layout;
        Fx6 h = cell.H;
        Fx6 hFloor = p.PhiFloor * cell.Hcap;
        Fx6 actRatio = Min(Fx6.One, cell.Act / p.ActNorm);
        Span<Fx6> tt = stackalloc Fx6[l.ProcessCount];
        Span<Fx6> mm = stackalloc Fx6[l.ProcessCount];
        Span<Fx6> vv = stackalloc Fx6[l.ProcessCount];
        Span<bool> wl = stackalloc bool[l.ProcessCount];
        int np = 0;
        Fx6 vc = Fx6.Zero;
        foreach (int r in l.Order)
        {
            if (l.ClusterIndex[r] != c || (s.V[r] <= Fx6.Zero && s.E[r] <= Fx6.Zero))
                continue;
            Fx6 req = l.Req[r][d];
            if (!cb.VolShare && req <= Fx6.Zero)
                continue; // v2.4: processes with r_d = 0 are not in the cell; V1 keeps them (A2 v2.3)
            Fx6 labour = s.LabourOf(r);
            Fx6 vt = VTilde(cb, s.V[r], s.Lic[r]);
            Fx6 cp = C(cb, vt, labour);
            Fx6 xE = cb.EFull == Fx6.Zero ? Fx6.Zero : Min(Fx6.One, s.E[r] / cb.EFull);
            Fx6 eV = Min(xE, cp);
            Fx6 eL = cb.Ell == Fx6.Zero ? Fx6.Zero : Min(xE, cb.Ell);
            Fx6 gap = Max0(req + GapOffset - h);
            Fx6 gapL = Max0(req - cb.DeltaL - h);
            Fx6 t = Max(eV * gap, eL * gapL);
            Fx6 vkeep = cb.RKeep * labour;
            Fx6 m = vkeep == Fx6.Zero ? Fx6.Zero : Min(Fx6.One, vt / vkeep);
            Fx6 mp = hFloor + (Max(req * actRatio, hFloor) - hFloor) * m;
            tt[np] = t;
            mm[np] = mp;
            vv[np] = s.V[r];
            wl[np] = s.WorldLead[r];
            vc += s.V[r];
            np++;
        }
        Fx6 drive = Fx6.Zero, catchUp = Fx6.Zero, maint = hFloor;
        if (!cb.VolShare || vc == Fx6.Zero)
        {
            // max over the cell (v2.4); V1 with no volume in the cell (money only): nothing to share by, max as well
            for (int i = 0; i < np; i++)
            {
                drive = Max(drive, tt[i]);
                if (wl[i])
                    catchUp = Max(catchUp, tt[i]);
                maint = Max(maint, mm[i]);
            }
        }
        else
        {
            Fx6 msum = Fx6.Zero;
            for (int i = 0; i < np; i++)
            {
                Fx6 sp = vv[i] / vc; // separate operation (§4.0 п.3)
                drive += sp * tt[i];
                if (wl[i])
                    catchUp += sp * tt[i];
                msum += sp * mm[i];
            }
            maint = Max(hFloor, msum);
        }
        Fx6 spillSum = Fx6.Zero;
        foreach (CultureSpill sp in spill)
            spillSum += sp.R * Max0(sp.HOther - h);
        Fx6 sat = Max0(Fx6.One - h / cell.Hcap);
        Fx6 alphaEff = cell.QualityBoost ? p.Alpha * (Fx6.One + p.KQp) : p.Alpha;
        Fx6 inner = alphaEff * cell.Act * cell.W * drive + p.KCatch * catchUp + p.Sigma * spillSum;
        Fx6 dPlus = sat * inner;
        Fx6 capped = dPlus < p.RhoMax ? dPlus : p.RhoMax;
        Fx6 dMinus = p.DeltaH * Max0(h - (maint > hFloor ? maint : hFloor));
        return new CultureStep(vc, drive, catchUp, maint, spillSum, hFloor, sat, alphaEff, dPlus, capped, dMinus, h + capped - dMinus);
    }

    /// <summary>
    /// <c>proc_E</c> of month t from the snapshot t−1 (§1.3; contract §4.1a, §4.1b). AI: candidates with
    /// <c>limiter₀ = EFFORT</c> and <c>E_useful &gt; 0</c>, weight <c>(v_exp/(r_ref·labour))·gap_H</c>, pour bounded by the
    /// number of candidates; the rest goes to tooling (D-1 on every print stand, so it has no effect there).
    /// </summary>
    public static Fx6[] Allocate(RuleStand s)
    {
        CbParams cb = s.Variant.Cb!;
        IndustryLayout l = s.Layout;
        int n = l.ProcessCount;
        var e = new Fx6[n];
        if (s.Effort == CbEffort.ZERO)
            return e;
        if (s.Effort == CbEffort.MAX)
        {
            for (int r = 0; r < n; r++)
                if (s.Running(r))
                    e[r] = cb.EFull;
            return e;
        }
        int cells = l.Clusters.Length * 3;
        var leadRow = new int[cells];
        var leadT = new Fx6[cells];
        Array.Fill(leadRow, -1);
        var cRow = new Fx6[n];
        foreach (int r in l.Order)
        {
            if (!s.Known(r))
                continue;
            cRow[r] = C(cb, VTilde(cb, s.V[r], s.Lic[r]), s.LabourOf(r));
            int c = l.ClusterIndex[r];
            for (int d = 0; d < 3; d++)
            {
                Fx6 req = l.Req[r][d];
                if (req <= Fx6.Zero)
                    continue;
                Fx6 h = s.H[c * 3 + d];
                Fx6 th = Max(cRow[r] * Max0(req + GapOffset - h), cb.Ell * Max0(req - cb.DeltaL - h));
                if (th > leadT[c * 3 + d])
                {
                    leadT[c * 3 + d] = th;
                    leadRow[c * 3 + d] = r; // strict >: tie keeps the smaller prc_id
                }
            }
        }
        var cand = new List<(int Row, Fx6 EU, Fx6 W)>();
        int target = s.Target;
        bool targetLeads = false;
        foreach (int r in l.Order)
        {
            if (!s.Known(r))
                continue;
            int c = l.ClusterIndex[r];
            int dStar = -1;
            Fx6 best = Fx6.Zero, gapH = Fx6.Zero;
            for (int d = 0; d < 3; d++)
            {
                Fx6 req = l.Req[r][d];
                if (req <= Fx6.Zero)
                    continue;
                Fx6 narrow = req - s.H[c * 3 + d];
                gapH = Max(gapH, Max0(narrow));
                if (!cb.VolShare && leadRow[c * 3 + d] != r)
                    continue;
                if (dStar < 0 || narrow > best)
                {
                    dStar = d;
                    best = narrow;
                }
            }
            if (dStar < 0)
                continue; // leads no cell: E_useful = 0
            if (r == target)
                targetLeads = true;
            int cell = c * 3 + dStar;
            Fx6 rq = l.Req[r][dStar], hh = s.H[cell];
            Fx6 gap = Max0(rq + GapOffset - hh), gapL = Max0(rq - cb.DeltaL - hh);
            if (gap == Fx6.Zero || s.M[cell] < hh || s.Hcap <= rq || (s.V[r] == Fx6.Zero && (cb.Ell == Fx6.Zero || hh >= rq - cb.DeltaL)))
                continue; // limiter₀ ∈ {NONE, TOOLING, SATURATION, LAB_CEILING}
            Fx6 kStar = cb.Ell * gapL > cRow[r] * gap ? cb.Ell : cRow[r];
            Fx6 eu = cb.EFull * kStar;
            if (eu <= Fx6.Zero)
                continue;
            Fx6 labour = s.LabourOf(r);
            Fx6 vexp = s.Running(r) ? s.Rate * labour : Fx6.Zero;
            Fx6 w = labour == Fx6.Zero ? Fx6.Zero : vexp / (cb.RRef * labour) * gapH;
            cand.Add((r, eu, w));
        }
        if (target >= 0 && s.V[target] > Fx6.Zero && !targetLeads)
            s.TargetNoLeadMonths++;
        Fx6 left = s.DBudget;
        int iters = cand.Count;
        for (int it = 0; it < iters && left > Fx6.Zero && cand.Count > 0; it++)
        {
            Fx6 sumW = Fx6.Zero;
            foreach (var k in cand)
                sumW += k.W;
            if (sumW == Fx6.Zero)
                break;
            Fx6 given = Fx6.Zero;
            foreach (var k in cand)
            {
                Fx6 a = left * k.W / sumW;
                Fx6 take = Min(a, k.EU - e[k.Row]);
                e[k.Row] += take;
                given += take;
            }
            left -= given;
            cand.RemoveAll(k => e[k.Row] >= k.EU);
            if (given == Fx6.Zero)
                break;
        }
        return e;
    }
}

/// <summary>
/// Diagnostic print for the owner (culture-round2.md §10): three variants of the provisional C-B rule, no pass/fail,
/// no tuning. D-1 (tooling not limiting) on every stand. Doubles only in the analysis of finished runs.
/// </summary>
internal static class CulturePrint
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly int[] PairsA = [40, 49, 41, 51, 52];
    private static readonly int[] PairsB = [38, 44, 45, 50, 53, 54, 55, 57];
    private static readonly long[] PrintAlphas = [20_000, 70_000, 150_000];
    private const long DeltaMid = 3_000;
    private const double KLine = 0.25, Eps = 0.01, RRef = 25;

    internal static readonly CbParams[] Variants =
    [
        new(true, Fx6.FromRaw(100_000), "V1"),
        new(false, Fx6.FromRaw(100_000), "V2"),
        new(false, Fx6.Zero, "V3"),
    ];

    private static RuleVariant Rv(CbParams cb) => new(CultureRule.CB, CultureRules.RNorm, false, Fx6.Zero, true, cb);

    private static readonly StandCondition CN = StandCondition.Default;
    private static readonly StandCondition CL = new(Fx6.One, LeadMode.None, true);
    private static readonly StandCondition CW = new(Fx6.One, LeadMode.Target);
    private static readonly StandCondition CLW = new(Fx6.One, LeadMode.Target, true);
    private static readonly StandCondition CA75 = new(Fx6.FromRaw(750_000), LeadMode.None);
    private static readonly StandCondition CA50 = new(Fx6.FromRaw(500_000), LeadMode.None);

    /// <summary>Result of one run of one target.</summary>
    internal sealed record Res(bool Admitted, int T, int THalf, double G, double GEnd, double CostE, double CostLine, double CostOut,
        double Spent1200, double Closed120, double Closed600, double Closed1200, int NoLead, double Budget)
    {
        public double Cost => CostE + CostLine + CostOut;
    }

    private static double BaseCost(int number) => Math.Floor(SmokeT4s.Labour[number] * 0.125) * 1e-6; // product_inputs.md §4 R-BC

    internal static Res Run(GameData data, ScenarioData sc, CbParams cb, long alpha, long delta, int actor, int proc, Fx6 rate,
        StandCondition cond, StartMod start, CbEffort effort, int horizon = 1200, (int, long)[]? extra = null, bool full = false)
    {
        RuleStand.FreeTooling.Value = true;
        RuleStand.Condition.Value = cond;
        RuleStand s;
        try
        {
            s = new RuleStand(data, sc, actor, CultureRules.Params(alpha, delta, 100), Rv(cb), rate, proc, RunSet.Solo, start, false, extra);
        }
        finally
        {
            RuleStand.FreeTooling.Value = false;
            RuleStand.Condition.Value = StandCondition.Default;
        }
        s.Effort = effort;
        int tr = s.Target;
        IndustryLayout l = s.Layout;
        int c = l.ClusterIndex[tr];
        var h0 = new double[3];
        double g = 0;
        for (int d = 0; d < 3; d++)
        {
            h0[d] = s.H[c * 3 + d].ToDouble();
            double rq = l.Req[tr][d].ToDouble();
            if (rq > 0)
                g += Math.Max(0, rq - h0[d]);
        }
        double Closed()
        {
            double x = 0;
            for (int d = 0; d < 3; d++)
            {
                double rq = l.Req[tr][d].ToDouble();
                if (rq > 0)
                    x += Math.Max(0, Math.Min(s.H[c * 3 + d].ToDouble(), rq) - h0[d]);
            }
            return g > 0 ? x / g : 1;
        }
        double b = BaseCost(proc), r = rate.ToDouble();
        double cE = 0, cL = 0, cO = 0, spent = 0;
        int T = s.GH(tr) >= CultureRules.G09 ? 0 : -1, tHalf = g < 0.05 ? -2 : -1;
        double tE = 0, tL = 0, tO = 0, c120 = 0, c600 = 0;
        bool admitted = false;
        for (int t = 1; t <= horizon; t++)
        {
            s.Step();
            bool out1 = s.V[tr] > Fx6.Zero;
            admitted |= out1;
            double eT = s.E[tr].ToDouble();
            cE += eT;
            spent += eT;
            if (out1)
            {
                cL += KLine * RRef * b;
                cO += r * b / Math.Max(s.Y[tr].Yield.ToDouble(), Eps);
            }
            if (tHalf == -1 && Closed() >= 0.5)
                tHalf = t;
            if (T == -1 && s.GH(tr) >= CultureRules.G09)
            {
                T = t;
                (tE, tL, tO) = (cE, cL, cO);
            }
            if (t == 120)
                c120 = Closed();
            if (t == 600)
                c600 = Closed();
            if (!full && T != -1 && tHalf != -1)
                break;
        }
        if (T == -1)
            (tE, tL, tO) = (cE, cL, cO);
        bool adm = admitted || rate == Fx6.Zero;
        return new Res(adm, T, tHalf, g, s.GH(tr).ToDouble(), tE, tL, tO, spent, c120, c600, Closed(), s.TargetNoLeadMonths, sc.BudgetIndex[actor].ToDouble());
    }

    private static IEnumerable<(int Actor, int Proc)> Pairs(ScenarioData sc)
    {
        for (int a = 0; a < sc.ActorCount; a++)
            foreach (int p in PairsA.Concat(PairsB))
                yield return (a, p);
    }

    private static string F(double x, string fmt = "0.##") => double.IsNaN(x) ? "-" : double.IsPositiveInfinity(x) ? "∞" : x.ToString(fmt, Inv);

    private static double Q(List<double> xs, double q)
    {
        if (xs.Count == 0)
            return double.NaN;
        var a = xs.OrderBy(x => x).ToList();
        return a[(int)Math.Min(a.Count - 1, Math.Floor(q * a.Count))];
    }

    private static string Dist(List<double> xs, string fmt = "0") =>
        xs.Count == 0 ? "-" : $"{F(Q(xs, 0.5), fmt)} [{F(Q(xs, 0.1), fmt)}…{F(Q(xs, 0.9), fmt)}; худш. {F(xs.Max(), fmt)}]";

    public static int Run(GameData data, ScenarioData sc, string outDir, TextWriter log)
    {
        outDir = Path.Combine(outDir, "cb-print");
        Directory.CreateDirectory(outDir);
        var sb = new StringBuilder();
        var pairs = Pairs(sc).ToList();
        foreach (CbParams cb in Variants)
            foreach (long a in PrintAlphas)
                PrintVariant(data, sc, cb, a, pairs, sb);
        foreach (CbParams cb in Variants)
            Multi(data, sc, cb, pairs, sb);
        foreach (CbParams cb in Variants)
            foreach (long a in new long[] { 70_000, 150_000 })
                Criteria(data, sc, cb, a, pairs, sb);
        File.WriteAllText(Path.Combine(outDir, "print.txt"), sb.ToString());
        log.Write(sb.ToString());
        return 0;
    }

    private static void PrintVariant(GameData data, ScenarioData sc, CbParams cb, long a, List<(int Actor, int Proc)> pairs, StringBuilder sb)
    {
        var specs = new (string Name, Fx6 Rate, StandCondition C, CbEffort E)[]
        {
            ("25N", CultureRules.RNorm, CN, CbEffort.AI), ("5N", Fx6.FromInt(5), CN, CbEffort.AI),
            ("2L", Fx6.FromInt(2), CL, CbEffort.AI), ("2N", Fx6.FromInt(2), CN, CbEffort.AI),
            ("1.25N", CultureRules.RTok, CN, CbEffort.AI), ("1.25L", CultureRules.RTok, CL, CbEffort.AI),
            ("0+E", Fx6.Zero, CN, CbEffort.MAX), ("0", Fx6.Zero, CN, CbEffort.ZERO), ("0+AI", Fx6.Zero, CN, CbEffort.AI),
        };
        var res = new Res[pairs.Count, specs.Length];
        Parallel.For(0, pairs.Count * specs.Length, i =>
        {
            int pi = i / specs.Length, si = i % specs.Length;
            (int actor, int proc) = pairs[pi];
            var sp = specs[si];
            res[pi, si] = Run(data, sc, cb, a, DeltaMid, actor, proc, sp.Rate, sp.C, StartMod.Edge, sp.E, full: sp.Rate == Fx6.Zero);
        });
        sb.AppendLine($"=== {cb.Label} α {Fx6.FromRaw(a)} δ_H {Fx6.FromRaw(DeltaMid)} (edge start, D-1, 1200 months), pairs {pairs.Count}");
        var ok = Enumerable.Range(0, pairs.Count).Where(p => res[p, 0].Admitted && res[p, 0].T > 0).ToList();
        int notRun = Enumerable.Range(0, pairs.Count).Count(p => !res[p, 0].Admitted), t0 = Enumerable.Range(0, pairs.Count).Count(p => res[p, 0].Admitted && res[p, 0].T == 0);
        sb.AppendLine($"pairs with running norm line and T(25)>0: {ok.Count} (line never admitted, yield 0: {notRun}; mastered at start: {t0})");
        for (int si = 0; si < 6; si++)
        {
            var T = new List<double>();
            var ratio = new List<double>();
            var costR = new List<double>();
            var costPct = new List<double>();
            var partE = new List<double>();
            var partO = new List<double>();
            var absE = new List<double>();
            var nlm = new List<double>();
            int inf = 0, noLead = 0;
            foreach (int p in ok)
            {
                Res r = res[p, si], n = res[p, 0];
                if (r.NoLead > 0)
                    noLead++;
                nlm.Add(r.NoLead);
                if (r.T < 0)
                {
                    inf++;
                    continue;
                }
                T.Add(r.T);
                if (n.T > 0)
                {
                    ratio.Add((double)r.T / n.T);
                    costR.Add(r.Cost / n.Cost);
                }
                costPct.Add(100 * r.Cost / (12 * r.Budget));
                partE.Add(100 * r.CostE / r.Cost);
                partO.Add(100 * r.CostOut / r.Cost);
                absE.Add(100 * r.CostE / (12 * r.Budget));
            }
            sb.AppendLine($"  {specs[si].Name,-6} T мес {Dist(T)}; не освоили за 1200: {inf}/{ok.Count}; T/T25 {Dist(ratio, "0.##")}; цена/цена25 {Dist(costR, "0.##")}; цена % год. бюджета {Dist(costPct, "0.#")}; доля наладки % {F(Q(partE, 0.5), "0.#")}, выпуска % {F(Q(partO, 0.5), "0.#")}; наладка % год. бюджета {Dist(absE, "0.#")}; target без lead: {noLead} пар, мес. {Dist(nlm)}");
        }
        foreach (int si in new[] { 4, 5 })
        {
            var pct = new List<double>();
            foreach (int p in ok)
                if (res[p, si].T < 0)
                    pct.Add(100 * res[p, si].Cost / (12 * res[p, si].Budget));
            if (pct.Count > 0)
                sb.AppendLine($"  {specs[si].Name} без освоения: истрачено за 1200 мес., % год. бюджета {Dist(pct, "0.#")}");
        }
        for (int si = 6; si < 9; si++)
        {
            var c120 = new List<double>();
            var c600 = new List<double>();
            var c1200 = new List<double>();
            var gEnd = new List<double>();
            var spent = new List<double>();
            int mastered = 0;
            foreach (int p in ok)
            {
                Res r = res[p, si];
                c120.Add(100 * r.Closed120);
                c600.Add(100 * r.Closed600);
                c1200.Add(100 * r.Closed1200);
                gEnd.Add(r.GEnd);
                spent.Add(100 * r.Spent1200 / (12 * r.Budget));
                if (r.T >= 0)
                    mastered++;
            }
            sb.AppendLine($"  {specs[si].Name,-6} закрыто разрыва % через 120 {Dist(c120)}, 600 {Dist(c600)}, 1200 {Dist(c1200)}; g_H(1200) {Dist(gEnd, "0.###")}; освоили {mastered}; деньги за 1200 мес., % год. бюджета {Dist(spent, "0.#")}");
        }
        int gbr = sc.ActorIndex("GBR");
        int ex = pairs.FindIndex(x => x.Actor == gbr && x.Proc == 40);
        if (ex >= 0)
            sb.AppendLine($"  пример GBR×40: " + string.Join("; ", Enumerable.Range(0, 6).Select(si => $"{specs[si].Name} T {F(res[ex, si].T < 0 ? double.PositiveInfinity : res[ex, si].T)} цена {F(100 * res[ex, si].Cost / (12 * res[ex, si].Budget), "0.#")}%")));
    }

    /// <summary>(d) a cell with several processes: target at 25/month alone vs with the 3 easiest other processes of its cluster, also at 25/month.</summary>
    private static void Multi(GameData data, ScenarioData sc, CbParams cb, List<(int Actor, int Proc)> pairs, StringBuilder sb)
    {
        long a = 70_000;
        IndustryLayout l = new RuleStand(data, sc, 0, CultureRules.Params(a, DeltaMid, 100), Rv(cb), CultureRules.RNorm, 40, RunSet.Solo, StartMod.None, false).Layout;
        var res = new (Res Solo, Res Four, int K, string Others)[pairs.Count];
        Parallel.For(0, pairs.Count, i =>
        {
            (int actor, int proc) = pairs[i];
            int tr = l.RowOf(proc);
            int c = l.ClusterIndex[tr];
            var others = Enumerable.Range(0, l.ProcessCount)
                .Where(r => r != tr && l.ClusterIndex[r] == c && SmokeT4s.Labour.ContainsKey(l.Number[r]))
                .OrderByDescending(r => Enumerable.Range(0, 3).Count(d => l.Req[r][d] > Fx6.Zero && l.Req[tr][d] > Fx6.Zero) > 0)
                .ThenBy(r => Enumerable.Range(0, 3).Max(d => l.Req[r][d].Raw))
                .ThenBy(r => l.Number[r])
                .Take(3).Select(r => (l.Number[r], 0L)).ToArray();
            Res solo = Run(data, sc, cb, a, DeltaMid, actor, proc, CultureRules.RNorm, CN, StartMod.Edge, CbEffort.AI);
            Res four = Run(data, sc, cb, a, DeltaMid, actor, proc, CultureRules.RNorm, CN, StartMod.Edge, CbEffort.AI, extra: others);
            res[i] = (solo, four, others.Length, string.Join(',', others.Select(o => o.Item1)));
            if (four.T < 0 || (solo.T > 0 && four.T > 1.2 * solo.T))
                lock (sb)
                    sb.AppendLine($"  slow4 {cb.Name} {sc.ActorId[actor]}×{proc}: T1 {solo.T} T4 {four.T} noLead4 {four.NoLead} E4 {four.CostE:0.####} E1 {solo.CostE:0.####} D {sc.BudgetIndex[actor].ToDouble() * 0.1:0.####}");
        });
        var ratio = new List<double>();
        var t1 = new List<double>();
        var t4 = new List<double>();
        int inf4 = 0, n = 0, k3 = 0;
        foreach (var r in res)
        {
            if (!r.Solo.Admitted || r.Solo.T <= 0)
                continue;
            n++;
            if (r.K == 3)
                k3++;
            t1.Add(r.Solo.T);
            if (r.Four.T < 0)
            {
                inf4++;
                ratio.Add(double.PositiveInfinity);
                continue;
            }
            t4.Add(r.Four.T);
            ratio.Add((double)r.Four.T / r.Solo.T);
        }
        sb.AppendLine($"=== (d) {cb.Label} α 0.07 δ_H 0.003: 1 процесс vs цель + 3 лёгких процесса кластера (все 25/мес., AI), пар {n} (из них с 3 соседями {k3})");
        sb.AppendLine($"  T(1) {Dist(t1)}; T(4) {Dist(t4)}; не освоили при 4: {inf4}; T(4)/T(1) {Dist(ratio, "0.##")}");
        var byProc = res.Select((r, i) => (pairs[i].Proc, r.Others)).Distinct().Where(x => pairs.Any(p => p.Proc == x.Proc)).GroupBy(x => x.Proc).Select(g => $"{g.Key}:[{g.First().Others}]");
        sb.AppendLine($"  соседи: {string.Join(' ', byProc)}");
    }

    /// <summary>The stand's pre-registered criteria of §1.5 (K2a #105 with F-1, K2b′ time and cost, HK8 a/b/c), information only.</summary>
    private static void Criteria(GameData data, ScenarioData sc, CbParams cb, long a, List<(int Actor, int Proc)> pairs, StringBuilder sb)
    {
        const long d17 = 1_700, d43 = 4_300;
        var k2aConds = new[] { CN, CW, CA75, CA50, CL };
        var k2bConds = new[] { CN, CW, CA75, CA50, CL, CLW };
        var modes = new[] { CbEffort.AI, CbEffort.MAX };
        int k2aN = 0, k2bTN = 0, k2bCN = 0, hk8N = 0, k2aExcl = 0;
        var k2aF = new int[2];
        var k2bTF = new int[2];
        var k2bCF = new int[2];
        int hk8a = 0, hk8b = 0, hk8c = 0;
        var k2aG = new List<double>();
        var hkQ = new List<double>();
        var costQ = new List<double>();
        var lockObj = new object();
        Parallel.For(0, pairs.Count, i =>
        {
            (int actor, int proc) = pairs[i];
            int ln = 0, ltN = 0, lcN = 0, lh = 0, lex = 0, la = 0, lb = 0, lc = 0;
            var kf = new int[2];
            var tf = new int[2];
            var cf = new int[2];
            var g1 = new List<double>();
            var q1 = new List<double>();
            var c1 = new List<double>();
            Res ctrl = Run(data, sc, cb, a, d17, actor, proc, Fx6.Zero, CN, StartMod.Hold, CbEffort.ZERO, full: true);
            if (ctrl.GEnd < 0.9)
            {
                double gc = ctrl.GEnd;
                foreach (StandCondition cnd in k2aConds)
                    for (int m = 0; m < 2; m++)
                    {
                        Res r = Run(data, sc, cb, a, d17, actor, proc, CultureRules.RTok, cnd, StartMod.Hold, modes[m], full: true);
                        if (!r.Admitted)
                        {
                            lex++;
                            continue;
                        }
                        if (m == 0)
                            ln++;
                        double rel = (r.GEnd - gc) / (1 - gc);
                        if (m == 0)
                            g1.Add(rel);
                        if (rel >= 0.9)
                            kf[m]++;
                    }
            }
            else
            {
                lex += 10;
            }
            foreach (StandCondition cnd in k2bConds)
                for (int m = 0; m < 2; m++)
                {
                    Res nn = Run(data, sc, cb, a, d17, actor, proc, CultureRules.RNorm, cnd, StartMod.Edge, modes[m]);
                    Res tk = Run(data, sc, cb, a, d17, actor, proc, CultureRules.RTok, cnd, StartMod.Edge, modes[m]);
                    if (!nn.Admitted || nn.G < 0.05)
                        continue;
                    if (nn.THalf > 0)
                    {
                        if (m == 0)
                            ltN++;
                        if (tk.THalf > 0 && tk.THalf < 2.0 * nn.THalf)
                            tf[m]++;
                    }
                    if (nn.T > 0)
                    {
                        if (m == 0)
                            lcN++;
                        if (tk.T >= 0)
                        {
                            double q = tk.Cost / nn.Cost;
                            if (m == 0)
                                c1.Add(q);
                            if (q < 1.5)
                                cf[m]++;
                        }
                    }
                }
            Res n25 = Run(data, sc, cb, a, d43, actor, proc, CultureRules.RNorm, CN, StartMod.Edge, CbEffort.AI);
            if (n25.Admitted && n25.T > 0)
            {
                lh++;
                Res s2 = Run(data, sc, cb, a, d43, actor, proc, Fx6.FromInt(2), CL, StartMod.Edge, CbEffort.AI);
                Res l25 = Run(data, sc, cb, a, d43, actor, proc, CultureRules.RNorm, CL, StartMod.Edge, CbEffort.AI);
                if (s2.T < 0)
                {
                    la++;
                    q1.Add(double.PositiveInfinity);
                }
                else
                {
                    q1.Add((double)s2.T / n25.T);
                    if (s2.T > 3 * n25.T)
                        lb++;
                    if (s2.T < l25.T - 1)
                        lc++;
                }
            }
            lock (lockObj)
            {
                k2aN += ln; k2bTN += ltN; k2bCN += lcN; hk8N += lh; k2aExcl += lex; hk8a += la; hk8b += lb; hk8c += lc;
                for (int m = 0; m < 2; m++)
                {
                    k2aF[m] += kf[m];
                    k2bTF[m] += tf[m];
                    k2bCF[m] += cf[m];
                }
                k2aG.AddRange(g1);
                hkQ.AddRange(q1);
                costQ.AddRange(c1);
            }
        });
        sb.AppendLine($"=== criteria (information only) {cb.Label} α {Fx6.FromRaw(a)}: K2a δ0.0017 (#105, F-1) AI {k2aF[0]} / MAX {k2aF[1]} из {k2aN} (исключено {k2aExcl} прогонов), мера #105 {Dist(k2aG, "0.##")}; "
            + $"K2b′ время AI {k2bTF[0]} / MAX {k2bTF[1]} из {k2bTN}; цена AI {k2bCF[0]} / MAX {k2bCF[1]} из {k2bCN}, Cost(1.25)/Cost(25) AI {Dist(costQ, "0.##")}; "
            + $"HK8 δ0.0043 (a) {hk8a} (b) {hk8b} (c) {hk8c} из {hk8N}, T(2,L)/T(25,N) {Dist(hkQ, "0.##")}");
    }
}
