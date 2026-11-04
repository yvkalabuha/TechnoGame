using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Core.Policy;

/// <summary>
/// The product AI of the policy (spec §3.5 A40, A41, §4, §6.8; contract §4.4e): <c>Requirement</c> with the preset of the profile, the
/// launch of a series by the doctrine with the closing of the previous line (one line per actor, #125), the line rate, and the give-up rule
/// A40 for a project in О (<c>cancel_design</c>) and for a series without output (<c>close_line</c>, stage С). Decisions of month t are
/// written in the compute of t to <c>pol.*</c> columns and in force from t+1 (a slot carries the <c>stamp</c> of the month that consumes
/// it, as the commands in <c>cmd.*</c>); the AI never forces a series. Without <see cref="IPolicyProducts"/> the policy makes none.
/// </summary>
public sealed partial class PolicySystem
{
    /// <summary>Design commands (compose, derive, cancel) the AI can have in force in one month: one compose, one derive, at most one cancel per generation.</summary>
    public const int DesignSlots = 6;

    private IPolicyProducts? _prod;
    private int _designs, _gens;
    private bool _derive;
    private PolicyParams _pp = new();
    private IntColumn[] _dsnStamp = [], _dsnKind = [], _dsnTarget = [], _startStamp = [], _closeStamp = [], _line = [], _rep = [];
    private IntColumn[][] _dsnAlt = [];
    private LongColumn[] _startRate = [];

    private void AddProductColumns(Table table, IPolicyProducts? products, int designs, int generations, bool derive, PolicyParams parameters)
    {
        _prod = products;
        _designs = designs;
        _gens = generations;
        _derive = derive;
        _pp = parameters;
        if (products is not null && parameters.FvBudget <= Fx6.Zero)
            throw new ArgumentException("The product AI needs a positive fv_budget (PolicyParams.FvBudgetOf).", nameof(parameters));
        int slots = products is null && designs == 0 ? 0 : DesignSlots;
        int alts = products?.Layout.SlotCount ?? 0;
        _dsnStamp = new IntColumn[slots];
        _dsnKind = new IntColumn[slots];
        _dsnTarget = new IntColumn[slots];
        _dsnAlt = new IntColumn[slots][];
        for (int k = 0; k < slots; k++)
        {
            _dsnStamp[k] = table.AddInt($"pol.dsn.{k}.stamp");
            _dsnKind[k] = table.AddInt($"pol.dsn.{k}.kind");
            _dsnTarget[k] = table.AddInt($"pol.dsn.{k}.target");
            _dsnAlt[k] = new IntColumn[alts];
            for (int s = 0; s < alts; s++)
                _dsnAlt[k][s] = table.AddInt($"pol.dsn.{k}.alt.{s}");
        }
        _startStamp = new IntColumn[designs];
        _startRate = new LongColumn[designs];
        _closeStamp = new IntColumn[designs];
        _line = new IntColumn[designs];
        for (int d = 0; d < designs; d++)
        {
            _startStamp[d] = table.AddInt($"pol.start.{d}.stamp");
            _startRate[d] = table.AddLong($"pol.start.{d}.rate");
            _closeStamp[d] = table.AddInt($"pol.close.{d}.stamp");
            _line[d] = table.AddInt($"pol.line.{d}");
        }
        _rep = new IntColumn[generations];
        for (int g = 0; g < generations; g++)
            _rep[g] = table.AddInt($"pol.rep.g{g + 1}");
        AddFleetColumns(table, products is null && designs == 0 ? 0 : designs, generations);
    }

    // ---- reads for the adapter (what is in force at the month step being computed)

    private static int Stamp(long month) => checked((int)month + 1);

    /// <summary>The AI's compose/derive/cancel commands consumed at <paramref name="month"/>, in the order they were decided.</summary>
    public IReadOnlyList<DesignCommand> AiDesignCommands(int actor, long month)
    {
        var list = new List<DesignCommand>();
        for (int k = 0; k < _dsnStamp.Length; k++)
        {
            if (_dsnStamp[k].Current[actor] != Stamp(month))
                continue;
            var kind = (DesignCommandKind)(_dsnKind[k].Current[actor] - 1);
            var alts = new int[_dsnAlt[k].Length];
            for (int s = 0; s < alts.Length; s++)
                alts[s] = _dsnAlt[k][s].Current[actor];
            list.Add(new DesignCommand(kind, _dsnTarget[k].Current[actor], kind == DesignCommandKind.Cancel ? [] : [.. alts]));
        }
        return list;
    }

