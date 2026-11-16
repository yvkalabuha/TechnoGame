using System.Collections.Immutable;
using System.Globalization;
using Singularity.Core;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Lab.Smoke;

/// <summary>
/// TASK-0054 (T12 preparation): the C1 stand of spec slice-jet-aviation.md §6.4 / §8.2 п.1 (contract §8 п.2 r9, #184–#188) →
/// <c>c1_stand.csv</c> (slice-csv-format.md §6). Deterministic, no seeds. Reference line: the G2 «superior» design of the core mod at a
/// constant pace <c>r_ref</c>; the unit cost goes through the functions of the run (<see cref="IndustryMath.Yield"/> →
/// <see cref="IndustryMath.Profile"/> → <see cref="ProductsMath.ProcessPart"/> → <see cref="ProductsMath.UnitCost"/>).
/// <list type="bullet">
/// <item>«освоение» (<c>DEV</c>): <c>X0 = 0</c>, <c>X = line_E = N</c>; <c>culture_H</c> and <c>tooling_M</c> of the GBR site of the 1945 scenario raised
/// to an admitted prc_set (#231, <see cref="DevSite"/> <c>AdmitStart</c>; arm <c>FULL_T0</c> keeps the t0 cells as a print),
/// moved month by month by the real capability system (C-B culture rule, §4.1b allocation of the item <c>D = share_D·budget</c> with an
/// empty focus, tooling plan A39 V5 with <c>B_cap = share_cap·budget</c>); month of a point <c>t = N/r_ref</c>. Arms: profile P3 (default),
/// P1, P2; <c>E ≡ 0</c>; <c>g ≡ 1</c>; GBR t0 cells.</item>
/// <item>«зрелая» (<c>MATURE</c>): <c>X0 = 6·X½</c>, <c>g = 1</c>, <c>X = X0 + line_E</c>, doublings of <c>line_E</c> 1 → … → 4096 (statistic: starts ≤ 400);
/// <c>MATURE_3X</c>: <c>X0 = 3·X½</c> (print).</item>
/// </list>
/// Channel ablations freeze the channel at the start point of the setup (yield at <c>t = 0</c> / <c>X = X0</c>; <c>line_E = 0</c>); the
/// decomposition is the symmetric one in log. Doubles appear only in the output rows.
/// </summary>
internal static class C1Stand
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public const string File = "c1_stand.csv";
    public const string Header = "setup,arm,k,n_from,n_to,min_yield,in_stat,dlog_cost,dlog_yield,dlog_asm,asm_drop,asm_over_floor_drop";
    private const int DevDoublings = 3;
    private const long MatureWindowStart = 400;
    private const long MatureLastStart = 4096;
    private static readonly Fx6 DevAdmission = Fx6.FromRaw(250_000);

    public static int Run(string modDir, string outDir, TextWriter log) => Run(modDir, outDir, log, new IndustryParams());

    /// <summary>One stand run with the capability parameters <paramref name="ip"/> (a calibration point of stage C1: ALPHA, X_SCALE, …; <see cref="StandPoint"/>).</summary>
    public static int Run(string modDir, string outDir, TextWriter log, IndustryParams ip)
    {
        GameData data = ModLoader.Load(modDir);
        ScenarioData sc = data.Scenario("jet_slice_1945");
        Directory.CreateDirectory(outDir);
        var line = new ReferenceLine(data, sc, ip);
        using var csv = new StreamWriter(Path.Combine(outDir, File));
        csv.WriteLine(Header);
        log.WriteLine($"c1-stand scenario_hash={sc.Hash} data_hash={data.DataHash} G2 superior prc_set={string.Join(' ', line.PrcSet)} af_class={line.AfClass} r_ref={line.RRef}");

        // ---- DEV
        var devArms = new List<(string Arm, Func<long, Fx6[]> Yields)>();
        // #231: the setup starts from a culture at which the whole prc_set is admitted (yield >= y_admit); FULL_T0 is the GBR t0 culture (print).
        foreach ((string arm, PolicyProfile profile, bool e0, bool admit) in new[]
        {
            ("FULL", PolicyProfile.Adaptive, false, true), ("FULL_P1", PolicyProfile.Quality, false, true), ("FULL_P2", PolicyProfile.Mass, false, true),
            ("FULL_E0", PolicyProfile.Adaptive, true, true), ("FULL_T0", PolicyProfile.Adaptive, false, false),
        })
        {
            var site = new DevSite(data, sc, line, profile, e0, admit, ip);
            Fx6[][] byMonth = site.Run(DevDoublings);
            devArms.Add((arm, n => byMonth[(int)(n / line.RRefUnits)]));
            log.WriteLine($"DEV {arm}: {site.Describe()}");
        }
        devArms.Add(("FULL_G1", n => line.YieldsG1(Fx6.FromInt(n))));
        var devFull = devArms[0].Yields;
        Fx6[] devStart = devFull(0);
        long n0 = line.RRefUnits;
        for (int k = 1; k <= DevDoublings; k++)
        {
            long n = n0 << (k - 1);
            double minY = Min(devFull(n));
            bool inStat = devFull(n).Min() >= DevAdmission;
            foreach ((string arm, Func<long, Fx6[]> y) in devArms)
            {
                Fx6[] y0 = arm == "FULL_G1" ? line.YieldsG1(Fx6.Zero) : y(0);
                Write(csv, line, "DEV", arm, k, n, Min(y(n)), arm == "FULL" ? inStat : Min(y(n)) >= 0.25, y, y0, 0);
            }
            WriteAblations(csv, line, "DEV", k, n, minY, inStat, devFull, devStart, 0);
        }

        // ---- MATURE (X0 = 6·X½, g = 1) and MATURE_3X (print)
        foreach ((string setup, int x0) in new[] { ("MATURE", 6), ("MATURE_3X", 3) })
        {
            Func<long, Fx6[]> y = e => line.YieldsMature(x0, e);
            Fx6[] y0 = y(0);
            int k = 0;
            for (long e = 1; e <= MatureLastStart; e *= 2)
            {
                k++;
                bool inStat = e <= MatureWindowStart;
                Write(csv, line, setup, "FULL", k, e, Min(y(e)), inStat, y, y0, 0);
                if (setup == "MATURE")
                    WriteAblations(csv, line, setup, k, e, Min(y(e)), inStat, y, y0, 0);
            }
            log.WriteLine($"{setup}: min process yield at line_E = 0: {Min(y0):0.000}");
        }
        return 0;
    }

    private static void Write(StreamWriter csv, ReferenceLine line, string setup, string arm, int k, long n, double minY, bool inStat,
        Func<long, Fx6[]> y, Fx6[] y0, long e0)
    {
        // dlog = log c(n) − log c(2n): positive = the unit cost falls (format §6, the sign fixed by T12 preparation).
        double D(bool yc, bool ac) => line.LogCost(yc ? y(n) : y0, ac ? n : e0) - line.LogCost(yc ? y(2 * n) : y0, ac ? 2 * n : e0);
        double full = D(true, true), none = D(false, false);
        double dy = 0.5 * ((full - D(false, true)) + (D(true, false) - none));
        double da = 0.5 * ((full - D(true, false)) + (D(false, true) - none));
        (double asm, double over) = line.AssemblyDrops(n);
        csv.WriteLine(string.Join(',', setup, arm, k, n, 2 * n, R(minY), inStat ? "1" : "0", R(full), R(dy), R(da), R(asm), R(over)));
    }

    // NO_YIELD / NO_ASM / NONE of the default arm: their own dlog_cost; the decomposition columns stay empty.
    private static void WriteAblations(StreamWriter csv, ReferenceLine line, string setup, int k, long n, double minY, bool inStat,
        Func<long, Fx6[]> y, Fx6[] y0, long e0)
    {
        foreach ((string arm, bool yc, bool ac) in new[] { ("NO_YIELD", false, true), ("NO_ASM", true, false), ("NONE", false, false) })
        {
            double d = line.LogCost(yc ? y(n) : y0, ac ? n : e0) - line.LogCost(yc ? y(2 * n) : y0, ac ? 2 * n : e0);
            (double asm, double over) = ac ? line.AssemblyDrops(n) : (0.0, 0.0);
            csv.WriteLine(string.Join(',', setup, arm, k, n, 2 * n, R(minY), inStat ? "1" : "0", R(d), "", "", R(asm), R(over)));
        }
    }

    private static double Min(Fx6[] y) => y.Min().ToDouble();

    private static string R(double x) => x.ToString("R", Inv);

    /// <summary>The G2 «superior» reference line of the core mod: prc_set, blocks, cost card (cf. <c>ProductsChannelBudgetTests</c>).</summary>
    internal sealed class ReferenceLine
    {
        private readonly ProductsLayout _l;
        private readonly IndustryLayout _il;
        private readonly IndustryParams _p;
        private readonly int[] _alts;
        private readonly int[] _pu, _af;
        private readonly int _q;

        public ReferenceLine(GameData data, ScenarioData sc, IndustryParams ip)
        {
            _p = ip;
            _il = IndustryLayout.FromData(data.Nodes, sc, 2, 64);
            _l = ProductsLayout.FromData(data.Nodes, data.ProductClasses, "jet_fighter", _il, 2, sc.StartYear, 33, sc.MaxDesigns);
            _alts = new int[_l.SlotCount];
            if (!DesignRules.Superior(_l, 2, _ => true, _alts))
                throw new InvalidOperationException("No G2 «superior» composition in the core mod.");
            PrcSet = DesignRules.PrcSet(_l, _alts);
            _pu = DesignRules.Block(_l, PrcSet, airframe: false);
            _af = DesignRules.Block(_l, PrcSet, airframe: true);
            AfClass = DesignRules.AfClass(_l, _alts);
            _q = new ProductsParams().LearningQ10;
            RRef = _l.RRef;
            RRefUnits = _l.RRef.Raw / Fx6.Scale;
            if (RRefUnits * Fx6.Scale != _l.RRef.Raw || RRefUnits <= 0)
                throw new InvalidOperationException($"r_ref = {_l.RRef} is not a whole number of units.");
        }

        public int[] PrcSet { get; }
        public int AfClass { get; }
        public Fx6 RRef { get; }
        public long RRefUnits { get; }
        public ProductsLayout Products => _l;
        public IndustryLayout Industry => _il;

        /// <summary>Yields of the prc_set (order of <see cref="PrcSet"/>) at <c>H = M = r_d</c> (g ≡ 1) and <c>X = x</c>.</summary>
        public Fx6[] YieldsG1(Fx6 x) => [.. PrcSet.Select(n => G1(n, x))];

        /// <summary>«зрелая»: <c>X = x0Mult·X½ + e</c>, g ≡ 1.</summary>
        public Fx6[] YieldsMature(int x0Mult, long e) =>
            [.. PrcSet.Select(n => G1(n, Fx6.FromInt(x0Mult) * _p.XHalf(_il.Tier[_il.RowOf(n)]) + Fx6.FromInt(e)))];

        private Fx6 G1(int number, Fx6 x)
        {
            int r = _il.RowOf(number);
            Dims3 req = _il.Req[r];
            return IndustryMath.Yield(req, req, req, x, _p.XHalf(_il.Tier[r]), Fx6.Zero, _p.Lambda0).Yield;
        }

        /// <summary>Yield of one process from a site's cells (DEV): the cluster's <c>H</c>, <c>M</c>, and <c>X</c> given by the setup.</summary>
        public Fx6 YieldAt(int number, Dims3 h, Dims3 m, Fx6 x)
        {
            int r = _il.RowOf(number);
            return IndustryMath.Yield(_il.Req[r], h, m, x, _p.XHalf(_il.Tier[r]), Fx6.Zero, _p.Lambda0).Yield;
        }

        public int ClusterOf(int number) => _il.ClusterIndex[_il.RowOf(number)];

        /// <summary><c>log unit_cost</c> at the yields <paramref name="y"/> (order of <see cref="PrcSet"/>) and <c>line_E = e</c>.</summary>
        public double LogCost(Fx6[] y, long e)
        {
            Fx6 U(int[] block)
            {
                if (block.Length == 0)
                    return Fx6.One;
                var items = new ProfileItem[block.Length];
                for (int i = 0; i < block.Length; i++)
                {
                    int k = _l.ProcIndex(block[i]);
                    items[i] = new ProfileItem(block[i], ProcessYield.Control(y[Array.IndexOf(PrcSet, block[i])]), true, _l.ProcN[k], _l.ProcBaseCost[k]);
                }
                return IndustryMath.Profile(items, _p.Eps).UnitCostMult;
            }
            int pr = DesignRules.PuRank(_l, _alts);
            Fx6 sumPu = DesignRules.BaseCostSum(_l, _pu), sumAf = DesignRules.BaseCostSum(_l, _af);
            var card = new CostCard(_l.HFirst[AfClass - 1], _l.HFloor[AfClass - 1], _l.WageKh, _l.MaterialsFloor[AfClass - 1],
                ProductsMath.ProcessPart(sumPu, sumPu == Fx6.Zero ? Fx6.One : U(_pu), _l.ProcScalePu[pr - 1], sumAf, sumAf == Fx6.Zero ? Fx6.One : U(_af), _l.ProcScaleAf[AfClass - 1]),
                _q);
            return Math.Log(ProductsMath.UnitCost(card, Fx6.FromInt(e)).ToDouble());
        }

        /// <summary>Fall of the full <c>assembly_hours</c> and of <c>assembly_hours − h_floor</c> over <c>line_E</c> n → 2n (fractions).</summary>
        public (double Full, double OverFloor) AssemblyDrops(long n)
        {
            Fx6 hFirst = _l.HFirst[AfClass - 1], hFloor = _l.HFloor[AfClass - 1];
            double h1 = ProductsMath.AssemblyHours(Fx6.FromInt(n), hFirst, hFloor, _q).ToDouble();
            double h2 = ProductsMath.AssemblyHours(Fx6.FromInt(2 * n), hFirst, hFloor, _q).ToDouble();
            double f = hFloor.ToDouble();
            return (1 - h2 / h1, 1 - (h2 - f) / (h1 - f));
        }
    }

    /// <summary>
    /// The GBR site of the 1945 scenario alone, with the real capability system and the C-B culture rule (as main), one line at <c>r_ref</c>
    /// of the reference prc_set (stage С). Stand inputs (not the slice's): only this line plans volume (<c>v_exp = max(proc_v, r_ref·n_p·labour_p)</c>,
    /// A37 stage С), no project in П/О, no other line of GBR, no rival (catch-up off), no licence, no quality programme, no workforce requests.
    /// </summary>
    [SingleLayerStand("C1 stand (TASK-0054): the capability layer of one site with a fixed reference line; not evidence for the assembled slice")]
    internal sealed class DevSite : IIndustryInputs
    {
        private readonly ReferenceLine _line;
        private readonly IndustryLayout _l;
        private readonly IndustryParams _p;
        private readonly bool[] _known;
        private readonly bool[] _inSet;
        private readonly IndustrySystem _ind;
        private readonly TickRunner _runner;
        private readonly TickCalendar _cal;
        private readonly PolicyParams _pp = new();
        private readonly BudgetShares _shares;
        private readonly bool _e0;
        private readonly Fx6[] _budget;
        private readonly Fx6 _w, _hcap;
        private readonly int[] _setRows;

        public DevSite(GameData data, ScenarioData sc, ReferenceLine line, PolicyProfile profile, bool effortZero, bool admittedStart, IndustryParams ip)
        {
            _p = ip;
            _line = line;
            _e0 = effortZero;
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
                _known[r] = true;   // spec §6.4: the whole prc_set of the reference line is known
                _inSet[r] = true;
            }
            IndustryStart start = IndustryStart.FromScenario(sc, data.Nodes, full, _p);
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
                own.X[r] = _inSet[r] ? Fx6.Zero : start.X[gbr * full.ProcessCount + r];   // «освоение»: X0 = 0 on the prc_set
            if (admittedStart)
                Gap = AdmitStart(own);
            _w = sc.W[gbr];
            _hcap = sc.Hcap[gbr];
            _budget = new Fx6[64];
            _budget[0] = sc.BudgetIndex[gbr];
            for (int t = 1; t < _budget.Length; t++)
                _budget[t] = BudgetMath.Next(_budget[t - 1], sc.GrowthPa[gbr]);

            var table = new Table(1);
            _cal = new TickCalendar(new CivilDate(sc.StartYear, sc.StartMonth, 1));
            _ind = new IndustrySystem(table, _l, own, _p, this, 1, new CbCultureRule());
            var lines = new Lines(this, _ind.CreateMailbox(1, Cadence.Monthly));
            _runner = new TickRunner(table, 1, [_ind, lines], 1, 1, _cal);
        }

        /// <summary>
        /// #231 (spec §8.2 п.1 v0.27): the start culture of «освоение». Every cell (cluster, dimension) that an active dimension of a prc_set process
        /// uses is raised to <c>max(own, r_d − gap)</c> in <c>culture_H</c> and <c>tooling_M</c> alike (the R-H-MS pairing of H and M), with one gap for
        /// all cells: the largest gap on the 0,01 grid at which the lowest yield of the prc_set at the start point (<c>X = 0</c>, month 0) is ≥ <c>y_admit</c>.
        /// Cells already above stay. Returns the gap; +∞ (no raise) if the GBR t0 cells already admit the set.
        /// </summary>
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
                    if (_line.YieldAt(_line.PrcSet[i], h, m, Fx6.Zero) < YAdmit)
                        return false;
                }
                return true;
            }
            if (Admits())
                return double.PositiveInfinity;
            for (long gap = 300; gap >= 0; gap--)   // 3,00 … 0,00 in steps of 0,01
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

        /// <summary><c>y_admit</c> (#231) = the serial admission 0,25 of the C1 filter (spec §8.2 п.1, <c>anchors_review.md</c> §4) [Предп.].</summary>
        public static readonly Fx6 YAdmit = Fx6.FromRaw(250_000);

        /// <summary>The gap of <see cref="AdmitStart"/> (+∞: GBR t0 already admits; NaN: the GBR t0 arm).</summary>
        public double Gap { get; } = double.NaN;

        /// <summary>Yields of the prc_set at the point of each month <c>t = 0…8·…</c>: the cells of the snapshot that the compute of month t reads, <c>X = t·r_ref</c>.</summary>
        public Fx6[][] Run(int doublings)
        {
            int months = 1 << doublings;   // N = r_ref·t up to 2^doublings·r_ref
            var result = new Fx6[months + 1][];
            Snapshot0 = Snapshot(Fx6.Zero);
            for (long t = 0; t <= months; t++)
            {
                result[t] = Snapshot(Fx6.FromInt(t * _line.RRefUnits));
                if (t < months)
                    RunMonth(t);
            }
            return result;
        }

        private Fx6[] Snapshot0 { get; set; } = [];

        private Fx6[] Snapshot(Fx6 x)
        {
            var y = new Fx6[_line.PrcSet.Length];
            for (int i = 0; i < y.Length; i++)
            {
                int c = _l.ClusterIndex[_setRows[i]];
                y[i] = _line.YieldAt(_line.PrcSet[i], Cells(c, false), Cells(c, true), x);
            }
            return y;
        }

        private Dims3 Cells(int c, bool tooling) => tooling
            ? new Dims3(_ind.ToolingM(0, c, Dimension.Precision), _ind.ToolingM(0, c, Dimension.Purity), _ind.ToolingM(0, c, Dimension.QualityControl))
            : new Dims3(_ind.CultureH(0, c, Dimension.Precision), _ind.CultureH(0, c, Dimension.Purity), _ind.CultureH(0, c, Dimension.QualityControl));

        public string Describe()
        {
            var parts = new List<string>();
            parts.Add($"start gap {Gap}");
            parts.Add("yield(t=0, X=0) " + string.Join(' ', _setRows.Select((r, i) => $"{_l.Number[r]}={Snapshot0[i]}")));
            parts.Add("yield(t=8, own X) " + string.Join(' ', _setRows.Select(r => $"{_l.Number[r]}={_ind.Yield(0, r, 8).Yield}")));
            foreach (int c in _setRows.Select(r => _l.ClusterIndex[r]).Distinct().Order())
                parts.Add($"{_l.Clusters[c]} H={Cells(c, false)} M={Cells(c, true)}");
            return string.Join("; ", parts);
        }

        private void RunMonth(long t)
        {
            long last = _cal.TickOfMonth(t);
            while (_runner.Tick <= last)
                _runner.RunTick();
        }

        private Fx6 Budget(long month) => _budget[Math.Min(month, _budget.Length - 1)];

        // ---- IIndustryInputs (stand rules, see the class summary)

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
            ToolingPlan.Compute(_ind, this, _pp.MKeep, 0, month, rest, _shares.Capability * Budget(month), new ToolingTargetSpec());

        public Fx6 EffortBudget(int actorRow, long month) => _e0 ? Fx6.Zero : _shares.Effort * Budget(month);

        public int EffortFocus(int actorRow, int k, long month) => -1;

        public Fx6 PlannedVolume(int siteRow, int processRow, long month)
        {
            Fx6 v = _ind.ProcV(siteRow, processRow);
            if (!_inSet[processRow])
                return v;
            Fx6 plan = Plan(processRow);
            return v > plan ? v : plan;
        }

        private Fx6 Plan(int processRow)
        {
            int k = _line.Products.ProcIndex(_l.Number[processRow]);
            return _line.RRef * _line.Products.ProcN[k] * _line.Products.ProcLabour[k];
        }

        public Fx6 EffortMultiplier(int actorRow, int processRow, long month) => Fx6.One;

        // The stand line counts as a series (stage С): planned, and V_C = r_ref·n·labour of its prc_set is the growth nominal (G-C1 checks the V_C branch only, contract §4.1a r12).
        public bool Planned(int siteRow, int processRow, long month) => _inSet[processRow];

        public Fx6 GrowthNominal(int siteRow, int processRow, long month) => _inSet[processRow] ? Plan(processRow) : Fx6.Zero;

        public Fx6 LicenceShare(int siteRow, int processRow, long month) => Fx6.Zero;

        public long WorkforceAvailable(int siteRow, int category, long month) => 0;

        public Fx6 AlloyCap(int siteRow, long month) => Fx6.Zero;

        public Fx6 AlloyDeficit(int siteRow, long month) => Fx6.Zero;

        public long KnownSince(int siteRow, int processRow, long month) => -1;

        /// <summary>The reference line: every month <c>units = r_ref·n_p</c>, <c>hours = units·labour_p</c> on each process of the prc_set (constant pace).</summary>
        private sealed class Lines(DevSite s, IndustryMailbox box) : ITickSystem
        {
            public int SystemId => 301;

            public Cadence Cadence => Cadence.Monthly;

            public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd)
            {
                long t = context.Calendar!.MonthIndex(context.Tick);
                foreach (int r in s._setRows)
                {
                    // Invariant I3: a process reports output only with yield > 0 (a slice line would not run at all, F17).
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
