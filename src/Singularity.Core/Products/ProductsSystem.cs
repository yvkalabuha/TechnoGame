using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Kernel;

namespace Singularity.Core.Products;

/// <summary>
/// Step 7 money of one line (observation, SLICE_COMPOSITION §13.3): <c>LeftIn</c> the common money before the line's purchase, <c>FromLeft</c> taken from it,
/// <c>Training</c> the line's training budget, <c>FromTraining</c> taken from that (<c>pay − FromLeft</c>).
/// </summary>
public readonly record struct LineMoneyStep(int Design, Fx6 LeftIn, Fx6 FromLeft, Fx6 Training, Fx6 FromTraining);

/// <summary>Full product model or the product-off control (spec §6.1).</summary>
public enum ProductsMode
{
    Full,

    /// <summary>L_P: canonical project with a fixed output rate; no bureaus, <c>line_E</c>, defects, presets, procurement budget.</summary>
    CanonicalLp,
}

/// <summary>A broken product invariant.</summary>
public sealed class ProductsInvariantException(long month, int actor, string message)
    : Exception($"product invariant violated at month {month}, actor {actor}: {message}")
{
    public long Month { get; } = month;
    public int Actor { get; } = actor;
}

/// <summary>
/// Product layer of the jet slice (contract v2.4 §3.1, §4.4, §4.6, §5; spec §1.4, §3.3, §3.5 A32–A38, §5.5, §6.1): a monthly
/// system on a table with one row per actor (one site per actor, site row = actor row, spec §3.5 A27). All state is in Table
/// columns (ADR-0013 §4): per design slot <c>i &lt; max_designs</c> (the lowest free slot, reused with generation + 1) its stage П/О/С/В (#67),
/// alternatives, platform generation <c>g_p</c>, parent, design work left, defects, cumulative test and fleet hours,
/// <c>line_E</c>, series gate (<c>d_gate</c>, <c>stage_o_month</c>, <c>gate_opened</c>, <c>series_forced</c>), accident and
/// retrofit state (<c>loss_acc</c>, <c>retro_pool</c>), last rate and unit cost; per bureau <c>bureau_exp</c> and its design; per
/// (design, calendar year) the cohort count; per actor the upkeep needed, the paid share, the money spent and the command
/// rejection counters. Compute of month t reads other layers only through <see cref="IProductsInputs"/> (snapshot t−1) and its
/// own state; own values computed earlier in the same step are used by the later parts of that step (order of §4.4d). It
/// posts <c>report_output</c> and <c>reserve_workforce</c> into the capability mailbox (applied in the capability merge of
/// month t; <c>granted</c> visible at t+1). No RNG anywhere in the layer (#28).
/// </summary>
public sealed partial class ProductsSystem : ITickSystem
{
    public const int DefaultSystemId = 300;

    /// <summary>
    /// Column layout version of the system (T-15, #117): 1 = TASK-0018 (slot (g, variant)), 2 = contract v2.4 (slots by
    /// capacity, gate, accident and retrofit columns), 3 = contract v2.4 r4 (<c>cancelled</c>, <c>idle_c</c>, <c>built_any</c>, #128), 4 = contract r6 (fleet commands: <c>retired</c>, <c>sold</c>, quota, <c>line_bind</c>, sale money, #141–#148).
    /// Part of the state-hash schema version of a model that hosts the system.
    /// </summary>
    public const int LayoutVersion = 4;

    private static readonly int RejectionKinds = Enum.GetValues<CommandRejection>().Length;

    private readonly ProductsLayout _l;
    private readonly ProductsParams _p;
    private readonly IProductsInputs _inputs;
    private readonly IndustryMailbox _mailbox;
    private readonly ProductsMode _mode;
    private readonly int _d, _s, _y, _b, _qb;
    private readonly Fx6 _dRef;

    // bureau_req[af_class] (§3.3, A34): computed once at construction, never in a tick.
    private readonly int[] _bureauReq;

    private readonly IntColumn[] _stage, _bureauOf, _alt, _bureauDesign, _gp, _parent, _stageO, _gateOpened, _forced, _lpSlot, _gen, _parentGen, _lpGen, _altReached;
    private readonly IntColumn[] _cancelled, _idleC, _built, _quotaBase, _soldY, _lineBind;
    private readonly LongColumn[] _work, _d0, _hidden, _open, _found, _hours, _lineE, _rate, _cost, _exp, _cohort;
    private readonly LongColumn[] _qMask;   // q[design, y] as a bit per cohort year, 62 years per block: [design * _qb + y / 62] (contract r13a §3.1, §9.1)
    private readonly LongColumn[] _rateCmd, _dGate, _lossAcc, _retro, _lost, _inService0, _rejected, _retired, _sold, _replCredit;
    // Mastery programme state per programme process (tier ≥ 5) and actor: ((start month + 1) << 1) | domestic, 0 = none (TASK-0134, prog_state).
    private readonly IntColumn[] _prog;
    private readonly int[] _progOf, _progRows, _progNumbers;
    private readonly LongColumn _need, _paid, _spent, _tbSpent, _saleIncome, _saleCap, _saleY, _extTotal;

    // Midyear month index of each cohort year (spec A32): age of cohort Y at month t is t − _m0[Y].
    private readonly int[] _m0;
    private readonly PrcMemo?[] _prcMemo;

    // Step 7 trace (SLICE_COMPOSITION §13.3): scratch, rewritten for row a at the start of its compute; not state, not hashed.
    [Scratch]
    private readonly bool[] _moneyTraced;
    [Scratch]
    private readonly long[] _traceLeftStart;
    [Scratch]
    private readonly LineMoneyStep?[] _traceLines;

    public ProductsSystem(Table table, ProductsLayout layout, ProductsStart start, ProductsParams parameters,
        IProductsInputs inputs, IndustryMailbox mailbox, ProductsMode mode = ProductsMode.Full, int systemId = DefaultSystemId, int startMonth = 9)
    {
        if (table.RowCount != layout.SiteCount || start.Actors != layout.SiteCount || start.Layout != layout)
            throw new ArgumentException("Table, start state and layout must have one row per site.", nameof(table));
        for (int s = 0; s < layout.SiteCount; s++)
        {
            if (layout.SiteOwner[s] != s)
                throw new ArgumentException("The slice product layer needs site row = actor row (spec §3.5 A27).", nameof(layout));
        }
        parameters.Validate();
        ValidatePsi(layout, parameters);
        // E1 (spec A32): one start line per actor.
        for (int a = 0; a < start.Actors; a++)
        {
            int lines = 0;
            for (int d = 0; d < layout.Designs; d++)
                lines += start.Stage[a * layout.Designs + d] == DesignStage.Series ? 1 : 0;
            if (lines > 1)
                throw new ArgumentException($"Actor row {a} has {lines} start lines; the scenario may start one (spec A32, E1).", nameof(start));
        }
        if (mode == ProductsMode.CanonicalLp && parameters.LpTDesign.Length != layout.Generations)
            throw new ArgumentException("L_P needs T_design and ρ for every generation.", nameof(parameters));
        _l = layout;
        _p = parameters;
        _inputs = inputs;
        _mailbox = mailbox;
        _mode = mode;
        _d = layout.Designs;
        _s = layout.SlotCount;
        _y = layout.Years;
        _b = layout.Bureaus;
        SystemId = systemId;
        _dRef = ProductsMath.DRef(parameters.D0Factor, layout.CRef);
        StartMonth = startMonth;
        _m0 = new int[layout.Years];
        for (int y = 0; y < _m0.Length; y++)
            _m0[y] = y * 12 + (7 - startMonth);
        _prcMemo = new PrcMemo?[layout.SiteCount * layout.Designs];
        _moneyTraced = new bool[layout.SiteCount];
        _traceLeftStart = new long[layout.SiteCount];
        _traceLines = new LineMoneyStep?[layout.SiteCount * layout.Designs];
        _bureauReq = new int[layout.DesignScale.Length];
        for (int k = 0; k < _bureauReq.Length; k++)
            _bureauReq[k] = parameters.BureauReq(layout.DesignScale[k]);

        _stage = new IntColumn[_d];
        _gen = new IntColumn[_d];
        _parentGen = new IntColumn[_d];
        _bureauOf = new IntColumn[_d];
        _gp = new IntColumn[_d];
        _parent = new IntColumn[_d];
        _stageO = new IntColumn[_d];
        _gateOpened = new IntColumn[_d];
        _forced = new IntColumn[_d];
        _cancelled = new IntColumn[_d];
        _idleC = new IntColumn[_d];
        _built = new IntColumn[_d];
        _quotaBase = new IntColumn[_d];
        _soldY = new IntColumn[_d];
        _lineBind = new IntColumn[_d];
        _retired = new LongColumn[_d];
        _sold = new LongColumn[_d];
        _replCredit = new LongColumn[_d];
        _alt = new IntColumn[_d * _s];
        _work = new LongColumn[_d];
        _d0 = new LongColumn[_d];
        _hidden = new LongColumn[_d];
        _open = new LongColumn[_d];
        _found = new LongColumn[_d];
        _hours = new LongColumn[_d];
        _lineE = new LongColumn[_d];
        _rate = new LongColumn[_d];
        _rateCmd = new LongColumn[_d];
        _cost = new LongColumn[_d];
        _dGate = new LongColumn[_d];
        _lossAcc = new LongColumn[_d];
        _retro = new LongColumn[_d];
        _lost = new LongColumn[_d];
        _inService0 = new LongColumn[_d];
        _cohort = new LongColumn[_d * _y];
        if (_y > ProductsLayout.MaxCohortYears)
            throw new ArgumentException($"q[design, y] is one bit per cohort year: at most {ProductsLayout.MaxCohortYears} years.", nameof(layout));
        _qb = ProductsLayout.MaskBlocks(_y);
        _qMask = new LongColumn[_d * _qb];
        for (int d = 0; d < _d; d++)
        {
            string n = $"prd.d{d}";
            _stage[d] = table.AddInt($"{n}.stage");
            _gen[d] = table.AddInt($"{n}.generation");
            _parentGen[d] = table.AddInt($"{n}.parentGen");
            _bureauOf[d] = table.AddInt($"{n}.bureau");
            _gp[d] = table.AddInt($"{n}.gp");
            _parent[d] = table.AddInt($"{n}.parent");
            _stageO[d] = table.AddInt($"{n}.stageO");
            _gateOpened[d] = table.AddInt($"{n}.gateOpened");
            _forced[d] = table.AddInt($"{n}.forced");
            _cancelled[d] = table.AddInt($"{n}.cancelled");
            _idleC[d] = table.AddInt($"{n}.idleC");
            _built[d] = table.AddInt($"{n}.builtAny");
            _quotaBase[d] = table.AddInt($"{n}.quotaBase");
            _soldY[d] = table.AddInt($"{n}.soldY");
            _lineBind[d] = table.AddInt($"{n}.lineBind");
            _retired[d] = table.AddLong($"{n}.retired");
            _sold[d] = table.AddLong($"{n}.sold");
            _replCredit[d] = table.AddLong($"{n}.replCredit");
            for (int s = 0; s < _s; s++)
                _alt[d * _s + s] = table.AddInt($"{n}.alt.{s}");
            _work[d] = table.AddLong($"{n}.work");
            _d0[d] = table.AddLong($"{n}.d0");
            _hidden[d] = table.AddLong($"{n}.hidden");
            _open[d] = table.AddLong($"{n}.open");
            _found[d] = table.AddLong($"{n}.found");
            _hours[d] = table.AddLong($"{n}.hours");
            _lineE[d] = table.AddLong($"{n}.lineE");
            _rate[d] = table.AddLong($"{n}.rate");
            _rateCmd[d] = table.AddLong($"{n}.rateCmd");
            _cost[d] = table.AddLong($"{n}.unitCost");
            _dGate[d] = table.AddLong($"{n}.dGate");
            _lossAcc[d] = table.AddLong($"{n}.lossAcc");
            _retro[d] = table.AddLong($"{n}.retroPool");
            _lost[d] = table.AddLong($"{n}.lost");
            _inService0[d] = table.AddLong($"{n}.inService0");
            // Block 0 keeps the name of the single-word mask; later blocks are new columns (.q.1: years 62…123).
            for (int b = 0; b < _qb; b++)
                _qMask[d * _qb + b] = table.AddLong(b == 0 ? $"{n}.q" : $"{n}.q.{b}");
            for (int y = 0; y < _y; y++)
                _cohort[d * _y + y] = table.AddLong($"{n}.cohort.{layout.StartYear + y}");
        }
        _exp = new LongColumn[_b];
        _bureauDesign = new IntColumn[_b];
        for (int b = 0; b < _b; b++)
        {
            _exp[b] = table.AddLong($"prd.b{b}.exp");
            _bureauDesign[b] = table.AddInt($"prd.b{b}.design");
        }
        // Novelty memory (DEBT-0054, spec C10): per actor and alternative, 1 once a project with it reached О or the start fleet
        // had it. Monotone and independent of the design slots, so it survives slot reuse.
        _altReached = new IntColumn[layout.AltStart[layout.SlotCount]];
        for (int alt = 0; alt < _altReached.Length; alt++)
            _altReached[alt] = table.AddInt($"prd.altReached.{alt}");
        _lpSlot = new IntColumn[layout.Generations];
        _lpGen = new IntColumn[layout.Generations];
        for (int g = 0; g < layout.Generations; g++)
        {
            _lpSlot[g] = table.AddInt($"prd.lp.g{g + 1}");
            _lpGen[g] = table.AddInt($"prd.lp.g{g + 1}.gen");
        }
        _rejected = new LongColumn[RejectionKinds];
        for (int r = 0; r < RejectionKinds; r++)
            _rejected[r] = table.AddLong($"prd.rejected.{(CommandRejection)r}");
        _progRows = MasteryParams.ProgrammeProcesses(layout);
        _progOf = new int[layout.ProcNumber.Length];
        Array.Fill(_progOf, -1);
        _progNumbers = [.. _progRows.Select(k => layout.ProcNumber[k])];
        _prog = new IntColumn[_progRows.Length];
        for (int j = 0; j < _progRows.Length; j++)
        {
            _progOf[_progRows[j]] = j;
            _prog[j] = table.AddInt($"prd.prog.p{layout.ProcNumber[_progRows[j]]}");
        }
        _need = table.AddLong("prd.upkeepNeed");
        _paid = table.AddLong("prd.paidRatio");
        _spent = table.AddLong("prd.spent");
        _tbSpent = table.AddLong("prd.tbSpent");
        _saleIncome = table.AddLong("prd.saleIncome");
        _saleCap = table.AddLong("prd.saleCap");
        _saleY = table.AddLong("prd.saleY");
        _extTotal = table.AddLong("prd.extTotal");

        int rows = layout.SiteCount;
        var ints = new int[rows];
        var longs = new long[rows];
        for (int d = 0; d < _d; d++)
        {
            for (int a = 0; a < rows; a++)
                ints[a] = (int)start.Stage[a * _d + d];
            _stage[d].Load(ints);
            for (int a = 0; a < rows; a++)
                ints[a] = start.PlatformGen[a * _d + d];
            _gp[d].Load(ints);
            // generation = number of times the slot was taken: a design of the scenario start counts as the first taking.
            for (int a = 0; a < rows; a++)
                ints[a] = start.Stage[a * _d + d] == DesignStage.None ? 0 : 1;
            _gen[d].Load(ints);
            for (int a = 0; a < rows; a++)
                ints[a] = start.Stage[a * _d + d] == DesignStage.None ? 0 : 1;
            _gateOpened[d].Load(ints);
            // built_any: the start fleets (the line of stage С, the closed ones of stage В) were built; a line never started has not (contract §3.1, r4).
            for (int a = 0; a < rows; a++)
                ints[a] = start.Stage[a * _d + d] is DesignStage.InService or DesignStage.Series ? 1 : 0;
            _built[d].Load(ints);
            Array.Fill(ints, -1);
            _bureauOf[d].Load(ints);
            _parent[d].Load(ints);
            _stageO[d].Load(ints);
            for (int s = 0; s < _s; s++)
            {
                for (int a = 0; a < rows; a++)
                    ints[a] = start.Alts[(a * _d + d) * _s + s];
                _alt[d * _s + s].Load(ints);
            }
            for (int y = 0; y < _y; y++)
            {
                for (int a = 0; a < rows; a++)
                    longs[a] = start.Cohort[(a * _d + d) * _y + y].Raw;
                _cohort[d * _y + y].Load(longs);
            }
            for (int a = 0; a < rows; a++)
                longs[a] = start.LineE[a * _d + d].Raw;
            _lineE[d].Load(longs);
        }
        for (int b = 0; b < _b; b++)
        {
            for (int a = 0; a < rows; a++)
                longs[a] = start.BureauExp[a * _b + b].Raw;
            _exp[b].Load(longs);
            Array.Fill(ints, -1);
            _bureauDesign[b].Load(ints);
        }
        Array.Fill(ints, -1);
        for (int g = 0; g < layout.Generations; g++)
            _lpSlot[g].Load(ints);
        // The start fleet counts as tried: every alternative of a scenario design past П.
        for (int alt = 0; alt < _altReached.Length; alt++)
        {
            Array.Clear(ints);
            for (int a = 0; a < rows; a++)
            {
                for (int d = 0; d < _d && ints[a] == 0; d++)
                {
                    if (start.Stage[a * _d + d] is DesignStage.None or DesignStage.Withdrawn or DesignStage.Project)
                        continue;   // a start project in П is covered by InLiveProject
                    for (int s = 0; s < _s; s++)
                    {
                        if (start.Alts[(a * _d + d) * _s + s] == alt)
                            ints[a] = 1;
                    }
                }
            }
            _altReached[alt].Load(ints);
        }
        Array.Fill(longs, Fx6.Scale);
        _paid.Load(longs);
        LoadInitialQ(rows);
    }

