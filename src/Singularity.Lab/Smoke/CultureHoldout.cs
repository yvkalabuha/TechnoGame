using System.Globalization;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Kernel;

namespace Singularity.Lab.Smoke;

/// <summary>
/// Several sites stepped in one month (hold-out §8.2 (c)). Before each step <c>world_lead(p, owner(s))</c> is
/// computed for every site from the snapshot of month t−1 (contract §4.1): another actor's site reported p with
/// <c>proc_v &gt; 0</c> and <c>proc_yield ≥ 0.7</c>. Own sites of the actor never count. Sites share nothing else:
/// own cells, own tooling budget, spill only inside a site.
/// </summary>
internal sealed class HoldoutWorld
{
    private static readonly Fx6 Y07 = Fx6.FromRaw(700_000);
    private readonly bool[][] _lead;

    public HoldoutWorld(RuleStand[] sites, int[] owner)
    {
        Sites = sites;
        Owner = owner;
        _lead = new bool[sites.Length][];
        for (int s = 0; s < sites.Length; s++)
            _lead[s] = new bool[sites[s].Layout.ProcessCount];
    }

    public RuleStand[] Sites { get; }

    public int[] Owner { get; }

    /// <summary>(site, month) pairs in which at least one process of the site had world_lead = 1.</summary>
    public int LeadSiteMonths { get; private set; }

    public void Step()
    {
        for (int s = 0; s < Sites.Length; s++)
        {
            bool any = false;
            for (int r = 0; r < _lead[s].Length; r++)
            {
                bool lead = false;
                for (int s2 = 0; s2 < Sites.Length; s2++)
                {
                    if (Owner[s2] != Owner[s] && Sites[s2].V[r] > Fx6.Zero && Sites[s2].Y[r].Yield >= Y07)
                        lead = true;
                }
                _lead[s][r] = lead;
                any |= lead;
            }
            if (any)
                LeadSiteMonths++;
        }
        for (int s = 0; s < Sites.Length; s++)
        {
            Array.Copy(_lead[s], Sites[s].WorldLead, _lead[s].Length);
            Sites[s].Step();
        }
    }
}

