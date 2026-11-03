using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Kernel;

namespace Singularity.Core.Products;

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
/// columns (ADR-0013 §4): per design slot <c>i &lt; max_designs</c> (the lowest free slot, never reused) its stage П/О/С/В (#67),
/// alternatives, platform generation <c>g_p</c>, parent, design work left, defects, cumulative test and fleet hours,
/// <c>line_E</c>, series gate (<c>d_gate</c>, <c>stage_o_month</c>, <c>gate_opened</c>, <c>series_forced</c>), accident and
/// retrofit state (<c>loss_acc</c>, <c>retro_pool</c>), last rate and unit cost; per bureau <c>bureau_exp</c> and its design; per
/// (design, calendar year) the cohort count; per actor the upkeep needed, the paid share, the money spent and the command
/// rejection counters. Compute of month t reads other layers only through <see cref="IProductsInputs"/> (snapshot t−1) and its
/// own state; own values computed earlier in the same step are used by the later parts of that step (order of §4.4d). It
/// posts <c>report_output</c> and <c>reserve_workforce</c> into the capability mailbox (applied in the capability merge of
/// month t; <c>granted</c> visible at t+1). No RNG anywhere in the layer (#28).
/// </summary>
public sealed class ProductsSystem : ITickSystem
{
    public const int DefaultSystemId = 300;

    /// <summary>
    /// Column layout version of the system (T-15, #117): 1 = TASK-0018 (slot (g, variant)), 2 = contract v2.4 (slots by
    /// capacity, gate, accident and retrofit columns), 3 = contract v2.4 r4 (<c>cancelled</c>, <c>idle_c</c>, <c>built_any</c>, #128).
    /// Part of the state-hash schema version of a model that hosts the system.
    /// </summary>
    public const int LayoutVersion = 3;

    private static readonly int RejectionKinds = Enum.GetValues<CommandRejection>().Length;

    private readonly ProductsLayout _l;
    private readonly ProductsParams _p;
    private readonly IProductsInputs _inputs;
    private readonly IndustryMailbox _mailbox;
    private readonly ProductsMode _mode;
    private readonly int _d, _s, _y, _b;
    private readonly Fx6 _dRef;

    // bureau_req[af_class] (§3.3, A34): computed once at construction, never in a tick.
    private readonly int[] _bureauReq;

    private readonly IntColumn[] _stage, _bureauOf, _alt, _bureauDesign, _gp, _parent, _stageO, _gateOpened, _forced, _lpSlot;
    private readonly IntColumn[] _cancelled, _idleC, _built;
    private readonly LongColumn[] _work, _d0, _hidden, _open, _found, _hours, _lineE, _rate, _cost, _exp, _cohort;
    private readonly LongColumn[] _rateCmd, _dGate, _lossAcc, _retro, _lost, _inService0, _rejected;
    private readonly LongColumn _need, _paid, _spent, _tbSpent;

    public ProductsSystem(Table table, ProductsLayout layout, ProductsStart start, ProductsParams parameters,
        IProductsInputs inputs, IndustryMailbox mailbox, ProductsMode mode = ProductsMode.Full, int systemId = DefaultSystemId)
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
        _bureauReq = new int[layout.DesignScale.Length];
        for (int k = 0; k < _bureauReq.Length; k++)
            _bureauReq[k] = parameters.BureauReq(layout.DesignScale[k]);