    /// <summary>The AI's <c>start_series</c> for the design consumed at <paramref name="month"/>: its rate (<c>force</c> is never set), or null.</summary>
    public Fx6? AiStart(int actor, int design, long month) =>
        _startStamp[design].Current[actor] == Stamp(month) ? Fx6.FromRaw(_startRate[design].Current[actor]) : null;

    public bool AiClose(int actor, int design, long month) => _closeStamp[design].Current[actor] == Stamp(month);

    /// <summary>The line of the design was launched by the AI and takes its rate from <c>r_AI</c> (spec A41); a line started by a command keeps that rate.</summary>
    public bool AiLine(int actor, int design) => _line[design].Current[actor] != 0;

    /// <summary>Times a <c>Requirement</c> for the generation was repeated after a project of it was given up (spec A40); at most 1.</summary>
    public int Repeats(int actor, int generation) => _rep[generation - 1].Current[actor];

    /// <summary>
    /// Class of a stage-О give-up (spec A40 v0.15) for the print: <see cref="GiveUpClass.Processes"/> if in month <paramref name="month"/> the gate and the
    /// doctrine condition hold and only <c>admitted</c> is false, else <see cref="GiveUpClass.Defects"/>. A pure function of the snapshot.
    /// </summary>
    public GiveUpClass ClassifyGiveUp(int actorRow, int design, long month)
    {
        PolicyDesign x = _prod!.Design(actorRow, design, month);
        Fx6 limit = _pp.LaunchShare[(int)Profile(actorRow)] * _prod.DRef;
        return x.GateOpen && x.Hidden <= limit && !_prod.Admitted(actorRow, design, month) ? GiveUpClass.Processes : GiveUpClass.Defects;
    }

    // ---- decisions

    private sealed class Decisions(int generations)
    {
        public readonly List<DesignCommand> Design = [];
        public readonly List<(int Design, Fx6 Rate)> Starts = [];
        public readonly List<int> Closes = [];
        public readonly bool[] Repeat = new bool[generations];
        public readonly List<FleetCommand> Fleet = [];
        public Fx6 IncomeExpected;
    }

    private void InitializeProducts()
    {
        if (_prod is null)
            return;
        // A8: the annual Requirement (and the derive of the arm) of month 0 are made before tick 0 and act in it.
        for (int row = 0; row < _actorNumber.Length; row++)
        {
            Decisions dec = Decide(row, 0, annual: true, monthly: false, default, default);
            int row0 = row;
            Commit(row, dec, Stamp(0), (c, v) => SetCurrent(c, row0, v), (c, v) => SetCurrent(c, row0, v));
        }
    }

    private void ComputeProducts(int row, long t, bool annual, BudgetShares sharesNext, Fx6 budgetNext)
    {
        if (_prod is null)
            return;
        Decisions dec = Decide(row, t, annual, monthly: true, sharesNext, budgetNext);
        Commit(row, dec, Stamp(t + 1), (c, v) => c.Next[row] = v, (c, v) => c.Next[row] = v);
    }

    private static void SetCurrent(IntColumn column, int row, int value)
    {
        int[] copy = column.Current.ToArray();
        copy[row] = value;
        column.Load(copy);
    }

    private static void SetCurrent(LongColumn column, int row, long value)
    {
        long[] copy = column.Current.ToArray();
        copy[row] = value;
        column.Load(copy);
    }

    private int NewestGeneration(int row, long t)
    {
        ProductsLayout l = _prod!.Layout;
        int g = 0;
        for (int i = 1; i <= l.Generations; i++)
        {
            if (_prod.Knows(row, l.GenApp[i - 1], t))
                g = i;
        }
        return g;
    }

    private static bool Live(DesignStage s) => s is DesignStage.Project or DesignStage.Prototype or DesignStage.Series or DesignStage.InService;

