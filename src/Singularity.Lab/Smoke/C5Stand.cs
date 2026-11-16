using System.Globalization;
using Singularity.Core;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Policy;
using Singularity.Kernel;

namespace Singularity.Lab.Smoke;

/// <summary>
/// C5 stand (test #30, «veteran against newcomer»; spec slice-jet-aviation.md v0.33 §8.2 п.1 C5, decision #248). One GBR site of the 1945
/// scenario with the real capability system, the reference line of the C1 stand (G2 «superior» prc_set, pace <c>r_ref</c>; rules of the
/// C1 «освоение» site: series line with <c>Planned</c> and <c>GrowthNominal = V_C</c>, effort <c>D = share_D·budget</c>, tooling A39 V5 with
/// <c>B_cap = share_cap·budget</c>). The newcomer starts from the GBR t0 cells, <c>X = 0</c> on the prc_set. An ascent from month <c>t_s</c>
/// lasts <c>n = min τ ∈ [0; horizon]</c> with the lowest prc_set yield read by the compute of month <c>t_s + τ</c> ≥ 0,7; none — censored.
/// The veteran is the same run: after the first ascent (<c>n_new</c>) the line is idle for <c>I</c> months (no output, not planned, no
/// growth nominal, <c>D = 0</c>, <c>B_cap = 0</c>), then restarts at <c>R = n_new + I</c> with the budget path from τ = 0; <c>n_vet</c> is the
/// ascent from R. <c>d_C5 = 0</c> only if neither ascent is censored and <c>n_vet &lt; n_new</c>. Only arm MAIN (P3, t0, I = 120,
/// horizon 383) is the statistic; the others are prints. Deterministic: the capability layer reads no RNG.
/// </summary>
internal static class C5Stand
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public const int Horizon = 383;
    public const int MainIdle = 120;
    public static readonly Fx6 Threshold = Fx6.FromRaw(700_000);
    public const string StandHeader = "arm,in_stat,profile,start,idle,horizon,threshold,n_new,cens_new,n_vet,cens_vet,restart_month,y_restart,ratio,d_c5";

    private sealed record Arm(string Name, PolicyProfile Profile, bool Admit, int Idle);

    private static readonly Arm[] Arms =
    [
        new("MAIN", PolicyProfile.Adaptive, false, MainIdle),
        new("P1", PolicyProfile.Quality, false, MainIdle),
        new("P2", PolicyProfile.Mass, false, MainIdle),
        new("IDLE60", PolicyProfile.Adaptive, false, 60),
        new("IDLE240", PolicyProfile.Adaptive, false, 240),
        new("ADMIT", PolicyProfile.Adaptive, true, MainIdle),
    ];

    /// <summary>
    /// One stand run with the capability parameters <paramref name="ip"/>. <paramref name="horizon"/> &lt; 383 is a smoke run (column
    /// <c>horizon</c>; <c>in_stat</c> = 0 everywhere), and so is a <paramref name="threshold"/> other than 0,7: the statistic is defined only at 383 and 0,7. Returns 0; <c>d_c5</c> is in the CSV.
    /// </summary>
    public static int Run(string modDir, string outDir, TextWriter log, IndustryParams ip, int horizon = Horizon, Fx6? threshold = null)
    {
        Fx6 thr = threshold ?? Threshold;
        if (thr <= Fx6.Zero || thr > Fx6.One)
            throw new UsageException("--threshold must be in (0; 1].");
        if (horizon < 1 || horizon > Horizon)
            throw new UsageException($"--horizon must be in [1, {Horizon}].");
        GameData data = ModLoader.Load(modDir);
        ScenarioData sc = data.Scenario("jet_slice_1945");
        var line = new C1Stand.ReferenceLine(data, sc, ip);
        Directory.CreateDirectory(outDir);
        log.WriteLine($"c5-stand scenario_hash={sc.Hash} data_hash={data.DataHash} G2 superior prc_set={string.Join(' ', line.PrcSet)} r_ref={line.RRef} horizon={horizon} threshold={thr}");
        using var stand = new StreamWriter(Path.Combine(outDir, "c5_stand.csv"));
        using var trace = new StreamWriter(Path.Combine(outDir, "c5_trace.csv"));
        using var proc = new StreamWriter(Path.Combine(outDir, "c5_process.csv"));
        using var cells = new StreamWriter(Path.Combine(outDir, "c5_cells.csv"));
        stand.WriteLine(StandHeader);
        trace.WriteLine("arm,phase,t,tau,min_yield,argmin");
        proc.WriteLine("arm,process,y_0,y_n_new,y_restart,y_vet");
        cells.WriteLine("arm,cluster,dim,h_0,h_n_new,h_restart,h_kept,m_0,m_n_new,m_restart,m_kept");
        foreach (Arm arm in Arms)
        {
            var site = new Site(data, sc, line, arm.Profile, arm.Admit, ip, horizon);
            Outcome o = site.Run(arm.Idle, horizon, thr, (phase, t, tau, y, argmin) =>
                trace.WriteLine(string.Join(',', arm.Name, phase, I(t), I(tau), R(y), I(argmin))));
            bool inStat = arm.Name == "MAIN" && horizon == Horizon && thr == Threshold;
            int d = o.NNew is long nn && o.NVet is long nv && nv < nn ? 0 : 1;
            stand.WriteLine(string.Join(',', arm.Name, inStat ? "1" : "0", arm.Profile, arm.Admit ? "ADMIT" : "T0", I(arm.Idle), I(horizon), thr.ToString(),
                O(o.NNew), o.NNew is null ? "1" : "0", O(o.NVet), o.NNew is not null && o.NVet is null ? "1" : (o.NNew is null ? "" : "0"),
                O(o.Restart), o.YRestart is double yr ? R(yr) : "",
                o.NNew is long a && o.NVet is long b && a > 0 ? R((double)b / a) : "", I(d)));
            for (int i = 0; i < line.PrcSet.Length; i++)
            {
                proc.WriteLine(string.Join(',', arm.Name, I(line.PrcSet[i]), Y(o.Y0, i), Y(o.YNew, i), Y(o.YRestart0, i), Y(o.YVet, i)));
            }
            foreach (CellRow c in o.Cells)
            {
                cells.WriteLine(string.Join(',', arm.Name, c.Cluster, c.Dim, V(c.H, 0), V(c.H, 1), V(c.H, 2), Kept(c.H), V(c.M, 0), V(c.M, 1), V(c.M, 2), Kept(c.M)));
            }
            log.WriteLine($"{arm.Name}: start gap {site.Gap}; n_new {O(o.NNew)}; restart {O(o.Restart)}; n_vet {O(o.NVet)}; d {d}{(inStat ? " (statistic)" : " (print)")}");
        }
        return 0;
    }

    private static string Y(Fx6[]? y, int i) => y is null ? "" : R(y[i].ToDouble());

    private static string V(double?[] x, int i) => x[i] is double v ? R(v) : "";

    // Share of the first-ascent gain kept over the idle: (x(R) − x(0)) / (x(n_new) − x(0)); empty without a gain.
    private static string Kept(double?[] x) =>
        x[1] is double xn && x[2] is double xr && xn != x[0] ? R((xr - x[0]!.Value) / (xn - x[0]!.Value)) : "";

    private static string I(long v) => v.ToString(Inv);

    private static string O(long? v) => v is long x ? I(x) : "";

    private static string R(double x) => x.ToString("R", Inv);

    internal sealed record CellRow(string Cluster, string Dim, double?[] H, double?[] M);

    internal sealed record Outcome(long? NNew, long? NVet, long? Restart, double? YRestart, Fx6[] Y0, Fx6[]? YNew, Fx6[]? YRestart0, Fx6[]? YVet,
        List<CellRow> Cells);

    /// <summary>
    /// The GBR site alone (cf. <see cref="C1Stand.DevSite"/>, same stand inputs) with an idle phase. The budget path is indexed by the month
    /// since the start of the current ascent, so the restart gets the same budget path as the first ascent.
    /// </summary>
    [SingleLayerStand("C5 stand (TASK-0056, #248): the capability layer of one site, one line with an idle phase; not evidence for the assembled slice")]
    internal sealed class Site : IIndustryInputs
    {
        private readonly C1Stand.ReferenceLine _line;
        private readonly IndustryLayout _l;
        private readonly bool[] _known;
        private readonly bool[] _inSet;
        private readonly int[] _setRows;
        private readonly IndustrySystem _ind;
        private readonly TickRunner _runner;
        private readonly TickCalendar _cal;
        private readonly PolicyParams _pp = new();
        private readonly BudgetShares _shares;
        private readonly Fx6[] _budget;
        private readonly Fx6 _w, _hcap;
        private bool _idle;
        private long _phaseStart;

        public Site(GameData data, ScenarioData sc, C1Stand.ReferenceLine line, PolicyProfile profile, bool admittedStart, IndustryParams ip, int horizon)
        {
            _line = line;
            _shares = _pp.SharesByProfile[(int)profile];
            int gbr = sc.ActorIndex("GBR");
            IndustryLayout full = IndustryLayout.FromData(data.Nodes, sc, 2, 4);
            var specs = new IndustryProcessSpec[full.ProcessCount];
            for (int r = 0; r < full.ProcessCount; r++)
                specs[r] = new IndustryProcessSpec(full.Number[r], full.Clusters[full.ClusterIndex[r]], full.Discipline[r], full.Tier[r], full.Req[r], full.Labour[r]);
            _l = IndustryLayout.Create([.. full.Clusters], specs, [0], 2, IndustryLayout.SliceSkilled, 4, IndustryLayout.SliceAlloyGrades);
            _known = new bool[_l.ProcessCount];
            foreach (int row in sc.KnownOf(gbr))
            {
                int r = _l.RowOf(data.Nodes.Number[row]);
                if (r >= 0)
                    _known[r] = true;
            }
            _inSet = new bool[_l.ProcessCount];
            _setRows = [.. line.PrcSet.Select(n => _l.RowOf(n))];
            foreach (int r in _setRows)
            {
                _known[r] = true;
                _inSet[r] = true;
            }
            IndustryStart start = IndustryStart.FromScenario(sc, data.Nodes, full, ip);
            var own = new IndustryStart(_l);
            for (int c = 0; c < _l.Clusters.Length; c++)
            {
                for (int d = 0; d < 3; d++)
                {
                    own.H[own.Cell(0, c, d)] = start.H[start.Cell(gbr, c, d)];
                    own.M[own.Cell(0, c, d)] = start.M[start.Cell(gbr, c, d)];
                }
            }
            for (int r = 0; r < _l.ProcessCount; r++)
                own.X[r] = _inSet[r] ? Fx6.Zero : start.X[gbr * full.ProcessCount + r];   // newcomer: X0 = 0 on the prc_set
            if (admittedStart)
                Gap = AdmitStart(own);
            _w = sc.W[gbr];
            _hcap = sc.Hcap[gbr];
            _budget = new Fx6[horizon + 1];
            _budget[0] = sc.BudgetIndex[gbr];
            for (int t = 1; t < _budget.Length; t++)
                _budget[t] = BudgetMath.Next(_budget[t - 1], sc.GrowthPa[gbr]);

            var table = new Table(1);
            _cal = new TickCalendar(new CivilDate(sc.StartYear, sc.StartMonth, 1));
            _ind = new IndustrySystem(table, _l, own, ip, this, 1, new CbCultureRule());
            var lines = new Lines(this, _ind.CreateMailbox(1, Cadence.Monthly));
            _runner = new TickRunner(table, 1, [_ind, lines], 1, 1, _cal);
        }

        /// <summary>Gap of the #231 start (arm ADMIT); NaN for the t0 start, +∞ if t0 already admits.</summary>
        public double Gap { get; } = double.NaN;

        public Outcome Run(int idle, int horizon, Fx6 threshold, Action<string, long, long, double, int> trace)
        {
            Fx6[] y0 = Yields(0);
            var snaps = new List<(Fx6[] H, Fx6[] M)> { CellSnapshot() };
            long t = 0;
            long? nNew = Ascent("ASCENT", ref t, horizon, threshold, trace);
            Fx6[]? yNew = null, yRestart = null, yVet = null;
            long? restart = null, nVet = null;
            double? yR = null;
            if (nNew is not null)
            {
                yNew = Yields(t);
                snaps.Add(CellSnapshot());
                _idle = true;
                for (int k = 0; k < idle; k++, t++)
                {
                    (double y, int arg) = MinYield(t);
                    trace("IDLE", t, k, y, arg);
                    RunMonth(t);
                }
                _idle = false;
                restart = t;
                yRestart = Yields(t);
                yR = yRestart.Min().ToDouble();
                snaps.Add(CellSnapshot());
                nVet = Ascent("RESTART", ref t, horizon, threshold, trace);
                if (nVet is not null)
                    yVet = Yields(t);
            }
            return new Outcome(nNew, nVet, restart, yR, y0, yNew, yRestart, yVet, Cells(snaps));
        }

        // Months t_s, t_s + 1, … : the yield the compute of month t reads, then month t is run; stops at the first month with Y ≥ 0,7 (not run).
        private long? Ascent(string phase, ref long t, int horizon, Fx6 threshold, Action<string, long, long, double, int> trace)
        {
            _phaseStart = t;
            for (long tau = 0; tau <= horizon; tau++, t++)
            {
                (double y, int arg) = MinYield(t);
                trace(phase, t, tau, y, arg);
                if (MinFx(t) >= threshold)   // the test in Fx6, not on the printed double
                    return tau;
                if (tau < horizon)
                    RunMonth(t);
            }
            t--;
            return null;
        }

        private Fx6 MinFx(long t) => Yields(t).Min();

        private Fx6[] Yields(long t) => [.. _setRows.Select(r => _ind.Yield(0, r, t).Yield)];

        private (double Y, int ArgMin) MinYield(long t)
        {
            Fx6[] y = Yields(t);
            int arg = 0;
            for (int i = 1; i < y.Length; i++)
            {
                if (y[i] < y[arg])
                    arg = i;
            }
            return (y[arg].ToDouble(), _line.PrcSet[arg]);
        }

        // Cells (cluster, dimension) used by an active dimension of a prc_set process (r_d > 0).
        private IEnumerable<(int Cluster, int Dim)> ActiveCells() =>
            _setRows.SelectMany(r => Enumerable.Range(0, 3).Where(d => _l.Req[r][d] > Fx6.Zero).Select(d => (_l.ClusterIndex[r], d))).Distinct().Order();

        private (Fx6[] H, Fx6[] M) CellSnapshot()
        {
            var cells = ActiveCells().ToArray();
            return ([.. cells.Select(c => _ind.CultureH(0, c.Cluster, (Dimension)c.Dim))], [.. cells.Select(c => _ind.ToolingM(0, c.Cluster, (Dimension)c.Dim))]);
        }

        private List<CellRow> Cells(List<(Fx6[] H, Fx6[] M)> snaps)
        {
            var cells = ActiveCells().ToArray();
            var rows = new List<CellRow>();
            for (int i = 0; i < cells.Length; i++)
            {
                double?[] h = [.. Enumerable.Range(0, 3).Select(k => k < snaps.Count ? snaps[k].H[i].ToDouble() : (double?)null)];
                double?[] m = [.. Enumerable.Range(0, 3).Select(k => k < snaps.Count ? snaps[k].M[i].ToDouble() : (double?)null)];
                rows.Add(new CellRow(_l.Clusters[cells[i].Cluster].ToString(), ((Dimension)cells[i].Dim).ToString(), h, m));
            }
            return rows;
        }

        private void RunMonth(long t)
        {
            long last = _cal.TickOfMonth(t);
            while (_runner.Tick <= last)
                _runner.RunTick();
        }

        // #231 start (arm ADMIT only): the same rule as C1Stand.DevSite.AdmitStart.
        private double AdmitStart(IndustryStart own)
        {
            Fx6[] h0 = [.. own.H], m0 = [.. own.M];
            bool Admits()
            {
                for (int i = 0; i < _setRows.Length; i++)
                {
                    int c = _l.ClusterIndex[_setRows[i]];
                    var h = new Dims3(own.H[own.Cell(0, c, 0)], own.H[own.Cell(0, c, 1)], own.H[own.Cell(0, c, 2)]);
                    var m = new Dims3(own.M[own.Cell(0, c, 0)], own.M[own.Cell(0, c, 1)], own.M[own.Cell(0, c, 2)]);
                    if (_line.YieldAt(_line.PrcSet[i], h, m, Fx6.Zero) < C1Stand.DevSite.YAdmit)
                        return false;
                }
                return true;
            }
            if (Admits())
                return double.PositiveInfinity;
            for (long gap = 300; gap >= 0; gap--)
            {
                h0.CopyTo(own.H, 0);
                m0.CopyTo(own.M, 0);
                foreach (int r in _setRows)
                {
                    int c = _l.ClusterIndex[r];
                    for (int d = 0; d < 3; d++)
                    {
                        Fx6 req = _l.Req[r][d];
                        if (req <= Fx6.Zero)
                            continue;
                        Fx6 floor = req - Fx6.FromRaw(gap * 10_000);
                        int cell = own.Cell(0, c, d);
                        if (own.H[cell] < floor)
                            own.H[cell] = floor;
                        if (own.M[cell] < floor)
                            own.M[cell] = floor;
                    }
                }
                if (Admits())
                    return gap / 100.0;
            }
            throw new InvalidOperationException("No culture admits the prc_set even at H = M = r_d.");
        }

        private Fx6 Budget(long month) => _budget[Math.Clamp(month - _phaseStart, 0, _budget.Length - 1)];

        // ---- IIndustryInputs (stand rules: C1Stand.DevSite while producing; idle — no plan, no nominal, no effort, no tooling money)

        public bool KnowsProcess(int siteRow, int processRow, long month) => _known[processRow];

        public Fx6 W(int siteRow, int cluster, long month) => _w;

        public Fx6 Hcap(int siteRow, Dimension dim, long month) => _hcap;

        public bool QualityProgramme(int siteRow, long month) => false;

        public Fx6 ToolingInvest(int siteRow, int cluster, Dimension dim, long month, Fx6 restActor) =>
            Plan(month, restActor).Applied(siteRow, cluster, dim);

        public Fx6 ToolingInvest(int siteRow, int cluster, Dimension dim, long month, Fx6 restActor, out Fx6 unmet)
        {
            ToolingPlanResult plan = Plan(month, restActor);
            unmet = plan.Unmet(siteRow, cluster, dim);
            return plan.Applied(siteRow, cluster, dim);
        }

        private ToolingPlanResult Plan(long month, Fx6 rest) =>
            ToolingPlan.Compute(_ind, this, _pp.MKeep, 0, month, rest, _idle ? Fx6.Zero : _shares.Capability * Budget(month), new ToolingTargetSpec());

        public Fx6 EffortBudget(int actorRow, long month) => _idle ? Fx6.Zero : _shares.Effort * Budget(month);

        public int EffortFocus(int actorRow, int k, long month) => -1;

        public Fx6 PlannedVolume(int siteRow, int processRow, long month)
        {
            Fx6 v = _ind.ProcV(siteRow, processRow);
            if (_idle || !_inSet[processRow])
                return v;
            Fx6 plan = LinePlan(processRow);
            return v > plan ? v : plan;
        }

        private Fx6 LinePlan(int processRow)
        {
            int k = _line.Products.ProcIndex(_l.Number[processRow]);
            return _line.RRef * _line.Products.ProcN[k] * _line.Products.ProcLabour[k];
        }

        public Fx6 EffortMultiplier(int actorRow, int processRow, long month) => Fx6.One;

        public bool Planned(int siteRow, int processRow, long month) => !_idle && _inSet[processRow];

        public Fx6 GrowthNominal(int siteRow, int processRow, long month) => !_idle && _inSet[processRow] ? LinePlan(processRow) : Fx6.Zero;

        public Fx6 LicenceShare(int siteRow, int processRow, long month) => Fx6.Zero;

        public long WorkforceAvailable(int siteRow, int category, long month) => 0;

        public Fx6 AlloyCap(int siteRow, long month) => Fx6.Zero;

        public Fx6 AlloyDeficit(int siteRow, long month) => Fx6.Zero;

        public long KnownSince(int siteRow, int processRow, long month) => -1;

        /// <summary>The reference line: while producing, every month <c>units = r_ref·n_p</c>, <c>hours = units·labour_p</c> on each prc_set process with yield &gt; 0 (I3).</summary>
        private sealed class Lines(Site s, IndustryMailbox box) : ITickSystem
        {
            public int SystemId => 302;

            public Cadence Cadence => Cadence.Monthly;

            public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd)
            {
                if (s._idle)
                    return;
                long t = context.Calendar!.MonthIndex(context.Tick);
                foreach (int r in s._setRows)
                {
                    if (s._ind.Yield(0, r, t).Yield <= Fx6.Zero)
                        continue;
                    int n = s._l.Number[r];
                    int k = s._line.Products.ProcIndex(n);
                    Fx6 units = s._line.RRef * s._line.Products.ProcN[k];
                    box.ReportOutput(chunk, new OutputReport(0, n, units, units * s._line.Products.ProcLabour[k]));
                }
            }

            public void Merge(in TickContext context, int chunkCount) { }

            public void Post(in TickContext context) { }
        }
    }
}
