using System.Collections.Immutable;
using System.Globalization;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Knowledge;
using Singularity.Kernel;

namespace Singularity.Lab.Smoke;

/// <summary>
/// TASK-0017 / spec slice-jet-aviation.md §11 T4s: smoke of the calibration anchors C3, C2 and the yield channel
/// of C1 on the knowledge and capability layers, before the product layer exists. Not a calibration: grids are
/// exploratory, nothing is frozen, and there is no leadership metric anywhere (RISK-0011). Every stand runs the
/// real Core systems on the Kernel <see cref="TickRunner"/> with the daily calendar and the <c>core</c> mod data;
/// product-layer inputs are fixed stand rules written in docs/simulation/smoke-t4s.md §2. Doubles appear only in
/// the analysis of finished runs, never in a stand's state.
/// </summary>
internal static class SmokeT4s
{
    public const int Horizon = 384;
    private const int C3Horizon = 1200;
    private const int C1Horizon = 1024;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Smoke seeds, disjoint from the calibration seeds 10001…10100 of spec §8.2.</summary>
    public const ulong SeedBase = 50001;

    public static int Run(string modDir, string outDir, int seeds, string? only, TextWriter log)
    {
        GameData data = ModLoader.Load(modDir);
        ScenarioData sc = data.Scenario("jet_slice_1945");
        Directory.CreateDirectory(outDir);
        log.WriteLine($"smoke-t4s scenario_hash={sc.Hash} seeds={seeds} (from {SeedBase})");
        if (only is null || only == "c3") RunC3(data, sc, outDir, seeds, log);
        if (only is null || only == "c2") RunC2(data, sc, outDir, log);
        if (only is null || only == "c1") RunC1(data, sc, outDir, log);
        return 0;
    }

    // ------------------------------------------------------------------ C3

    private static readonly int[] KeyNodes = [17, 19, 23, 27, 28, 30, 40, 41, 49, 51, 52];

    private static void RunC3(GameData data, ScenarioData sc, string outDir, int seeds, TextWriter log)
    {
        // (label, S in budget index/month, θ_start, h_max); S_ref = 0.1. Base row = spec §8.1 values.
        var points = new List<(string Label, long S, long Theta, long HMax)>();
        foreach (long s in new long[] { 12_500, 25_000, 50_000, 100_000, 200_000, 400_000 })
            points.Add(("S", s, 600_000, 80_000));
        foreach (long th in new long[] { 300_000, 600_000, 900_000 })
        {
            foreach (long hm in new long[] { 40_000, 80_000, 160_000 })
            {
                if (th != 600_000 || hm != 80_000)
                    points.Add(("grid", 100_000, th, hm));
            }
        }
        using var csv = new StreamWriter(Path.Combine(outDir, "c3_runs.csv"));
        csv.WriteLine("label,node,S,theta_start,h_max,seed,months_to_discovery,months_to_theta");
        using var sum = new StreamWriter(Path.Combine(outDir, "c3_summary.csv"));
        sum.WriteLine("label,node,type,gen,S,theta_start,h_max,n,censored,mean_months,cv,p10,p50,p90,months_to_theta");
        log.WriteLine("C3: node S theta h_max | mean CV p10 p50 p90 censored | months_to_theta");
        foreach ((string label, long s, long th, long hm) in points)
        {
            var kp = new KnowledgeParams { ThetaStart = Fx6.FromRaw(th), HMax = Fx6.FromRaw(hm) };
            KnowledgeGraph g = KnowledgeGraph.FromData(data.Nodes, sc.Disciplines, kp);
            foreach (int node in KeyNodes)
            {
                var times = new List<double>();
                int censored = 0;
                long toTheta = -1;
                for (int k = 0; k < seeds; k++)
                {
                    var stand = new C3Stand(g, sc, kp, node, Fx6.FromRaw(s), SeedBase + (ulong)k);
                    (long t, long th0) = stand.Run(C3Horizon);
                    toTheta = th0;
                    csv.WriteLine(string.Join(',', label, node, F(s), F(th), F(hm), SeedBase + (ulong)k, t, th0));
                    if (t < 0) censored++;
                    else times.Add(t);
                }
                Stats st = Stats.Of(times);
                int row = g.RowOf(node);
                sum.WriteLine(string.Join(',', label, node, g.Type[row], g.Gen[row], F(s), F(th), F(hm), times.Count, censored,
                    D(st.Mean), D(st.Cv), D(st.P10), D(st.P50), D(st.P90), toTheta));
                log.WriteLine($"C3 {label} n{node} S={F(s)} θ={F(th)} h={F(hm)} | {D(st.Mean)} {D(st.Cv)} {D(st.P10)} {D(st.P50)} {D(st.P90)} cens={censored} | θ@{toTheta}");
            }
        }
    }