        _stage = new IntColumn[_d];
        _bureauOf = new IntColumn[_d];
        _gp = new IntColumn[_d];
        _parent = new IntColumn[_d];
        _stageO = new IntColumn[_d];
        _gateOpened = new IntColumn[_d];
        _forced = new IntColumn[_d];
        _cancelled = new IntColumn[_d];
        _idleC = new IntColumn[_d];
        _built = new IntColumn[_d];
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
        for (int d = 0; d < _d; d++)
        {
            string n = $"prd.d{d}";
            _stage[d] = table.AddInt($"{n}.stage");
            _bureauOf[d] = table.AddInt($"{n}.bureau");
            _gp[d] = table.AddInt($"{n}.gp");
            _parent[d] = table.AddInt($"{n}.parent");
            _stageO[d] = table.AddInt($"{n}.stageO");
            _gateOpened[d] = table.AddInt($"{n}.gateOpened");
            _forced[d] = table.AddInt($"{n}.forced");
            _cancelled[d] = table.AddInt($"{n}.cancelled");
            _idleC[d] = table.AddInt($"{n}.idleC");
            _built[d] = table.AddInt($"{n}.builtAny");
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
        _lpSlot = new IntColumn[layout.Generations];
        for (int g = 0; g < layout.Generations; g++)
            _lpSlot[g] = table.AddInt($"prd.lp.g{g + 1}");
        _rejected = new LongColumn[RejectionKinds];
        for (int r = 0; r < RejectionKinds; r++)
            _rejected[r] = table.AddLong($"prd.rejected.{(CommandRejection)r}");
        _need = table.AddLong("prd.upkeepNeed");
        _paid = table.AddLong("prd.paidRatio");
        _spent = table.AddLong("prd.spent");
        _tbSpent = table.AddLong("prd.tbSpent");

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
            for (int a = 0; a < rows; a++)
                ints[a] = start.Stage[a * _d + d] == DesignStage.None ? 0 : 1;
            _gateOpened[d].Load(ints);
            // built_any: the start fleets (stage В) were built; a line never started has not (contract §3.1, r4).
            for (int a = 0; a < rows; a++)
                ints[a] = start.Stage[a * _d + d] == DesignStage.InService ? 1 : 0;
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
        Array.Fill(longs, Fx6.Scale);
        _paid.Load(longs);
    }

    public int SystemId { get; }

    public Cadence Cadence => Cadence.Monthly;

    public ProductsLayout Layout => _l;

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

    /// <summary>Alternatives of a design (layout-global indices), −1 for an empty slot.</summary>
    public int[] Alternatives(int actor, int design)
    {
        var alts = new int[_s];
        for (int s = 0; s < _s; s++)
            alts[s] = _alt[design * _s + s].Current[actor];
        return alts;
    }

    /// <summary><c>prc_set</c> of a design (ascending process numbers); empty without a design.</summary>
    public int[] PrcSet(int actor, int design) =>
        Stage(actor, design) == DesignStage.None ? [] : DesignRules.PrcSet(_l, Alternatives(actor, design));

    public Fx6 Perf(int actor, int design) => DesignRules.Perf(_l, Alternatives(actor, design));

    public int Complexity(int actor, int design) => DesignRules.Complexity(_l, PrcSet(actor, design));

    /// <summary><c>g_p</c> of a design, fixed at creation (contract §4.4a).</summary>
    public int PlatformGen(int actor, int design) => _gp[design].Current[actor];

    /// <summary><c>gen(d) = min(g_p, eng_gen(engine) + 1)</c> (contract §4.4a, #70).</summary>
    public int Gen(int actor, int design) => DesignRules.Gen(_l, PlatformGen(actor, design), Alternatives(actor, design));

    /// <summary><c>parent_design</c>, −1 for a design that is not derived.</summary>
    public int Parent(int actor, int design) => _parent[design].Current[actor];

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

    /// <summary>Commands of the actor rejected for <paramref name="reason"/> so far (the journal of rejections, contract §3.1).</summary>
    public long RejectionCount(int actor, CommandRejection reason) => _rejected[(int)reason].Current[actor];

    /// <summary>The canonical L_P project slot of generation <paramref name="gen"/>, −1 before it exists.</summary>
    public int CanonicalSlot(int actor, int gen) => _lpSlot[gen - 1].Current[actor];

    /// <summary><c>unit_cost_floor</c> of a design (STATE_WORDS §3; R-PS2m at <c>U</c> = 1).</summary>
    public Fx6 UnitCostFloor(int actor, int design) => FloorOf(Alternatives(actor, design));