    private Decisions Decide(int row, long t, bool annual, bool monthly, BudgetShares sharesNext, Fx6 budgetNext)
    {
        IPolicyProducts prod = _prod!;
        var profile = (PolicyProfile)_profile.Current[row];
        var dec = new Decisions(_gens);
        var designs = new PolicyDesign[_designs];
        for (int d = 0; d < _designs; d++)
            designs[d] = prod.Design(row, d, t);
        ProductsLayout l = prod.Layout;

        if (annual)
        {
            int free = prod.FreeBureaus(row);
            bool issued = false;
            int g = NewestGeneration(row, t);
            if (g > 0)
            {
                bool live = false, gaveUp = false;
                for (int d = 0; d < _designs; d++)
                {
                    if (designs[d].PlatformGen != g)
                        continue;
                    live |= Live(designs[d].Stage);
                    gaveUp |= designs[d].Cancelled;
                }
                // A project given up (cancelled) is not live; the Requirement for its generation repeats once (A40).
                bool repeatOk = !gaveUp || _rep[g - 1].Current[row] == 0;
                var alts = new int[l.SlotCount];
                if (!live && repeatOk && free > 0 && ProductPresets.Compose(profile, prod, _pp, row, g, t, alts))
                {
                    dec.Design.Add(new DesignCommand(DesignCommandKind.Compose, g, [.. alts]));
                    // The repeat is counted when the Requirement is issued, even if the layer later rejects the command (no bureau, capacity): the
                    // dead end then stays visible instead of looping; the next year does not repeat it again.
                    dec.Repeat[g - 1] = gaveUp;
                    issued = true;
                }
            }
            if (_derive && free >= (issued ? 2 : 1))
            {
                int parent = -1;
                for (int d = 0; d < _designs; d++)
                {
                    if (designs[d].Parent >= 0 || designs[d].Stage is not (DesignStage.Series or DesignStage.InService))
                        continue;
                    bool has = false;
                    for (int e = 0; e < _designs; e++)
                        has |= designs[e].Stage != DesignStage.None && designs[e].Parent == d;
                    if (!has)
                        parent = d;
                }
                var alts = new int[l.SlotCount];
                if (parent >= 0 && ProductPresets.Derive(profile, prod, _pp, row, parent, t, alts))
                    dec.Design.Add(new DesignCommand(DesignCommandKind.Derive, parent, [.. alts]));
            }
        }
        if (!monthly)
            return dec;

        // A40: one give-up per generation and month; only while the Requirement for the generation has not been repeated.
        var gaveUpNow = new bool[l.Generations + 1];
        var cancelNow = new bool[_designs];
        for (int d = 0; d < _designs; d++)
        {
            PolicyDesign x = designs[d];
            if (x.Stage != DesignStage.Prototype || x.StageOMonth < 0 || t - x.StageOMonth < _pp.TAb || !GiveUpAllowed(row, x.PlatformGen, gaveUpNow, designs))
                continue;
            // A launch decided last month is still pending in the snapshot: a launching project is not given up.
            if (_startStamp[d].Current[row] == Stamp(t))
                continue;
            if (Pending(_dsnStamp, row, t, k => _dsnKind[k].Current[row] == 3 && _dsnTarget[k].Current[row] == d))
                continue;
            dec.Design.Add(new DesignCommand(DesignCommandKind.Cancel, d, []));
            cancelNow[d] = true;
            gaveUpNow[x.PlatformGen] = true;
        }
        for (int d = 0; d < _designs; d++)
        {
            PolicyDesign x = designs[d];
            if (x.Stage != DesignStage.Series || x.IdleMonths < _pp.TAb || !GiveUpAllowed(row, x.PlatformGen, gaveUpNow, designs) || _closeStamp[d].Current[row] == Stamp(t))
                continue;
            dec.Closes.Add(d);
            gaveUpNow[x.PlatformGen] = true;
        }

        // Launch (A35, A41): the first project in О by id whose gate is open, whose hidden defects are within k_P·D_ref and (F17, #138) whose
        // processes are admitted at the site; one line per actor. A project that waits keeps the working line: nothing is closed without a launch.
        bool startPending = false;
        for (int d = 0; d < _designs; d++)
            startPending |= _startStamp[d].Current[row] == Stamp(t);
        if (!startPending)
        {
            Fx6 limit = _pp.LaunchShare[(int)profile] * prod.DRef;
            for (int d = 0; d < _designs; d++)
            {
                PolicyDesign x = designs[d];
                // A project cancelled this month or whose cancel is still pending does not launch.
                if (x.Stage != DesignStage.Prototype || !x.GateOpen || x.Hidden > limit || cancelNow[d]
                    || Pending(_dsnStamp, row, t, k => _dsnKind[k].Current[row] == 3 && _dsnTarget[k].Current[row] == d)
                    || !prod.Admitted(row, d, t))
                    continue;
                Fx6 procurement = Shares(row).Procurement * Budget(row);
                dec.Starts.Add((d, PolicyRules.LineRate(procurement, x.UnitCostFloor)));
                for (int e = 0; e < _designs; e++)
                {
                    // The parent of a derived design is not closed: the layer moves its line (contract §4.4a).
                    if (designs[e].Stage == DesignStage.Series && e != x.Parent && !dec.Closes.Contains(e) && _closeStamp[e].Current[row] != Stamp(t))
                        dec.Closes.Add(e);
                }
                break;
            }
        }
        FleetDecision fleet = DecideFleet(row, t, designs, dec, sharesNext, budgetNext);
        dec.Fleet.AddRange(fleet.Commands);
        dec.IncomeExpected = fleet.IncomeExpected;
        return dec;
    }