    /// <summary>
    /// One actor (GBR's number 1 for RNG keys, GBR's K_d), constant R&amp;D money <c>S</c> per month, all of it on one ★
    /// node: the node's requires-closure is known, every other frontier node has prio 0, <c>j ≡ 1</c> (no PRIO_J
    /// spread: the only noise left is the hazard draw), no industry (<c>V_d = 0</c>), no rival (<c>known_elsewhere = 0</c>).
    /// </summary>
    private sealed class C3Stand : IKnowledgeInputs
    {
        private const int ActorNumber = 1;
        private readonly int _target;
        private readonly Fx6 _s;
        private readonly KnowledgeSystem _k;
        private readonly TickRunner _runner;
        private readonly TickCalendar _cal;

        public C3Stand(KnowledgeGraph g, ScenarioData sc, KnowledgeParams kp, int node, Fx6 s, ulong seed)
        {
            _target = g.RowOf(node);
            _s = s;
            var start = new KnowledgeStart([ActorNumber], g.Count, g.DisciplineNames.Length);
            foreach (int r in Closure(g, _target))
                start.Known[r] = true;
            start.Known[_target] = false;
            for (int d = 0; d < g.DisciplineNames.Length; d++)
                start.KD[d] = sc.KD[ActorNumber * sc.Disciplines.Length + d];
            var table = new Table(1);
            _cal = new TickCalendar(new CivilDate(sc.StartYear, sc.StartMonth, 1));
            var rng = new CounterRng(seed);
            _k = new KnowledgeSystem(table, g, start, kp, this, rng, 1);
            _runner = new TickRunner(table, seed, [_k], 1, 1, _cal);
        }

        /// <summary>Months of investment until discovery (−1 = censored) and until <c>prog ≥ θ_start</c>.</summary>
        public (long Months, long ToTheta) Run(int horizon)
        {
            long toTheta = -1;
            for (long t = 0; t < horizon; t++)
            {
                long last = _cal.TickOfMonth(t);
                while (_runner.Tick <= last)
                    _runner.RunTick();
                if (toTheta < 0 && _k.LastStep(0, _target).HazardActive)
                    toTheta = t + 1;
                if (_k.IsKnown(0, _target))
                    return (t + 1, toTheta);
            }
            return (-1, toTheta);
        }

        public Fx6 ResearchBudget(int actorRow, long month) => _s;

        public Fx6 Priority(int actorRow, int nodeRow, long month) => nodeRow == _target ? Fx6.One : Fx6.Zero;

        public Fx6 J(int actorRow, int nodeRow, long month) => Fx6.One;

        public Fx6 DisciplineVolume(int actorRow, int discipline, long month) => Fx6.Zero;
    }

    /// <summary>Requires-closure of a node (rows), the node included; an OR-group contributes its first member.</summary>
    internal static SortedSet<int> Closure(KnowledgeGraph g, int row)
    {
        var seen = new SortedSet<int>();
        var stack = new Stack<int>();
        stack.Push(row);
        while (stack.Count > 0)
        {
            int r = stack.Pop();
            if (!seen.Add(r))
                continue;
            RequirementLists req = g.Requires;
            for (int k = req.GroupStart[r]; k < req.GroupStart[r + 1]; k++)
                stack.Push(req.GroupMembers(k)[0]);
        }
        return seen;
    }