    /// <summary>Upkeep per unit and month of a design (contract §4.4).</summary>
    public Fx6 Upkeep(int actor, int design) => UpkeepOf(Alternatives(actor, design));

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
        ProductionProfile profile = _inputs.Profile(actor, DesignRules.Items(_l, PrcSet(actor, design)), month);
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
        var used = new List<int>();
        for (int e = 0; e < _d; e++)
        {
            var st = Stage(actor, e);
            if (st == DesignStage.None || st == DesignStage.Withdrawn && StageOMonth(actor, e) < 0)
                continue;
            for (int s = 0; s < _s; s++)
                used.Add(_alt[e * _s + s].Current[actor]);
        }
        Fx6 exp = Fx6.Zero;
        for (int b = 0; b < _b; b++)
        {
            if (BureauExp(actor, b) > exp)
                exp = BureauExp(actor, b);
        }
        int complexity = DesignRules.Complexity(_l, prc);
        Fx6 d0 = ProductsMath.InitialDefects(_p.D0Factor, DesignRules.Novelty(_l, alts, used.ToArray()), complexity, exp, _p.EB);
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
            Retire(a, t, calendar);
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
        long month = context.Calendar!.MonthIndex(context.Tick - 1);
        for (int a = 0; a < _l.SiteCount; a++)
        {
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
            }
        }
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
        ApplyDesignCommands(a, t, knows);
        BureausRequestOrIdle(chunk, a);
        ApplySeriesCommands(a, t);
        Accidents(a);

        Fx6 budget = _inputs.ProcurementBudget(a, t);
        if (budget < Fx6.Zero)
            throw new ProductsInvariantException(t, a, $"negative procurement budget {budget}");
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
            throw new ProductsInvariantException(t, a, $"spent {spent} of {budget} (spec §3.4)");
        _need.Next[a] = need.Raw;
        _paid.Next[a] = ProductsMath.PaidRatio(paid, need).Raw;
        _spent.Next[a] = spent.Raw;
        _tbSpent.Next[a] = tbSpent.Raw;
    }

    // L_P (spec §6.1): canonical project of the newest due generation, output r = min(ρ[g]·procurement budget, alloy capacity).
    private void StepCanonical(int chunk, int a, long t, int y)
    {
        int cur = 0;
        for (int g = 1; g <= _l.Generations; g++)
        {
            long since = _inputs.AppKnownSince(a, g, t);
            if (since >= 0 && t >= since + _p.LpTDesign[g - 1])
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
                int[] alts = AltsNext(a, d);
                int[] prc = DesignRules.PrcSet(_l, alts);
                CostCard card = CardFor(a, alts, prc, t);
                Fx6 alloyKg = _l.AltAlloyKg[alts[_l.AlloySlot]];
                bool free = DesignRules.AlloyFree(_l, alts);
                if (_inputs.Profile(a, DesignRules.Items(_l, prc), t).Admitted)
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
                    Produce(chunk, a, d, y, r, prc);
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
        _lpSlot[gen - 1].Next[a] = slot;
        for (int d = 0; d < _d; d++)
        {
            if ((DesignStage)_stage[d].Next[a] == DesignStage.Series)
                _stage[d].Next[a] = (int)DesignStage.InService;
        }
        SetAlts(a, slot, alts);
        _gp[slot].Next[a] = gen;
        _gateOpened[slot].Next[a] = 1;
        _stage[slot].Next[a] = (int)DesignStage.Series;
    }

    // Cohorts of calendar year Y are written off when t − m0(Y) ≥ service_life, m0(Y) = month index of July of Y (spec §3.5
    // A32: a (design, year) cohort ages as a whole from mid-year; with the start 1945-09, 1945 → month 178, 1946 → 190).
    private void Retire(int a, long t, TickCalendar calendar)
    {
        for (int y = 0; y < _y; y++)
        {
            long m0 = (long)(_l.StartYear + y - calendar.Start.Year) * 12 + (7 - calendar.Start.Month);
            if (t - m0 < _p.ServiceLife)
                continue;
            for (int d = 0; d < _d; d++)
                _cohort[d * _y + y].Next[a] = 0;
        }
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
            Set(_dGate, d, a, ProductsMath.PreliminaryGate(_p.KGate, _dRef, Get(_d0, d, a), _p.TTestMin, _p.TestRate, _p.LambdaD, _p.BetaQ10));
        }
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
                _ => Cancel(a, cmd.Target),
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
        int parent = cmd.Target;
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

        var used = new List<int>();
        for (int e = 0; e < _d; e++)
        {
            // A project cancelled in П never tested its alternatives: they stay new (contract §4.4e; one cancelled in О does not).
            var st = (DesignStage)_stage[e].Next[a];
            if (st == DesignStage.None || st == DesignStage.Withdrawn && _stageO[e].Next[a] < 0)
                continue;
            for (int s = 0; s < _s; s++)
                used.Add(_alt[e * _s + s].Next[a]);
        }
        Fx6 novelty = DesignRules.Novelty(_l, noveltyAlts, used.ToArray());
        int complexity = DesignRules.Complexity(_l, DesignRules.PrcSet(_l, alts));
        int k = DesignRules.AfClass(_l, alts);
        Fx6 exp = Get(_exp, bureau, a);
        Fx6 work = ProductsMath.DesignWork(_p.W0, complexity, _l.DesignScale[k - 1], exp, _p.EB);
        if (parent >= 0)
            work = work * Fx6.FromInt(changed) / Fx6.FromInt(slots);
        Fx6 d0 = ProductsMath.InitialDefects(_p.D0Factor, novelty, complexity, exp, _p.EB);
        SetAlts(a, d, alts);
        _stage[d].Next[a] = (int)DesignStage.Project;
        _bureauOf[d].Next[a] = bureau;
        _bureauDesign[bureau].Next[a] = d;
        _gp[d].Next[a] = platformGen;
        _parent[d].Next[a] = parent;
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
                _mailbox.ReserveWorkforce(chunk, new WorkforceRequest(a, ProductsLayout.EngineerCategory, _l.BureauRequester(b),
                    _bureauReq[_l.AltAfClass[_alt[d * _s + _l.AirframeSlot].Next[a]] - 1]));
            }
            else if (_bureauDesign[b].Current[a] < 0)
            {
                Fx6 exp = Get(_exp, b, a);
                Set(_exp, b, a, exp - _p.DeltaB * exp);
            }
        }
    }

    // Commands in force (contract §4.4c): start_series from О or В, close a line in С (line_E → 0, contract §3).
    private void ApplySeriesCommands(int a, long t)
    {
        for (int d = 0; d < _d; d++)
        {
            // start_series is validated against the snapshot stage (contract §4.4d step 2); a line closes from the live stage.
            var snapshot = (DesignStage)_stage[d].Current[a];
            // A project cancelled earlier in this step (Next stage «снят») cannot start a series.
            if (snapshot is (DesignStage.Prototype or DesignStage.InService) && (DesignStage)_stage[d].Next[a] == snapshot
                && _inputs.StartSeries(a, d, t) is { } cmd)
                StartSeries(a, d, snapshot, cmd);
            else if ((DesignStage)_stage[d].Next[a] == DesignStage.Series && _inputs.CloseLine(a, d, t))
                CloseSeries(a, d);
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
            if (_parent[e].Next[a] == d && (DesignStage)_stage[e].Next[a] == DesignStage.Series)
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
            if (parent >= 0 && (DesignStage)_stage[parent].Next[a] == DesignStage.Series)
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
                _mailbox.ReserveWorkforce(chunk, new WorkforceRequest(a, ProductsLayout.EngineerCategory, _l.FixRequester(d), team < cap ? team : cap));
        }
    }

    private bool DefectsApply(int a, int d) =>
        (DesignStage)_stage[d].Next[a] is DesignStage.Prototype or DesignStage.Series or DesignStage.InService && Get(_d0, d, a) > Fx6.Zero;

    // Step 7 (contract §4.4d): r = min(rate asked, alloy line capacity, (money left + training money) / unit_cost); lines share alloy and money in
    // ascending design_id (§1.4); line_E forgets δ_E and gains r.
    private void Purchases(int chunk, int a, long t, int y, ref Fx6 left, ref Fx6 spent, ref Fx6 tbSpentSum)
    {
        Fx6 alloyLeft = _inputs.AlloySupply(a, t);
        for (int d = 0; d < _d; d++)
        {
            Fx6 e = Get(_lineE, d, a);
            Fx6 r = Fx6.Zero;
            if ((DesignStage)_stage[d].Next[a] == DesignStage.Series)
            {
                int[] alts = AltsNext(a, d);
                int[] prc = DesignRules.PrcSet(_l, alts);
                Fx6 cost = ProductsMath.UnitCost(CardFor(a, alts, prc, t), e);
                Fx6 want = _inputs.LineRate(a, d, t) ?? Get(_rateCmd, d, a);
                if (want < Fx6.Zero)
                    throw new ProductsInvariantException(t, a, $"negative line rate {want} for design {d}");
                if (_inputs.Profile(a, DesignRules.Items(_l, prc), t).Admitted)
                {
                    Fx6 alloyKg = _l.AltAlloyKg[alts[_l.AlloySlot]];
                    bool free = DesignRules.AlloyFree(_l, alts);
                    // r3 (F15): the common money first, then the training money of this line only; tb = 0 gives the former formula.
                    Fx6 tb = _inputs.TrainingBudget(a, d, t);
                    if (tb < Fx6.Zero)
                        throw new ProductsInvariantException(t, a, $"negative training budget {tb} for design {d}");
                    // alloy_free: r = min(rate, (left + tb)/unit_cost) with no alloy term; alloy_left stays (spec v0.14 §1.4 item 1).
                    r = free ? Min(want, (left + tb) / cost) : Min(Min(want, alloyLeft / alloyKg), (left + tb) / cost);
                    if (!free)
                        alloyLeft -= r * alloyKg;
                    Fx6 pay = r * cost;
                    Fx6 fromLeft = pay < left ? pay : left;
                    left -= fromLeft;
                    spent += fromLeft;
                    tbSpentSum += pay - fromLeft;
                }
                Set(_cost, d, a, cost);
                if (r > Fx6.Zero)
                    Produce(chunk, a, d, y, r, prc);
                // Contract §4.4d step 7 (r4): idle_c counts the months of С with a zero line output, built_any is set by the first output.
                _idleC[d].Next[a] = r > Fx6.Zero ? 0 : _idleC[d].Next[a] + 1;
            }
            Set(_rate, d, a, r);
            Set(_lineE, d, a, e - _p.DeltaE * e + r);
        }
        if (alloyLeft < Fx6.Zero)
            throw new ProductsInvariantException(t, a, "alloy overdrawn (spec §3.4)");
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
        for (int c = 0; c < _l.ClusterCount; c++)
        {
            long n = ProductsMath.People(Fx6.FromRaw(clusterHours[c]), _l.HPm);
            if (n > 0)
                _mailbox.ReserveWorkforce(chunk, new WorkforceRequest(a, _l.SkilledCategory, _l.SkilledRequester(d, c), n));
        }
        LongColumn col = _cohort[d * _y + y];
        col.Next[a] = (Fx6.FromRaw(col.Next[a]) + r).Raw;
        _built[d].Next[a] = 1;
    }

    private void Withdraw(int a)
    {
        for (int d = 0; d < _d; d++)
        {
            if ((DesignStage)_stage[d].Next[a] == DesignStage.InService && Units(a, d) == Fx6.Zero)
                _stage[d].Next[a] = (int)DesignStage.Withdrawn;
        }
    }

    // ---- helpers

    /// <summary>The lowest slot without a design (stage None), −1 if the capacity is used up. Slots are never reused (§3.1).</summary>
    private int FreeSlot(int a)
    {
        for (int d = 0; d < _d; d++)
        {
            if ((DesignStage)_stage[d].Next[a] == DesignStage.None)
                return d;
        }
        return -1;
    }

    private void Reject(int a, CommandRejection reason) => _rejected[(int)reason].Next[a]++;

    // R-PS2m cost card (contract §4.4a): h_first, h_floor, materials_floor by af_class; process part with U_B from the
    // production_profile of each block (П-8). A block without processes costs nothing and is not asked for.
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

    private Fx6 UpkeepOf(ReadOnlySpan<int> alts) =>
        ProductsMath.Upkeep(_p.UpkeepShare, FloorOf(alts), DesignRules.Complexity(_l, DesignRules.PrcSet(_l, alts)), _p.UpkeepQ10, _l.Maintainability,
            DesignRules.UpkeepMult(_l, alts));

    private Fx6 UpkeepNext(int a, int d) => UpkeepOf(AltsNext(a, d));

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