    // t0 (contract r13a §9.1): the scenario cohorts were built while all processes of the prc_set were mastered at t0.
    private void LoadInitialQ(int rows)
    {
        var q = new long[rows];
        for (int d = 0; d < _d; d++)
        {
            for (int b = 0; b < _qb; b++)
            {
                for (int a = 0; a < rows; a++)
                {
                    q[a] = 0;
                    if (Stage(a, d) == DesignStage.None || !PrcSet(a, d).All(p => _inputs.Mastered(a, p, 0)))
                        continue;
                    int yEnd = Math.Min(_y, (b + 1) * ProductsLayout.MaskBits);
                    for (int y = b * ProductsLayout.MaskBits; y < yEnd; y++)
                    {
                        if (_cohort[d * _y + y].Current[a] > 0)
                            q[a] |= 1L << y % ProductsLayout.MaskBits;
                    }
                }
                _qMask[d * _qb + b].Load(q);
            }
        }
        RebuildPrcMemo();
    }

    /// <summary>
    /// <c>q(d, y)</c> of the cohort of year index <paramref name="yearIndex"/>: it was built while every process of the <c>prc_set</c> was mastered in the
    /// last month of its output (contract r13a §9.1). State; written by <see cref="Compute"/> from the snapshot.
    /// </summary>
    public bool CohortMastered(int actor, int design, int yearIndex) => (_qMask[design * _qb + yearIndex / ProductsLayout.MaskBits].Current[actor] >> yearIndex % ProductsLayout.MaskBits & 1) != 0;

    // Month t writes q(d, year(t−1)) for every line with rate(t−1) > 0 from the snapshot of t−1 (mastered_run is visible from t+1, §4.0).
    private void WriteQ(int a, long t, TickCalendar calendar, long tick)
    {
        if (t == 0)
            return;
        int yi = calendar.DateOf(tick - 1).Year - _l.StartYear;
        if (yi < 0 || yi >= _y)
            return;
        for (int d = 0; d < _d; d++)
        {
            if (_rate[d].Current[a] <= 0)
                continue;
            bool all = true;
            foreach (int p in PrcSet(a, d))
                all &= _inputs.Mastered(a, p, t - 1);
            LongColumn word = _qMask[d * _qb + yi / ProductsLayout.MaskBits];
            long bit = 1L << yi % ProductsLayout.MaskBits;
            word.Next[a] = all ? word.Next[a] | bit : word.Next[a] & ~bit;
        }
    }

    public int SystemId { get; }

    public Cadence Cadence => Cadence.Monthly;

    public ProductsLayout Layout => _l;

    /// <summary>Calendar month (1…12) of the scenario start; cohorts age from July of their year (spec A32).</summary>
    public int StartMonth { get; }

    /// <summary>Parameters of the layer.</summary>
    public ProductsParams Parameters => _p;

    /// <summary>Month index of the midyear of cohort year index <paramref name="yearIndex"/> (age = t − this, spec A32).</summary>
    public int CohortMidyear(int yearIndex) => _m0[yearIndex];

    /// <summary><c>D_ref = d0·c_ref</c> (contract §4.4b).</summary>
    public Fx6 DRef => _dRef;

    /// <summary><c>bureau_req[af_class]</c>: engineers a bureau requests per month for a design of the airframe class (§3.3, A34).</summary>
    public int BureauReq(int afClass) => _bureauReq[afClass - 1];

    /// <summary>
    /// Manifest validator (contract §4.4b): <c>ψ_D·c_max/c_ref &lt; 1</c>, so that <c>readiness ≥ 0</c> without a clamp. <c>c_max</c> is the
    /// largest <c>complexity</c> over every combination of slot alternatives.
    /// </summary>
    public static void ValidatePsi(ProductsLayout layout, ProductsParams parameters)
    {
        int cMax = MaxComplexity(layout);
        if (parameters.PsiD * Fx6.FromInt(cMax) / Fx6.FromInt(layout.CRef) >= Fx6.One)
            throw new ArgumentException($"ψ_D·c_max/c_ref must be below 1 (c_max {cMax}, c_ref {layout.CRef}, ψ_D {parameters.PsiD}).", nameof(parameters));
    }

    /// <summary>Largest <c>complexity</c> (Σ tier over the union <c>prc_set</c>) over all assemblies of the class.</summary>
    public static int MaxComplexity(ProductsLayout layout)
    {
        var alts = new int[layout.SlotCount];
        int best = 0;
        void Walk(int slot)
        {
            if (slot == layout.SlotCount)
            {
                best = Math.Max(best, DesignRules.Complexity(layout, DesignRules.PrcSet(layout, alts)));
                return;
            }
            for (int a = layout.AltStart[slot]; a < layout.AltStart[slot + 1]; a++)
            {
                alts[slot] = a;
                Walk(slot + 1);
            }
        }
        Walk(0);
        return best;
    }

    // ---- reads of the current state (snapshot for other layers and metrics)

    public DesignStage Stage(int actor, int design) => (DesignStage)_stage[design].Current[actor];

    /// <summary>
    /// Whether the design is a line (stage С) in the compute being run, after the closes and starts of step 2. Valid only inside the compute of
    /// the actor's own row (the adapter of <c>IProductsInputs.LineRate</c> calls it from step 7); elsewhere use <see cref="Stage"/>.
    /// </summary>
    public bool IsLineNow(int actor, int design) => (DesignStage)_stage[design].Next[actor] == DesignStage.Series;

    /// <summary>The alternative in one slot of a design (<see cref="Alternatives"/> without the array).</summary>
    public int AltAt(int actor, int design, int slot) => _alt[design * _s + slot].Current[actor];

    /// <summary>Alternatives of a design (layout-global indices), −1 for an empty slot.</summary>
    public int[] Alternatives(int actor, int design)
    {
        var alts = new int[_s];
        AlternativesInto(actor, design, alts);
        return alts;
    }

    private void AlternativesInto(int actor, int design, Span<int> alts)
    {
        for (int s = 0; s < _s; s++)
            alts[s] = _alt[design * _s + s].Current[actor];
    }

    /// <summary><c>prc_set</c> of a design (ascending process numbers); empty without a design.</summary>
    public int[] PrcSet(int actor, int design)
    {
        if (Stage(actor, design) == DesignStage.None)
            return [];
        // Memo of DesignRules.PrcSet, built only in the constructor and in Post (single-threaded), never on read, so parallel readers do not race.
        // Keyed by the alternatives in Current: a stale entry is never returned, it is recomputed without being stored. Not state: not hashed or saved.
        // Callers must not mutate the returned array.
        PrcMemo? memo = SameMemo(actor, design, next: false);
        return memo is not null ? memo.Prc : DesignRules.PrcSet(_l, Alternatives(actor, design));
    }

    /// <summary>The <c>prc_set</c> with <c>n_p</c> and <c>base_cost_p</c> of a design for <c>production_profile</c> (<see cref="DesignRules.Items"/> of <see cref="PrcSet"/>); do not mutate.</summary>
    public PrcSetItem[] PrcItems(int actor, int design) =>
        SameMemo(actor, design, next: false)?.Items ?? DesignRules.Items(_l, PrcSet(actor, design));