    // ------------------------------------------------------------------ C2

    internal static readonly (int Number, int Tier)[] KeyProcesses = [(40, 4), (49, 4), (41, 5), (51, 5), (52, 5)];

    /// <summary>Mature direct hours per set (docs/research/data/jet-slice/product_inputs.md §4); 47 has none there.</summary>
    internal static readonly ImmutableDictionary<int, long> Labour = new Dictionary<int, long>
    {
        [38] = 600, [53] = 3700, [55] = 3450, [56] = 7500, [42] = 6750, [44] = 150, [54] = 1350, [39] = 225,
        [40] = 600, [41] = 1800, [45] = 225, [46] = 1200, [49] = 750, [51] = 2250, [43] = 3750, [52] = 5250,
        [48] = 450, [50] = 1200, [57] = 900, [47] = 450,
    }.ToImmutableDictionary();

    /// <summary>base_cost_p in raw Fx6 (product_inputs.md §4), G2 "superior" prc_set: engine g2, hot_sa2_forged, comp_axial, af_swept, ab_on.</summary>
    internal static readonly (int Number, long BaseRaw)[] G2Superior =
        [(38, 60), (40, 60), (44, 15), (45, 22), (49, 75), (50, 120), (53, 370), (54, 135), (55, 345), (57, 90)];

    private sealed record C2Point(long Alpha, long DeltaH, long CM, int XHalfPct, long Rate);

    private static IndustryParams Params(C2Point p) => new()
    {
        Alpha = Fx6.FromRaw(p.Alpha),
        KCatch = Fx6.FromRaw(p.Alpha / 2),
        DeltaH = Fx6.FromRaw(p.DeltaH),
        CM = Fx6.FromRaw(p.CM),
        XHalfByTier = [Fx6.FromInt(2 * p.XHalfPct), Fx6.FromInt(8 * p.XHalfPct), Fx6.FromInt(25 * p.XHalfPct)],
    };