/// <summary>
/// Hold-out of R1c (culture-rule-selection.md §8, pre-registered in commit 607778b): rival (world_lead), two sites,
/// act &lt; 1, new process pairs. The rule is fixed (R1c, r_full 6.25, u_m 0.5, G-5 floor fix); nothing is tuned.
/// Doubles only in the analysis of finished runs; every stand is deterministic (K1 uses the SplitMix of §1.3).
/// </summary>
internal static class CultureHoldout
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly RuleVariant Rule = new(CultureRule.R1c, Fx6.FromRaw(6_250_000), false, Fx6.FromRaw(500_000), true);
    private static readonly int[] PairsA = [40, 49, 41, 51, 52];
    private static readonly int[] PairsB = [38, 44, 45, 50, 53, 54, 55, 57];
    private static readonly Fx6 Tol01 = Fx6.FromRaw(100_000);

    private sealed record Cond(string Name, StandCondition C);

    private static readonly Cond N = new("N", StandCondition.Default);
    private static readonly Cond W = new("W", new StandCondition(Fx6.One, LeadMode.Target));
    private static readonly Cond WM = new("W", new StandCondition(Fx6.One, LeadMode.Mastered));
    private static readonly Cond A75 = new("A75", new StandCondition(Fx6.FromRaw(750_000), LeadMode.None));
    private static readonly Cond A50 = new("A50", new StandCondition(Fx6.FromRaw(500_000), LeadMode.None));

    public static int Run(GameData data, ScenarioData sc, string outDir, TextWriter log)
    {
        outDir = Path.Combine(outDir, "holdout");
        Directory.CreateDirectory(outDir);
        log.WriteLine($"holdout rule {Rule.Label}; grid α {string.Join(' ', CultureRules.Alphas.Select(a => Fx6.FromRaw(a).ToString()))} × δ_H {string.Join(' ', CultureRules.Deltas.Select(d => Fx6.FromRaw(d).ToString()))}");
        if (!CheckStand(data, sc, log))
            return 1;

        bool pass = true;
        pass &= HK1(log);
        pass &= HK24(data, sc, log, outDir);
        pass &= HK6(data, sc, log);
        pass &= HK7(data, sc, log);
        Split(data, sc, log);
        Rival(data, sc, log);
        log.WriteLine($"HOLDOUT {(pass ? "PASS" : "FAIL")} (HK1, HK2, HK4, HK6, HK7 with D-1, D-3, D-4)");
        return 0;
    }

    private static string F(double x) => double.IsNaN(x) ? "-" : double.IsPositiveInfinity(x) ? "inf" : x.ToString("0.###", Inv);

    private static IEnumerable<(int Actor, int Proc, char Set)> Pairs(ScenarioData sc, bool a, bool b)
    {
        for (int actor = 0; actor < sc.ActorCount; actor++)
        {
            if (a)
                foreach (int p in PairsA)
                    yield return (actor, p, 'A');
            if (b)
                foreach (int p in PairsB)
                    yield return (actor, p, 'B');
        }
    }

    private static RuleStand Make(GameData data, ScenarioData sc, int actor, IndustryParams ip, Fx6 rate, int target, RunSet run, StartMod mod, bool rhms,
        StandCondition cond, bool free)
    {
        RuleStand.FreeTooling.Value = free;
        RuleStand.Condition.Value = cond;
        try
        {
            return new RuleStand(data, sc, actor, ip, Rule, rate, target, run, mod, rhms);
        }
        finally
        {
            RuleStand.FreeTooling.Value = false;
            RuleStand.Condition.Value = StandCondition.Default;
        }
    }

    // ------------------------------------------------------------------ prototype check (§8.2)

    /// <summary>
    /// world_lead ≡ 0, act = 1, one site: the hold-out world (explicit condition, world-computed world_lead) equals
    /// the plain <see cref="RuleStand"/> of §7 bit for bit (H, M, proc_X, proc_v). R0 vs the core is checked by
    /// <see cref="CultureRules.Check"/> before this.
    /// </summary>
    private static bool CheckStand(GameData data, ScenarioData sc, TextWriter log)
    {
        var jobs = new List<(long A, bool Free, int Actor, int Proc, int Kind)>();
        foreach (long a in new long[] { 20_000, 80_000 })
            foreach (bool free in new[] { false, true })
                for (int actor = 0; actor < sc.ActorCount; actor++)
                {
                    foreach (int p in PairsA.Concat(PairsB))
                        for (int k = 0; k < 3; k++)
                            jobs.Add((a, free, actor, p, k));
                    jobs.Add((a, free, actor, -1, 3));
                }
        var bad = new int[jobs.Count];
        Parallel.For(0, jobs.Count, i =>
        {
            (long a, bool free, int actor, int p, int kind) = jobs[i];
            IndustryParams ip = CultureRules.Params(a, 3_000, 100);
            (Fx6 rate, RunSet run, StartMod mod, bool rhms) = kind switch
            {
                0 => (CultureRules.RTok, RunSet.Solo, StartMod.Hold, false),
                1 => (Fx6.FromInt(5), RunSet.Solo, StartMod.Edge, false),
                2 => (Fx6.FromInt(25), RunSet.Cluster, StartMod.Edge, false),
                _ => (Fx6.FromInt(25), RunSet.Mastered, StartMod.None, true),
            };
            RuleStand.FreeTooling.Value = free;
            var plain = new RuleStand(data, sc, actor, ip, Rule, rate, p, run, mod, rhms);
            RuleStand.FreeTooling.Value = false;
            var world = new HoldoutWorld([Make(data, sc, actor, ip, rate, p, run, mod, rhms, new StandCondition(Fx6.One, LeadMode.None), free)], [actor]);
            RuleStand s = world.Sites[0];
            for (int t = 0; t < 384; t++)
            {
                plain.Step();
                world.Step();
                if (!plain.H.AsSpan().SequenceEqual(s.H) || !plain.M.AsSpan().SequenceEqual(s.M) || !plain.X.AsSpan().SequenceEqual(s.X) || !plain.V.AsSpan().SequenceEqual(s.V))
                {
                    bad[i] = 1;
                    break;
                }
            }
            if (world.LeadSiteMonths != 0)
                bad[i] = 1;
        });
        log.WriteLine($"check: hold-out world (1 site, act 1, world_lead 0) vs RuleStand R1c/G-5, {jobs.Count} cases × 384 months, mismatches {bad.Sum()}");
        return bad.Sum() == 0;
    }

    // ------------------------------------------------------------------ HK1

    private static bool HK1(TextWriter log)
    {
        var res = new CultureRules.VariantResult();
        CultureRules.K1(Rule, res, randomLead: true);
        bool ok = res.K1Fail[0] == 0 && res.K1Fail[1] == 0 && res.K1Fail[2] == 0 && res.K1Fail[3] == 0 && res.K1Fail[4] == 0 && res.K1Fail[5] == 0 && res.K1Fail[7] == 0;
        log.WriteLine($"HK1 {(ok ? "pass" : "FAIL")}: I1 {res.K1Fail[0]} I2 {res.K1Fail[1]} I3 {res.K1Fail[2]} I4 {res.K1Fail[3]} I5 (literal) {res.K1Fail[4]} continuity {res.K1Fail[5]} (max maint jump {F(res.K1MaxJump)}, threshold 0.05) I5eff {res.K1Fail[6]} I6 {res.K1Fail[7]}; 20000 cases, seed 79080, world_lead ½");
        return ok;
    }

    // ------------------------------------------------------------------ HK2, HK4 (and K3, printed)

    private sealed class Agg
    {
        public int K2aN, K2aFail, K2bN, K2bFail, K4N, K4a, K4b, K4c, K3N, K3Fail, Excluded;
        public double K2aWorstG = double.NaN, K2bWorst = double.NaN, T5 = double.NaN, T10 = double.NaN;
        public string K2aCase = "", K2bCase = "", T5Case = "";
        public readonly List<string> Fails = new();

        public void Add(CultureRules.VariantResult r)
        {
            K2aN += r.K2aN; K2aFail += r.K2aFail; K2bN += r.K2bN; K2bFail += r.K2bFail; Excluded += r.Excluded;
            K4N += r.K4N; K4a += r.K4aFail; K4b += r.K4bFail; K4c += r.K4cFail; K3N += r.K3N; K3Fail += r.K3Fail;
            if (!double.IsNaN(r.K2aWorstG) && (double.IsNaN(K2aWorstG) || r.K2aWorstG > K2aWorstG))
                (K2aWorstG, K2aCase) = (r.K2aWorstG, r.K2aWorstCase);
            if (!double.IsNaN(r.K2bWorstRatio) && (double.IsNaN(K2bWorst) || r.K2bWorstRatio > K2bWorst))
                (K2bWorst, K2bCase) = (r.K2bWorstRatio, r.K2bWorstCase);
            if (!double.IsNaN(r.K4WorstT5Ratio) && (double.IsNaN(T5) || r.K4WorstT5Ratio > T5))
                (T5, T5Case) = (r.K4WorstT5Ratio, r.K4WorstCase);
            if (!double.IsNaN(r.K4WorstT10Ratio) && (double.IsNaN(T10) || r.K4WorstT10Ratio > T10))
                T10 = r.K4WorstT10Ratio;
            foreach (string s in r.K4Table)
                if (s.StartsWith("FAIL", StringComparison.Ordinal) && Fails.Count < 60)
                    Fails.Add(s);
        }
    }

    private static bool HK24(GameData data, ScenarioData sc, TextWriter log, string outDir)
    {
        // (condition, pair sets, with K4) per §8.3; "N-A" is the in-sample reproduction of §7.1, printed only.
        var cases = new (Cond C, bool A, bool B, bool K4, bool Counted)[]
        {
            (N, false, true, true, true),
            (W, true, true, true, true),
            (A75, true, true, true, true),
            (A50, true, true, false, true),
            (N, true, false, true, false),
        };
        var jobs = new List<(int Case, bool Dev, long A, long Dh, int Actor, int Proc, char Set)>();
        for (int c = 0; c < cases.Length; c++)
            foreach (bool dev in new[] { true, false })
                foreach (long a in CultureRules.Alphas)
                    foreach (long dh in CultureRules.Deltas)
                        foreach ((int actor, int p, char set) in Pairs(sc, cases[c].A, cases[c].B))
                            jobs.Add((c, dev, a, dh, actor, p, set));
        var res = new CultureRules.VariantResult[jobs.Count];
        Parallel.For(0, jobs.Count, i =>
        {
            (int c, bool dev, long a, long dh, int actor, int p, char _) = jobs[i];
            var r = new CultureRules.VariantResult { Mode = dev ? CultureRules.EvalMode.AllDev : CultureRules.EvalMode.Literal };
            IndustryParams ip = CultureRules.Params(a, dh, 100);
            RuleStand.FreeTooling.Value = dev; // D-1
            RuleStand.Condition.Value = cases[c].C.C;
            string id = $"{sc.ActorId[actor]}-{p} α{Fx6.FromRaw(a)} δ{Fx6.FromRaw(dh)}";
            CultureRules.K2(data, sc, Rule, ip, actor, p, id, r);
            if (cases[c].K4)
                CultureRules.K34(data, sc, Rule, ip, actor, p, id, r);
            RuleStand.FreeTooling.Value = false;
            RuleStand.Condition.Value = StandCondition.Default;
            res[i] = r;
        });

        bool pass = true;
        using var csv = new StreamWriter(Path.Combine(outDir, "hk24.csv"));
        csv.WriteLine("condition,pairs,mode,counted,K2a_fail,K2a_n,K2a_worst_g,K2b_fail,K2b_n,K2b_worst_ratio,K4_n,K4a,K4b,K4c,K4_worst_T5_T25,K4_worst_T10_T25,K3_fail,K3_n,excluded");
        for (int c = 0; c < cases.Length; c++)
            foreach (bool dev in new[] { true, false })
                foreach (char set in new[] { 'A', 'B' })
                {
                    if ((set == 'A' && !cases[c].A) || (set == 'B' && !cases[c].B))
                        continue;
                    var agg = new Agg();
                    for (int i = 0; i < jobs.Count; i++)
                        if (jobs[i].Case == c && jobs[i].Dev == dev && jobs[i].Set == set)
                            agg.Add(res[i]);
                    bool ok = agg.K2aFail == 0 && agg.K2bFail == 0 && (!cases[c].K4 || (agg.K4a == 0 && agg.K4b == 0 && agg.K4c == 0));
                    string tag = !cases[c].Counted ? "print (in-sample)" : dev ? (ok ? "pass" : "FAIL") : "literal (print)";
                    if (cases[c].Counted && dev)
                        pass &= ok;
                    string mode = dev ? "D-1,D-4" : "literal";
                    log.WriteLine($"HK2{(cases[c].K4 ? "/HK4" : "")} {cases[c].C.Name} pairs {set} [{mode}] {tag}: K2a {agg.K2aFail}/{agg.K2aN} (worst g {F(agg.K2aWorstG)} {agg.K2aCase}) "
                        + $"K2b {agg.K2bFail}/{agg.K2bN} (worst {F(agg.K2bWorst)} {agg.K2bCase}; threshold 0.25)"
                        + (cases[c].K4 ? $" K4a/b/c {agg.K4a}/{agg.K4b}/{agg.K4c} of {agg.K4N} (worst T5/T25 {F(agg.T5)} {agg.T5Case}; T10/T25 {F(agg.T10)}) K3 (print) {agg.K3Fail}/{agg.K3N}" : "")
                        + $"; not started {agg.Excluded}");
                    foreach (string f in agg.Fails.Take(dev ? 20 : 5))
                        log.WriteLine($"    {f}");
                    csv.WriteLine(string.Join(',', cases[c].C.Name, set, mode, cases[c].Counted ? 1 : 0, agg.K2aFail, agg.K2aN, F(agg.K2aWorstG), agg.K2bFail, agg.K2bN, F(agg.K2bWorst),
                        agg.K4N, agg.K4a, agg.K4b, agg.K4c, F(agg.T5), F(agg.T10), agg.K3Fail, agg.K3N, agg.Excluded));
                }
        return pass;
    }

    // ------------------------------------------------------------------ HK6

    private sealed class K6Agg
    {
        public int Fail, LitFail, LitFailSkip0, MonoFail, N;
        public double MinMargin = double.PositiveInfinity, MinG = double.PositiveInfinity, MinGSkip0 = double.PositiveInfinity, MaxMonoViolation;
        public string MarginCase = "", MonoCase = "";
    }

    private static bool HK6(GameData data, ScenarioData sc, TextWriter log)
    {
        Cond[] conds = [N, WM, A75, A50];
        var jobs = new List<(bool Free, long A, long Dh, int Actor)>();
        foreach (bool free in new[] { true, false })
            foreach (long a in CultureRules.Alphas)
                foreach (long dh in CultureRules.Deltas)
                    for (int actor = 0; actor < sc.ActorCount; actor++)
                        jobs.Add((free, a, dh, actor));
        var res = new K6Agg[jobs.Count][];
        var notes = new SortedSet<string>(StringComparer.Ordinal);
        Parallel.For(0, jobs.Count, i =>
        {
            (bool free, long a, long dh, int actor) = jobs[i];
            IndustryParams ip = CultureRules.Params(a, dh, 100);
            var st = new RuleStand[conds.Length];
            var agg = new K6Agg[conds.Length];
            for (int k = 0; k < conds.Length; k++)
            {
                st[k] = Make(data, sc, actor, ip, CultureRules.RNorm, -1, RunSet.Mastered, StartMod.None, true, conds[k].C, free);
                agg[k] = new K6Agg { N = 1 };
            }
            lock (notes)
                foreach (string n in st[0].Notes)
                    notes.Add($"{sc.ActorId[actor]}: {n}");
            string id = $"{sc.ActorId[actor]} α{Fx6.FromRaw(a)} δ{Fx6.FromRaw(dh)}";
            IndustryLayout l = st[0].Layout;
            Fx6 hFloor = ip.PhiFloor * st[0].Hcap;
            for (int t = 0; t < CultureRules.Horizon; t++)
            {
                for (int k = 0; k < conds.Length; k++)
                    st[k].Step();
                for (int k = 0; k < conds.Length; k++)
                {
                    RuleStand s = st[k];
                    Fx6 act = s.Act;
                    for (int r = 0; r < l.ProcessCount; r++)
                    {
                        if (!s.Running(r))
                            continue;
                        double g = s.GH(r).ToDouble();
                        agg[k].MinG = Math.Min(agg[k].MinG, g);
                        if (t > 0)
                            agg[k].MinGSkip0 = Math.Min(agg[k].MinGSkip0, g);
                        int c = l.ClusterIndex[r];
                        for (int d = 0; d < 3; d++)
                        {
                            Fx6 req = l.Req[r][d];
                            if (req <= Fx6.Zero)
                                continue;
                            // §8.3 HK6: at act < 1 maintenance itself goes down to r_d·a_r (a_r = act, act_norm 1).
                            Fx6 keep = req;
                            if (act < Fx6.One)
                            {
                                keep = req * act;
                                if (keep < hFloor)
                                    keep = hFloor;
                            }
                            double margin = (s.H[c * 3 + d] - (keep - Tol01)).ToDouble();
                            if (margin < agg[k].MinMargin)
                            {
                                agg[k].MinMargin = margin;
                                agg[k].MarginCase = $"{id} p{l.Number[r]} {(Dimension)d} m{t}";
                            }
                        }
                    }
                }
                // Monotonicity in act: H(0.5) ≤ H(0.75) ≤ H(1) in every cell and month, tolerance 100 raw.
                for (int cell = 0; cell < st[0].H.Length; cell++)
                {
                    long v1 = st[3].H[cell].Raw - st[2].H[cell].Raw, v2 = st[2].H[cell].Raw - st[0].H[cell].Raw;
                    long v = Math.Max(v1, v2);
                    if (v > agg[0].MaxMonoViolation * 1e6)
                    {
                        agg[0].MaxMonoViolation = v / 1e6;
                        agg[0].MonoCase = $"{id} cell {l.Clusters[cell / 3]}.{(Dimension)(cell % 3)} m{t}";
                    }
                }
            }
            for (int k = 0; k < conds.Length; k++)
            {
                agg[k].Fail = agg[k].MinMargin < 0 ? 1 : 0;
                agg[k].LitFail = agg[k].MinG < 0.81 ? 1 : 0;
                agg[k].LitFailSkip0 = agg[k].MinGSkip0 < 0.81 ? 1 : 0;
            }
            agg[0].MonoFail = agg[0].MaxMonoViolation > 100e-6 ? 1 : 0;
            res[i] = agg;
        });

        bool pass = true;
        foreach (bool free in new[] { true, false })
        {
            string mode = free ? "D-1,D-3" : "real tooling";
            for (int k = 0; k < conds.Length; k++)
            {
                var tot = new K6Agg { MinMargin = double.PositiveInfinity };
                var mono = new K6Agg();
                for (int i = 0; i < jobs.Count; i++)
                {
                    if (jobs[i].Free != free)
                        continue;
                    K6Agg r = res[i][k];
                    tot.N++;
                    tot.Fail += r.Fail;
                    tot.LitFail += r.LitFail;
                    tot.LitFailSkip0 += r.LitFailSkip0;
                    if (r.MinMargin < tot.MinMargin)
                        (tot.MinMargin, tot.MarginCase) = (r.MinMargin, r.MarginCase);
                    tot.MinG = Math.Min(tot.MinG, r.MinG);
                    tot.MinGSkip0 = Math.Min(tot.MinGSkip0, r.MinGSkip0);
                    if (k == 0)
                    {
                        mono.MonoFail += res[i][0].MonoFail;
                        if (res[i][0].MaxMonoViolation > mono.MaxMonoViolation)
                            (mono.MaxMonoViolation, mono.MonoCase) = (res[i][0].MaxMonoViolation, res[i][0].MonoCase);
                    }
                }
                bool counted = k > 0 && free;
                bool ok = tot.Fail == 0;
                if (counted)
                    pass &= ok;
                string tag = counted ? (ok ? "pass" : "FAIL") : "print";
                log.WriteLine($"HK6 {conds[k].Name} [{mode}] {tag}: per-dim fails {tot.Fail}/{tot.N} (min margin H − (max(r·a_r, H_floor) − 0.1) {F(tot.MinMargin)} at {tot.MarginCase}); "
                    + $"literal g_H ≥ 0.81: fails {tot.LitFail} (min {F(tot.MinG)}), without month 0 {tot.LitFailSkip0} (min {F(tot.MinGSkip0)})");
                if (k == 0)
                {
                    bool mok = mono.MonoFail == 0;
                    if (free)
                        pass &= mok;
                    log.WriteLine($"HK6 monotonicity in act [{mode}] {(free ? (mok ? "pass" : "FAIL") : "print")}: violations {mono.MonoFail}/{tot.N} stands, max H(lower act) − H(higher act) {F(mono.MaxMonoViolation)} (tolerance 0.0001) at {mono.MonoCase}");
                }
            }
        }
        foreach (string n in notes)
            log.WriteLine($"  HK6 note {n}");
        return pass;
    }

    // ------------------------------------------------------------------ HK7

    private static bool HK7(GameData data, ScenarioData sc, TextWriter log)
    {
        var jobs = new List<(bool Free, long A, long Dh, int Actor, int Proc, char Set)>();
        foreach (bool free in new[] { true, false })
            foreach (long a in CultureRules.Alphas)
                foreach (long dh in CultureRules.Deltas)
                    foreach ((int actor, int p, char set) in Pairs(sc, true, true))
                        jobs.Add((free, a, dh, actor, p, set));
        var res = new (bool Started, double G, bool Mismatch, int Lead)[jobs.Count];
        Parallel.For(0, jobs.Count, i =>
        {
            (bool free, long a, long dh, int actor, int p, char _) = jobs[i];
            IndustryParams ip = CultureRules.Params(a, dh, 100);
            StandCondition c0 = StandCondition.Default;
            var world = new HoldoutWorld(
                [Make(data, sc, actor, ip, CultureRules.RNorm, p, RunSet.Solo, StartMod.Hold, false, c0, free),
                 Make(data, sc, actor, ip, CultureRules.RTok, p, RunSet.Solo, StartMod.Hold, false, c0, free)], [actor, actor]);
            RuleStand alone = Make(data, sc, actor, ip, CultureRules.RTok, p, RunSet.Solo, StartMod.Hold, false, c0, free);
            RuleStand s2 = world.Sites[1];
            bool started = false, mismatch = false;
            for (int t = 0; t < 1200; t++)
            {
                world.Step();
                alone.Step();
                started |= s2.V[s2.Target] > Fx6.Zero;
                if (!mismatch && !s2.H.AsSpan().SequenceEqual(alone.H))
                    mismatch = true;
            }
            res[i] = (started, s2.GH(s2.Target).ToDouble(), mismatch, world.LeadSiteMonths);
        });
        bool pass = true;
        foreach (bool free in new[] { true, false })
            foreach (char set in new[] { 'A', 'B' })
            {
                int n = 0, fail = 0, mism = 0, lead = 0, excl = 0;
                double worst = double.NaN;
                string at = "";
                for (int i = 0; i < jobs.Count; i++)
                {
                    if (jobs[i].Free != free || jobs[i].Set != set)
                        continue;
                    if (res[i].Mismatch)
                        mism++;
                    lead += res[i].Lead;
                    if (!res[i].Started)
                    {
                        excl++;
                        continue;
                    }
                    n++;
                    if (res[i].G >= 0.9)
                        fail++;
                    if (double.IsNaN(worst) || res[i].G > worst)
                        (worst, at) = (res[i].G, $"{sc.ActorId[jobs[i].Actor]}-{jobs[i].Proc} α{Fx6.FromRaw(jobs[i].A)} δ{Fx6.FromRaw(jobs[i].Dh)}");
                }
                bool ok = fail == 0 && mism == 0 && lead == 0;
                if (free)
                    pass &= ok;
                log.WriteLine($"HK7 pairs {set} [{(free ? "D-1" : "real tooling")}] {(free ? (ok ? "pass" : "FAIL") : "literal (print)")}: (a) site 2 g_H(1200) ≥ 0.9: {fail}/{n} (worst {F(worst)} at {at}; threshold 0.9), not started {excl}; "
                    + $"world_lead from own site 1: {lead} site-months; (b) H of site 2 ≠ single stand: {mism} cases");
            }
        return pass;
    }

    // ------------------------------------------------------------------ diagnostics of the failures (print only, no tuning)

    /// <summary>
    /// Why HK2/HK7 K2a and HK4 K4c fail: (1) K2a control "no output" (rate 0, the line admitted but reporting nothing):
    /// if g_H(1200) ≥ 0.9 there too, the floor/spill holds the process and no rate rule can make K2a fail; also
    /// r_d vs H_floor per active dimension. (2) K4c with horizon 2400 instead of 1200 (censoring check), and the
    /// equilibrium H at rate 5 vs 25 at the end.
    /// </summary>
    public static int Diag(GameData data, ScenarioData sc, TextWriter log)
    {
        Cond[] conds = [N, W, A75, A50];
        var jobs = new List<(int C, long A, long Dh, int Actor, int Proc, char Set)>();
        for (int c = 0; c < conds.Length; c++)
            foreach (long a in CultureRules.Alphas)
                foreach (long dh in CultureRules.Deltas)
                    foreach ((int actor, int p, char set) in Pairs(sc, true, true))
                        jobs.Add((c, a, dh, actor, p, set));
        var res = new (double G, double G0, bool Floor, string Req)[jobs.Count];
        Parallel.For(0, jobs.Count, i =>
        {
            (int c, long a, long dh, int actor, int p, char _) = jobs[i];
            IndustryParams ip = CultureRules.Params(a, dh, 100);
            RuleStand tok = Make(data, sc, actor, ip, CultureRules.RTok, p, RunSet.Solo, StartMod.Hold, false, conds[c].C, true);
            RuleStand zero = Make(data, sc, actor, ip, Fx6.Zero, p, RunSet.Solo, StartMod.Hold, false, conds[c].C, true);
            for (int t = 0; t < 1200; t++)
            {
                tok.Step();
                zero.Step();
            }
            Fx6 hFloor = ip.PhiFloor * tok.Hcap;
            bool floor = true;
            var req = new List<string>();
            for (int d = 0; d < 3; d++)
            {
                Fx6 r = tok.Layout.Req[tok.Target][d];
                if (r <= Fx6.Zero)
                    continue;
                floor &= r <= hFloor;
                req.Add($"{(Dimension)d} r {r.ToDouble():0.00}");
            }
            res[i] = (tok.GH(tok.Target).ToDouble(), zero.GH(zero.Target).ToDouble(), floor, $"{string.Join(", ", req)}; H_floor {hFloor.ToDouble():0.00}");
        });
        for (int c = 0; c < conds.Length; c++)
            foreach (char set in new[] { 'A', 'B' })
            {
                int fail = 0, failZero = 0, failFloor = 0, n = 0;
                var procs = new SortedSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < jobs.Count; i++)
                {
                    if (jobs[i].C != c || jobs[i].Set != set)
                        continue;
                    n++;
                    if (res[i].G < 0.9)
                        continue;
                    fail++;
                    if (res[i].G0 >= 0.9)
                        failZero++;
                    if (res[i].Floor)
                        failFloor++;
                    procs.Add($"{sc.ActorId[jobs[i].Actor]}-{jobs[i].Proc} α{Fx6.FromRaw(jobs[i].A).ToDouble():0.000} δ{Fx6.FromRaw(jobs[i].Dh).ToDouble():0.0000}: g_H tok {res[i].G:0.000}, no output {res[i].G0:0.000} ({res[i].Req})");
                }
                log.WriteLine($"diag K2a {conds[c].Name} {set}: fails {fail}/{n}; of them also ≥ 0.9 with NO output {failZero}; all active r_d ≤ H_floor {failFloor}");
                foreach (string pr in procs)
                    log.WriteLine($"    {pr}");
            }

        // K4c censoring: the failing pairs of the main run, horizon 2400.
        Fx6[] rates = [Fx6.FromInt(5), Fx6.FromInt(10), CultureRules.RNorm];
        var k4 = new List<(int C, long A, long Dh, int Actor, int Proc)>();
        foreach (int c in new[] { 1, 2 })
            foreach (long a in CultureRules.Alphas)
                foreach (long dh in CultureRules.Deltas)
                    foreach ((int actor, int p, char _) in Pairs(sc, true, true))
                        k4.Add((c, a, dh, actor, p));
        var t4 = new (double T5, double T10, double T25, double H5)[k4.Count];
        Parallel.For(0, k4.Count, i =>
        {
            (int c, long a, long dh, int actor, int p) = k4[i];
            IndustryParams ip = CultureRules.Params(a, dh, 100);
            var T = new double[3];
            var hs = new double[3];
            for (int k = 0; k < 3; k++)
            {
                RuleStand s = Make(data, sc, actor, ip, rates[k], p, RunSet.Solo, StartMod.Edge, false, conds[c].C, true);
                T[k] = CultureRules.Inf(CultureRules.TimeToG(s, 1200).T);
                if (T[k] > 0 && double.IsInfinity(T[k]))
                {
                    // continue the same stand to 2400 months
                    int extra = -1;
                    for (int t = 0; t < 1200 && extra < 0; t++)
                    {
                        s.Step();
                        if (s.GH(s.Target) >= CultureRules.G09)
                            extra = 1200 + t + 1;
                    }
                    T[k] = extra < 0 ? double.PositiveInfinity : extra;
                }
                hs[k] = s.GH(s.Target).ToDouble();
            }
            t4[i] = (T[0], T[1], T[2], hs[0]);
        });
        foreach (int c in new[] { 1, 2 })
        {
            int n = 0, fail1200 = 0, fail2400 = 0;
            var lines = new List<string>();
            for (int i = 0; i < k4.Count; i++)
            {
                if (k4[i].C != c)
                    continue;
                var r = t4[i];
                bool fin1200 = r.T25 <= 1200;
                if (!fin1200)
                    continue;
                n++;
                bool ok1200 = r.T5 <= 1200 && r.T10 <= 1200 && r.T5 <= 2 * r.T25 && r.T10 <= 1.5 * r.T25;
                bool ok2400 = r.T5 <= 2 * r.T25 && r.T10 <= 1.5 * r.T25;
                if (!ok1200)
                    fail1200++;
                if (!ok2400)
                {
                    fail2400++;
                    if (lines.Count < 30)
                        lines.Add($"{sc.ActorId[k4[i].Actor]}-{k4[i].Proc} α{Fx6.FromRaw(k4[i].A)} δ{Fx6.FromRaw(k4[i].Dh)}: T5 {F(r.T5)} T10 {F(r.T10)} T25 {F(r.T25)}; g_H at end rate 5 {F(r.H5)}");
                }
            }
            log.WriteLine($"diag K4c {conds[c].Name}: pairs with T25 ≤ 1200: {n}; K4c fails at horizon 1200 {fail1200}, at horizon 2400 (T5, T10 censored at 2400) {fail2400}");
            foreach (string l in lines)
                log.WriteLine($"    {l}");
        }
        return 0;
    }

    // ------------------------------------------------------------------ prints

    private static int TimeTo(HoldoutWorld w, int site, int horizon)
    {
        RuleStand s = w.Sites[site];
        if (s.GH(s.Target) >= CultureRules.G09)
            return 0;
        for (int t = 0; t < horizon; t++)
        {
            w.Step();
            if (s.GH(s.Target) >= CultureRules.G09)
                return t + 1;
        }
        return -1;
    }

    /// <summary>Print: rate split over two own sites (r/2 + r/2) vs r on one site, T_split / T_single (D-1, horizon 1200).</summary>
    private static void Split(GameData data, ScenarioData sc, TextWriter log)
    {
        long[] rates = [5_000_000, 10_000_000, 25_000_000, 50_000_000];
        var jobs = new List<(long A, long Dh, int Actor, int Proc)>();
        foreach (long a in CultureRules.Alphas)
            foreach (long dh in CultureRules.Deltas)
                foreach ((int actor, int p, char _) in Pairs(sc, true, true))
                    jobs.Add((a, dh, actor, p));
        var res = new double[jobs.Count][];
        Parallel.For(0, jobs.Count, i =>
        {
            (long a, long dh, int actor, int p) = jobs[i];
            IndustryParams ip = CultureRules.Params(a, dh, 100);
            var row = new double[rates.Length * 2];
            for (int k = 0; k < rates.Length; k++)
            {
                Fx6 r = Fx6.FromRaw(rates[k]), half = Fx6.FromRaw(rates[k] / 2);
                var single = new HoldoutWorld([Make(data, sc, actor, ip, r, p, RunSet.Solo, StartMod.Edge, false, StandCondition.Default, true)], [actor]);
                var split = new HoldoutWorld([Make(data, sc, actor, ip, half, p, RunSet.Solo, StartMod.Edge, false, StandCondition.Default, true),
                    Make(data, sc, actor, ip, half, p, RunSet.Solo, StartMod.Edge, false, StandCondition.Default, true)], [actor, actor]);
                row[2 * k] = CultureRules.Inf(TimeTo(single, 0, 1200));
                row[2 * k + 1] = CultureRules.Inf(TimeTo(split, 0, 1200));
            }
            res[i] = row;
        });
        for (int k = 0; k < rates.Length; k++)
        {
            var ratios = new List<double>();
            int infSplit = 0, infBoth = 0;
            foreach (double[] row in res)
            {
                double s = row[2 * k], sp = row[2 * k + 1];
                if (double.IsInfinity(s))
                {
                    infBoth += double.IsInfinity(sp) ? 1 : 0;
                    continue;
                }
                if (double.IsInfinity(sp))
                    infSplit++;
                ratios.Add(s == 0 ? 1 : sp / s);
            }
            log.WriteLine($"print split r {Fx6.FromRaw(rates[k])} = 2 × {Fx6.FromRaw(rates[k] / 2)} [D-1, 1200]: T_split/T_single p50 {F(Q(ratios, 0.5))} p90 {F(Q(ratios, 0.9))} max {F(ratios.Count > 0 ? ratios.Max() : double.NaN)} (n {ratios.Count}; split never {infSplit}; both never {infBoth})");
        }
    }

    private static double Q(List<double> xs, double q)
    {
        if (xs.Count == 0)
            return double.NaN;
        double[] s = [.. xs.Order()];
        return s[(int)Math.Floor(q * (s.Length - 1))];
    }

    /// <summary>
    /// Print: the rival modelled by the stand instead of W ≡ 1. Rival = each other actor, its line on the target from
    /// the edge at rate 25; world_lead from its snapshot (yield ≥ 0.7, v &gt; 0). K2a, K2b, K4c on pairs A and B,
    /// δ_H 0.003, D-1, D-4.
    /// </summary>
    private static void Rival(GameData data, ScenarioData sc, TextWriter log)
    {
        var jobs = new List<(long A, int Actor, int Proc, int Rival, char Set)>();
        foreach (long a in CultureRules.Alphas)
            foreach ((int actor, int p, char set) in Pairs(sc, true, true))
                for (int rv = 0; rv < sc.ActorCount; rv++)
                    if (rv != actor)
                        jobs.Add((a, actor, p, rv, set));
        Fx6[] rates = [CultureRules.RTok, Fx6.FromInt(5), Fx6.FromInt(10), CultureRules.RNorm, Fx6.FromInt(50)];
        var res = new (bool A, double G, double K2b, double[] T, int Lead)[jobs.Count];
        Parallel.For(0, jobs.Count, i =>
        {
            (long a, int actor, int p, int rival, char _) = jobs[i];
            IndustryParams ip = CultureRules.Params(a, 3_000, 100);
            StandCondition c0 = StandCondition.Default;
            RuleStand Rv() => Make(data, sc, rival, ip, CultureRules.RNorm, p, RunSet.Solo, StartMod.Edge, false, c0, true);
            var hold = new HoldoutWorld([Make(data, sc, actor, ip, CultureRules.RTok, p, RunSet.Solo, StartMod.Hold, false, c0, true), Rv()], [actor, rival]);
            bool started = false;
            for (int t = 0; t < 1200; t++)
            {
                hold.Step();
                started |= hold.Sites[0].V[hold.Sites[0].Target] > Fx6.Zero;
            }
            double g = hold.Sites[0].GH(hold.Sites[0].Target).ToDouble();
            var tok = new HoldoutWorld([Make(data, sc, actor, ip, CultureRules.RTok, p, RunSet.Solo, StartMod.Edge, false, c0, true), Rv()], [actor, rival]);
            var norm = new HoldoutWorld([Make(data, sc, actor, ip, CultureRules.RNorm, p, RunSet.Solo, StartMod.Edge, false, c0, true), Rv()], [actor, rival]);
            Fx6 h0 = norm.Sites[0].TargetHSum();
            for (int t = 0; t < 120; t++)
            {
                tok.Step();
                norm.Step();
            }
            double gN = (norm.Sites[0].TargetHSum() - h0).ToDouble(), gT = (tok.Sites[0].TargetHSum() - h0).ToDouble();
            double k2b = gN >= 0.05 ? gT / gN : double.NaN;
            var T = new double[rates.Length];
            int lead = hold.LeadSiteMonths;
            for (int k = 0; k < rates.Length; k++)
            {
                var w = new HoldoutWorld([Make(data, sc, actor, ip, rates[k], p, RunSet.Solo, StartMod.Edge, false, c0, true), Rv()], [actor, rival]);
                T[k] = CultureRules.Inf(TimeTo(w, 0, 1200));
            }
            res[i] = (started, g, k2b, T, lead);
        });
        foreach (char set in new[] { 'A', 'B' })
        {
            int n2a = 0, f2a = 0, n2b = 0, f2b = 0, n4 = 0, f4c = 0, f4a = 0, f4b = 0, leadOn = 0, tot = 0;
            double wg = double.NaN, wb = double.NaN, w5 = double.NaN;
            for (int i = 0; i < jobs.Count; i++)
            {
                if (jobs[i].Set != set)
                    continue;
                var r = res[i];
                tot++;
                if (r.Lead > 0)
                    leadOn++;
                if (r.A)
                {
                    n2a++;
                    f2a += r.G >= 0.9 ? 1 : 0;
                    wg = double.IsNaN(wg) ? r.G : Math.Max(wg, r.G);
                }
                if (!double.IsNaN(r.K2b))
                {
                    n2b++;
                    f2b += r.K2b > 0.25 ? 1 : 0;
                    wb = double.IsNaN(wb) ? r.K2b : Math.Max(wb, r.K2b);
                }
                double[] T = r.T;
                if (!double.IsInfinity(T[3]))
                {
                    n4++;
                    bool ok = !double.IsInfinity(T[1]) && T[1] <= 2 * T[3] && T[2] <= 1.5 * T[3];
                    f4c += ok ? 0 : 1;
                    double r5 = T[3] == 0 ? (T[1] == 0 ? 1 : double.PositiveInfinity) : T[1] / T[3];
                    w5 = double.IsNaN(w5) ? r5 : Math.Max(w5, r5);
                }
                for (int k = 1; k < T.Length; k++)
                    if (T[k] > T[k - 1] + 1)
                    {
                        f4a++;
                        break;
                    }
                if (Math.Abs(T[4] - T[3]) > 1 && !(double.IsInfinity(T[4]) && double.IsInfinity(T[3])))
                {
                    f4b++;
                    log.WriteLine($"    rival K4b {sc.ActorId[jobs[i].Actor]}-{jobs[i].Proc} rival {sc.ActorId[jobs[i].Rival]} α{Fx6.FromRaw(jobs[i].A)}: T {string.Join(' ', T.Select(F))}");
                }
            }
            log.WriteLine($"print rival-by-stand pairs {set} [δ_H 0.003, D-1, 1200]: rival reached yield ≥ 0.7 (world_lead on) in {leadOn}/{tot} hold runs; K2a {f2a}/{n2a} (worst g {F(wg)}); K2b {f2b}/{n2b} (worst {F(wb)}); K4a/b/c {f4a}/{f4b}/{f4c} of {n4} (worst T5/T25 {F(w5)})");
        }
    }
}
