using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Core.Policy;

/// <summary>Which rule closed a line (observation, SLICE_COMPOSITION §13.4): A40 branch С, F17b rule 2, F17b rule 1 closing every line, F17b rule 1 replacing the candidate.</summary>
public enum LineCloseReason
{
    None = 0,
    A40C = 1,
    YProd = 2,
    LaunchAll = 3,
    LaunchCandidate = 4,
}

/// <summary>A licence offer the AI wanted and could not pay (A43.3 (б)): the seller's row, its generation, the price and the room (<c>room &lt; price</c>).</summary>
public readonly record struct MoneyRefusal(int Seller, int Gen, Fx6 Price, Fx6 Room);

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
    private IntColumn[] _dsnStamp = [], _dsnKind = [], _dsnTarget = [], _dsnGen = [], _startStamp = [], _startGen = [], _closeStamp = [], _closeGen = [], _line = [], _rep = [];
    private IntColumn[][] _dsnAlt = [];
    private LongColumn[] _startRate = [];

    // Traces of the last compute (SLICE_COMPOSITION §13.4, §13.6): scratch outside the table, a row is rewritten by its own compute; not state, not hashed.
    [Scratch]
    private LineCloseReason[] _closeReason = [];
    [Scratch]
    private List<MoneyRefusal>[] _moneyRefusals = [];

    private void AddProductColumns(Table table, IPolicyProducts? products, int designs, int generations, bool derive, PolicyParams parameters)
    {
        _prod = products;
        _closeReason = new LineCloseReason[table.RowCount * designs];
        _moneyRefusals = [.. Enumerable.Range(0, table.RowCount).Select(_ => new List<MoneyRefusal>())];
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
        _dsnGen = new IntColumn[slots];
        _dsnAlt = new IntColumn[slots][];
        for (int k = 0; k < slots; k++)
        {
            _dsnStamp[k] = table.AddInt($"pol.dsn.{k}.stamp");
            _dsnKind[k] = table.AddInt($"pol.dsn.{k}.kind");
            _dsnTarget[k] = table.AddInt($"pol.dsn.{k}.target");
            _dsnGen[k] = table.AddInt($"pol.dsn.{k}.gen");
            _dsnAlt[k] = new IntColumn[alts];
            for (int s = 0; s < alts; s++)
                _dsnAlt[k][s] = table.AddInt($"pol.dsn.{k}.alt.{s}");
        }
        _startStamp = new IntColumn[designs];
        _startRate = new LongColumn[designs];
        _startGen = new IntColumn[designs];
        _closeGen = new IntColumn[designs];
        _closeStamp = new IntColumn[designs];
        _line = new IntColumn[designs];
        for (int d = 0; d < designs; d++)
        {
            _startStamp[d] = table.AddInt($"pol.start.{d}.stamp");
            _startRate[d] = table.AddLong($"pol.start.{d}.rate");
            _startGen[d] = table.AddInt($"pol.start.{d}.gen");
            _closeStamp[d] = table.AddInt($"pol.close.{d}.stamp");
            _closeGen[d] = table.AddInt($"pol.close.{d}.gen");
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
            list.Add(kind == DesignCommandKind.Compose
                ? DesignCommand.Compose(_dsnTarget[k].Current[actor], [.. alts])
                : new DesignCommand(kind, 0, kind == DesignCommandKind.Cancel ? [] : [.. alts], new DesignHandle(_dsnTarget[k].Current[actor], _dsnGen[k].Current[actor])));
        }
        return list;
    }

    /// <summary>The AI's <c>start_series</c> for the slot consumed at <paramref name="month"/>: the handle seen at the decision and the rate (<c>force</c> is never set), or null.</summary>
    public (DesignHandle Design, Fx6 Rate)? AiStart(int actor, int design, long month) =>
        _startStamp[design].Current[actor] == Stamp(month) ? (new DesignHandle(design, _startGen[design].Current[actor]), Fx6.FromRaw(_startRate[design].Current[actor])) : null;

    /// <summary><c>LINE_CLOSE</c> of the run (spec A41 F17b).</summary>
    public LineCloseRule LineClose => _pp.LineClose;

    /// <summary>The AI's <c>close_line</c> for the slot consumed at <paramref name="month"/>: the handle seen at the decision, or null.</summary>
    public DesignHandle? AiClose(int actor, int design, long month) =>
        _closeStamp[design].Current[actor] == Stamp(month) ? new DesignHandle(design, _closeGen[design].Current[actor]) : null;

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

    // One per thread, reset by Decide: a decision lives from Decide to Commit of the same actor and is never kept, so a compute allocates none.
    private sealed class Decisions
    {
        public readonly List<DesignCommand> Design = [];
        public readonly List<(int Design, Fx6 Rate)> Starts = [];
        public readonly List<int> Closes = [];
        public readonly List<LineCloseReason> CloseReasons = [];   // parallel to Closes
        public bool[] Repeat = [];
        public readonly List<FleetCommand> Fleet = [];
        public Fx6 IncomeExpected;
        public PolicyDesign[] Designs = [];
        public bool[] GaveUpNow = [], CancelNow = [];
        public readonly List<int> Lines = [];

        public void Reset(int generations, int designs)
        {
            Design.Clear();
            Starts.Clear();
            Closes.Clear();
            CloseReasons.Clear();
            Fleet.Clear();
            Lines.Clear();
            IncomeExpected = Fx6.Zero;
            if (Repeat.Length != generations)
            {
                Repeat = new bool[generations];
                GaveUpNow = new bool[generations + 1];
            }
            if (Designs.Length != designs)
            {
                Designs = new PolicyDesign[designs];
                CancelNow = new bool[designs];
            }
            Array.Clear(Repeat);
            Array.Clear(GaveUpNow);
            Array.Clear(CancelNow);
        }
    }

    [Scratch, ThreadStatic] private static Decisions? t_decisions;

    private void InitializeProducts()
    {
        if (_prod is null)
            return;
        // A8: the annual Requirement (and the derive of the arm) of month 0 are made before tick 0 and act in it.
        for (int row = 0; row < _actorNumber.Length; row++)
        {
            Decisions dec = Decide(row, 0, annual: true, monthly: false, default, default);
            Commit(row, dec, Stamp(0), next: false);
        }
    }

    private void ComputeProducts(int row, long t, bool annual, BudgetShares sharesNext, Fx6 budgetNext, WhyList why)
    {
        if (_prod is null)
            return;
        Array.Clear(_closeReason, row * _designs, _designs);
        _moneyRefusals[row].Clear();
        Decisions dec = Decide(row, t, annual, monthly: true, sharesNext, budgetNext);
        for (int i = 0; i < dec.Closes.Count; i++)
            _closeReason[row * _designs + dec.Closes[i]] = dec.CloseReasons[i];
        ComputeLicence(row, t, dec.Designs, dec, sharesNext, budgetNext, why);
        Commit(row, dec, Stamp(t + 1), next: true);
    }

    /// <summary>Adds a close of the design with its reason (callers check <c>Contains</c>: one close per design and month, the first reason stays).</summary>
    private static void AddClose(Decisions dec, int design, LineCloseReason reason)
    {
        dec.Closes.Add(design);
        dec.CloseReasons.Add(reason);
    }

    /// <summary>
    /// Observation: why the AI closed the line <paramref name="design"/> in the last monthly compute of the actor (the first reason of the month),
    /// or <see cref="LineCloseReason.None"/>.
    /// </summary>
    [Observation]
    public LineCloseReason LastCloseReason(int actorRow, int design) => _closeReason[actorRow * _designs + design];

    /// <summary>Observation: licence offers the actor wanted in the last monthly compute but could not pay, by ascending seller row.</summary>
    [Observation]
    public IReadOnlyList<MoneyRefusal> LastMoneyRefusals(int actorRow) => _moneyRefusals[actorRow].ToArray();

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

    /// <summary>
    /// The variants of the annual <c>Requirement</c> the AI would face at <paramref name="month"/>: the generation is the newest the actor knows
    /// (<c>Count</c> 0 without one). A pure read of the snapshot.
    /// </summary>
    public PresetVariants RequirementVariants(int actorRow, long month)
    {
        int g = NewestGeneration(actorRow, month);
        return g == 0 ? new PresetVariants(0, 0, [], [], Fx6.Zero, null) : ProductPresets.Variants(_prod!, _pp, actorRow, g, month);
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
        Decisions dec = t_decisions ??= new Decisions();
        dec.Reset(_gens, _designs);
        PolicyDesign[] designs = dec.Designs;
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
                    dec.Design.Add(DesignCommand.Compose(g, [.. alts]));
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
                    dec.Design.Add(DesignCommand.Derive(designs[parent].HandleOf(parent), [.. alts]));
            }
        }
        if (!monthly)
            return dec;

        // A40: one give-up per generation and month; only while the Requirement for the generation has not been repeated.
        bool[] gaveUpNow = dec.GaveUpNow, cancelNow = dec.CancelNow;
        Fx6 launchLimit = _pp.LaunchShare[(int)profile] * prod.DRef;
        for (int d = 0; d < _designs; d++)
        {
            PolicyDesign x = designs[d];
            if (x.Stage != DesignStage.Prototype || x.StageOMonth < 0 || t - x.StageOMonth < _pp.TAb || !GiveUpAllowed(row, x.PlatformGen, gaveUpNow, designs))
                continue;
            // v0.21 guard: a project that satisfies the launch condition L(d, t) of the snapshot is not given up (it launches by A41).
            if (x.GateOpen && x.Hidden <= launchLimit && prod.Admitted(row, d, t))
                continue;
            // A launch decided last month is still pending in the snapshot: a launching project is not given up.
            if (_startStamp[d].Current[row] == Stamp(t))
                continue;
            if (CancelPending(row, t, x.HandleOf(d)))
                continue;
            dec.Design.Add(DesignCommand.Cancel(x.HandleOf(d)));
            cancelNow[d] = true;
            gaveUpNow[x.PlatformGen] = true;
        }
        for (int d = 0; d < _designs; d++)
        {
            PolicyDesign x = designs[d];
            if (x.Stage != DesignStage.Series || x.IdleMonths < _pp.TAb || !GiveUpAllowed(row, x.PlatformGen, gaveUpNow, designs) || _closeStamp[d].Current[row] == Stamp(t))
                continue;
            AddClose(dec, d, LineCloseReason.A40C);
            gaveUpNow[x.PlatformGen] = true;
        }

        // F17b: an AI actor holds at most two lines (the working one and the candidate); a derive arm moves lines by the layer (contract §4.4a).
        // «Lines» are the snapshot's С stages minus closes still pending. A start pending from the previous month is not counted: it blocks any
        // new launch (startPending) and lands together with its own closes, so the count never exceeds two when it becomes a line. (The fleet
        // rule A42.4 projects pending starts too, because it looks at the book of t+1.)
        List<int> lines = dec.Lines;
        for (int d = 0; d < _designs; d++)
        {
            if (designs[d].Stage == DesignStage.Series && _closeStamp[d].Current[row] != Stamp(t))
                lines.Add(d);
        }
        bool yprod = _pp.LineClose == LineCloseRule.YProd;
        if (yprod && !_derive && lines.Count > 2)
            throw new PolicyInvariantException(t, row, $"{lines.Count} lines of one AI actor (F17b allows two)");
        int candidate = lines.Count == 2 ? Candidate(designs, lines[0], lines[1]) : -1;

        // F17b rule 2 comes BEFORE the launch (owner decision on review of TASK-0030): two lines and the candidate has y_prod in the snapshot ->
        // the working line closes and the candidate is the working one; a launch of the same month below y_prod then becomes the new candidate
        // (still at most two lines). One close per project and month.
        if (yprod && candidate >= 0 && !dec.Closes.Contains(candidate))
        {
            int working = lines[0] == candidate ? lines[1] : lines[0];
            if (designs[candidate].Parent != working && YMin(row, candidate, t) >= _pp.YProd)
            {
                if (!dec.Closes.Contains(working))
                    AddClose(dec, working, LineCloseReason.YProd);
                candidate = -1;
            }
        }
        // Launch (A35, A41): the first project in О by id whose gate is open, whose hidden defects are within k_P·D_ref and (F17, #138) whose
        // processes are admitted at the site; one line per actor. A project that waits keeps the working line: nothing is closed without a launch.
        bool startPending = false;
        for (int d = 0; d < _designs; d++)
            startPending |= _startStamp[d].Current[row] == Stamp(t);
        if (!startPending)
        {
            for (int d = 0; d < _designs; d++)
            {
                PolicyDesign x = designs[d];
                // A project cancelled this month or whose cancel is still pending does not launch.
                if (x.Stage != DesignStage.Prototype || !x.GateOpen || x.Hidden > launchLimit || cancelNow[d]
                    || CancelPending(row, t, x.HandleOf(d))
                    || !prod.Admitted(row, d, t))
                    continue;
                Fx6 procurement = Shares(row).Procurement * Budget(row);
                dec.Starts.Add((d, PolicyRules.LineRate(procurement, x.UnitCostFloor)));
                // F17b (A41): below y_prod only the candidate is replaced and the working line stays; at y_prod (or with ADMIT) every other line closes.
                bool all = !yprod || YMin(row, d, t) >= _pp.YProd;
                for (int e = 0; e < _designs; e++)
                {
                    // The parent of a derived design is not closed: the layer moves its line (contract §4.4a).
                    if (designs[e].Stage == DesignStage.Series && e != x.Parent && !dec.Closes.Contains(e) && _closeStamp[e].Current[row] != Stamp(t)
                        && (all || e == candidate))
                        AddClose(dec, e, all ? LineCloseReason.LaunchAll : LineCloseReason.LaunchCandidate);
                }
                break;
            }
        }
        FleetDecision fleet = DecideFleet(row, t, designs, dec, sharesNext, budgetNext);
        dec.Fleet.AddRange(fleet.Commands);
        dec.IncomeExpected = fleet.IncomeExpected;
        return dec;
    }

    /// <summary>
    /// The candidate of a pair of lines (spec A41 F17b): the one with the larger platform generation, a tie to the newer project
    /// (the later <see cref="PolicyDesign.StageOMonth"/>, the month the project entered stage O), a further tie to the larger design id.
    /// A slot index alone is not an age: a reused lower slot holds a newer project (ADR-0005 p. 3, DEBT-0053).
    /// </summary>
    internal static int Candidate(PolicyDesign[] designs, int a, int b) =>
        Candidate(a, designs[a].PlatformGen, designs[a].StageOMonth, b, designs[b].PlatformGen, designs[b].StageOMonth);

    /// <summary>The same ordering on bare keys, shared by every reader that picks the candidate line (policy and the slice adapter).</summary>
    public static int Candidate(int a, int platformGenA, int stageOA, int b, int platformGenB, int stageOB)
    {
        if (platformGenA != platformGenB)
            return platformGenA > platformGenB ? a : b;
        if (stageOA != stageOB)
            return stageOA > stageOB ? a : b;
        return Math.Max(a, b);
    }

    // y_min(d, t): the least yield of the design's prc_set at the home site in the snapshot; an unknown process gives 0 (spec A41 F17b).
    private Fx6 YMin(int row, int design, long t)
    {
        int[] prc = _prod!.PrcSet(row, design);
        if (prc.Length == 0)
            return Fx6.Zero;
        Fx6 min = _prod.AdmittedYield(row, prc[0], t);
        for (int i = 1; i < prc.Length; i++)
        {
            Fx6 y = _prod.AdmittedYield(row, prc[i], t);
            if (y < min)
                min = y;
        }
        return min;
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
    private bool CancelPending(int row, long t, DesignHandle design)
    {
        for (int k = 0; k < _dsnStamp.Length; k++)
        {
            if (_dsnStamp[k].Current[row] == Stamp(t) && _dsnKind[k].Current[row] == 3 && _dsnTarget[k].Current[row] == design.Slot
                && _dsnGen[k].Current[row] == design.Generation)
                return true;
        }
        return false;
    }

    // next = true inside the compute (writes Next); false before tick 0 (loads Current).
    private void Commit(int row, Decisions dec, int stamp, bool next)
    {
        void setInt(IntColumn c, int v)
        {
            if (next)
                c.Next[row] = v;
            else
                SetCurrent(c, row, v);
        }
        void setLong(LongColumn c, long v)
        {
            if (next)
                c.Next[row] = v;
            else
                SetCurrent(c, row, v);
        }

        if (dec.Design.Count > DesignSlots)
            throw new PolicyInvariantException(stamp, row, $"{dec.Design.Count} design commands for {DesignSlots} slots");
        for (int k = 0; k < dec.Design.Count; k++)
        {
            DesignCommand c = dec.Design[k];
            setInt(_dsnStamp[k], stamp);
            setInt(_dsnKind[k], (int)c.Kind + 1);
            setInt(_dsnTarget[k], c.Target);
            setInt(_dsnGen[k], c.Design.Generation);
            for (int s = 0; s < _dsnAlt[k].Length; s++)
                setInt(_dsnAlt[k][s], c.Alts.IsDefaultOrEmpty ? -1 : c.Alts[s]);
        }
        foreach ((int d, Fx6 rate) in dec.Starts)
        {
            setInt(_startStamp[d], stamp);
            setLong(_startRate[d], rate.Raw);
            setInt(_startGen[d], dec.Designs[d].SlotGeneration);
            // Limitation: a start_series of the player/harness in the same month wins (the adapter takes the external command first), but this flag
            // still gives the line to r_AI afterwards; a line whose rate must be kept is started by a command in an earlier month than the AI's start.
            setInt(_line[d], 1);
        }
        foreach (int d in dec.Closes)
        {
            setInt(_closeStamp[d], stamp);
            setInt(_closeGen[d], dec.Designs[d].SlotGeneration);
        }
        if (dec.Fleet.Count > _fltStamp.Length)
            throw new PolicyInvariantException(stamp, row, $"{dec.Fleet.Count} fleet commands for {_fltStamp.Length} slots");
        for (int k = 0; k < dec.Fleet.Count; k++)
        {
            setInt(_fltStamp[k], stamp);
            setInt(_fltKind[k], (int)dec.Fleet[k].Kind);
            setInt(_fltDesign[k], dec.Fleet[k].Design.Slot);
            setInt(_fltGen[k], dec.Fleet[k].Design.Generation);
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
