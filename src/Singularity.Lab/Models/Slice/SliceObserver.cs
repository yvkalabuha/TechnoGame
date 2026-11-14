using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Core.WhyNot;
using Singularity.Kernel;
using Singularity.Session;

namespace Singularity.Lab.Models.Slice;

/// <summary>A broken check of slice-csv-format.md v0.5: the run is <c>failed</c>, never written with a wrong or empty value.</summary>
public sealed class SliceFormatException(string message) : Exception(message);

/// <summary>
/// Writes the tables of slice-csv-format.md v0.5 from the read views of one run. Reads views by property name only (SLICE_COMPOSITION §13.7 r5),
/// never writes the world. "Snapshot t" is the view the compute of month t read (post t−1, or the initial view); "post t" is the view published
/// by the monthly tick of t, with the inputs and the step traces of t (§12.1, §13.7).
/// </summary>
internal sealed class SliceObserver
{
    private static readonly string[] Great = ["USA", "GBR", "SUN"];
    private static readonly string[] ToolingClusters = ["MET", "CHEM", "MECH", "AERO"];
    private static readonly string[] KdNames = ["aero", "prop", "metal", "chem", "mech"];
    private static readonly int[] Melts = [39, 40, 41];
    private const long Yield07 = 700_000;
    private const long Units100 = 100 * Fx6.Scale;

    internal enum Role { None, W, N, Sole }

    private readonly SliceSession _s;
    private readonly IMetricSink _sink;
    private readonly bool _calib, _full;
    private readonly Dictionary<string, int> _table = new(StringComparer.Ordinal);
    private readonly SliceLayoutView _l;
    private readonly string[] _ids;
    private readonly int _nA, _nD, _nG, _tab;
    private readonly int[] _kdIndex;            // column kd_* → discipline index of the KD vector
    private readonly int[] _clusterIndex;       // column tool_unmet_* → index of Layout.Clusters
    private readonly int[] _meltRows;
    private int[] _prcRows = [];                // graph rows that are processes

    // node_times
    private long?[,] _tKnown = null!, _tFirst = null!, _t07 = null!, _tMastered = null!;
    private bool[,] _lic = null!;
    // gen_times (g = 1…G at index g − 1)
    private long?[,] _tReq = null!, _tFirstUnit = null!, _tFielded = null!;
    private long?[,] _costU100 = null!;
    private int[,] _firstSeries = null!;        // design of the first series of a generation, −2 = a series at the start, −1 = none yet
    private long[,] _cum = null!;               // cumulative output of that series, raw
    private long?[,] _tLaunch = null!;          // [a, d] first month in С (post), −1 at the start
    private long?[,] _forcedSent = null!;       // [a, d] month after whose step the harness sent start_series with force (FORCE arms)
    // summary
    private readonly StringBuilder _lead = new(), _leadAll = new(), _leadField = new();
    private readonly List<string> _hashYears = new();
    private int _leader, _leaderAll;
    private PolicyProfile[] _profiles = null!;
    // actor_year: rows of the years still open (volume and eng_short need the next 11 months)
    private readonly List<YearRow> _years = new();
    // whynot
    private readonly List<WhyRow> _why = new();
    private readonly int?[] _qcRank;
    private readonly long[] _qcRankMonth;
    // a40_c_months: the last T_ab months
    private readonly Queue<MonthRecord> _ring = new();
    // licences
    private readonly List<LicenceRow> _licences = new();
    private readonly LicenceRow?[] _openLicence;
    private readonly LapseTracker _lapse;
    private int _req;
    // counters for run.json (format §4.9, §4.7)
    private long _closeIdleOtherReason, _lapsesInferred, _licEndUnobservable, _wAfterNMonths, _closesWithoutLine, _linesWithoutMoney;

    private sealed class YearRow
    {
        public long T;
        public int Actor;
        public string[] Head = [];
        public string[] Tail = [];
        public long Volume;
        public int EngShort;
        public int Months;
        public Fx6 Budget;
    }

    private sealed class WhyRow
    {
        public long T;
        public int Actor;
        public WhyNotTarget Target;
        public string[] Cells = [];
        public WhyNotLayer Layer;
        public string Subject = "";
        public long? Closed;
    }

    private sealed class LicenceRow
    {
        public long T, Start;
        public int Seller, Buyer, Gen;
        public PolicyProfile Profile;
        public Fx6 Price;
        public long? End;
    }

    private sealed record MonthRecord(long T, ActorMonth[] Actors);

    internal sealed record ActorMonth(Fx6 Alloy, bool Deficit, Fx6 Ta, bool MoneyNull, bool TwoLines, bool WAfterN, bool MeltsZero,
        Role[] Roles, Fx6[] YMin, int[] ArgP, Fx6?[] LeftIn, Fx6?[] Training, Fx6[] Rate, LineBind[] Bind);

    public SliceObserver(SliceSession s, GameData data, string scenarioId, IMetricSink sink, IReadOnlyList<MetricTableSpec> tables, bool calibration, bool full)
    {
        _s = s;
        _sink = sink;
        _calib = calibration;
        _full = full;
        for (int i = 0; i < tables.Count; i++)
            _table[tables[i].Name] = i;
        _l = s.Layout;
        _ids = [.. _l.ActorIds];
        _nA = _ids.Length;
        _nD = _l.Designs;
        _nG = _l.Generations;
        _tab = _l.TAb;
        for (int i = 0; i < Great.Length; i++)
        {
            if (_nA <= i || _ids[i] != Great[i])
                throw new SliceFormatException($"Actor {i} is '{(_nA > i ? _ids[i] : "-")}', the format needs USA, GBR, SUN as actors 0, 1, 2 (§1).");
        }
        if (_nA != 5)
            throw new SliceFormatException($"The format has prof_* columns for 5 actors, the scenario has {_nA}.");
        ImmutableArray<Discipline> disciplines = data.Scenario(scenarioId).Disciplines;
        _kdIndex = [.. KdNames.Select(n =>
        {
            int i = -1;
            for (int k = 0; k < disciplines.Length; k++)
            {
                if (DataNames.Disciplines.First(x => x.Value == disciplines[k]).Name == n)
                    i = k;
            }
            return i >= 0 ? i : throw new SliceFormatException($"Discipline '{n}' is not in the scenario.");
        })];
        if (_l.Clusters.Length != ToolingClusters.Length || ToolingClusters.Any(c => _l.Clusters.IndexOf(c) < 0))
            throw new SliceFormatException($"The tooling ledger has columns for {string.Join(",", ToolingClusters)}, the layout has {string.Join(",", _l.Clusters)}.");
        _clusterIndex = [.. ToolingClusters.Select(c => _l.Clusters.IndexOf(c))];
        _meltRows = [.. Melts.Select(n => _l.NodeNumbers.IndexOf(n) is int r and >= 0 ? r : throw new SliceFormatException($"Melt node {n} is not in the graph."))];
        _qcRank = new int?[_nA];
        _qcRankMonth = new long[_nA];
        _openLicence = new LicenceRow?[_nA];
        _lapse = new LapseTracker(_nA);
    }