    // The memo of (actor, design) if it was built for the alternatives now in Current (or in Next); no allocation.
    private PrcMemo? SameMemo(int actor, int design, bool next)
    {
        PrcMemo? memo = _prcMemo[actor * _d + design];
        if (memo is null)
            return null;
        for (int s = 0; s < _s; s++)
        {
            int alt = next ? _alt[design * _s + s].Next[actor] : _alt[design * _s + s].Current[actor];
            if (memo.Alts[s] != alt)
                return null;
        }
        return memo;
    }

    // Everything that is a pure function of the alternatives, made once when they change (Post, single-threaded).
    private PrcMemo MakeMemo(int[] alts)
    {
        int[] prc = DesignRules.PrcSet(_l, alts);
        int[] pu = DesignRules.Block(_l, prc, airframe: false), af = DesignRules.Block(_l, prc, airframe: true);
        Fx6 floor = FloorOf(alts);
        return new PrcMemo(alts, prc, DesignRules.Items(_l, prc), DesignRules.Items(_l, pu), DesignRules.Items(_l, af),
            DesignRules.BaseCostSum(_l, pu), DesignRules.BaseCostSum(_l, af), floor, UpkeepOf(alts, floor));
    }

    // Refreshes the memo of every (actor, design) from Current; keeps the entry when the alternatives did not change.
    private void RebuildPrcMemo()
    {
        for (int a = 0; a < _l.SiteCount; a++)
        {
            for (int d = 0; d < _d; d++)
            {
                ref PrcMemo? slot = ref _prcMemo[a * _d + d];
                bool filled = Stage(a, d) != DesignStage.None;
                for (int s = 0; s < _s && filled; s++)
                {
                    int x = _alt[d * _s + s].Current[a];
                    filled = x >= 0 && x < _l.AltPrcStart.Length - 1;
                }
                if (!filled)
                {
                    slot = null; // no design or a slot not filled: PrcSet answers without the memo, as before
                    continue;
                }
                if (SameMemo(a, d, next: false) is not null)
                    continue;
                slot = MakeMemo(Alternatives(a, d));
            }
        }
    }

    private sealed record PrcMemo(int[] Alts, int[] Prc, PrcSetItem[] Items, PrcSetItem[] PuItems, PrcSetItem[] AfItems, Fx6 SumPu, Fx6 SumAf, Fx6 Floor, Fx6 Upkeep);

    public Fx6 Perf(int actor, int design) => DesignRules.Perf(_l, Alternatives(actor, design));

    public int Complexity(int actor, int design) => DesignRules.Complexity(_l, PrcSet(actor, design));

    /// <summary><c>g_p</c> of a design, fixed at creation (contract §4.4a).</summary>
    public int PlatformGen(int actor, int design) => _gp[design].Current[actor];

    /// <summary><c>gen(d) = min(g_p, eng_gen(engine) + 1)</c> (contract §4.4a, #70).</summary>
    public int Gen(int actor, int design)
    {
        Span<int> alts = stackalloc int[_s];
        AlternativesInto(actor, design, alts);
        return DesignRules.Gen(_l, PlatformGen(actor, design), alts);
    }

    /// <summary><c>parent_design</c>, −1 for a design that is not derived.</summary>
    public int Parent(int actor, int design) => _parent[design].Current[actor];

    /// <summary>
    /// The parent slot while it still holds the project this design was derived from (same generation), else −1: a slot reused by another
    /// project is not a parent (ADR-0005 p. 3), as in the line transfer of step 2f.
    /// </summary>
    public int LiveParent(int actor, int design)
    {
        int parent = _parent[design].Current[actor];
        return parent >= 0 && _parentGen[design].Current[actor] == _gen[parent].Current[actor] ? parent : -1;
    }

    public Fx6 WorkLeft(int actor, int design) => Fx6.FromRaw(_work[design].Current[actor]);
    public Fx6 InitialDefects(int actor, int design) => Fx6.FromRaw(_d0[design].Current[actor]);
    public Fx6 DefectsHidden(int actor, int design) => Fx6.FromRaw(_hidden[design].Current[actor]);
    public Fx6 DefectsOpen(int actor, int design) => Fx6.FromRaw(_open[design].Current[actor]);
    public Fx6 DefectsFound(int actor, int design) => Fx6.FromRaw(_found[design].Current[actor]);
    public Fx6 Hours(int actor, int design) => Fx6.FromRaw(_hours[design].Current[actor]);
    public Fx6 LineE(int actor, int design) => Fx6.FromRaw(_lineE[design].Current[actor]);

    /// <summary>Series gate level <c>d_gate</c>: preliminary after entering О, final once the gate opened (contract §4.4c).</summary>
    public Fx6 DGate(int actor, int design) => Fx6.FromRaw(_dGate[design].Current[actor]);

    /// <summary>The gate has opened once (<c>d_gate</c> is final).</summary>
    public bool GateOpened(int actor, int design) => _gateOpened[design].Current[actor] != 0;

    /// <summary>Month the design entered stage О, −1 if it has not.</summary>
    public int StageOMonth(int actor, int design) => _stageO[design].Current[actor];

    /// <summary>Literal «С · досрочно»: the series was started with the gate closed (journal flag, not in formulas).</summary>
    public bool SeriesForced(int actor, int design) => _forced[design].Current[actor] != 0;

    /// <summary>
    /// «Снят без выпуска» (contract §3.1, r3/r4): cancelled by <c>cancel_design</c> from П/О or closed in С before the line ever produced.
    /// Withdrawn after service (every cohort written off) is not cancelled.
    /// </summary>
    public bool Cancelled(int actor, int design) => _cancelled[design].Current[actor] != 0;

    /// <summary>Consecutive months in stage С with the line's output <c>rate = 0</c>, the launch month included (contract §3.1, r4).</summary>
    public int IdleMonths(int actor, int design) => _idleC[design].Current[actor];

    /// <summary>The line of the design has produced <c>rate &gt; 0</c> in at least one month of stage С (contract §3.1, r4).</summary>
    public bool BuiltAny(int actor, int design) => _built[design].Current[actor] != 0;

    public Fx6 LossAcc(int actor, int design) => Fx6.FromRaw(_lossAcc[design].Current[actor]);
    public Fx6 RetroPool(int actor, int design) => Fx6.FromRaw(_retro[design].Current[actor]);

    /// <summary>Machines written off by accidents in the last month step.</summary>
    public Fx6 AccidentsLost(int actor, int design) => Fx6.FromRaw(_lost[design].Current[actor]);

    /// <summary>Units in service the last month step saw before it wrote accidents off.</summary>
    public Fx6 InServiceBeforeAccidents(int actor, int design) => Fx6.FromRaw(_inService0[design].Current[actor]);

    /// <summary><c>X = max(0, D_hidden − d_gate)</c> of the snapshot (contract §4.4c).</summary>
    public Fx6 ExcessDefects(int actor, int design) => ProductsMath.Excess(DefectsHidden(actor, design), DGate(actor, design));

    /// <summary>
    /// <c>gate_open</c> of the snapshot in month <paramref name="month"/> (contract §4.4b): the pure function behind the
    /// <c>SERIES_GATE</c> link of <c>why_not</c>. A design that has no stage О month (start fleets) counts as open.
    /// </summary>
    public bool GateOpen(int actor, int design, long month)
    {
        if (GateOpened(actor, design))
            return true;
        int o = StageOMonth(actor, design);
        return o >= 0 && ProductsMath.GateOpen(DefectsHidden(actor, design), _p.KGate, _dRef, month - o, _p.TTestMin);
    }

    /// <summary>Units produced in the last month step.</summary>
    public Fx6 Rate(int actor, int design) => Fx6.FromRaw(_rate[design].Current[actor]);

    /// <summary><c>unit_cost</c> of the last month step (0 without series).</summary>
    public Fx6 UnitCost(int actor, int design) => Fx6.FromRaw(_cost[design].Current[actor]);

    public Fx6 BureauExp(int actor, int bureau) => Fx6.FromRaw(_exp[bureau].Current[actor]);
    public int BureauDesign(int actor, int bureau) => _bureauDesign[bureau].Current[actor];
    public int DesignBureau(int actor, int design) => _bureauOf[design].Current[actor];

    public Fx6 Cohort(int actor, int design, int year) => Fx6.FromRaw(_cohort[design * _y + year - _l.StartYear].Current[actor]);

    /// <summary>Units of the design in service (all cohorts).</summary>
    public Fx6 InService(int actor, int design)
    {
        Fx6 sum = Fx6.Zero;
        for (int y = 0; y < _y; y++)
            sum += Fx6.FromRaw(_cohort[design * _y + y].Current[actor]);
        return sum;
    }

    public Fx6 UpkeepNeeded(int actor) => Fx6.FromRaw(_need.Current[actor]);
    public Fx6 PaidRatio(int actor) => Fx6.FromRaw(_paid.Current[actor]);
    public Fx6 Spent(int actor) => Fx6.FromRaw(_spent.Current[actor]);

    /// <summary><c>Σ_d tb_spent</c> of the last month step: money of the training series spent in step 7 (not part of <see cref="Spent"/>; contract §4.4d, F15).</summary>
    public Fx6 TrainingSpent(int actor) => Fx6.FromRaw(_tbSpent.Current[actor]);

    /// <summary>
    /// Observation: the common money <c>left</c> at the start of step 7 of the last monthly step (after upkeep and fixes); null for a row
    /// whose step has no step 7 (L_P) or has not run.
    /// </summary>
    [Observation]
    public Fx6? LastLeftStart(int actor) => _moneyTraced[actor] ? Fx6.FromRaw(_traceLeftStart[actor]) : null;

    /// <summary>
    /// Observation: step 7 of the last monthly step by line, ascending design id, for the lines in stage С that step 7 reached. The training
    /// money is the line's input, also for a line that is not admitted (then nothing is taken from it).
    /// </summary>
    [Observation]
    public IReadOnlyList<LineMoneyStep> LastLineMoney(int actor)
    {
        var lines = new List<LineMoneyStep>();
        for (int d = 0; d < _d; d++)
        {
            if (_traceLines[actor * _d + d] is { } line)
                lines.Add(line);
        }
        return lines;
    }

    /// <summary>Commands of the actor rejected for <paramref name="reason"/> so far (the journal of rejections, contract §3.1).</summary>
    public long RejectionCount(int actor, CommandRejection reason) => _rejected[(int)reason].Current[actor];

    /// <summary>
    /// The canonical L_P project slot of generation <paramref name="gen"/>, −1 before it exists and −1 once the slot was freed and
    /// retaken (its handle is stale: the canonical design no longer exists).
    /// </summary>
    public int CanonicalSlot(int actor, int gen) =>
        _lpSlot[gen - 1].Current[actor] is var slot and >= 0 && _lpGen[gen - 1].Current[actor] == _gen[slot].Current[actor] ? slot : -1;

    /// <summary>Generation of a design slot (ADR-0005 p. 3): +1 each time the slot is taken; a handle is the pair (slot, generation).</summary>
    public int Generation(int actor, int slot) => _gen[slot].Current[actor];

    /// <summary>Handle resolution on the snapshot <c>Current</c> (ADR-0005 p. 3): the slot is occupied and its generation equals the handle's.</summary>
    public bool Resolves(int actor, DesignHandle handle) =>
        handle.Slot >= 0 && handle.Slot < _d && HandleHolds(_stage[handle.Slot].Current[actor], _gen[handle.Slot].Current[actor], handle.Generation);

    /// <summary>The current handle of an occupied slot of the snapshot, or null when the slot is free.</summary>
    public DesignHandle? HandleOf(int actor, int slot) =>
        slot >= 0 && slot < _d && Resolves(actor, new DesignHandle(slot, _gen[slot].Current[actor])) ? new DesignHandle(slot, _gen[slot].Current[actor]) : null;

    // The single predicate over (stage, generation); Current and Next wrappers differ only in the buffer they read.
    private static bool HandleHolds(int stage, int slotGeneration, int handleGeneration) =>
        slotGeneration == handleGeneration && (DesignStage)stage is not (DesignStage.None or DesignStage.Withdrawn);

    /// <summary><c>unit_cost_floor</c> of a design (STATE_WORDS §3; R-PS2m at <c>U</c> = 1).</summary>
    public Fx6 UnitCostFloor(int actor, int design) =>
        SameMemo(actor, design, next: false) is { } memo ? memo.Floor : FloorOf(Alternatives(actor, design));

    /// <summary>Upkeep per unit and month of a design (contract §4.4).</summary>
    public Fx6 Upkeep(int actor, int design) =>
        SameMemo(actor, design, next: false) is { } memo ? memo.Upkeep : UpkeepOf(Alternatives(actor, design));