    private static void RunC2(GameData data, ScenarioData sc, string outDir, TextWriter log)
    {
        var points = new List<C2Point>();
        foreach (long a in new long[] { 20_000, 50_000, 80_000 })
            foreach (long dh in new long[] { 1_700, 3_000, 4_300 })
                foreach (long cm in new long[] { 50_000, 100_000, 200_000 })
                    foreach (int xh in new[] { 50, 100, 200 })
                        points.Add(new C2Point(a, dh, cm, xh, 25_000_000));
        foreach (long rate in new long[] { 5_000_000, 10_000_000, 50_000_000 })
            points.Add(new C2Point(50_000, 3_000, 100_000, 100, rate));

        using var csv = new StreamWriter(Path.Combine(outDir, "c2_runs.csv"));
        csv.WriteLine("alpha,delta_H,c_M,xhalf_pct,rate,variant,actor,process,tier,t_first,t_07,years_first_to_07,yield_at_horizon,bottleneck");
        using var sum = new StreamWriter(Path.Combine(outDir, "c2_summary.csv"));
        sum.WriteLine("alpha,delta_H,c_M,xhalf_pct,rate,variant,tier,n_started,never_first,censored_07,p10_years,p50_years,p90_years,anchor_ok");
        log.WriteLine("C2: alpha dH c_M xh% rate variant | t4 p50 years over started (n started, never started, censored 0.7) | t5 same | anchor");
        foreach (C2Point p in points)
        {
            IndustryParams ip = Params(p);
            foreach (string variant in new[] { "solo", "cluster", "edge" })
            {
                var byTier = new Dictionary<int, List<double>> { [4] = new(), [5] = new() };
                var never = new Dictionary<int, int> { [4] = 0, [5] = 0 };
                var cens = new Dictionary<int, int> { [4] = 0, [5] = 0 };
                foreach ((int proc, int tier) in KeyProcesses)
                {
                    for (int a = 0; a < sc.ActorCount; a++)
                    {
                        if (IsMastered(data, sc, a, proc))
                            continue; // left-censored (spec §5.4): mastered at start
                        var stand = new LineStand(data, sc, a, ip, proc, variant, Fx6.FromRaw(p.Rate));
                        LineResult r = stand.RunC2(Horizon);
                        double years = r.TFirst >= 0 && r.T07 >= 0 ? (r.T07 - r.TFirst) / 12.0 : double.PositiveInfinity;
                        // t_first → t_07 exists only for a started line: never-started cases are counted, not ranked.
                        if (r.TFirst < 0) never[tier]++;
                        else
                        {
                            if (r.T07 < 0) cens[tier]++;
                            byTier[tier].Add(years);
                        }
                        csv.WriteLine(string.Join(',', F(p.Alpha), F(p.DeltaH), F(p.CM), p.XHalfPct, F(p.Rate), variant,
                            sc.ActorId[a], proc, tier, r.TFirst, r.T07, double.IsInfinity(years) ? "" : D(years), D(r.YieldEnd), r.Bottleneck));
                    }
                }
                Stats s4 = Stats.OfCensored(byTier[4]), s5 = Stats.OfCensored(byTier[5]);
                bool ok = s4.P50 >= 2 && s4.P50 <= 8 && s5.P50 > s4.P50;
                foreach ((int tier, Stats st) in new[] { (4, s4), (5, s5) })
                {
                    sum.WriteLine(string.Join(',', F(p.Alpha), F(p.DeltaH), F(p.CM), p.XHalfPct, F(p.Rate), variant, tier,
                        byTier[tier].Count, never[tier], cens[tier], D(st.P10), D(st.P50), D(st.P90), ok ? 1 : 0));
                }
                log.WriteLine($"C2 {F(p.Alpha)} {F(p.DeltaH)} {F(p.CM)} {p.XHalfPct}% r={F(p.Rate)} {variant} | t4 {D(s4.P50)} ({byTier[4].Count}, never {never[4]}, cens {cens[4]}) | t5 {D(s5.P50)} ({byTier[5].Count}, never {never[5]}, cens {cens[5]}) | {(ok ? "OK" : "-")}");
            }
        }
    }