    private bool Has(string table) => _table.ContainsKey(table);

    // A heavy table keeps only its header in detail = lite (§4).
    private bool Writes(string table) => Has(table) && (_full || Array.IndexOf(SliceTables.HeavyInLite, table) < 0);

    private void Row(string table, params string[] cells)
    {
        if (Writes(table))
            _sink.Row(_table[table], cells);
    }

    private static string I(long v) => v.ToString(CultureInfo.InvariantCulture);
    private static string F(Fx6 v) => v.Raw.ToString(CultureInfo.InvariantCulture);
    private static string O(long? v) => v is long x ? I(x) : "";
    private static string B(bool v) => v ? "1" : "0";
    private static int Prof(PolicyProfile p) => (int)p + 1;

    private static string Preset(PolicyProfile p) => p switch
    {
        PolicyProfile.Quality => "SUPERIOR",
        PolicyProfile.Mass => "PRODUCIBLE",
        _ => "ADAPTIVE",
    };

    /// <summary>UPPER_SNAKE of an enum name: <c>ToolingPoolExhausted</c> → <c>TOOLING_POOL_EXHAUSTED</c>, <c>A40C</c> → <c>A40C</c>.</summary>
    internal static string Snake(string name)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i > 0 && char.IsUpper(c) && char.IsLower(name[i - 1]))
                sb.Append('_');
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------- start

    public void Start(SliceView initial)
    {
        if (initial.Month != -1 || initial.Inputs is not null || initial.Step is not null)
            throw new SliceFormatException("The initial view must have month −1 and no inputs or step.");
        _profiles = [.. initial.Actors.Select(a => a.Profile)];
        int nN = _l.NodeNumbers.Length;
        _tKnown = new long?[_nA, nN];
        _tFirst = new long?[_nA, nN];
        _t07 = new long?[_nA, nN];
        _tMastered = new long?[_nA, nN];
        _lic = new bool[_nA, nN];
        _tReq = new long?[_nA, _nG];
        _tFirstUnit = new long?[_nA, _nG];
        _tFielded = new long?[_nA, _nG];
        _costU100 = new long?[_nA, _nG];
        _firstSeries = new int[_nA, _nG];
        _cum = new long[_nA, _nG];
        _tLaunch = new long?[_nA, _nD];
        _forcedSent = new long?[_nA, _nD];
        var prc = new List<int>();
        for (int r = 0; r < nN; r++)
        {
            if (initial.Actors[0].Nodes[r].Process is not null)
                prc.Add(r);
        }
        _prcRows = [.. prc];

        for (int a = 0; a < _nA; a++)
        {
            ActorView av = initial.Actors[a];
            if (av.Actor != a)
                throw new SliceFormatException($"View actor {av.Actor} at row {a}.");
            for (int r = 0; r < nN; r++)
            {
                NodeView n = av.Nodes[r];
                if (n.Known)
                    _tKnown[a, r] = -1;
                if (n.Process is ProcessView p)
                {
                    if (p.Yield > Fx6.Zero && p.V > Fx6.Zero) _tFirst[a, r] = -1;
                    if (p.Yield.Raw >= Yield07) _t07[a, r] = -1;
                    if (p.Mastered) _tMastered[a, r] = -1;
                }
                _lic[a, r] |= n.ByLicence;
            }
            for (int g = 1; g <= _nG; g++)
            {
                _firstSeries[a, g - 1] = -1;
                if (av.FieldedGen >= g) _tFielded[a, g - 1] = -1;
            }
            foreach (DesignView d in av.Designs)
            {
                if (d.Stage == DesignStage.None)
                    continue;
                if (d.PlatformGen >= 1 && d.PlatformGen <= _nG)
                    _tReq[a, d.PlatformGen - 1] = -1;
                if (d.Gen >= 1 && d.Gen <= _nG)
                {
                    if (d.InService > Fx6.Zero) _tFirstUnit[a, d.Gen - 1] = -1;
                    if (d.Stage == DesignStage.Series) _firstSeries[a, d.Gen - 1] = -2;   // a start line: units before t0 are unknown
                }
                if (d.Stage == DesignStage.Series)
                    _tLaunch[a, d.Design] = -1;
            }
            // Month-0 decisions of Initialize (format §4.6 r2): compose → t_req 0; the requirement row is written with the inputs of month 0.
            foreach (DesignCommandView c in av.Decisions.DesignCommands)
            {
                if (c.Kind == DesignCommandKind.Compose && c.Target >= 1 && c.Target <= _nG)
                    _tReq[a, c.Target - 1] ??= 0;
                if (c.Kind == DesignCommandKind.Cancel)
                    throw new SliceFormatException($"Initialize gave actor {_ids[a]} a cancel_design; A40 is a rule of the monthly compute.");
            }
            // P1 starts a quality programme on t0 (spec A41, A8).
            if (av.Quality.Active && av.Quality.Start == 0 && Has("events"))
                Row("events", "0", "QP_START", _ids[a], $"P{Prof(av.Profile)}:T0", "");
        }
        _leader = 0;
        _leaderAll = 0;
        _initial = initial;
    }

    private SliceView _initial = null!;

    // ---------------------------------------------------------------- before the monthly step: why_not (snapshot of month t)

    public void BeforeStep(long t)
    {
        if (_calib)
            return;   // ViewMode.Calibration: why_not is never called (spec §8.2 п. 2)
        var cache = new Dictionary<(int, WhyNotTarget), IReadOnlyList<Link>>();
        IReadOnlyList<Link> WhyNotOf(int a, WhyNotTarget target)
        {
            if (!cache.TryGetValue((a, target), out IReadOnlyList<Link>? links))
            {
                links = _s.WhyNot(a, target);
                cache[(a, target)] = links;
            }
            return links;
        }

        // closed_t (§4.3): an open link is closed in the first month t' > t whose list has no link with the same (layer, subject).
        if (_full)
        {
            foreach (WhyRow w in _why)
            {
                if (w.Closed is not null || w.T >= t)
                    continue;
                IReadOnlyList<Link> now = WhyNotOf(w.Actor, w.Target);
                bool present = false;
                foreach (Link l in now)
                    present |= l.Layer == w.Layer && Subject(l) == w.Subject;
                if (!present)
                    w.Closed = t;
            }
        }
        if (t % 12 != 0)
            return;
        for (int a = 0; a < _nA; a++)
        {
            _qcRank[a] = null;
            _qcRankMonth[a] = t;
            if (_s.CurrentRequirement(a) is not WhyNotTarget target)
                continue;
            IReadOnlyList<Link> links = WhyNotOf(a, target);
            for (int i = 0; i < links.Count; i++)
            {
                Link l = links[i];
                if (_qcRank[a] is null && l.Layer == WhyNotLayer.Capability && l.Cause == WhyNotCause.CultureH && l.Dim == Dimension.QualityControl)
                    _qcRank[a] = i + 1;
                if (!_full)
                    continue;
                // Cells 0…9, then closed_t (index 10, filled in End), then cluster / order / unmet of a TOOLING_POOL_EXHAUSTED link (§4.3).
                bool pool = l.Cause == WhyNotCause.ToolingPoolExhausted;
                _why.Add(new WhyRow
                {
                    T = t, Actor = a, Target = target, Layer = l.Layer, Subject = Subject(l),
                    Cells =
                    [
                        I(t), _ids[a], $"{Snake(target.Kind.ToString())}:{I(target.Id)}", I(i + 1), Snake(l.Layer.ToString()), Snake(l.Cause.ToString()),
                        Subject(l), O(l.EtaMonths), string.Join(";", l.Levers.Select(x => Snake(x.ToString()))),
                        B(LeverValidator.Check([l], LeverRegistry.Slice).Count == 0), "",
                        pool && l.Cluster >= 0 ? _l.Clusters[l.Cluster] : "", pool ? I(l.Order) : "", pool ? F(l.Unmet) : "0",
                    ],
                });
            }
        }
    }

    // subject_id of a link: the narrow process and dimension of a CAPABILITY link, else the subject number (§4.3).
    private static string Subject(Link l) =>
        l.Layer == WhyNotLayer.Capability && l.Process >= 0 && l.Dim is Dimension d ? $"{I(l.Process)}:{Snake(d.ToString())}" : I(l.SubjectId);

    // ---------------------------------------------------------------- the month

    public void Month(SliceView snap, SliceView v, long t)
    {
        if (v.Month != t || v.Inputs is not MonthInputsView inputs || v.Step is not MonthStepView step || inputs.Month != t || step.Month != t)
            throw new SliceFormatException($"Month {t}: the view after the monthly tick has month {v.Month} and inputs/step {v.Inputs?.Month}/{v.Step?.Month}.");
        var rec = new ActorMonth[_nA];
        for (int a = 0; a < _nA; a++)
            rec[a] = ActorMonthOf(snap.Actors[a], v.Actors[a], inputs.Actors[a], step.Actors[a], t);
        _ring.Enqueue(new MonthRecord(t, rec));
        while (_ring.Count > _tab)
            _ring.Dequeue();

        for (int a = 0; a < _nA; a++)
        {
            ActorView sv = snap.Actors[a], av = v.Actors[a];
            ActorInputsView ai = inputs.Actors[a];
            ActorStepView st = step.Actors[a];
            if (av.Actor != a || ai.Actor != a || st.Actor != a)
                throw new SliceFormatException($"Month {t}: actor rows out of order.");
            Nodes(av, t, a);
            Gens(sv, av, t, a);
            A40(sv, av, ai, rec[a], t, a);
            Tooling(ai, t, a);
            Lines(sv, av, ai, st, rec[a], t, a);
            if (_calib)
            {
                CalibMonth(av, ai, t, a);
                continue;
            }
            Fleet(sv, av, inputs, t, a);
            Events(sv, av, ai, st, t, a);
            Requirements(t == 0 ? _initial.Actors[a] : null, av, ai, t, a);
            Year(av, ai, t, a);
        }
        if (!_calib)
        {
            Licences(snap, v, t);
            Leads(v);
            if (t % 12 == 11)
                _hashYears.Add(v.StateHash);
        }
    }

    private ActorMonth ActorMonthOf(ActorView sv, ActorView av, ActorInputsView ai, ActorStepView st, long t)
    {
        Role[] roles = Roles(sv, st.Money, out int w, out int n);
        var yMin = new Fx6[_nD];
        var argP = new int[_nD];
        var left = new Fx6?[_nD];
        var training = new Fx6?[_nD];
        var rate = new Fx6[_nD];
        var bind = new LineBind[_nD];
        for (int d = 0; d < _nD; d++)
        {
            DesignInputsView di = ai.Designs[d];
            if (di.Design != d)
                throw new SliceFormatException($"Month {t}: design inputs out of order.");
            yMin[d] = di.YMin;
            argP[d] = di.ArgP;
            rate[d] = av.Designs[d].Rate;
            bind[d] = av.Designs[d].Bind;
            if (st.Money is MoneyStepView money)
            {
                foreach (LineMoneyView l in money.Lines)
                {
                    if (l.Design == d)
                    {
                        left[d] = l.LeftIn;
                        training[d] = l.Training;
                    }
                }
            }
        }
        bool meltsZero = _meltRows.All(r => sv.Nodes[r].Process is not ProcessView p || p.Yield == Fx6.Zero);
        bool two = w >= 0 && n >= 0;
        bool wAfterN = two && w > n;
        if (wAfterN)
            _wAfterNMonths++;
        return new ActorMonth(ai.Capability.AlloySupply, ai.Capability.AlloyDeficit > Fx6.Zero, ai.Tooling.TrainingBudget, st.Money is null,
            two, wAfterN, meltsZero, roles, yMin, argP, left, training, rate, bind);
    }

    // The lines of month t (format v0.5 r3 §4.12): in С by the snapshot the AI decided on (spec A41 F17b «Пара линий»: no state, the pair is read
    // from the stages), without a line the AI closed in the previous compute (it leaves С in this step). A line of the snapshot that step 7 did not
    // reach (closed by a command before it: the FORCE proxy, a derive that moves its parent) keeps its role and has no money row: counted, left empty.
    // N: the larger g_p (tie: the larger design), W: the other.
    private Role[] Roles(ActorView sv, MoneyStepView? money, out int w, out int n)
    {
        var lines = new List<int>();
        for (int d = 0; d < _nD; d++)
        {
            if (sv.Designs[d].Stage != DesignStage.Series || sv.Decisions.Closes.Any(c => c.Design == d))
                continue;
            if (money is not null && !money.Lines.Any(l => l.Design == d))
                _linesWithoutMoney++;
            lines.Add(d);
        }
        var roles = new Role[_nD];
        w = n = -1;
        if (lines.Count == 1)
        {
            roles[lines[0]] = Role.Sole;
            return roles;
        }
        if (lines.Count > 2)
            throw new SliceFormatException($"Actor {_ids[sv.Actor]} has {lines.Count} lines in С; F17b allows at most two.");
        foreach (int d in lines)
        {
            if (n < 0 || sv.Designs[d].PlatformGen > sv.Designs[n].PlatformGen || sv.Designs[d].PlatformGen == sv.Designs[n].PlatformGen && d > n)
                n = d;
        }
        foreach (int d in lines)
        {
            roles[d] = d == n ? Role.N : Role.W;
            if (d != n)
                w = d;
        }
        return roles;
    }

    // ---------------------------------------------------------------- node_times, prc_year, prc_month

    private void Nodes(ActorView av, long t, int a)
    {
        for (int r = 0; r < av.Nodes.Length; r++)
        {
            NodeView n = av.Nodes[r];
            if (n.Known && _tKnown[a, r] is null)
            {
                _tKnown[a, r] = t;
                if (n.KnownSince is long since && since != t)
                    throw new SliceFormatException($"Actor {_ids[a]} node {n.Number}: known in month {t}, know.since = {since} (§4.4).");
            }
            _lic[a, r] |= n.ByLicence;
            if (n.Process is not ProcessView p)
                continue;
            if (_tFirst[a, r] is null && p.Yield > Fx6.Zero && p.V > Fx6.Zero) _tFirst[a, r] = t;
            if (_t07[a, r] is null && p.Yield.Raw >= Yield07) _t07[a, r] = t;
            if (_tMastered[a, r] is null && p.Mastered) _tMastered[a, r] = t;
            if (t % 12 == 0 && Has("prc_year"))
                Row("prc_year", I(t), _ids[a], I(n.Number), B(n.Known), B(p.Admitted), F(p.Yield), F(p.UnitCostMult), F(p.LicenceShare));
            if (Has("prc_month"))
                Row("prc_month", I(t), _ids[a], I(n.Number), B(n.Known), B(p.Admitted), F(p.Yield), F(p.UnitCostMult), F(p.V));
        }
    }

    // ---------------------------------------------------------------- gen_times

    private void Gens(ActorView sv, ActorView av, long t, int a)
    {
        foreach (DesignCommandView c in av.Decisions.DesignCommands)
        {
            if (c.Kind == DesignCommandKind.Compose && c.Target >= 1 && c.Target <= _nG)
                _tReq[a, c.Target - 1] ??= t;
        }
        for (int g = 1; g <= _nG; g++)
        {
            if (_tFielded[a, g - 1] is null && av.FieldedGen >= g)
                _tFielded[a, g - 1] = t;
        }
        foreach (DesignView d in av.Designs)
        {
            if (d.Stage == DesignStage.Series && _tLaunch[a, d.Design] is null)
                _tLaunch[a, d.Design] = t;
            if (d.Gen < 1 || d.Gen > _nG)
                continue;
            int gi = d.Gen - 1;
            if (_tFirstUnit[a, gi] is null && d.InService > Fx6.Zero)
                _tFirstUnit[a, gi] = t;
            if (_firstSeries[a, gi] == -1 && d.Stage == DesignStage.Series)
                _firstSeries[a, gi] = d.Design;
            if (_firstSeries[a, gi] == d.Design && _costU100[a, gi] is null)
            {
                _cum[a, gi] = checked(_cum[a, gi] + d.Rate.Raw);
                if (_cum[a, gi] >= Units100)
                    _costU100[a, gi] = d.UnitCost.Raw;
            }
        }
    }

    // ---------------------------------------------------------------- a40, a40_c_months

    private void A40(ActorView sv, ActorView av, ActorInputsView ai, ActorMonth rec, long t, int a)
    {
        PolicyProfile profile = av.Profile;
        foreach (DesignCommandView c in av.Decisions.DesignCommands)
        {
            if (c.Kind != DesignCommandKind.Cancel)
                continue;
            int d = c.Target;
            DesignView dv = sv.Designs[d];
            DesignInputsView di = ai.Designs[d];
            string cls = di.GiveUp switch
            {
                GiveUpClass.Processes => "PROCESSES",
                GiveUpClass.Defects => "DEFECTS",
                _ => throw new SliceFormatException($"Month {t} actor {_ids[a]}: cancel_design({d}) without a give-up class (stage {di.Stage})."),
            };
            Row("a40", I(t), _ids[a], I(d), I(dv.PlatformGen), I(Prof(profile)), Preset(profile), "O", cls, I(dv.StageOMonth), "", "0",
                I(di.IdleMonths), B(RepBefore(sv, dv.PlatformGen)));
        }
        foreach (CloseView c in av.Decisions.Closes)
        {
            int d = c.Design;
            DesignInputsView di = ai.Designs[d];
            if (c.Reason == LineCloseReason.None)
                throw new SliceFormatException($"Month {t} actor {_ids[a]}: close_line({d}) of a monthly step without a reason (SC §13.8).");
            if (c.Reason != LineCloseReason.A40C)
            {
                if (di.IdleMonths >= _tab)
                    _closeIdleOtherReason++;
                continue;
            }
            if (di.IdleMonths < _tab)
                throw new SliceFormatException($"Month {t} actor {_ids[a]}: A40C close of design {d} with idle {di.IdleMonths} < T_ab {_tab} (§4.9).");
            DesignView dv = sv.Designs[d];
            MonthRecord[] months = [.. _ring.Where(m => m.T > t - _tab && m.T <= t)];
            if (months.Length != _tab)
                throw new SliceFormatException($"Month {t} actor {_ids[a]}: A40C close of design {d} with {months.Length} recorded months, the class needs T_ab = {_tab} (§4.9).");
            bool culture = months.All(m => m.Actors[a].YMin[d] == Fx6.Zero || m.Actors[a].Alloy == Fx6.Zero && m.Actors[a].MeltsZero);
            string cls = culture ? "CULTURE" : CoexistMoney([.. months.Select(m => m.Actors[a])], d) ? "COEXIST_MONEY" : "OTHER";
            Row("a40", I(t), _ids[a], I(d), I(dv.PlatformGen), I(Prof(profile)), Preset(profile), "C", cls, I(dv.StageOMonth),
                O(_tLaunch[a, d]), B(LaunchedWithForce(a, d)), I(di.IdleMonths), B(RepBefore(sv, dv.PlatformGen)));
            foreach (MonthRecord m in months)
            {
                ActorMonth x = m.Actors[a];
                Row("a40_c_months", I(t), _ids[a], I(d), I(m.T), F(x.YMin[d]), x.ArgP[d] >= 0 ? I(x.ArgP[d]) : "", F(x.Alloy), B(x.Deficit), F(x.Ta),
                    x.LeftIn[d] is Fx6 l ? F(l) : "", B(x.WAfterN), x.Training[d] is Fx6 tr ? F(tr) : "");
            }
        }
    }

    /// <summary>
    /// Class С <c>COEXIST_MONEY</c> (format v0.5 r4 §4.16 (б); spec A40 «v0.20: две линии»: «`N` с выпуском 0 в каждом из `T_ab` месяцев при
    /// `left` после `W` = 0 и без `T_a`»): in every month the line is N of a pair, made nothing, saw no shared money (<c>LeftIn = 0</c>), got no
    /// training money (<c>LineMoneyView.Training = 0</c>: <c>T_a</c> goes only to the training target line), and step 7 bound it by money (<c>Bind = Money</c>: an unadmitted line has <c>None</c> and is OTHER).
    /// <c>Bind = Money</c> alone (r3) also held with positive money short of one unit or with <c>T_a &gt; 0</c> on the line (review Codex r4).
    /// </summary>
    internal static bool CoexistMoney(IReadOnlyList<ActorMonth> months, int d) =>
        months.Count > 0 && months.All(x => x.TwoLines && x.Roles[d] == Role.N && x.Rate[d] == Fx6.Zero && x.LeftIn[d] is Fx6 left && left == Fx6.Zero
            && x.Training[d] is Fx6 training && training == Fx6.Zero && x.Bind[d] == LineBind.Money);

    /// <summary>
    /// <c>a40.force</c> (format §4.9): the series was launched by the harness's forced start. The command sent after the step of month f applies at
    /// the next tick boundary, so an accepted one puts the design in С in the post of month f + 1; a refused one (e.g. the line moved to a derived
    /// design) leaves it in О, and a later start of the AI is never forced (P0) — then <c>t_launch ≠ f + 1</c> and the flag is 0.
    /// </summary>
    private bool LaunchedWithForce(int a, int d) => _forcedSent[a, d] is long f && _tLaunch[a, d] == f + 1;

    private static bool RepBefore(ActorView sv, int gp) => gp >= 1 && gp <= sv.Decisions.Repeats.Length && sv.Decisions.Repeats[gp - 1] > 0;

    // ---------------------------------------------------------------- tooling_ledger

    private void Tooling(ActorInputsView ai, long t, int a)
    {
        if (!Writes("tooling_ledger"))
            return;
        ToolingLedgerView l = ai.Tooling;
        Fx6 sum = Fx6.Zero;
        foreach (Fx6 u in l.UnmetByCluster)
            sum += u;
        Row("tooling_ledger", I(t), _ids[a], F(l.Rest), F(l.CapBudget), F(l.Total), F(l.TakenRest), F(l.TakenCap), F(l.Rest - l.TakenRest),
            F(l.TrainingBudget), F(l.RestReturned), I(l.Participating),
            F(l.UnmetByCluster[_clusterIndex[0]]), F(l.UnmetByCluster[_clusterIndex[1]]), F(l.UnmetByCluster[_clusterIndex[2]]), F(l.UnmetByCluster[_clusterIndex[3]]),
            B(sum > Fx6.Zero));
    }

    // ---------------------------------------------------------------- lines

    private void Lines(ActorView sv, ActorView av, ActorInputsView ai, ActorStepView st, ActorMonth rec, long t, int a)
    {
        bool anyStart = av.Decisions.Starts.Length > 0;
        for (int d = 0; d < _nD; d++)
        {
            Role role = rec.Roles[d];
            CloseView? close = null;
            foreach (CloseView c in av.Decisions.Closes)
            {
                if (c.Design == d)
                    close = c;
            }
            if (role == Role.None)
            {
                if (close is not null)
                    _closesWithoutLine++;
                continue;
            }
            string rule = "", core = "";
            if (close is CloseView cv)
            {
                core = Snake(cv.Reason.ToString());
                rule = cv.Reason switch
                {
                    LineCloseReason.A40C => "A40C_" + (role == Role.Sole ? "SOLE" : role.ToString()),
                    LineCloseReason.YProd when role == Role.W => "YPROD",
                    LineCloseReason.LaunchAll => "REPLACE",
                    LineCloseReason.LaunchCandidate when role == Role.N => "REPLACE",
                    _ => throw new SliceFormatException($"Month {t} actor {_ids[a]} design {d}: close {cv.Reason} of a line with role {role} (§4.12); "
                        + $"snapshot {string.Join(" ", sv.Designs.Select(x => $"{x.Design}:{x.Stage}/g{x.PlatformGen}/p{x.Parent}"))}; "
                        + $"step-7 lines {string.Join(" ", st.Money?.Lines.Select(l => l.Design.ToString(CultureInfo.InvariantCulture)) ?? [])}."),
                };
                if (cv.Reason is LineCloseReason.LaunchAll or LineCloseReason.LaunchCandidate && !anyStart)
                    throw new SliceFormatException($"Month {t} actor {_ids[a]}: close {cv.Reason} without a start_series of the actor (§4.12).");
            }
            string leftShare = "";
            if (st.Money is MoneyStepView money && money.LeftStart > Fx6.Zero)
            {
                foreach (LineMoneyView l in money.Lines)
                {
                    if (l.Design == d)
                        leftShare = I((long)((Int128)l.FromLeft.Raw * Fx6.Scale / money.LeftStart.Raw));
                }
            }
            if (!Writes("lines"))
                continue;
            DesignInputsView di = ai.Designs[d];
            DesignView post = av.Designs[d];
            Row("lines", I(t), _ids[a], I(d), I(sv.Designs[d].PlatformGen), role == Role.Sole ? "SOLE" : role.ToString(), F(di.YMin),
                di.ArgP >= 0 ? I(di.ArgP) : "", F(post.Rate), F(post.UnitCost), I(di.IdleMonths), B(close is not null),
                B(av.Decisions.Starts.Any(s => s.Design == d)), rule, core, leftShare, B(rec.WAfterN));
        }
    }

    // ---------------------------------------------------------------- calibration months

    private void CalibMonth(ActorView av, ActorInputsView ai, long t, int a)
    {
        var outG = new long[4];
        var active = new bool[4];
        foreach (DesignView d in av.Designs)
        {
            if (d.Gen < 1 || d.Gen > 4)
                continue;
            outG[d.Gen - 1] += d.Rate.Raw;
            active[d.Gen - 1] |= d.Stage == DesignStage.Series;
        }
        Row("actor_month", I(t), _ids[a], F(ai.Budget), F(ai.ProcurementBudget),
            I(outG[0] / Fx6.Scale), I(outG[1] / Fx6.Scale), I(outG[2] / Fx6.Scale), I(outG[3] / Fx6.Scale),
            B(active[0]), B(active[1]), B(active[2]), B(active[3]));
    }

    // ---------------------------------------------------------------- fleet_cmd

    private void Fleet(ActorView sv, ActorView av, MonthInputsView inputs, long t, int a)
    {
        if (av.Decisions.Fleet.Length == 0)
            return;
        // The line L of the fleet rule: the newest line in С by the snapshot (N of a pair or the only one).
        int line = -1;
        for (int d = 0; d < _nD; d++)
        {
            if (sv.Designs[d].Stage == DesignStage.Series && (line < 0 || sv.Designs[d].PlatformGen > sv.Designs[line].PlatformGen
                || sv.Designs[d].PlatformGen == sv.Designs[line].PlatformGen && d > line))
                line = d;
        }
        foreach (FleetCommand c in av.Decisions.Fleet)
        {
            Row("fleet_cmd", I(t), _ids[a], Snake(c.Kind.ToString()), I(c.Design), I(sv.Designs[c.Design].Gen), line >= 0 ? I(sv.Designs[line].Gen) : "",
                I(checked(c.N * Fx6.Scale)), I(inputs.GFront), line >= 0 ? Snake(av.Designs[line].Bind.ToString()) : "");
        }
    }

    // ---------------------------------------------------------------- events (DIS, QP_START, TRAINING, LIC_REFUSE_MONEY; LIC_* in Licences)

    private void Events(ActorView sv, ActorView av, ActorInputsView ai, ActorStepView st, long t, int a)
    {
        DisruptionView dis = av.Disruption;
        if (dis.Start >= 0 && dis.Start != sv.Disruption.Start)
            Row("events", I(dis.Start), "DIS", _ids[a], "", I(dis.End - dis.Start + 1));
        QualityView q = av.Quality;
        if (q.Active && q.Start >= 0 && q.Start != sv.Quality.Start)
        {
            // Started by the yearly compute of t (it acts from t + 1); the rank of the QC link is read from why_not of the same snapshot (§4.7).
            string other;
            if (av.Profile == PolicyProfile.Mass)
                other = "P2";
            else
            {
                int? rank = _qcRankMonth[a] == t ? _qcRank[a] : null;
                int limit = av.Profile == PolicyProfile.Quality ? 3 : 1;
                if (rank is not int r || r > limit)
                    throw new SliceFormatException($"Month {t} actor {_ids[a]} ({av.Profile}): quality programme with QC rank {rank?.ToString(CultureInfo.InvariantCulture) ?? "none"} (§4.7 needs ≤ {limit}).");
                other = $"P{Prof(av.Profile)}:{I(r)}";
            }
            Row("events", I(q.Start), "QP_START", _ids[a], other, "");
        }
        if (sv.Quality.TrainingTarget >= 0 && ai.Tooling.TrainingBudget > Fx6.Zero)
            Row("events", I(t), "TRAINING", _ids[a], I(sv.Quality.TrainingTarget), F(ai.Tooling.TrainingBudget));
        foreach (LicenceRefusalView r in st.LicenceMoneyRefusals)
            Row("events", I(t), "LIC_REFUSE_MONEY", _ids[a], $"{_ids[r.Seller]}:G{I(r.Gen)}", F(r.Price));
    }

    // ---------------------------------------------------------------- licences: LIC_BUY, LIC_END and licences.csv

    private void Licences(SliceView snap, SliceView v, long t)
    {
        var buyers = new List<int>();
        Fx6 flowSum = Fx6.Zero;
        for (int a = 0; a < _nA; a++)
        {
            LicenceView now = v.Actors[a].Licence;
            flowSum += now.Flow;
            if (now.Gen > 0 && now.Start == t)
                buyers.Add(a);
        }
        if (flowSum != Fx6.Zero)
            throw new SliceFormatException($"Month {t}: the licence flows sum to {flowSum.Raw}, not 0 (§4.7).");
        // price(b) = Σ prices paid to b by this month's buyers − Flow(b); solved from the buyers nobody buys from (§4.7).
        var price = new Fx6?[_nA];
        for (int round = 0; round <= _nA && buyers.Any(b => price[b] is null); round++)
        {
            foreach (int b in buyers)
            {
                if (price[b] is not null)
                    continue;
                Fx6 inflow = Fx6.Zero;
                bool ready = true;
                foreach (int y in buyers)
                {
                    if (v.Actors[y].Licence.Seller != b)
                        continue;
                    ready &= price[y] is not null;
                    inflow += price[y] ?? Fx6.Zero;
                }
                if (ready)
                    price[b] = inflow - v.Actors[b].Licence.Flow;
            }
        }
        for (int a = 0; a < _nA; a++)
        {
            Fx6 inflow = Fx6.Zero;
            foreach (int y in buyers)
                inflow += v.Actors[y].Licence.Seller == a ? price[y] ?? Fx6.Zero : Fx6.Zero;
            Fx6 expect = inflow - (price[a] ?? Fx6.Zero);
            if (price[a] is null && buyers.Contains(a) || v.Actors[a].Licence.Flow != expect || price[a] is Fx6 p && p <= Fx6.Zero)
                throw new SliceFormatException($"Month {t} actor {_ids[a]}: the licence price cannot be read from the flows (§4.7).");
        }
        for (int a = 0; a < _nA; a++)
        {
            LicenceView now = v.Actors[a].Licence;
            // A lapse (A43.4) leaves the columns: it is read from LicenceShare, which is l_lic exactly while the licence is active (and the PRC known).
            if (_openLicence[a] is LicenceRow open && (buyers.Contains(a) || Lapsed(v.Actors[a], now)))
            {
                long end = buyers.Contains(a) ? t : t + 1;
                open.End = end;
                _openLicence[a] = null;
                if (!buyers.Contains(a))
                    _lapsesInferred++;
                else if (_lapse.NeverShared(a))
                    _licEndUnobservable++;     // replaced: t_end is the new purchase, an earlier expiry was not visible
                Row("events", I(end), "LIC_END", _ids[a], _ids[open.Seller], I(end - open.Start));
            }
            if (!buyers.Contains(a))
                continue;
            var row = new LicenceRow { T = t, Start = now.Start, Seller = now.Seller, Buyer = a, Gen = now.Gen, Profile = v.Actors[a].Profile, Price = price[a]!.Value };
            _licences.Add(row);
            _openLicence[a] = row;
            _lapse.Bought(a);
            Row("events", I(t), "LIC_BUY", _ids[a], _ids[now.Seller], F(row.Price));
        }
    }

    private bool Lapsed(ActorView av, LicenceView l)
    {
        bool share = false;
        foreach (NodeView n in av.Nodes)
        {
            if (n.Process is ProcessView p && p.LicenceShare > Fx6.Zero && (l.Mask >> (n.Number - 1) & 1UL) != 0)
                share = true;
        }
        return _lapse.Lapsed(av.Actor, share);
    }

    // ---------------------------------------------------------------- requirements

    private void Requirements(ActorView? initial, ActorView av, ActorInputsView ai, long t, int a)
    {
        if (t % 12 != 0)
            return;
        if (ai.Requirement is not RequirementView r)
            throw new SliceFormatException($"Month {t} actor {_ids[a]}: no Requirement variants in a yearly month (SC §13.5).");
        foreach (ActorView? source in new[] { initial, av })
        {
            if (source is null)
                continue;
            foreach (DesignCommandView c in source.Decisions.DesignCommands)
            {
                if (c.Kind != DesignCommandKind.Compose)
                    continue;
                if (c.Target != r.Gen || r.Count == 0)
                    throw new SliceFormatException($"Month {t} actor {_ids[a]}: compose of G{c.Target} against the variants of G{r.Gen} (count {r.Count}).");
                ImmutableArray<int> chosen = av.Profile switch
                {
                    PolicyProfile.Quality => r.Superior,
                    PolicyProfile.Mass => r.Count == 2 ? r.Producible : r.Superior,
                    _ => r.Count == 2 && r.FvProducible is Fx6 fp && fp > r.FvSuperior ? r.Producible : r.Superior,
                };
                if (!c.Alts.SequenceEqual(chosen))
                    throw new SliceFormatException($"Month {t} actor {_ids[a]} ({av.Profile}): the compose command is not the profile's choice among the variants (§4.6).");
                Row("requirements", I(_req++), _ids[a], I(r.Gen), I(t), I(r.Count), F(r.FvSuperior), r.Count == 2 && r.FvProducible is Fx6 f ? F(f) : "");
            }
        }
    }

    // ---------------------------------------------------------------- actor_year

    private void Year(ActorView av, ActorInputsView ai, long t, int a)
    {
        if (t % 12 == 0)
        {
            TechKey key = av.Key ?? throw new SliceFormatException($"Month {t}: no TechKey in ViewMode.Full.");
            Fx6 fv = av.FleetValue ?? throw new SliceFormatException($"Month {t}: no FleetValue in a yearly month.");
            int gamma = new ProductsParams().GammaQ10;   // the channels and arms of the slice do not change γ (SliceWorld.ApplyChannels)
            Fx6 fleet = Fx6.Zero;
            foreach (DesignView d in av.Designs)
            {
                if (d.InService > Fx6.Zero)
                    fleet += ProductsMath.FleetValue(checked((int)(d.InService.Raw / Fx6.Scale)), d.Readiness, d.Perf, gamma);
            }
            _years.Add(new YearRow
            {
                T = t, Actor = a,
                Head = [I(t), _ids[a], I(key.G), I(key.T), I(key.N), F(key.P), F(fv), F(fleet)],
                Tail = [.. _kdIndex.Select(k => F(av.KD[k]))],
            });
            _years[^1].Budget = ai.Budget;
        }
        foreach (YearRow y in _years)
        {
            if (y.Actor != a || y.Months >= 12 || t < y.T)
                continue;
            foreach (NodeView n in av.Nodes)
            {
                if (n.Process is ProcessView p && _l.ProcessTier[_l.ProcessNumbers.IndexOf(n.Number)] >= 4 && p.Yield.Raw >= Yield07)
                    y.Volume = checked(y.Volume + p.V.Raw);
            }
            if (av.Bureaus.Any(b => b.Design >= 0 && b.Required > 0 && b.Granted * 10 < b.Required * 9))
                y.EngShort++;
            y.Months++;
        }
        FlushYears(final: false);
    }

    private void FlushYears(bool final)
    {
        for (int i = 0; i < _years.Count; i++)
        {
            YearRow y = _years[i];
            if (y.Months < 12 && !final)
                continue;
            Row("actor_year", [.. y.Head, F(Fx6.FromRaw(y.Volume)), F(y.Budget), I(y.EngShort), .. y.Tail]);
            _years.RemoveAt(i--);
        }
    }

    // ---------------------------------------------------------------- summary

    private void Leads(SliceView v)
    {
        var keys = new TechKey[_nA];
        for (int a = 0; a < _nA; a++)
            keys[a] = v.Actors[a].Key ?? throw new SliceFormatException($"Month {v.Month}: no TechKey in ViewMode.Full.");
        _leader = Leader(keys, Great.Length, _leader);
        _leaderAll = Leader(keys, _nA, _leaderAll);
        _lead.Append((char)('0' + _leader));
        _leadAll.Append((char)('0' + _leaderAll));
        // The field on which the leader is strictly ahead of the best other great power; '=' for a tie held by the incumbent.
        TechKey best = default;
        bool any = false;
        for (int a = 0; a < Great.Length; a++)
        {
            if (a == _leader)
                continue;
            if (!any || keys[a].CompareTo(best) > 0)
                best = keys[a];
            any = true;
        }
        TechKey l = keys[_leader];
        _leadField.Append(l.G != best.G ? 'G' : l.T != best.T ? 'T' : l.N != best.N ? 'N' : l.P != best.P ? 'P' : '=');
    }

    // tech_lead (contract §9.1 v2.2): the greatest key; the incumbent keeps a tie; among new leaders tied for the top, the first actor.
    private static int Leader(TechKey[] keys, int count, int incumbent)
    {
        int best = 0;
        for (int a = 1; a < count; a++)
        {
            if (keys[a].CompareTo(keys[best]) > 0)
                best = a;
        }
        return keys[incumbent].CompareTo(keys[best]) == 0 ? incumbent : best;
    }

    // ---------------------------------------------------------------- end

    public void End(string hashFinal)
    {
        if (!_calib)
        {
            FlushYears(final: true);
            foreach (WhyRow w in _why)
                Row("whynot", [.. w.Cells[..10], O(w.Closed), .. w.Cells[11..]]);
            Row("summary", [.. _profiles.Select(p => I(Prof(p))), _lead.ToString(), _leadAll.ToString(), _leadField.ToString(), hashFinal, string.Join(";", _hashYears)]);
            foreach (LicenceRow l in _licences)
                Row("licences", I(l.T), _ids[l.Seller], _ids[l.Buyer], I(l.Gen), I(Prof(l.Profile)), F(l.Price), O(l.End));
        }
        else
            Row("calib_summary", [.. _profiles.Select(p => I(Prof(p))), hashFinal]);
        for (int a = 0; a < _nA; a++)
        {
            for (int r = 0; r < _l.NodeNumbers.Length; r++)
            {
                bool prc = Array.IndexOf(_prcRows, r) >= 0;
                Row("node_times", _ids[a], I(_l.NodeNumbers[r]), O(_tKnown[a, r]), prc ? O(_tFirst[a, r]) : "", prc ? O(_t07[a, r]) : "",
                    prc ? O(_tMastered[a, r]) : "", B(_lic[a, r]));
            }
            for (int g = 1; g <= _nG; g++)
                Row("gen_times", _ids[a], I(g), O(_tReq[a, g - 1]), O(_tFirstUnit[a, g - 1]), O(_tFielded[a, g - 1]), O(_costU100[a, g - 1]));
        }
    }

    /// <summary>The harness sent <c>start_series</c> with <c>force</c> for (actor row, design) after the step of month <paramref name="month"/>.</summary>
    public void ForcedStart(int actor, int design, long month) => _forcedSent[actor, design] ??= month;

    public IEnumerable<KeyValuePair<string, string>> Counters() =>
    [
        new("a40_close_idle_ge_tab_other_reason", I(_closeIdleOtherReason)),
        new("lic_end_lapses_inferred", I(_lapsesInferred)),
        new("w_after_n_actor_months", I(_wAfterNMonths)),
        new("closes_of_designs_not_lines", I(_closesWithoutLine)),
        new("lines_without_step7_money", I(_linesWithoutMoney)),
        // Licences whose expiry LicenceShare could not show: replaced ones (counted then) and those still open at the end.
        new("lic_end_unobservable", I(_licEndUnobservable + _openLicence.Count(l => l is not null && _lapse.NeverShared(l.Buyer)))),
    ];
}

/// <summary>
/// The lapse of a licence (A43.4) read from <c>LicenceShare</c> (format §9.1): a licence that gave its share in an earlier month and gives none now
/// has lapsed. A new purchase starts the tracking afresh, so the share of the replaced licence never ends the new one.
/// </summary>
internal sealed class LapseTracker(int actors)
{
    private readonly bool[] _effective = new bool[actors];
    private readonly bool[] _seen = new bool[actors];

    public void Bought(int actor)
    {
        _effective[actor] = false;
        _seen[actor] = false;
    }

    /// <summary>
    /// The current licence of <paramref name="actor"/> never gave a share: no PRC of its mask was known to the buyer, so its expiry (A43.4) cannot be
    /// read from <c>LicenceShare</c> and an empty <c>t_end</c> means «unknown», not «active» (format §11 п. 7, review Codex r4).
    /// </summary>
    public bool NeverShared(int actor) => !_seen[actor];

    /// <summary>True once, in the first month without a share after a month with one.</summary>
    public bool Lapsed(int actor, bool shareNow)
    {
        if (shareNow)
        {
            _effective[actor] = true;
            _seen[actor] = true;
            return false;
        }
        bool lapsed = _effective[actor];
        _effective[actor] = false;
        return lapsed;
    }
}