    /// <summary>
    /// <c>unit_cost</c> of a design at its current <c>line_E</c> from the <c>production_profile</c> of month <paramref name="month"/>
    /// (contract §4.4): a pure function of the snapshot, also for a design without series (the column <see cref="UnitCost"/> is 0 there).
    /// </summary>
    public Fx6 UnitCostAt(int actor, int design, long month)
    {
        int[] alts = Alternatives(actor, design);
        return ProductsMath.UnitCost(CardFor(actor, alts, DesignRules.PrcSet(_l, alts), month), LineE(actor, design));
    }

    /// <summary>
    /// <c>readiness</c> of a design's cohorts (contract §4.4b) from the current state and the <c>q</c> of
    /// <c>production_profile</c> of month <paramref name="month"/>; 1 under L_P (§6.1).
    /// </summary>
    /// <exception cref="InvalidOperationException">In the full model, for an empty design slot (stage None): it has no <c>prc_set</c>.</exception>
    public Fx6 Readiness(int actor, int design, long month)
    {
        if (_mode == ProductsMode.CanonicalLp)
            return Fx6.One;
        if (Stage(actor, design) == DesignStage.None)
            throw new InvalidOperationException($"Design slot {design} of actor {actor} is empty: it has no readiness.");
        ProductionProfile profile = _inputs.Profile(actor, PrcItems(actor, design), month);
        return ProductsMath.Readiness(PaidRatio(actor), profile.Q, DefectsHidden(actor, design) + DefectsOpen(actor, design), _dRef, _p.PsiD);
    }

    /// <summary>
    /// <c>n_v</c> and <c>FV = (n_v·readiness)·PowFrac(perf_rel, q10_γ)</c> at equal budget (contract §9.1, spec §5.5) for alternatives
    /// <paramref name="alts"/> at line experience <paramref name="e"/>, with <c>production_profile</c> frozen at
    /// <paramref name="month"/>: <c>n_v = min(n_budget, ⌊(supply/alloy_kg)·H_build⌋)</c>, step 10 down. A pure function of the
    /// snapshot; not part of the tick.
    /// </summary>
    public (int Count, Fx6 Value) FleetValue(int actor, ReadOnlySpan<int> alts, Fx6 e, Fx6 readiness, Fx6 perfRel, Fx6 budget, long month)
    {
        int[] prc = DesignRules.PrcSet(_l, alts);
        CostCard card = CardFor(actor, alts, prc, month);
        int nBudget = ProductsMath.FleetCount(card, e, UpkeepOf(alts), _p.FvHorizon, budget, _p.FvStep, _p.FvMaxUnits);
        // alloy_free: no alloy cap, n_v = n_budget (spec v0.14 §1.4 item 4).
        int n = DesignRules.AlloyFree(_l, alts) ? ProductsMath.AlloyFreeCount(nBudget, _p.FvStep)
            : ProductsMath.AlloyLimitedCount(nBudget, _inputs.AlloySupply(actor, month), _l.AltAlloyKg[alts[_l.AlloySlot]], _p.HBuild, _p.FvStep);
        return (n, ProductsMath.FleetValue(n, readiness, perfRel, _p.GammaQ10));
    }

    /// <summary>
    /// <c>FV</c> of a hypothetical project with alternatives <paramref name="alts"/> (spec §5.5, COMPONENT_SELECTION §6): the value of the
    /// fleet of one type the actor could buy at <paramref name="budget"/>, for the preset <c>adaptive</c>. A new line (<c>E</c> = 0), the
    /// <c>production_profile</c> of month <paramref name="month"/>, <c>readiness</c> of a project that would start with its
    /// <c>D0</c> (bureau of the largest experience, <c>novelty</c> against the actor's designs, as <c>compose_design</c> would set it) and
    /// everything paid, <c>perf_rel = perf</c> (the common reference cancels in the comparison of two compositions of one actor).
    /// A pure function of the snapshot; not part of the tick.
    /// </summary>
    public (int Count, Fx6 Value) CandidateValue(int actor, ReadOnlySpan<int> alts, Fx6 budget, long month)
    {
        int[] prc = DesignRules.PrcSet(_l, alts);
        Fx6 q = _inputs.Profile(actor, DesignRules.Items(_l, prc), month).Q;
        Fx6 exp = Fx6.Zero;
        for (int b = 0; b < _b; b++)
        {
            if (BureauExp(actor, b) > exp)
                exp = BureauExp(actor, b);
        }
        int complexity = DesignRules.Complexity(_l, prc);
        Fx6 d0 = ProductsMath.InitialDefects(_p.D0Factor, FreshShare(alts, actor, next: false), complexity, exp, _p.EB);
        Fx6 readiness = ProductsMath.Readiness(Fx6.One, q, d0, _dRef, _p.PsiD);
        return FleetValue(actor, alts, Fx6.Zero, readiness, DesignRules.Perf(_l, alts), budget, month);
    }

    /// <summary>Bureaus without a design in the snapshot (the AI asks before it orders a project, spec A41).</summary>
    public int FreeBureaus(int actor)
    {
        int n = 0;
        for (int b = 0; b < _b; b++)
        {
            if (_bureauDesign[b].Current[actor] < 0)
                n++;
        }
        return n;
    }

    /// <summary>
    /// <c>FV_a = max_v FV_v</c> over the actor's designs that exist and are admitted on the site (П-11: the value of a
    /// hypothetical fleet of one type); <c>perf_rel = perf/<paramref name="perfRef"/></c>, <c>E</c> = the design's <c>line_E</c>.
    /// Zero without such a design.
    /// </summary>
    public Fx6 MaxFleetValue(int actor, Fx6 perfRef, Fx6 budget, long month)
    {
        Fx6 best = Fx6.Zero;
        for (int d = 0; d < _d; d++)
        {
            if (Stage(actor, d) is DesignStage.None or DesignStage.Withdrawn)
                continue;
            int[] alts = Alternatives(actor, d);
            if (!_inputs.Profile(actor, DesignRules.Items(_l, DesignRules.PrcSet(_l, alts)), month).Admitted)
                continue;
            Fx6 v = FleetValue(actor, alts, LineE(actor, d), Readiness(actor, d, month), Perf(actor, d) / perfRef, budget, month).Value;
            if (v > best)
                best = v;
        }
        return best;
    }