    // One withdrawal per (actor, g_p) for both branches (spec A40): none this month, no design of the generation already given up, no repeat made.
    private bool GiveUpAllowed(int row, int generation, bool[] gaveUpNow, PolicyDesign[] designs)
    {
        if (generation < 1 || generation > _gens || gaveUpNow[generation] || _rep[generation - 1].Current[row] != 0)
            return false;
        foreach (PolicyDesign x in designs)
        {
            if (x.Cancelled && x.PlatformGen == generation)
                return false;
        }
        return true;
    }

    // A decision of the previous month that the layer applies only in this month's compute is still invisible in the snapshot.
    private bool Pending(IntColumn[] stamps, int row, long t, Func<int, bool> match)
    {
        for (int k = 0; k < stamps.Length; k++)
        {
            if (stamps[k].Current[row] == Stamp(t) && match(k))
                return true;
        }
        return false;
    }

    private void Commit(int row, Decisions dec, int stamp, Action<IntColumn, int> setInt, Action<LongColumn, long> setLong)
    {
        if (dec.Design.Count > DesignSlots)
            throw new PolicyInvariantException(stamp, row, $"{dec.Design.Count} design commands for {DesignSlots} slots");
        for (int k = 0; k < dec.Design.Count; k++)
        {
            DesignCommand c = dec.Design[k];
            setInt(_dsnStamp[k], stamp);
            setInt(_dsnKind[k], (int)c.Kind + 1);
            setInt(_dsnTarget[k], c.Target);
            for (int s = 0; s < _dsnAlt[k].Length; s++)
                setInt(_dsnAlt[k][s], c.Alts.IsDefaultOrEmpty ? -1 : c.Alts[s]);
        }
        foreach ((int d, Fx6 rate) in dec.Starts)
        {
            setInt(_startStamp[d], stamp);
            setLong(_startRate[d], rate.Raw);
            // Limitation: a start_series of the player/harness in the same month wins (the adapter takes the external command first), but this flag
            // still gives the line to r_AI afterwards; a line whose rate must be kept is started by a command in an earlier month than the AI's start.
            setInt(_line[d], 1);
        }
        foreach (int d in dec.Closes)
            setInt(_closeStamp[d], stamp);
        if (dec.Fleet.Count > _fltStamp.Length)
            throw new PolicyInvariantException(stamp, row, $"{dec.Fleet.Count} fleet commands for {_fltStamp.Length} slots");
        for (int k = 0; k < dec.Fleet.Count; k++)
        {
            setInt(_fltStamp[k], stamp);
            setInt(_fltKind[k], (int)dec.Fleet[k].Kind);
            setInt(_fltDesign[k], dec.Fleet[k].Design);
            setInt(_fltN[k], checked((int)dec.Fleet[k].N));
        }
        setLong(_incExp, dec.IncomeExpected.Raw);
        for (int g = 0; g < dec.Repeat.Length; g++)
        {
            if (dec.Repeat[g])
                setInt(_rep[g], _rep[g].Current[row] + 1);
        }
    }
}