    internal static bool IsMastered(GameData data, ScenarioData sc, int actor, int number)
    {
        foreach (int row in sc.MasteredOf(actor))
        {
            if (data.Nodes.Number[row] == number)
                return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ C1 (yield channel)

    private static void RunC1(GameData data, ScenarioData sc, string outDir, TextWriter log)
    {
        int gbr = sc.ActorIndex("GBR");
        var points = new List<C2Point>();
        foreach (long a in new long[] { 20_000, 50_000, 80_000 })
            foreach (long dh in new long[] { 1_700, 4_300 })
                foreach (int xh in new[] { 50, 100, 200 })
                    points.Add(new C2Point(a, dh, 100_000, xh, 25_000_000));
        using var csv = new StreamWriter(Path.Combine(outDir, "c1_yield_channel.csv"));
        csv.WriteLine("alpha,delta_H,xhalf_pct,doubling,month_from,month_to,cum_units_from,unit_cost_mult_from,unit_cost_mult_to,dlog_yield,min_yield_from,phase");
        using var tr = new StreamWriter(Path.Combine(outDir, "c1_trace_base.csv"));
        tr.WriteLine("month,cum_units,unit_cost_mult,min_yield,bottleneck_process");
        log.WriteLine("C1y: alpha dH xh% | mastery dlog per doubling (1..3) | max dlog in mature phase (min yield >= 0.9), first mature month");
        foreach (C2Point p in points)
        {
            var stand = new LineStand(data, sc, gbr, Params(p), -1, "c1", Fx6.FromRaw(p.Rate), G2Superior);
            (double[] u, double[] minY, int[] bn) = stand.RunC1(C1Horizon);
            log.WriteLine($"C1y {F(p.Alpha)} {F(p.DeltaH)} {p.XHalfPct}% yields at month {C1Horizon}: {stand.YieldsText(C1Horizon)}");
            bool isBase = p.Alpha == 50_000 && p.DeltaH == 4_300 && p.XHalfPct == 100;
            if (isBase)
            {
                for (int t = 0; t < u.Length; t++)
                    tr.WriteLine(string.Join(',', t, (t + 1) * 25, D(u[t]), D(minY[t]), bn[t]));
            }
            var mastery = new List<double>();
            double matureMax = double.NaN;
            int firstMature = -1;
            for (int i = 0; (2 << i) <= u.Length; i++)
            {
                int from = (1 << i) - 1, to = (2 << i) - 1;
                double dlog = Math.Log(u[from]) - Math.Log(u[to]);
                string phase = i < 3 ? "mastery" : minY[from] >= 0.9 ? "mature" : "between";
                if (i < 3) mastery.Add(dlog);
                if (phase == "mature")
                {
                    matureMax = double.IsNaN(matureMax) ? dlog : Math.Max(matureMax, dlog);
                    if (firstMature < 0) firstMature = from;
                }
                csv.WriteLine(string.Join(',', F(p.Alpha), F(p.DeltaH), p.XHalfPct, i + 1, from, to, (from + 1) * 25, D(u[from]), D(u[to]), D(dlog), D(minY[from]), phase));
            }
            log.WriteLine($"C1y {F(p.Alpha)} {F(p.DeltaH)} {p.XHalfPct}% | {string.Join(' ', mastery.Select(D))} | mature max {D(matureMax)} from month {firstMature}");
        }
    }

    // ------------------------------------------------------------------ the line stand (C2, C1)

    internal readonly record struct LineResult(long TFirst, long T07, double YieldEnd, string Bottleneck);

    /// <summary>
    /// One actor's site from the scenario, alone (no rival: <c>world_lead = 0</c>, catch-up off). Known processes =
    /// the actor's start PRC ∪ the PRC of the target's requires-closure ∪ the target. A product-layer stand-in runs
    /// fixed lines at a constant <c>rate</c> (n_p = 1): <c>solo</c> = the target only, <c>cluster</c> = every known
    /// process of the target's cluster, <c>edge</c> = solo with the target cluster's culture raised to <c>r_d − 0.9</c>
    /// (a line that has just become admissible), C1 = a given prc_set. A line reports only while its process is admitted.
    /// Tooling: a constant capability budget <c>0.25·budget_index</c> per month, spent in canonical cell order on
    /// cells of running clusters up to <c>min(M_cap, max r_d)</c> of the running processes (stand rule for AI §4).
    /// No workforce requests (<c>act = 1</c>), no quality programme, no licence.
    /// </summary>
    internal sealed class LineStand : IIndustryInputs
    {
        private static readonly Fx6 CapShare = Fx6.FromRaw(250_000);
        private readonly IndustryLayout _l;
        private readonly IndustryParams _p;
        private readonly bool[] _known;
        private readonly bool[] _running;
        private readonly int _target;
        private readonly Fx6 _rate, _hcap, _w, _capBudget;
        private readonly IndustrySystem _ind;
        private readonly TickRunner _runner;
        private readonly TickCalendar _cal;
        private readonly (int Number, long BaseRaw)[]? _prcSet;
        private readonly Fx6[] _m0;

        public LineStand(GameData data, ScenarioData sc, int actor, IndustryParams ip, int target, string variant, Fx6 rate,
            (int, long)[]? prcSet = null)
        {
            _p = ip;
            _rate = rate;
            _prcSet = prcSet;
            IndustryLayout full = IndustryLayout.FromData(data.Nodes, sc, 2, 4);
            var specs = new IndustryProcessSpec[full.ProcessCount];
            for (int r = 0; r < full.ProcessCount; r++)
                specs[r] = new IndustryProcessSpec(full.Number[r], full.Clusters[full.ClusterIndex[r]], full.Discipline[r], full.Tier[r], full.Req[r]);
            _l = IndustryLayout.Create([.. full.Clusters], specs, [0], 2, IndustryLayout.SliceSkilled, 4, IndustryLayout.SliceAlloyGrades);

            var kg = KnowledgeGraph.FromData(data.Nodes, sc.Disciplines, new KnowledgeParams());
            _known = new bool[_l.ProcessCount];
            foreach (int row in sc.KnownOf(actor))
                MarkKnown(data.Nodes.Number[row]);
            if (target > 0)
            {
                foreach (int r in Closure(kg, kg.RowOf(target)))
                    MarkKnown(kg.Number[r]);
            }
            _running = new bool[_l.ProcessCount];
            if (prcSet is not null)
            {
                foreach ((int n, long _) in prcSet)
                {
                    _known[_l.RowOf(n)] = true; // C1 reference line: the whole G2 set is known (spec §6.4)
                    _running[_l.RowOf(n)] = true;
                }
            }
            else
            {
                _target = _l.RowOf(target);
                int c = _l.ClusterIndex[_target];
                for (int r = 0; r < _l.ProcessCount; r++)
                    _running[r] = r == _target || (variant == "cluster" && _known[r] && _l.ClusterIndex[r] == c);
            }

            IndustryStart start = IndustryStart.FromScenario(sc, data.Nodes, IndustryLayout.FromData(data.Nodes, sc, 2, 4), ip);
            var own = new IndustryStart(_l);
            for (int c = 0; c < _l.Clusters.Length; c++)
            {
                for (int d = 0; d < 3; d++)
                {
                    own.H[own.Cell(0, c, d)] = start.H[start.Cell(actor, c, d)];
                    own.M[own.Cell(0, c, d)] = start.M[start.Cell(actor, c, d)];
                }
            }
            _m0 = new Fx6[_l.Clusters.Length * 3];
            for (int i = 0; i < _m0.Length; i++)
                _m0[i] = own.M[i];
            if (variant == "edge")
            {
                // Start the target's cluster at the first-yield edge: H = max(H0, r_d − 0.9) on its active dims.
                for (int d = 0; d < 3; d++)
                {
                    Fx6 edge = _l.Req[_target][d] - Fx6.FromRaw(900_000);
                    int cell = own.Cell(0, _l.ClusterIndex[_target], d);
                    if (_l.Req[_target][d] > Fx6.Zero && own.H[cell] < edge)
                        own.H[cell] = edge;
                }
            }
            for (int r = 0; r < _l.ProcessCount; r++)
                own.X[r] = start.X[actor * full.ProcessCount + r];
            _hcap = sc.Hcap[actor];
            _w = sc.W[actor];
            _capBudget = CapShare * sc.BudgetIndex[actor];

            var table = new Table(1);
            _cal = new TickCalendar(new CivilDate(sc.StartYear, sc.StartMonth, 1));
            _ind = new IndustrySystem(table, _l, own, ip, this, 1);
            var lines = new Lines(this, _ind.CreateMailbox(1, Cadence.Monthly));
            _runner = new TickRunner(table, 1, [_ind, lines], 1, 1, _cal);
        }

        private void MarkKnown(int number)
        {
            int r = _l.RowOf(number);
            if (r >= 0)
                _known[r] = true;
        }

        /// <summary>Culture-rule prototype check (culture-rule-selection.md §1.2): the core system and one month step.</summary>
        internal IndustrySystem Industry => _ind;

        internal void StepMonth(long t) => RunMonth(t);

        private void RunMonth(long t)
        {
            long last = _cal.TickOfMonth(t);
            while (_runner.Tick <= last)
                _runner.RunTick();
        }

        public LineResult RunC2(int horizon)
        {
            long first = -1, t07 = -1;
            for (long t = 0; t < horizon; t++)
            {
                RunMonth(t);
                if (first < 0 && _ind.ProcV(0, _target) > Fx6.Zero)
                    first = t;
                if (t07 < 0 && _ind.ProcYield(0, _target) >= Fx6.FromRaw(700_000))
                {
                    t07 = t;
                    break;
                }
            }
            ProcessYield y = _ind.LastYield(0, _target);
            string bn = "ramp";
            double min = 1.0;
            for (int d = 0; d < 3; d++)
            {
                if (y.IsActive(d) && y.G[d].ToDouble() < min)
                {
                    min = y.G[d].ToDouble();
                    bn = $"{(Dimension)d}:{y.Source(d)}";
                }
            }
            return new LineResult(first, t07, _ind.ProcYield(0, _target).ToDouble(), bn);
        }

        /// <summary>Unit-cost multiplier of the prc_set in each month (snapshot before the month = the month's yields).</summary>
        public (double[] U, double[] MinYield, int[] Bottleneck) RunC1(int horizon)
        {
            PrcSetItem[] items = [.. _prcSet!.Select(x => new PrcSetItem(x.Number, Fx6.One, Fx6.FromRaw(x.BaseRaw)))];
            var u = new double[horizon];
            var minY = new double[horizon];
            var bn = new int[horizon];
            for (long t = 0; t < horizon; t++)
            {
                ProductionProfile pr = _ind.Profile(0, items, t);
                u[t] = pr.UnitCostMult.ToDouble();
                bn[t] = pr.BottleneckProcess;
                double m = 1.0;
                foreach (PrcSetItem it in items)
                    m = Math.Min(m, _ind.Yield(0, _l.RowOf(it.ProcessNumber), t).Yield.ToDouble());
                minY[t] = m;
                RunMonth(t);
            }
            return (u, minY, bn);
        }

        /// <summary>"number=yield(bottleneck)" of every running process in month <paramref name="t"/>, from the snapshot.</summary>
        public string YieldsText(long t)
        {
            var parts = new List<string>();
            foreach (int r in _l.Order)
            {
                if (!_running[r])
                    continue;
                ProcessYield y = _ind.Yield(0, r, t);
                string bn = "ramp";
                double min = 1.0;
                for (int d = 0; d < 3; d++)
                {
                    if (y.IsActive(d) && y.G[d].ToDouble() < min)
                    {
                        min = y.G[d].ToDouble();
                        bn = $"{(Dimension)d}:{y.Source(d)}";
                    }
                }
                parts.Add($"{_l.Number[r]}={y.Yield.ToDouble().ToString("0.###", Inv)}({bn})");
            }
            return string.Join(' ', parts);
        }

        // ---- IIndustryInputs

        public bool KnowsProcess(int siteRow, int processRow, long month) => _known[processRow];

        public Fx6 W(int siteRow, int cluster, long month) => _w;

        public Fx6 Hcap(int siteRow, Dimension dim, long month) => _hcap;

        public bool QualityProgramme(int siteRow, long month) => false;

        public Fx6 ToolingInvest(int siteRow, int cluster, Dimension dim, long month)
        {
            // Recomputed per cell from the snapshot: pure, and the canonical cell order fixes who gets the money first.
            int mechTier = 0;
            for (int r = 0; r < _l.ProcessCount; r++)
            {
                if (_known[r] && _l.Clusters[_l.ClusterIndex[r]] == Cluster.MECH && _l.Tier[r] > mechTier
                    && _ind.Yield(siteRow, r, month).Yield > Fx6.Zero)
                    mechTier = _l.Tier[r];
            }
            Fx6 cap = Fx6.FromInt(1 + Math.Max(3, mechTier));
            Fx6 left = _capBudget;
            for (int c = 0; c < _l.Clusters.Length; c++)
            {
                for (int d = 0; d < 3; d++)
                {
                    Fx6 need = Need(siteRow, c, d, cap);
                    Fx6 give = need < left ? need : left;
                    if (c == cluster && d == (int)dim)
                        return give;
                    left -= give;
                }
            }
            return Fx6.Zero;
        }

        private Fx6 Need(int site, int c, int d, Fx6 cap)
        {
            Fx6 target = Fx6.Zero;
            for (int r = 0; r < _l.ProcessCount; r++)
            {
                if (_running[r] && _l.ClusterIndex[r] == c && _l.Req[r][d] > target)
                    target = _l.Req[r][d];
            }
            if (target > cap)
                target = cap;
            // MECH tooling is held at its start level: M_cap (A22) depends on admitted MECH processes, and a MECH
            // cell left to wear would pull the cap of every cluster down with it (smoke-t4s.md §2).
            if (_l.Clusters[c] == Cluster.MECH && _m0[c * 3 + d] > target)
                target = _m0[c * 3 + d];
            Fx6 m = _ind.ToolingM(site, c, (Dimension)d);
            Fx6 dec = m - _p.DeltaM * m;
            if (target <= dec)
                return Fx6.Zero;
            return (target - dec) * _p.CostM(IndustryMath.CostTier(m));
        }

        public Fx6 LicenceShare(int siteRow, int processRow, long month) => Fx6.Zero;

        public long WorkforceAvailable(int siteRow, int category, long month) => 0;

        public Fx6 AlloyCap(int siteRow, long month) => Fx6.Zero;

        public Fx6 AlloyDeficit(int siteRow, long month) => Fx6.Zero;

        public long KnownSince(int siteRow, int processRow, long month) => -1;

        /// <summary>Product-layer stand-in: every running, admitted process reports <c>units = rate</c>, <c>labour = rate·labour_p</c>.</summary>
        private sealed class Lines(LineStand s, IndustryMailbox box) : ITickSystem
        {
            public int SystemId => 300;

            public Cadence Cadence => Cadence.Monthly;

            public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd)
            {
                long t = context.Calendar!.MonthIndex(context.Tick);
                foreach (int r in s._l.Order)
                {
                    if (!s._running[r] || !s._known[r] || s._ind.Yield(0, r, t).Yield <= Fx6.Zero)
                        continue;
                    int n = s._l.Number[r];
                    box.ReportOutput(chunk, new OutputReport(0, n, s._rate, s._rate * Fx6.FromInt(Labour[n])));
                }
            }

            public void Merge(in TickContext context, int chunkCount) { }

            public void Post(in TickContext context) { }
        }
    }

    // ------------------------------------------------------------------ analysis helpers (doubles: outputs only)

    private readonly record struct Stats(double Mean, double Cv, double P10, double P50, double P90)
    {
        public static Stats Of(List<double> xs)
        {
            if (xs.Count == 0)
                return new Stats(double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);
            double mean = xs.Average();
            double var = xs.Sum(x => (x - mean) * (x - mean)) / Math.Max(1, xs.Count - 1);
            double[] s = [.. xs.Order()];
            return new Stats(mean, Math.Sqrt(var) / mean, Q(s, 0.1), Q(s, 0.5), Q(s, 0.9));
        }

        /// <summary>Quantiles with right-censored values as +∞ (lower median, as A29); no mean.</summary>
        public static Stats OfCensored(List<double> xs)
        {
            if (xs.Count == 0)
                return new Stats(double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);
            double[] s = [.. xs.Order()];
            return new Stats(double.NaN, double.NaN, Q(s, 0.1), Q(s, 0.5), Q(s, 0.9));
        }

        private static double Q(double[] sorted, double q) => sorted[(int)Math.Floor(q * (sorted.Length - 1))];
    }

    private static string F(long raw) => Fx6.FromRaw(raw).ToString();

    private static string D(double x) => double.IsNaN(x) ? "" : double.IsPositiveInfinity(x) ? "inf" : x.ToString("0.####", Inv);
}