    /// <summary>
    /// <c>planned(site, prc)</c> (contract §5, G4-2; r4, #128): the process is in the <c>prc_set</c> of an own project in stage П, О
    /// <b>or С</b>. A pure function of the snapshot; <paramref name="processNumber"/> is the stable process number.
    /// </summary>
    public bool Planned(int actor, int processNumber)
    {
        if (ProgrammeStamp(actor, processNumber) > 0)
            return true;
        for (int d = 0; d < _d; d++)
        {
            if (Stage(actor, d) is (DesignStage.Project or DesignStage.Prototype or DesignStage.Series)
                && PrcSet(actor, d).AsSpan().IndexOf(processNumber) >= 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// <c>v_exp(site, p) = max(proc_v, V_C) + V_PO</c> on <c>home(a)</c>, <c>proc_v</c> on the other sites (contract §4.1a r4, spec A37).
    /// <c>plan(d, p) = (r_plan·n_p)·labour_p</c>; <c>r_plan = r_ref</c> in О and С and <c>r_ref/divisor</c> in П; <c>V_PO</c> sums the
    /// own projects in П and О, <c>V_C</c> those in С; В and «снят» weigh 0. A pure function of the snapshot and of <c>proc_v</c> of
    /// the site (<paramref name="procV"/>), which the capability layer owns; the adapter only passes it in.
    /// </summary>
    /// <param name="isHome">The site is the actor's first site (<c>home(a)</c>): only there the project part is counted.</param>
    public Fx6 ExpectedVolume(int actor, int processNumber, Fx6 procV, bool isHome)
    {
        int k = _l.ProcIndex(processNumber);
        if (!isHome || k < 0)
            return procV;
        Fx6 vPo = Fx6.Zero, vC = Fx6.Zero;
        for (int d = 0; d < _d; d++)
        {
            DesignStage stage = Stage(actor, d);
            if (stage is not (DesignStage.Project or DesignStage.Prototype or DesignStage.Series) || PrcSet(actor, d).AsSpan().IndexOf(processNumber) < 0)
                continue;
            Fx6 rPlan = stage == DesignStage.Project ? _l.RRef / Fx6.FromInt(_p.PlanProjectDivisor) : _l.RRef;
            Fx6 plan = rPlan * _l.ProcN[k] * _l.ProcLabour[k];
            if (stage == DesignStage.Series)
                vC += plan;
            else
                vPo += plan;
        }
        return Max(procV, vC) + vPo;
    }

    /// <summary>
    /// <c>V_OC(site, p) = V_C + V_O</c> on <c>home(a)</c>, 0 elsewhere (contract §4.1a r12, I-OC): the nominal of the actor's own lines in stages О and С,
    /// <c>plan(d, p) = (r_ref·n_p)·labour_p</c>, each design once (<c>prc_set</c> is a set, one stage per design); П, В and «снят» weigh 0.
    /// Read from the same snapshot, in the same phase, as <see cref="ExpectedVolume"/> and <see cref="Planned"/>.
    /// </summary>
    public Fx6 GrowthNominal(int actor, int processNumber, bool isHome)
    {
        int k = _l.ProcIndex(processNumber);
        if (!isHome || k < 0)
            return Fx6.Zero;
        Fx6 sum = Fx6.Zero;
        for (int d = 0; d < _d; d++)
        {
            if (Stage(actor, d) is (DesignStage.Prototype or DesignStage.Series) && PrcSet(actor, d).AsSpan().IndexOf(processNumber) >= 0)
                sum += _l.RRef * _l.ProcN[k] * _l.ProcLabour[k];
        }
        // The programme input is not added to the nominal of a project: the larger of the two (production-mastery.md §2 p. 3).
        if (ProgrammeStamp(actor, processNumber) > 0)
        {
            Fx6 prog = ProgrammeNominal(processNumber);
            if (prog > sum)
                sum = prog;
        }
        return sum;
    }

    // ---- mastery programme (TASK-0134, production-mastery.md rev 3, O-045)

    /// <summary><c>V_prog = x_prog·r_ref·n_p·labour_p</c>: the nominal volume a programme feeds into the culture of the process.</summary>
    public Fx6 ProgrammeNominal(int processNumber)
    {
        int k = _l.ProcIndex(processNumber);
        return k < 0 ? Fx6.Zero : _p.Mastery.XProg * _l.RRef * _l.ProcN[k] * _l.ProcLabour[k];
    }

    /// <summary>Stamp (start month + 1) of the programme of the actor on the process in the snapshot, 0 without one.</summary>
    public long ProgrammeStamp(int actor, int processNumber)
    {
        int k = _l.ProcIndex(processNumber);
        int j = k < 0 ? -1 : _progOf[k];
        return j < 0 ? 0 : _prog[j].Current[actor] >> 1;
    }

    /// <summary>The programme runs «at home» (own pilot site): no catch-up from a world leader.</summary>
    public bool ProgrammeDomestic(int actor, int processNumber)
    {
        int k = _l.ProcIndex(processNumber);
        int j = k < 0 ? -1 : _progOf[k];
        return j >= 0 && (_prog[j].Current[actor] & 1) != 0 && _prog[j].Current[actor] != 0;
    }

    /// <summary>Programmes of the actor in the snapshot.</summary>
    public int ProgrammeCount(int actor)
    {
        int n = 0;
        for (int j = 0; j < _prog.Length; j++)
            n += _prog[j].Current[actor] != 0 ? 1 : 0;
        return n;
    }

    /// <summary>Process numbers with a programme column (tier ≥ 5), ascending.</summary>
    public ReadOnlySpan<int> ProgrammeProcessNumbers => _progNumbers;

    // ---- tick phases

    public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd)
    {
        TickCalendar calendar = context.Calendar ?? throw new InvalidOperationException("The product system is monthly and needs a runner calendar.");
        long t = calendar.MonthIndex(context.Tick);
        int year = calendar.DateOf(context.Tick).Year;
        if (year < _l.StartYear || year >= _l.StartYear + _y)
            throw new InvalidOperationException($"Month {t} (year {year}) is outside the cohort years of the layout.");
        for (int a = rowStart; a < rowEnd; a++)
        {
            ClearMoneyTrace(a);
            WriteQ(a, t, calendar, context.Tick);
            ExpendReplacement(a, t);
            if (_mode == ProductsMode.CanonicalLp)
                StepCanonical(chunk, a, t, year - _l.StartYear);
            else
                StepFull(chunk, a, t, year - _l.StartYear);
            Withdraw(a);
        }
    }

    public void Merge(in TickContext context, int chunkCount)
    {
        // Nothing to merge: the layer receives no messages; its own messages go to the capability merge.
    }

    public void Post(in TickContext context)
    {
        RebuildPrcMemo();
        long month = context.Calendar!.MonthIndex(context.Tick - 1);
        for (int a = 0; a < _l.SiteCount; a++)
        {
            CheckSaleMoney(month, a);
            for (int d = 0; d < _d; d++)
            {
                long d0 = _d0[d].Current[a], hidden = _hidden[d].Current[a], found = _found[d].Current[a], open = _open[d].Current[a];
                if (hidden < 0 || open < 0 || found < 0 || found > d0 || hidden + found != d0 || open > found)
                    throw new ProductsInvariantException(month, a, $"defects of design {d}: D0 {d0}, hidden {hidden}, found {found}, open {open}");
                if (_lineE[d].Current[a] < 0 || _work[d].Current[a] < 0 || _rate[d].Current[a] < 0)
                    throw new ProductsInvariantException(month, a, $"negative line_E, work or rate of design {d}");
                if (_retro[d].Current[a] < 0 || _lossAcc[d].Current[a] < 0)
                    throw new ProductsInvariantException(month, a, $"negative retro_pool or loss_acc of design {d}");
                if (_lost[d].Current[a] < 0 || _lost[d].Current[a] > _inService0[d].Current[a])
                    throw new ProductsInvariantException(month, a, $"accident write-off of design {d} exceeds the units in service");
                // series_forced = false ⇒ X ≡ 0 in stages С and В (contract §4.4c).
                var stage = (DesignStage)_stage[d].Current[a];
                if (stage is DesignStage.Series or DesignStage.InService && _forced[d].Current[a] == 0 && hidden > _dGate[d].Current[a])
                    throw new ProductsInvariantException(month, a, $"design {d} has excess defects X > 0 without a forced series");
                for (int y = 0; y < _y; y++)
                {
                    if (_cohort[d * _y + y].Current[a] < 0)
                        throw new ProductsInvariantException(month, a, $"negative cohort of design {d}");
                }
                if (_mode == ProductsMode.Full)
                    CheckLineFlags(month, a, d, stage);
                // Contract r6 §4.4f, FL2, FL4, FL5.
                if (_retired[d].Current[a] < 0 || _sold[d].Current[a] < 0)
                    throw new ProductsInvariantException(month, a, $"negative retired or sold of design {d}");
                if (stage == DesignStage.InService && InService(a, d) == Fx6.Zero)
                    throw new ProductsInvariantException(month, a, $"design {d} is in service without a fleet (FL4)");
                if (_soldY[d].Current[a] > FleetRules.QuotaCap(_p.SaleQuota, _quotaBase[d].Current[a]))
                    throw new ProductsInvariantException(month, a, $"design {d} sold {_soldY[d].Current[a]} above its yearly quota (FL5)");
            }
        }
    }

    private void CheckSaleMoney(long month, int a)
    {
        if (!_p.SaleChiOff && _saleY.Current[a] > _saleCap.Current[a])
            throw new ProductsInvariantException(month, a, "sale revenue of the year is above the ceiling (FL5)");
    }

    // Contract §4.4e r4, invariants (8)–(10): stage В ⇒ built_any; cancelled ⇒ «снят», no line, no output, built_any = false; idle_c ≥ 0 and
    // idle_c > 0 only for a design that has been in С.
    private void CheckLineFlags(long month, int a, int d, DesignStage stage)
    {
        bool cancelled = _cancelled[d].Current[a] != 0, built = _built[d].Current[a] != 0;
        if (stage == DesignStage.InService && !built)
            throw new ProductsInvariantException(month, a, $"design {d} is in service without ever producing (built_any = false)");
        if (cancelled && (built || stage != DesignStage.Withdrawn || _lineE[d].Current[a] != 0 || InService(a, d) != Fx6.Zero))
            throw new ProductsInvariantException(month, a, $"cancelled design {d} is not a withdrawn project without output");
        if (_idleC[d].Current[a] < 0 || _idleC[d].Current[a] > 0 && stage is DesignStage.None or DesignStage.Project or DesignStage.Prototype)
            throw new ProductsInvariantException(month, a, $"idle_c of design {d} is out of range for its stage");
    }

    // ---- compute parts (own row a only; reads and writes Next, which starts as a copy of the snapshot)

    // Order of contract §4.4d: 1 gate, 2 bureau and commands, 3 accidents, 4 upkeep, 5 fixes and retrofit, 6 detection, 7 purchases.
    private void StepFull(int chunk, int a, long t, int y)
    {
        Func<int, bool> knows = n => _inputs.Knows(a, n, t);
        OpenGates(a, t);
        AdvanceBureaus(a, t);
        BeginFleetMonth(a, t);
        ApplyDesignCommands(a, t, knows);
        StepProgrammes(a, t);
        BureausRequestOrIdle(chunk, a);
        CloseLines(a, t);
        ApplyFleetCommands(a, t);
        StartSeriesCommands(a, t);
        Fx6 procurement = _inputs.ProcurementBudget(a, t);
        if (procurement < Fx6.Zero)
            throw new ProductsInvariantException(t, a, $"negative procurement budget {procurement}");
        // Step 2g: every external inflow of procurement money is counted once (contract r6 §4.4d).
        Fx6 saleIncome = Fx6.FromRaw(_saleIncome.Next[a]);
        _extTotal.Next[a] = (Fx6.FromRaw(_extTotal.Next[a]) + (procurement - _inputs.ProcurementBase(a, t)) + saleIncome).Raw;
        Accidents(a);

        // Step 4: B_in is the one input of procurement money, the sale revenue of this month included.
        Fx6 budget = procurement + saleIncome;
        Fx6 need = Fx6.Zero;
        for (int d = 0; d < _d; d++)
        {
            Fx6 units = Units(a, d);
            if (units > Fx6.Zero)
                need += units * UpkeepNext(a, d);
        }
        // Upkeep is paid before retrofits and purchases (contract §4.4d).
        Fx6 paid = need < budget ? need : budget;
        Fx6 left = budget - paid, spent = paid;
        FixDefects(a, t, ref left, ref spent);
        DetectDefects(chunk, a);
        Fx6 tbSpent = Fx6.Zero;
        Purchases(chunk, a, t, y, ref left, ref spent, ref tbSpent);
        if (spent > budget)
            throw new ProductsInvariantException(t, a, $"spent {spent} of B_in {budget} (spec §3.4, contract r6 §4.4f)");
        _need.Next[a] = need.Raw;
        _paid.Next[a] = ProductsMath.PaidRatio(paid, need).Raw;
        _spent.Next[a] = spent.Raw;
        _tbSpent.Next[a] = tbSpent.Raw;
    }

    // The mastery programme of the actor (production-mastery.md §2): commands first (the check of the layer is the same pure function the sender used),
    // then the end: «ready for the series» (yield of the process reaches y_start) or T_max = T_look months. The state is read from Next, written to Next.
    private void StepProgrammes(int a, long t)
    {
        if (_prog.Length == 0)
            return;
        MasteryParams m = _p.Mastery;
        if (!m.On(t, MasteryRules.Programme))
        {
            for (int j = 0; j < _prog.Length; j++)
                _prog[j].Next[a] = 0;
            return;
        }
        Span<ProgrammeCommand> cmds = stackalloc ProgrammeCommand[MaxProgrammeCommands];
        int n = _inputs.ProgrammeCommands(a, t, cmds);
        for (int i = 0; i < n; i++)
        {
            int k = _l.ProcIndex(cmds[i].ProcessNumber);
            int j = k < 0 ? -1 : _progOf[k];
            if (j < 0)
            {
                Reject(a, CommandRejection.BadProgramme);
                continue;
            }
            if (!cmds[i].Start)
            {
                _prog[j].Next[a] = 0;
                continue;
            }
            if (_prog[j].Next[a] != 0)
                continue;
            int active = 0;
            for (int q = 0; q < _prog.Length; q++)
                active += _prog[q].Next[a] != 0 ? 1 : 0;
            if (active >= m.SMax || !_inputs.ProgrammeVerdict(a, cmds[i].ProcessNumber, cmds[i].Abroad, t).Accepted)
            {
                Reject(a, CommandRejection.BadProgramme);
                continue;
            }
            _prog[j].Next[a] = (int)((t + 1) << 1) | (cmds[i].Abroad ? 0 : 1);
        }
        for (int j = 0; j < _prog.Length; j++)
        {
            int state = _prog[j].Next[a];
            if (state == 0)
                continue;
            long start = (state >> 1) - 1;
            bool ready = _inputs.CultureYield(a, _progNumbers[j], t) >= _p.YProd;
            if (ready || t - start >= m.TLook)
                _prog[j].Next[a] = 0;
        }
    }

    // Programme commands one actor can have in force in a month: a start or a cancel per programme process, bounded.
    private const int MaxProgrammeCommands = 16;

    // L_P (spec §6.1): canonical project of the newest due generation, output r = min(ρ[g]·procurement budget, alloy capacity).
    private void StepCanonical(int chunk, int a, long t, int y)
    {
        int cur = 0;
        for (int g = 1; g <= _l.Generations; g++)
        {
            long since = _inputs.AppKnownSince(a, g, t);
            // A node known at the start has a negative t_known; the design term counts from month 0 at the earliest (spec §6.1 v0.24 r2).
            if (since != KnownSinceMarks.Unknown && t >= Math.Max(since, 0) + _p.LpTDesign[g - 1])
                cur = g;
        }
        if (cur > 0)
            LaunchCanonical(a, t, cur);
        Fx6 alloyLeft = _inputs.AlloySupply(a, t);
        Fx6 budget = _inputs.ProcurementBudget(a, t);
        for (int d = 0; d < _d; d++)
        {
            Fx6 r = Fx6.Zero;
            if ((DesignStage)_stage[d].Next[a] == DesignStage.Series)
            {
                PrcMemo m = MemoNext(a, d);
                int[] alts = m.Alts;
                CostCard card = CardFor(a, m, t);
                Fx6 alloyKg = _l.AltAlloyKg[alts[_l.AlloySlot]];
                bool free = DesignRules.AlloyFree(_l, alts);
                if (_inputs.Profile(a, m.Items, t).Admitted)
                {
                    // alloy_free: no alloy term, the line does not touch alloy_left (spec v0.14 §1.4 item 1).
                    Fx6 rho = _p.LpRho[_gp[d].Next[a] - 1] * budget;
                    r = free ? rho : Min(rho, alloyLeft / alloyKg);
                    if (!free)
                        alloyLeft -= r * alloyKg;
                }
                // §6.1: price = R-PS2m process part + materials_floor + h_floor hours (print only; no budget is spent).
                Set(_cost, d, a, card.MaterialsFloor + ProductsMath.AssemblyCost(card.HFloor, card.WageKh) + card.ProcessPart);
                if (r > Fx6.Zero)
                    Produce(chunk, a, d, y, r, m.Prc);
            }
            Set(_rate, d, a, r);
        }
        _need.Next[a] = 0;
        _paid.Next[a] = Fx6.Scale;
        _spent.Next[a] = 0;
        _tbSpent.Next[a] = 0;
    }

    // The canonical project of generation g (spec §6.1): the producible preset over admitted alternatives with target g, in the
    // lowest free slot, g_p = g; it is created once per generation and never relaunched.
    private void LaunchCanonical(int a, long t, int gen)
    {
        // Created once per generation: the mark stays even when its slot is later freed and reused.
        int slot = _lpSlot[gen - 1].Next[a];
        if (slot >= 0)
            return;
        Span<int> alts = stackalloc int[_s];
        bool Admitted(int alt)
        {
            foreach (int p in _l.PrcOf(alt))
            {
                if (_inputs.AdmittedYield(a, p, t) <= Fx6.Zero)
                    return false;
            }
            return true;
        }
        if (!DesignRules.Producible(_l, gen, n => _inputs.Knows(a, n, t), p => _inputs.AdmittedYield(a, p, t), _p.YProd, null, alts, Admitted))
            return;
        slot = FreeSlot(a);
        if (slot < 0)
        {
            Reject(a, CommandRejection.DesignCapacity);
            return;
        }
        TakeSlot(a, slot);
        _lpSlot[gen - 1].Next[a] = slot;
        _lpGen[gen - 1].Next[a] = _gen[slot].Next[a];
        for (int d = 0; d < _d; d++)
        {
            if ((DesignStage)_stage[d].Next[a] == DesignStage.Series)
                _stage[d].Next[a] = (int)DesignStage.InService;
        }
        SetAlts(a, slot, alts);
        // L_P goes straight to С (past О): the canonical project counts as tried.
        for (int s = 0; s < _s; s++)
            _altReached[alts[s]].Next[a] = 1;
        _gp[slot].Next[a] = gen;
        _gateOpened[slot].Next[a] = 1;
        _stage[slot].Next[a] = (int)DesignStage.Series;
    }

    // Step 0 (#431, memo §2.3): a cohort no longer leaves at t − m0(Y) ≥ service_life, m0(Y) = month index of July of Y (spec §3.5 A32). Only
    // the replacement credit that new units earned takes the oldest overage units off; the credit never exceeds the units left.
    private void ExpendReplacement(int a, long t)
    {
        Span<Fx6> counts = stackalloc Fx6[_y];
        for (int d = 0; d < _d; d++)
        {
            long raw = _replCredit[d].Next[a];
            if (raw == 0)
                continue;
            for (int y = 0; y < _y; y++)
                counts[y] = Fx6.FromRaw(_cohort[d * _y + y].Next[a]);
            Fx6 credit = Fx6.FromRaw(raw);
            FleetRules.ExpendCredit(counts, ref credit, t, _m0, _p);
            StoreNext(a, d, counts);
            _replCredit[d].Next[a] = credit.Raw;
        }
    }

    // The units that arrive in the month credit the projects they replace (memo §2.3), on the live state of the step.
    private void AccrueReplacement(int a, int e, Fx6 r)
    {
        Span<int> gen = stackalloc int[_d];
        Span<Fx6> perf = stackalloc Fx6[_d];
        Span<long> whole = stackalloc long[_d];
        Span<Fx6> credit = stackalloc Fx6[_d];
        Span<bool> done = stackalloc bool[_d];
        for (int d = 0; d < _d; d++)
        {
            whole[d] = d == e ? 0 : Whole(a, d);
            credit[d] = Fx6.FromRaw(_replCredit[d].Next[a]);
            if (whole[d] <= 0 && d != e)
                continue;
            // The live state of the step: a project launched in this very step has its alternatives in Next only.
            int[] alts = MemoNext(a, d).Alts;
            gen[d] = DesignRules.Gen(_l, _gp[d].Next[a], alts);
            perf[d] = DesignRules.Perf(_l, alts);
        }
        FleetRules.Accrue(r, e, gen, perf, whole, credit, done, _p);
        for (int d = 0; d < _d; d++)
            _replCredit[d].Next[a] = credit[d].Raw;
    }

    // Step 1 (contract §4.4c): the first month the gate is open by the snapshot, d_gate := D_hidden of the snapshot (final).
    private void OpenGates(int a, long t)
    {
        for (int d = 0; d < _d; d++)
        {
            if ((DesignStage)_stage[d].Current[a] is not (DesignStage.Prototype or DesignStage.Series or DesignStage.InService)
                || _gateOpened[d].Next[a] != 0 || _stageO[d].Next[a] < 0)
                continue;
            Fx6 hidden = Get(_hidden, d, a);
            if (ProductsMath.GateOpen(hidden, _p.KGate, _dRef, t - _stageO[d].Next[a], _p.TTestMin))
            {
                Set(_dGate, d, a, hidden);
                _gateOpened[d].Next[a] = 1;
            }
        }
    }

    // Bureau progress in t equals granted from t−1 (contract §4.6, spec §3.3); finishing W gives +1 bureau_exp, frees the bureau,
    // enters stage О and sets the preliminary d_gate and stage_o_month (contract §4.4c step 1).
    private void AdvanceBureaus(int a, long t)
    {
        for (int b = 0; b < _b; b++)
        {
            int d = _bureauDesign[b].Next[a];
            if (d < 0)
                continue;
            Fx6 granted = Fx6.FromInt(_inputs.Granted(a, ProductsLayout.EngineerCategory, _l.BureauRequester(b), t));
            Fx6 work = Get(_work, d, a) - granted;
            if (work > Fx6.Zero)
            {
                Set(_work, d, a, work);
                continue;
            }
            Set(_work, d, a, Fx6.Zero);
            _stage[d].Next[a] = (int)DesignStage.Prototype;
            _bureauDesign[b].Next[a] = -1;
            Set(_exp, b, a, Get(_exp, b, a) + Fx6.One);
            _stageO[d].Next[a] = checked((int)t);
            for (int s = 0; s < _s; s++)
                _altReached[_alt[d * _s + s].Next[a]].Next[a] = 1;
            Set(_dGate, d, a, ProductsMath.PreliminaryGate(_p.KGate, _dRef, Get(_d0, d, a), _p.TTestMin, _p.TestRate, _p.LambdaD, _p.BetaQ10));
        }
    }

    /// <summary>
    /// <c>novelty</c> (spec C10) a project with <paramref name="alts"/> would get now: the share of alternatives that no project of the actor
    /// has taken to О, that are not in the start fleet and not in a project still in П. A pure function of the snapshot.
    /// </summary>
    public Fx6 Novelty(int actor, ReadOnlySpan<int> alts) => FreshShare(alts, actor, next: false);

    /// <summary>The actor has taken alternative <paramref name="alt"/> to О or has it in the start fleet (novelty memory, spec C10).</summary>
    public bool AltReached(int actor, int alt) => _altReached[alt].Current[actor] != 0;

    // A project still in П counts as tried too (behaviour before DEBT-0054): it is live, so its slot cannot be reused and the
    // answer does not depend on slot history; one cancelled in П leaves the stage Project and its alternatives are new again (§4.4e).
    private bool InLiveProject(int alt, int a, bool next)
    {
        for (int e = 0; e < _d; e++)
        {
            if ((DesignStage)(next ? _stage[e].Next[a] : _stage[e].Current[a]) != DesignStage.Project)
                continue;
            for (int s = 0; s < _s; s++)
            {
                if ((next ? _alt[e * _s + s].Next[a] : _alt[e * _s + s].Current[a]) == alt)
                    return true;
            }
        }
        return false;
    }

    // novelty (spec C10): the share of alternatives not yet reached О by the actor and not in the start fleet.
    private Fx6 FreshShare(ReadOnlySpan<int> alts, int a, bool next)
    {
        int fresh = 0;
        foreach (int alt in alts)
        {
            if ((next ? _altReached[alt].Next[a] : _altReached[alt].Current[a]) == 0 && !InLiveProject(alt, a, next))
                fresh++;
        }
        return Fx6.FromInt(fresh) / Fx6.FromInt(_l.SlotCount);
    }

    // compose_design / derive in command order (contract §4.4a); an invalid command is rejected with its reason and changes nothing.
    private void ApplyDesignCommands(int a, long t, Func<int, bool> knows)
    {
        foreach (DesignCommand cmd in _inputs.DesignCommands(a, t))
        {
            CommandRejection? rejected = cmd.Kind switch
            {
                DesignCommandKind.Compose => Compose(a, cmd, knows),
                DesignCommandKind.Derive => Derive(a, cmd, knows),
                _ => !IsCurrent(a, cmd.Design) ? CommandRejection.StaleHandle : Cancel(a, cmd.Design.Slot),
            };
            if (rejected is { } reason)
                Reject(a, reason);
        }
    }

    private CommandRejection? Compose(int a, DesignCommand cmd, Func<int, bool> knows)
    {
        if (!SlotsInRange(cmd.Alts, allowUnchanged: false))
            return CommandRejection.SlotUnavailable;
        int newest = 0;
        for (int g = 1; g <= _l.Generations; g++)
        {
            if (knows(_l.GenApp[g - 1]))
                newest = g;
        }
        if (cmd.Target < 1 || cmd.Target != newest)
            return CommandRejection.BadRequirement;
        foreach (int alt in cmd.Alts)
        {
            if (!DesignRules.Available(_l, alt, knows))
                return CommandRejection.SlotUnavailable;
        }
        // max_eng_gen (spec v0.14 §1.4): checked against the engine of the same composition; the rejection code is SLOT_UNAVAILABLE.
        if (!DesignRules.EngineLimitsHold(_l, cmd.Alts.AsSpan()))
            return CommandRejection.SlotUnavailable;
        if (!DesignRules.MeetsPlatform(_l, cmd.Alts[_l.AirframeSlot], cmd.Target))
            return CommandRejection.PlatformThreshold;
        return Create(a, cmd.Alts.AsSpan(), cmd.Alts.AsSpan(), DesignRules.PlatformGen(_l, cmd.Alts.AsSpan(), knows), -1, 1, 1);
    }

    private CommandRejection? Derive(int a, DesignCommand cmd, Func<int, bool> knows)
    {
        int parent = cmd.Design.Slot;
        if (!IsCurrent(a, cmd.Design))
            return CommandRejection.StaleHandle;
        if (!SlotsInRange(cmd.Alts, allowUnchanged: true))
            return CommandRejection.SlotUnavailable;
        if (parent < 0 || parent >= _d || (DesignStage)_stage[parent].Next[a] is not (DesignStage.Series or DesignStage.InService))
            return CommandRejection.BadDerive;
        Span<int> alts = stackalloc int[_s];
        Span<int> changed = stackalloc int[_s];
        int n = 0;
        for (int s = 0; s < _s; s++)
        {
            int old = _alt[parent * _s + s].Next[a];
            alts[s] = cmd.Alts[s] < 0 ? old : cmd.Alts[s];
            if (alts[s] == old)
                continue;
            if (s == _l.AirframeSlot)
                return CommandRejection.BadDerive;
            if (!DesignRules.Available(_l, alts[s], knows))
                return CommandRejection.SlotUnavailable;
            changed[n++] = alts[s];
        }
        if (n == 0)
            return CommandRejection.BadDerive;
        // The resulting composition (parent + changes) must respect max_eng_gen: an engine step on a steel parent is refused.
        if (!DesignRules.EngineLimitsHold(_l, alts))
            return CommandRejection.SlotUnavailable;
        return Create(a, alts, changed[..n], _gp[parent].Next[a], parent, n, _s);
    }

    // cancel_design (contract §4.4e): an own project in П or О, judged by the live stage of step 2 (a project whose W ended in this step
    // is already in О). The slot is final; in П the bureau is freed without the +1 bureau_exp; nothing is refunded.
    private CommandRejection? Cancel(int a, int design)
    {
        if (design < 0 || design >= _d || (DesignStage)_stage[design].Next[a] is not (DesignStage.Project or DesignStage.Prototype))
            return CommandRejection.BadCancel;
        int bureau = _bureauOf[design].Next[a];
        if (bureau >= 0 && _bureauDesign[bureau].Next[a] == design)
            _bureauDesign[bureau].Next[a] = -1;
        Set(_work, design, a, Fx6.Zero);
        _stage[design].Next[a] = (int)DesignStage.Withdrawn;
        _cancelled[design].Next[a] = 1;
        return null;
    }

    private bool SlotsInRange(System.Collections.Immutable.ImmutableArray<int> alts, bool allowUnchanged)
    {
        if (alts.IsDefault || alts.Length != _s)
            return false;
        for (int s = 0; s < _s; s++)
        {
            if (alts[s] < 0 && allowUnchanged)
                continue;
            if (alts[s] < _l.AltStart[s] || alts[s] >= _l.AltStart[s + 1])
                return false;
        }
        return true;
    }

    // Creates a project in the lowest free slot with the lowest-id free bureau of the largest bureau_exp (tie: lower id): W and
    // D0 are fixed now. A derived design has W_der = (W·n_changed)/5; novelty and D0 count only the changed slots (§4.4a).
    private CommandRejection? Create(int a, ReadOnlySpan<int> alts, ReadOnlySpan<int> noveltyAlts, int platformGen, int parent, int changed, int slots)
    {
        int d = FreeSlot(a);
        if (d < 0)
            return CommandRejection.DesignCapacity;
        int bureau = -1;
        for (int b = 0; b < _b; b++)
        {
            if (_bureauDesign[b].Next[a] < 0 && (bureau < 0 || Get(_exp, b, a) > Get(_exp, bureau, a)))
                bureau = b;
        }
        if (bureau < 0)
            return CommandRejection.NoBureau;

        Fx6 novelty = FreshShare(noveltyAlts, a, next: true);
        int complexity = DesignRules.Complexity(_l, DesignRules.PrcSet(_l, alts));
        int k = DesignRules.AfClass(_l, alts);
        Fx6 exp = Get(_exp, bureau, a);
        Fx6 work = ProductsMath.DesignWork(_p.W0, complexity, _l.DesignScale[k - 1], exp, _p.EB);
        if (parent >= 0)
            work = work * Fx6.FromInt(changed) / Fx6.FromInt(slots);
        Fx6 d0 = ProductsMath.InitialDefects(_p.D0Factor, novelty, complexity, exp, _p.EB);
        TakeSlot(a, d);
        SetAlts(a, d, alts);
        _stage[d].Next[a] = (int)DesignStage.Project;
        _bureauOf[d].Next[a] = bureau;
        _bureauDesign[bureau].Next[a] = d;
        _gp[d].Next[a] = platformGen;
        _parent[d].Next[a] = parent;
        _parentGen[d].Next[a] = parent >= 0 ? _gen[parent].Next[a] : 0;
        _stageO[d].Next[a] = -1;
        _gateOpened[d].Next[a] = 0;
        _forced[d].Next[a] = 0;
        Set(_work, d, a, work);
        Set(_d0, d, a, d0);
        Set(_hidden, d, a, d0);
        Set(_open, d, a, Fx6.Zero);
        Set(_found, d, a, Fx6.Zero);
        Set(_hours, d, a, Fx6.Zero);
        Set(_lineE, d, a, Fx6.Zero);
        Set(_dGate, d, a, Fx6.Zero);
        Set(_lossAcc, d, a, Fx6.Zero);
        Set(_retro, d, a, Fx6.Zero);
        Set(_rateCmd, d, a, Fx6.Zero);
        return null;
    }

    // A bureau with a design in П asks for its engineers; one idle in the snapshot and still idle forgets δ_B of bureau_exp.
    private void BureausRequestOrIdle(int chunk, int a)
    {
        for (int b = 0; b < _b; b++)
        {
            int d = _bureauDesign[b].Next[a];
            if (d >= 0)
            {
                _mailbox.ReserveWorkforce(chunk, ProductsLayout.EngineerRequest(a, _l.BureauRequester(b),
                    _bureauReq[_l.AltAfClass[_alt[d * _s + _l.AirframeSlot].Next[a]] - 1]));
            }
            else if (_bureauDesign[b].Current[a] < 0)
            {
                Fx6 exp = Get(_exp, b, a);
                Set(_exp, b, a, exp - _p.DeltaB * exp);
            }
        }
    }

    // Step 2c (contract §4.4d): close_line of a line in С by the live stage, before the fleet commands and the starts.
    private void CloseLines(int a, long t)
    {
        Span<DesignHandle> candidates = stackalloc DesignHandle[2];
        for (int d = 0; d < _d; d++)
        {
            int n = _inputs.LineCloses(a, d, t, candidates);
            // The first candidate that resolves wins; every stale one is counted, also after the winner (ADR-0005 p. 3).
            bool done = false;
            for (int i = 0; i < n; i++)
            {
                if (candidates[i].Slot != d)
                    throw new ProductsInvariantException(-1, a, $"close_line candidate {candidates[i]} offered for slot {d}");
                if (!IsCurrent(a, candidates[i]))
                {
                    Reject(a, CommandRejection.StaleHandle);
                    continue;
                }
                if (done)
                    continue;
                done = true;
                if ((DesignStage)_stage[d].Next[a] == DesignStage.Series)
                    CloseSeries(a, d);
            }
        }
    }

    // Step 2f (contract §4.4c): start_series from О or В, validated against the snapshot stage; a line closes (line_E → 0, contract §3) in CloseLines.
    private void StartSeriesCommands(int a, long t)
    {
        Span<SeriesStart> candidates = stackalloc SeriesStart[2];
        for (int d = 0; d < _d; d++)
        {
            int n = _inputs.SeriesStarts(a, d, t, candidates);
            bool done = false;
            for (int i = 0; i < n; i++)
            {
                if (candidates[i].Design.Slot != d)
                    throw new ProductsInvariantException(-1, a, $"start_series candidate {candidates[i].Design} offered for slot {d}");
                if (!IsCurrent(a, candidates[i].Design))
                {
                    Reject(a, CommandRejection.StaleHandle);
                    continue;
                }
                if (done)
                    continue;
                done = true;
                var snapshot = (DesignStage)_stage[d].Current[a];
                // A project cancelled earlier in this step (Next stage «снят») cannot start a series.
                if (snapshot is (DesignStage.Prototype or DesignStage.InService) && (DesignStage)_stage[d].Next[a] == snapshot)
                    StartSeries(a, d, snapshot, candidates[i]);
            }
        }
    }

    // Closing a line by any path (close_line of the player or the AI, the A41 bundle, the transfer to a derived design; contract §4.4e r4):
    // С → В if the line ever produced, otherwise «снят» with cancelled = true. line_E is lost either way; the other columns freeze.
    private void CloseSeries(int a, int d)
    {
        Set(_lineE, d, a, Fx6.Zero);
        if (_built[d].Next[a] != 0)
        {
            _stage[d].Next[a] = (int)DesignStage.InService;
            return;
        }
        _stage[d].Next[a] = (int)DesignStage.Withdrawn;
        _cancelled[d].Next[a] = 1;
    }

    // start_series (contract §4.4c): with the gate closed it is refused (SERIES_GATE) unless forced; a forced series is
    // irreversible. The first launch sets retro_pool = X (d_gate of step 1) and, for a derived design, moves the parent's line (П-9).
    private void StartSeries(int a, int d, DesignStage stage, SeriesStart cmd)
    {
        if (cmd.Rate < Fx6.Zero)
            throw new ProductsInvariantException(-1, a, $"negative series rate {cmd.Rate} for design {d}");
        for (int e = 0; e < _d; e++)
        {
            // The parent's line moved to a derived design in series: a second line of the platform is FUTURE (#118).
            if (_parent[e].Next[a] == d && _parentGen[e].Next[a] == _gen[d].Next[a] && (DesignStage)_stage[e].Next[a] == DesignStage.Series)
            {
                Reject(a, CommandRejection.LineTransferred);
                return;
            }
        }
        bool open = _gateOpened[d].Next[a] != 0;
        if (!open && !cmd.Force)
        {
            Reject(a, CommandRejection.SeriesGate);
            return;
        }
        if (!open)
            _forced[d].Next[a] = 1;
        Set(_rateCmd, d, a, cmd.Rate);
        if (stage == DesignStage.Prototype)
        {
            Set(_retro, d, a, ProductsMath.Excess(Get(_hidden, d, a), Get(_dGate, d, a)));
            int parent = _parent[d].Next[a];
            if (parent >= 0 && _parentGen[d].Next[a] == _gen[parent].Next[a] && (DesignStage)_stage[parent].Next[a] == DesignStage.Series)
            {
                // The line moves, it is not copied: E → τ_der·E of the snapshot, the parent's line closes (§4.4a).
                Set(_lineE, d, a, _p.TauDer * Fx6.FromRaw(_lineE[parent].Current[a]));
                CloseSeries(a, parent);
            }
        }
        _stage[d].Next[a] = (int)DesignStage.Series;
        _idleC[d].Next[a] = 0;
    }

    // Step 3 (contract §4.4c): loss_acc += (((units·fleet_hours)·a_acc)·X)/D_ref with units and the design's hidden defects from the
    // snapshot (d_gate of step 1); n = min(⌊loss_acc⌋, ⌊units⌋) machines are struck off the cohorts directly, no message. The loss
    // formula reads the snapshot units, while the cap n and later c_u and upkeep use the units after retirement by age (accepted, L1).
    private void Accidents(int a)
    {
        for (int d = 0; d < _d; d++)
        {
            Set(_lost, d, a, Fx6.Zero);
            Set(_inService0, d, a, Fx6.Zero);
            if ((DesignStage)_stage[d].Next[a] is not (DesignStage.Series or DesignStage.InService))
                continue;
            Fx6 snapshotUnits = InService(a, d);
            Fx6 units = Units(a, d);
            Set(_inService0, d, a, units);
            if (snapshotUnits <= Fx6.Zero)
                continue;
            Fx6 x = ProductsMath.Excess(Get(_hidden, d, a), Get(_dGate, d, a));
            Fx6 acc = Get(_lossAcc, d, a) + ProductsMath.AccidentLoss(snapshotUnits, _p.FleetHoursPerUnit, _p.AAcc, x, _dRef);
            long n = ProductsMath.WholeLosses(acc, units);
            Set(_lossAcc, d, a, acc - Fx6.FromInt(n));
            Set(_lost, d, a, Fx6.FromInt(n));
            for (long i = 0; i < n; i++)
                StrikeOff(a, d);
            // Contract r6 §4.4d step 3: no fleet, no phantom accidents.
            if (Units(a, d) == Fx6.Zero)
                Set(_lossAcc, d, a, Fx6.Zero);
        }
    }

    // One machine: min(1, count) from the cohort with the largest count (tie: the earlier year).
    private void StrikeOff(int a, int d)
    {
        int best = -1;
        for (int y = 0; y < _y; y++)
        {
            if (best < 0 || _cohort[d * _y + y].Next[a] > _cohort[d * _y + best].Next[a])
                best = y;
        }
        LongColumn col = _cohort[d * _y + best];
        Fx6 count = Fx6.FromRaw(col.Next[a]);
        col.Next[a] = (count - Min(Fx6.One, count)).Raw;
    }

    // Step 5 (contract §4.4d): fixes by the team granted from t−1 (fix_hours each); while retro_pool > 0 a fixed defect is a retrofit
    // of built machines and is paid from the money left after upkeep, c_u = ((unit_cost_floor·ρ_retro)·units)/D_ref in one floor.
    // Unpaid retrofits stay open; found-but-unfixed defects keep hitting readiness.
    private void FixDefects(int a, long t, ref Fx6 left, ref Fx6 spent)
    {
        for (int d = 0; d < _d; d++)
        {
            if (!DefectsApply(a, d))
                continue;
            Fx6 open = Get(_open, d, a);
            Fx6 granted = Fx6.FromInt(_inputs.Granted(a, ProductsLayout.EngineerCategory, _l.FixRequester(d), t));
            Fx6 fixN = Min(open, granted * _l.HPm / _p.FixHours);
            Fx6 fixedNow = fixN;
            Fx6 pool = Get(_retro, d, a);
            if (pool > Fx6.Zero)
            {
                Fx6 cU = ProductsMath.RetrofitCost(FloorOf(AltsNext(a, d)), _p.RhoRetro, Units(a, d), _dRef);
                fixedNow = ProductsMath.RetrofitFixed(fixN, pool, cU, left, out Fx6 kPaid);
                Set(_retro, d, a, pool - kPaid);
                left -= kPaid * cU;
                spent += kPaid * cU;
            }
            Set(_open, d, a, open - fixedNow);
        }
    }

    // Step 6 (contract §4.4d, spec §3.5 A31): detection by cumulative hours T += test_rate (О, С) + k·units·fleet_hours;
    // defects_hidden falls at detection. Then the fix team asks for people (at most the bureau staff of the airframe class).
    private void DetectDefects(int chunk, int a)
    {
        for (int d = 0; d < _d; d++)
        {
            if (!DefectsApply(a, d))
                continue;
            Fx6 d0 = Get(_d0, d, a);
            var stage = (DesignStage)_stage[d].Next[a];
            Fx6 test = stage == DesignStage.InService ? Fx6.Zero : _p.TestRate;
            Fx6 hours = Get(_hours, d, a) + test + _p.KFleet * (Units(a, d) * _p.FleetHoursPerUnit);
            Fx6 found = ProductsMath.DetectedTotal(hours, d0, _p.LambdaD, _p.BetaQ10);
            Fx6 fresh = found - Get(_found, d, a);
            if (fresh < Fx6.Zero)
                throw new ProductsInvariantException(-1, a, $"detected defects of design {d} fell");
            Fx6 open = Get(_open, d, a) + fresh;
            Set(_hours, d, a, hours);
            Set(_found, d, a, found);
            Set(_hidden, d, a, d0 - found);
            Set(_open, d, a, open);

            long team = ProductsMath.People(open * _p.FixHours, _l.HPm);
            long cap = _bureauReq[DesignRules.AfClass(_l, AltsNext(a, d)) - 1];
            if (team > 0)
                _mailbox.ReserveWorkforce(chunk, ProductsLayout.EngineerRequest(a, _l.FixRequester(d), team < cap ? team : cap));
        }
    }

    private bool DefectsApply(int a, int d) =>
        (DesignStage)_stage[d].Next[a] is DesignStage.Prototype or DesignStage.Series or DesignStage.InService && Get(_d0, d, a) > Fx6.Zero;

    // Step 7 (contract §4.4d): r = min(rate asked, alloy line capacity, (money left + training money) / unit_cost); lines share alloy and money in
    // ascending design_id (§1.4); line_E forgets δ_E and gains r.
    private void Purchases(int chunk, int a, long t, int y, ref Fx6 left, ref Fx6 spent, ref Fx6 tbSpentSum)
    {
        Fx6 alloyLeft = _inputs.AlloySupply(a, t);
        _moneyTraced[a] = true;
        _traceLeftStart[a] = left.Raw;
        for (int d = 0; d < _d; d++)
        {
            Fx6 e = Get(_lineE, d, a);
            Fx6 leftIn = left, training = Fx6.Zero, paidFromLeft = Fx6.Zero, fromTraining = Fx6.Zero;
            Fx6 r = Fx6.Zero;
            LineBind bind = LineBind.None;
            if ((DesignStage)_stage[d].Next[a] == DesignStage.Series)
            {
                PrcMemo m = MemoNext(a, d);
                int[] alts = m.Alts;
                Fx6 cost = ProductsMath.UnitCost(CardFor(a, m, t), e);
                Fx6 want = _inputs.LineRate(a, d, t) ?? Get(_rateCmd, d, a);
                if (want < Fx6.Zero)
                    throw new ProductsInvariantException(t, a, $"negative line rate {want} for design {d}");
                // r3 (F15): the common money first, then the training money of this line only; tb = 0 gives the former formula.
                // Read for every line of С (a pure read of the snapshot) so the trace shows the input even when the line is not admitted.
                Fx6 tb = _inputs.TrainingBudget(a, d, t);
                training = tb;
                if (_inputs.Profile(a, m.Items, t).Admitted)
                {
                    Fx6 alloyKg = _l.AltAlloyKg[alts[_l.AlloySlot]];
                    bool free = DesignRules.AlloyFree(_l, alts);
                    if (tb < Fx6.Zero)
                        throw new ProductsInvariantException(t, a, $"negative training budget {tb} for design {d}");
                    // alloy_free: r = min(rate, (left + tb)/unit_cost) with no alloy term; alloy_left stays (spec v0.14 §1.4 item 1).
                    Fx6 moneyLimit = (left + tb) / cost;
                    r = free ? Min(want, moneyLimit) : Min(Min(want, alloyLeft / alloyKg), moneyLimit);
                    // Contract r6 §4.4d step 7: what bound the output; ties go to RATE, then ALLOY (money counts only when strictly the least).
                    bind = r == want ? LineBind.Rate : !free && r == alloyLeft / alloyKg ? LineBind.Alloy : LineBind.Money;
                    if (!free)
                        alloyLeft -= r * alloyKg;
                    Fx6 pay = r * cost;
                    Fx6 fromLeft = pay < left ? pay : left;
                    left -= fromLeft;
                    spent += fromLeft;
                    tbSpentSum += pay - fromLeft;
                    paidFromLeft = fromLeft;
                    fromTraining = pay - fromLeft;
                }
                _traceLines[a * _d + d] = new LineMoneyStep(d, leftIn, paidFromLeft, training, fromTraining);
                Set(_cost, d, a, cost);
                if (r > Fx6.Zero)
                    Produce(chunk, a, d, y, r, m.Prc);
                // Contract §4.4d step 7 (r4): idle_c counts the months of С with a zero line output, built_any is set by the first output.
                _idleC[d].Next[a] = r > Fx6.Zero ? 0 : _idleC[d].Next[a] + 1;
            }
            Set(_rate, d, a, r);
            _lineBind[d].Next[a] = (int)bind;
            Set(_lineE, d, a, e - _p.DeltaE * e + r);
        }
        if (alloyLeft < Fx6.Zero)
            throw new ProductsInvariantException(t, a, "alloy overdrawn (spec §3.4)");
    }

    private void ClearMoneyTrace(int a)
    {
        _moneyTraced[a] = false;
        _traceLeftStart[a] = 0;
        Array.Clear(_traceLines, a * _d, _d);
    }

    // report_output per process (units = r·n_p, labour_hours = units·labour_p, spec §3.2) and one skilled request per
    // (line, cluster) with n = ⌈Σ v_p / h_pm⌉ (spec §3.5 A26); the units join the cohort of the calendar year.
    private void Produce(int chunk, int a, int d, int y, Fx6 r, int[] prc)
    {
        Span<long> clusterHours = stackalloc long[_l.ClusterCount];
        foreach (int p in prc)
        {
            int k = _l.ProcIndex(p);
            Fx6 units = r * _l.ProcN[k];
            Fx6 hours = units * _l.ProcLabour[k];
            _mailbox.ReportOutput(chunk, new OutputReport(a, p, units, hours));
            clusterHours[_l.ProcCluster[k]] += hours.Raw;
        }
        _l.ReserveSkilled(_mailbox, chunk, a, d, clusterHours);
        LongColumn col = _cohort[d * _y + y];
        AccrueReplacement(a, d, r);
        col.Next[a] = (Fx6.FromRaw(col.Next[a]) + r).Raw;
        _built[d].Next[a] = 1;
    }

    private void Withdraw(int a)
    {
        for (int d = 0; d < _d; d++)
        {
            if ((DesignStage)_stage[d].Next[a] == DesignStage.InService && Units(a, d) == Fx6.Zero)
            {
                _stage[d].Next[a] = (int)DesignStage.Withdrawn;
                _replCredit[d].Next[a] = 0;
            }
        }
    }

    // ---- helpers

    /// <summary>
    /// The lowest free slot, −1 only when all <c>max_designs</c> are taken (ADR-0005 p. 3). A function of <c>Current</c> only: a slot is free
    /// when its snapshot stage is None or «снят» and no command of this step has taken it yet (its generation in Next is still the snapshot's).
    /// A slot freed by a command of this step (cancel) is not reusable until the next step. No free-list, so history of releases is irrelevant.
    /// </summary>
    private int FreeSlot(int a)
    {
        for (int d = 0; d < _d; d++)
        {
            if ((DesignStage)_stage[d].Current[a] is DesignStage.None or DesignStage.Withdrawn && _gen[d].Next[a] == _gen[d].Current[a])
                return d;
        }
        return -1;
    }

    // Takes a free slot: generation + 1 (checked) and every per-design column back to the state of a never used slot, so
    // nothing of the previous design survives into the new one.
    private void TakeSlot(int a, int d)
    {
        _gen[d].Next[a] = checked(_gen[d].Next[a] + 1);
        _parentGen[d].Next[a] = 0;
        _stage[d].Next[a] = (int)DesignStage.None;
        foreach (IntColumn[] col in (IntColumn[][])[_bureauOf, _parent, _stageO])
            col[d].Next[a] = -1;
        foreach (IntColumn[] col in (IntColumn[][])[_gp, _gateOpened, _forced, _cancelled, _idleC, _built, _quotaBase, _soldY, _lineBind])
            col[d].Next[a] = 0;
        foreach (LongColumn[] col in (LongColumn[][])[_work, _d0, _hidden, _open, _found, _hours, _lineE, _rate, _rateCmd, _cost, _dGate,
            _lossAcc, _retro, _lost, _inService0, _retired, _sold, _replCredit])
            col[d].Next[a] = 0;
        for (int b = 0; b < _qb; b++)
            _qMask[d * _qb + b].Next[a] = 0;
        for (int y = 0; y < _y; y++)
            _cohort[d * _y + y].Next[a] = 0;
    }

    // Handle resolution on the live state of the step (Next): earlier commands of the step may have cancelled or retaken the slot.
    private bool IsCurrent(int a, DesignHandle h) =>
        h.Slot >= 0 && h.Slot < _d && HandleHolds(_stage[h.Slot].Next[a], _gen[h.Slot].Next[a], h.Generation);

    private void Reject(int a, CommandRejection reason) => _rejected[(int)reason].Next[a]++;

    // R-PS2m cost card (contract §4.4a): h_first, h_floor, materials_floor by af_class; process part with U_B from the
    // production_profile of each block (П-8). A block without processes costs nothing and is not asked for.
    private CostCard CardFor(int a, PrcMemo m, long t)
    {
        int k = DesignRules.AfClass(_l, m.Alts), r = DesignRules.PuRank(_l, m.Alts);
        Fx6 uPu = m.SumPu == Fx6.Zero ? Fx6.One : _inputs.Profile(a, m.PuItems, t).UnitCostMult;
        Fx6 uAf = m.SumAf == Fx6.Zero ? Fx6.One : _inputs.Profile(a, m.AfItems, t).UnitCostMult;
        return new CostCard(_l.HFirst[k - 1], _l.HFloor[k - 1], _l.WageKh, _l.MaterialsFloor[k - 1],
            ProductsMath.ProcessPart(m.SumPu, uPu, _l.ProcScalePu[r - 1], m.SumAf, uAf, _l.ProcScaleAf[k - 1]), _p.LearningQ10);
    }

    private CostCard CardFor(int a, ReadOnlySpan<int> alts, int[] prc, long t)
    {
        int k = DesignRules.AfClass(_l, alts), r = DesignRules.PuRank(_l, alts);
        int[] pu = DesignRules.Block(_l, prc, airframe: false), af = DesignRules.Block(_l, prc, airframe: true);
        Fx6 sumPu = DesignRules.BaseCostSum(_l, pu), sumAf = DesignRules.BaseCostSum(_l, af);
        Fx6 uPu = sumPu == Fx6.Zero ? Fx6.One : _inputs.Profile(a, DesignRules.Items(_l, pu), t).UnitCostMult;
        Fx6 uAf = sumAf == Fx6.Zero ? Fx6.One : _inputs.Profile(a, DesignRules.Items(_l, af), t).UnitCostMult;
        return new CostCard(_l.HFirst[k - 1], _l.HFloor[k - 1], _l.WageKh, _l.MaterialsFloor[k - 1],
            ProductsMath.ProcessPart(sumPu, uPu, _l.ProcScalePu[r - 1], sumAf, uAf, _l.ProcScaleAf[k - 1]), _p.LearningQ10);
    }

    private Fx6 FloorOf(ReadOnlySpan<int> alts)
    {
        int[] prc = DesignRules.PrcSet(_l, alts);
        int k = DesignRules.AfClass(_l, alts), r = DesignRules.PuRank(_l, alts);
        return ProductsMath.UnitCostFloor(_l.MaterialsFloor[k - 1], _l.HFloor[k - 1], _l.WageKh,
            DesignRules.BaseCostSum(_l, DesignRules.Block(_l, prc, airframe: false)), _l.ProcScalePu[r - 1],
            DesignRules.BaseCostSum(_l, DesignRules.Block(_l, prc, airframe: true)), _l.ProcScaleAf[k - 1]);
    }

    private Fx6 UpkeepOf(ReadOnlySpan<int> alts) => UpkeepOf(alts, FloorOf(alts));

    private Fx6 UpkeepOf(ReadOnlySpan<int> alts, Fx6 floor) =>
        ProductsMath.Upkeep(_p.UpkeepShare, floor, DesignRules.Complexity(_l, DesignRules.PrcSet(_l, alts)), _p.UpkeepQ10, _l.Maintainability,
            DesignRules.UpkeepMult(_l, alts));

    private Fx6 UpkeepNext(int a, int d) => SameMemo(a, d, next: true) is { } memo ? memo.Upkeep : UpkeepOf(AltsNext(a, d));

    // The derived data of the alternatives in Next: the memo if they did not change, else made now (a design composed or derived this month).
    private PrcMemo MemoNext(int a, int d) => SameMemo(a, d, next: true) ?? MakeMemo(AltsNext(a, d));

    private int[] AltsNext(int a, int d)
    {
        var alts = new int[_s];
        for (int s = 0; s < _s; s++)
            alts[s] = _alt[d * _s + s].Next[a];
        return alts;
    }

    private void SetAlts(int a, int d, ReadOnlySpan<int> alts)
    {
        for (int s = 0; s < _s; s++)
            _alt[d * _s + s].Next[a] = alts[s];
    }

    private Fx6 Units(int a, int d)
    {
        Fx6 sum = Fx6.Zero;
        for (int y = 0; y < _y; y++)
            sum += Fx6.FromRaw(_cohort[d * _y + y].Next[a]);
        return sum;
    }

    private static Fx6 Get(LongColumn[] cols, int i, int a) => Fx6.FromRaw(cols[i].Next[a]);

    private static void Set(LongColumn[] cols, int i, int a, Fx6 v) => cols[i].Next[a] = v.Raw;

    private static Fx6 Min(Fx6 x, Fx6 y) => x < y ? x : y;

    private static Fx6 Max(Fx6 x, Fx6 y) => x > y ? x : y;
}
