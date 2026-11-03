using System.Globalization;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Kernel;

namespace Singularity.Lab.Smoke;

/// <summary>
/// Gate 2026-11-03 tasks for R1c (culture-rule-selection.md §7): G-1 α window by C1′ and C2′ prints, G-2 low rates,
/// G-3 "only D-1" column, G-5 floor fix, G-7 y_ser sensitivity. No criterion threshold changes. Doubles only in the
/// analysis of finished runs; every stand is deterministic (no RNG).
/// </summary>
internal static class CultureGate
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Serial admission threshold <c>y_ser</c> of C2′/C1′ (decision #87, [Предп.]).</summary>
    public static readonly Fx6 YSer = Fx6.FromRaw(250_000);

    private static readonly Fx6 Y07 = Fx6.FromRaw(700_000);
    private const int C1Horizon = 1024;

    private static RuleVariant[] Candidates() =>
    [
        new(CultureRule.R1c, Fx6.FromRaw(6_250_000), false, Fx6.FromRaw(500_000), true),
        new(CultureRule.R1c, Fx6.FromRaw(6_250_000), false, Fx6.FromRaw(250_000), true),
    ];

    public static int Run(GameData data, ScenarioData sc, string outDir, TextWriter log)
    {
        outDir = Path.Combine(outDir, "gate");
        Directory.CreateDirectory(outDir);
        RuleVariant[] cand = Candidates();

        // G-5: the floor fix must not change the trajectory (only the raw value of maint).
        if (!FloorFixSame(data, sc, log))
            return 1;

        // G-1: C1′ window (independent of r_full/u_m at rate 25 ≥ r_full; printed for the main candidate).
        log.WriteLine("--- G-1 K0 by C1′ (doublings from t_ser, only months with min yield ≥ y_ser; threshold 0.409)");
        foreach (long a in CultureRules.Alphas)
        {
            K0PrimePoint pt = K0PrimeAt(data, sc, cand[0], a, YSer);
            log.WriteLine($"K0′ α{Fx6.FromRaw(a)}: max d_y {F(pt.MaxDy)} (by doubling {string.Join('/', pt.MaxByDoubling.Select(F))}), t_ser {pt.TSerMin}–{pt.TSerMax}, "
                + $"combos without t_ser {pt.NoSer}, doublings skipped (y < y_ser) {pt.Skipped}, incomplete {pt.Incomplete}; old C1 max d_y {F(pt.OldMaxDy)}");
        }

        // G-3 / G-5 / G-1: criteria on the C1′ window in four measurement modes.
        var modes = new (string Name, CultureRules.EvalMode Mode)[]
        {
            ("literal", new(false, false, false, false, true)),
            ("onlyD1", new(true, false, false, false, true, true)),
            ("D1-D4-noD3", new(true, false, false, true, true, true)),
            ("D1-D4", new(true, true, true, true, true)),
        };
        var jobs = new List<(int V, int M)>();
        for (int v = 0; v < cand.Length; v++)
            for (int m = 0; m < modes.Length; m++)
                jobs.Add((v, m));
        var res = new CultureRules.VariantResult[jobs.Count];
        Parallel.For(0, jobs.Count, i => res[i] = CultureRules.Evaluate(data, sc, cand[jobs[i].V], modes[jobs[i].M].Mode));
        for (int m = 0; m < modes.Length; m++)
        {
            string dir = Path.Combine(outDir, modes[m].Name);
            Directory.CreateDirectory(dir);
            log.WriteLine($"--- mode {modes[m].Name} {modes[m].Mode}");
            var vs = new List<RuleVariant>();
            var rs = new List<CultureRules.VariantResult>();
            for (int i = 0; i < jobs.Count; i++)
            {
                if (jobs[i].M != m)
                    continue;
                vs.Add(cand[jobs[i].V]);
                rs.Add(res[i]);
            }
            CultureRules.Write(dir, vs, [.. rs], log);
            for (int i = 0; i < vs.Count; i++)
                log.WriteLine($"  {vs[i].Label}: K6 literal fails {rs[i].K6Fail} (min g {F(rs[i].K6MinG)}), without month 0 {rs[i].K6FailSkip0} (min g {F(rs[i].K6MinGSkip0)}), per-dim {rs[i].K6DimFail}; "
                    + $"I5 raw {rs[i].K1Fail[4]}, I5 eff {rs[i].K1Fail[6]}, raw maint jump {F(rs[i].K1MaxJump)}, eff {F(rs[i].K1MaxJumpEff)}");
        }
        List<long> window = res[0].Window;
        log.WriteLine($"C1′ window: [{string.Join(' ', window.Select(a => Fx6.FromRaw(a).ToString()))}]");

        // G-1 / G-7: C2′ and t_first → t_ser on the new window.
        log.WriteLine("--- G-1 C2′ (t_ser → t_07) and t_first → t_ser, years: p10/p50/p90 (n, no t_first, no t_ser); horizon 384, ∞ = censored");
        foreach (bool free in new[] { false, true })
            foreach (long a in window)
                foreach (long dh in CultureRules.Deltas)
                    foreach (long ys in new long[] { 250_000, 150_000, 350_000 })
                    {
                        if (ys != 250_000 && dh != 3_000)
                            continue; // G-7 sensitivity: main δ_H only
                        foreach (string start in new[] { "edge", "cluster" })
                            log.WriteLine($"C2′ {(free ? "D-1" : "real")} α{Fx6.FromRaw(a)} δ{Fx6.FromRaw(dh)} y_ser {Fx6.FromRaw(ys)} {start}: {C2Prime(data, sc, cand[0], a, dh, Fx6.FromRaw(ys), free, start)}");
                    }

        // G-2: K4c and D2 at low rates.
        LowRates(data, sc, cand[0], window, log);
        return 0;
    }

    private static string F(double x) => double.IsNaN(x) ? "-" : double.IsPositiveInfinity(x) ? "inf" : x.ToString("0.###", Inv);

    // ------------------------------------------------------------------ G-5 check

    private static bool FloorFixSame(GameData data, ScenarioData sc, TextWriter log)
    {
        int cases = 0, bad = 0;
        foreach (long um in new long[] { 500_000, 250_000 })
        {
            var plain = new RuleVariant(CultureRule.R1c, Fx6.FromRaw(6_250_000), false, Fx6.FromRaw(um));
            RuleVariant fix = plain with { FloorFix = true };
            foreach (bool free in new[] { false, true })
            {
                RuleStand.FreeTooling.Value = free;
                foreach (long a in new long[] { 20_000, 80_000 })
                {
                    IndustryParams ip = CultureRules.Params(a, 3_000, 100);
                    var configs = new List<Func<RuleVariant, RuleStand>>();
                    for (int actor = 0; actor < sc.ActorCount; actor++)
                    {
                        int ac = actor;
                        foreach ((int proc, int _) in SmokeT4s.KeyProcesses)
                        {
                            int pr = proc;
                            configs.Add(rv => new RuleStand(data, sc, ac, ip, rv, Fx6.FromRaw(1_250_000), pr, RunSet.Solo, StartMod.Hold, false));
                            configs.Add(rv => new RuleStand(data, sc, ac, ip, rv, Fx6.FromInt(5), pr, RunSet.Solo, StartMod.Edge, false));
                            configs.Add(rv => new RuleStand(data, sc, ac, ip, rv, Fx6.FromInt(25), pr, RunSet.Cluster, StartMod.Edge, false));
                        }
                        configs.Add(rv => new RuleStand(data, sc, ac, ip, rv, Fx6.FromInt(25), -1, RunSet.Mastered, StartMod.None, true));
                        configs.Add(rv => new RuleStand(data, sc, ac, ip, rv, Fx6.FromInt(2), -1, RunSet.Mastered, StartMod.None, true));
                    }
                    foreach (Func<RuleVariant, RuleStand> make in configs)
                    {
                        RuleStand s1 = make(plain), s2 = make(fix);
                        cases++;
                        for (int t = 0; t < 384; t++)
                        {
                            s1.Step();
                            s2.Step();
                            if (!s1.H.AsSpan().SequenceEqual(s2.H) || !s1.M.AsSpan().SequenceEqual(s2.M) || !s1.X.AsSpan().SequenceEqual(s2.X))
                            {
                                bad++;
                                break;
                            }
                        }
                    }
                }
            }
        }
        RuleStand.FreeTooling.Value = false;
        log.WriteLine($"G-5 check: R1c with and without the floor fix, {cases} cases × 384 months, trajectory mismatches {bad}");
        return bad == 0;
    }

    // ------------------------------------------------------------------ G-1 K0 by C1′

    internal sealed record K0PrimePoint(double MaxDy, double[] MaxByDoubling, int TSerMin, int TSerMax, int NoSer, int Skipped, int Incomplete, double OldMaxDy);

    /// <summary>
    /// C1′ (anchors_review.md §4 p.2, decision #87): doublings of the line's own output counted from its output at
    /// <c>t_ser</c> (first month with min yield of the set ≥ y_ser and every process reporting); a doubling counts
    /// only if every month in it has min yield ≥ y_ser. The month convention is that of K0: <c>u[m]</c> is the snapshot
    /// of month m and goes with the output <c>n(m)</c> of months ≤ m.
    /// </summary>
    internal static K0PrimePoint K0PrimeAt(GameData data, ScenarioData sc, RuleVariant rv, long a, Fx6 ySer)
    {
        int gbr = sc.ActorIndex("GBR");
        var byD = new double[3];
        double worst = 0, oldWorst = 0;
        int tMin = int.MaxValue, tMax = -1, noSer = 0, skipped = 0, incomplete = 0;
        foreach (long dh in CultureRules.Deltas)
            foreach (int x in new[] { 50, 100, 200 })
                foreach (bool rhms in new[] { false, true })
                {
                    var s = new RuleStand(data, sc, gbr, CultureRules.Params(a, dh, x), rv, Fx6.FromInt(25), -1, RunSet.PrcSet, StartMod.None, rhms, SmokeT4s.G2Superior);
                    (double[] u, double[] minY, int[] n) = Line(s, C1Horizon);
                    for (int i = 0; i < 3; i++)
                        oldWorst = Math.Max(oldWorst, Math.Log(u[(1 << i) - 1]) - Math.Log(u[(2 << i) - 1]));
                    int tSer = -1;
                    for (int t = 0; t < C1Horizon; t++)
                    {
                        if (minY[t] >= ySer.ToDouble() && n[t] > (t > 0 ? n[t - 1] : 0))
                        {
                            tSer = t;
                            break;
                        }
                    }
                    if (tSer < 0)
                    {
                        noSer++;
                        continue;
                    }
                    tMin = Math.Min(tMin, tSer);
                    tMax = Math.Max(tMax, tSer);
                    int from = tSer, measured = 0;
                    for (int k = 1; k <= 3; k++)
                    {
                        long goal = (long)n[tSer] << k;
                        int to = from;
                        while (to < C1Horizon && n[to] < goal)
                            to++;
                        if (to >= C1Horizon)
                            break;
                        bool ok = true;
                        for (int t = from; t <= to; t++)
                            ok &= minY[t] >= ySer.ToDouble();
                        if (ok)
                        {
                            double dy = Math.Log(u[from]) - Math.Log(u[to]);
                            byD[k - 1] = Math.Max(byD[k - 1], dy);
                            worst = Math.Max(worst, dy);
                            measured++;
                        }
                        else
                        {
                            skipped++;
                        }
                        from = to;
                    }
                    if (measured < 3)
                        incomplete++;
                }
        return new K0PrimePoint(worst, byD, tMin == int.MaxValue ? -1 : tMin, tMax, noSer, skipped, incomplete, oldWorst);
    }

    /// <summary>K0 by C1′: α is in the window if max d_y ≤ 0.409 over all combos and every combo reaches t_ser.</summary>
    internal static void K0Prime(GameData data, ScenarioData sc, RuleVariant rv, CultureRules.VariantResult res, Fx6 ySer)
    {
        foreach (long a in CultureRules.Alphas)
        {
            K0PrimePoint pt = K0PrimeAt(data, sc, rv, a, ySer);
            res.C1MaxDy[a] = pt.MaxDy;
            if (pt.NoSer == 0 && pt.MaxDy <= 0.409)
                res.Window.Add(a);
        }
    }

    /// <summary>Per month: unit_cost_mult and min yield of the G2 set (snapshot of month m), output months n(m).</summary>
    private static (double[] U, double[] MinY, int[] N) Line(RuleStand s, int months)
    {
        var u = new double[months];
        var minY = new double[months];
        var n = new int[months];
        IndustryLayout l = s.Layout;
        var items = new ProfileItem[SmokeT4s.G2Superior.Length];
        int count = 0;
        for (int t = 0; t < months; t++)
        {
            s.Step();
            Fx6 mn = Fx6.One;
            bool all = true;
            for (int i = 0; i < items.Length; i++)
            {
                (int num, long baseRaw) = SmokeT4s.G2Superior[i];
                int r = l.RowOf(num);
                ProcessYield y = s.Y[r];
                items[i] = new ProfileItem(num, y, true, Fx6.One, Fx6.FromRaw(baseRaw));
                if (y.Yield < mn)
                    mn = y.Yield;
                all &= s.V[r] > Fx6.Zero;
            }
            if (all)
                count++;
            u[t] = IndustryMath.Profile(items, Fx6.FromRaw(10_000)).UnitCostMult.ToDouble();
            minY[t] = mn.ToDouble();
            n[t] = count;
        }
        return (u, minY, n);
    }

    // ------------------------------------------------------------------ G-1 / G-7 C2′

    private static string C2Prime(GameData data, ScenarioData sc, RuleVariant rv, long a, long dh, Fx6 ySer, bool free, string start)
    {
        IndustryParams ip = CultureRules.Params(a, dh, 100);
        RuleStand.FreeTooling.Value = free;
        var ser = new Dictionary<int, List<double>> { [4] = new(), [5] = new() };
        var pre = new Dictionary<int, List<double>> { [4] = new(), [5] = new() };
        var noFirst = new Dictionary<int, int> { [4] = 0, [5] = 0 };
        var noSer = new Dictionary<int, int> { [4] = 0, [5] = 0 };
        foreach ((int proc, int tier) in SmokeT4s.KeyProcesses)
        {
            for (int actor = 0; actor < sc.ActorCount; actor++)
            {
                if (SmokeT4s.IsMastered(data, sc, actor, proc))
                    continue;
                RuleStand s = start == "edge"
                    ? new RuleStand(data, sc, actor, ip, rv, Fx6.FromInt(25), proc, RunSet.Solo, StartMod.Edge, false)
                    : new RuleStand(data, sc, actor, ip, rv, Fx6.FromInt(25), proc, RunSet.Cluster, StartMod.None, false);
                long first = -1, tSer = -1, t07 = -1;
                for (long t = 0; t < CultureRules.Horizon; t++)
                {
                    s.Step();
                    bool v = s.V[s.Target] > Fx6.Zero;
                    Fx6 y = s.Y[s.Target].Yield;
                    if (first < 0 && v)
                        first = t;
                    if (tSer < 0 && v && y >= ySer)
                        tSer = t;
                    if (t07 < 0 && y >= Y07)
                    {
                        t07 = t;
                        break;
                    }
                }
                if (first < 0)
                {
                    noFirst[tier]++;
                    continue;
                }
                pre[tier].Add(tSer >= 0 ? (tSer - first) / 12.0 : double.PositiveInfinity);
                if (tSer < 0)
                    noSer[tier]++;
                else
                    ser[tier].Add(t07 >= 0 ? (t07 - tSer) / 12.0 : double.PositiveInfinity);
            }
        }
        RuleStand.FreeTooling.Value = false;
        string Q(List<double> xs) => xs.Count == 0 ? "-" : $"{F(P(xs, 0.1))}/{F(P(xs, 0.5))}/{F(P(xs, 0.9))}";
        return string.Join(" | ", new[] { 4, 5 }.Select(tr =>
            $"t{tr} C2′ {Q(ser[tr])} pre {Q(pre[tr])} (n {pre[tr].Count}, no t_first {noFirst[tr]}, no t_ser {noSer[tr]})"));
    }

    /// <summary>Lower quantile s[floor(q·(n−1))], as the lower median of spec §8.2; +∞ sorts last.</summary>
    private static double P(List<double> xs, double q)
    {
        double[] s = [.. xs.Order()];
        return s[(int)Math.Floor(q * (s.Length - 1))];
    }

    // ------------------------------------------------------------------ G-2 low rates

    private static void LowRates(GameData data, ScenarioData sc, RuleVariant rv, List<long> window, TextWriter log)
    {
        Fx6[] rates = [Fx6.FromInt(2), Fx6.FromRaw(2_500_000), Fx6.FromRaw(3_125_000), Fx6.FromInt(5), Fx6.FromInt(10), Fx6.FromInt(25)];
        foreach (bool free in new[] { true, false })
        {
            var tasks = new List<(long A, long Dh, int Actor, int Proc)>();
            foreach (long a in window)
                foreach (long dh in CultureRules.Deltas)
                    for (int actor = 0; actor < sc.ActorCount; actor++)
                        foreach ((int proc, int _) in SmokeT4s.KeyProcesses)
                            tasks.Add((a, dh, actor, proc));
            var T = new double[tasks.Count][];
            Parallel.For(0, tasks.Count, i =>
            {
                RuleStand.FreeTooling.Value = free;
                (long a, long dh, int actor, int proc) = tasks[i];
                IndustryParams ip = CultureRules.Params(a, dh, 100);
                var row = new double[rates.Length];
                bool started = true;
                for (int k = 0; k < rates.Length; k++)
                {
                    (int t, bool st) = CultureRules.TimeToG(new RuleStand(data, sc, actor, ip, rv, rates[k], proc, RunSet.Solo, StartMod.Edge, false), 1200);
                    row[k] = t < 0 ? double.PositiveInfinity : t;
                    if (k == rates.Length - 1)
                        started = st;
                }
                T[i] = started ? row : [];
                RuleStand.FreeTooling.Value = false;
            });
            log.WriteLine($"--- G-2 K4c at low rates ({(free ? "D-1" : "real tooling")}, horizon 1200, edge, solo; pairs with T(25) finite)");
            for (int k = 0; k < rates.Length - 1; k++)
            {
                var ratios = new List<double>();
                int n = 0, fail2 = 0, fail15 = 0, inf = 0;
                string worst = "";
                double w = 0;
                for (int i = 0; i < tasks.Count; i++)
                {
                    if (T[i].Length == 0 || double.IsInfinity(T[i][^1]))
                        continue;
                    n++;
                    double t25 = T[i][^1], tr = T[i][k];
                    double ratio = t25 == 0 ? (tr == 0 ? 1 : double.PositiveInfinity) : tr / t25;
                    ratios.Add(ratio);
                    if (double.IsInfinity(tr))
                        inf++;
                    if (!(tr <= 2 * t25))
                        fail2++;
                    if (!(tr <= 1.5 * t25))
                        fail15++;
                    if (ratio > w)
                    {
                        w = ratio;
                        worst = $"{sc.ActorId[tasks[i].Actor]}-{tasks[i].Proc} α{Fx6.FromRaw(tasks[i].A)} δ{Fx6.FromRaw(tasks[i].Dh)} T {F(tr)}/{F(t25)}";
                    }
                }
                log.WriteLine($"rate {rates[k]}: n {n}, T(r) > 2·T(25) {fail2}, > 1.5·T(25) {fail15}, never {inf}; ratio p50 {F(ratios.Count > 0 ? P(ratios, 0.5) : double.NaN)} p90 {F(ratios.Count > 0 ? P(ratios, 0.9) : double.NaN)} max {F(w)} at {worst}");
            }

            log.WriteLine($"--- G-2 D2: K6 stand (mastered set, R-H-MS) at low rates ({(free ? "D-1" : "real tooling")}); min g_H without month 0, min H − r over actors × window × δ");
            foreach (Fx6 rate in rates)
            {
                double minG = 1, minGap = 10;
                string at = "";
                foreach (long a in window)
                    foreach (long dh in CultureRules.Deltas)
                        for (int actor = 0; actor < sc.ActorCount; actor++)
                        {
                            RuleStand.FreeTooling.Value = free;
                            var s = new RuleStand(data, sc, actor, CultureRules.Params(a, dh, 100), rv, rate, -1, RunSet.Mastered, StartMod.None, true);
                            RuleStand.FreeTooling.Value = false;
                            for (int t = 0; t < CultureRules.Horizon; t++)
                            {
                                s.Step();
                                for (int r = 0; r < s.Layout.ProcessCount; r++)
                                {
                                    if (!s.Running(r))
                                        continue;
                                    double g = s.GH(r).ToDouble();
                                    if (t > 0 && g < minG)
                                    {
                                        minG = g;
                                        at = $"{sc.ActorId[actor]} p{s.Layout.Number[r]} α{Fx6.FromRaw(a)} δ{Fx6.FromRaw(dh)} m{t}";
                                    }
                                    int c = s.Layout.ClusterIndex[r];
                                    for (int d = 0; d < 3; d++)
                                    {
                                        Fx6 req = s.Layout.Req[r][d];
                                        if (req > Fx6.Zero)
                                            minGap = Math.Min(minGap, (s.H[c * 3 + d] - req).ToDouble());
                                    }
                                }
                            }
                        }
                log.WriteLine($"rate {rate}: min g_H {F(minG)} at {at}; min H − r {F(minGap)}");
            }
        }
    }
}
