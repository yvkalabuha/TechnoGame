using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Kernel;

namespace Singularity.Core.Products;

/// <summary>Full product model or the product-off control (spec §6.1).</summary>
public enum ProductsMode
{
    Full,

    /// <summary>L_P: canonical project with a fixed output rate; no bureaus, <c>line_E</c>, defects, variants, procurement budget.</summary>
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
/// Product layer of the jet slice (contract v2.3 §3, §4.4, §4.6, §5; spec §1.4, §3.3, §5.5, §6.1): a monthly system on a
/// table with one row per actor (one site per actor, site row = actor row, spec §3.5 A27). All state is in Table columns
/// (ADR-0013 §4): per design slot (stable id <c>(gen − 1)·2 + variant</c>) its stage П/О/С/В (#67), alternatives, design work
/// left, defects, cumulative test and fleet hours, <c>line_E</c>, last rate and unit cost; per bureau <c>bureau_exp</c> and
/// its design; per (design, calendar year) the cohort count; per actor the upkeep needed, the paid share and the money spent.
/// Compute of month t reads other layers only through <see cref="IProductsInputs"/> (snapshot t−1) and its own state;
/// own values computed earlier in the same step (retirement, bureau progress) are used by the later parts of that step.
/// It posts <c>report_output</c> and <c>reserve_workforce</c> into the capability mailbox (applied in the capability merge
/// of month t; <c>granted</c> visible at t+1). No RNG anywhere in the layer (#28).
/// </summary>
public sealed class ProductsSystem : ITickSystem
{
    public const int DefaultSystemId = 300;

    private readonly ProductsLayout _l;
    private readonly ProductsParams _p;
    private readonly IProductsInputs _inputs;
    private readonly IndustryMailbox _mailbox;
    private readonly ProductsMode _mode;
    private readonly int _d, _s, _y, _b;

    private readonly IntColumn[] _stage, _bureauOf, _alt, _bureauDesign;
    private readonly LongColumn[] _work, _d0, _hidden, _open, _found, _hours, _lineE, _rate, _cost, _exp, _cohort;
    private readonly LongColumn _need, _paid, _spent;

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

        _stage = new IntColumn[_d];
        _bureauOf = new IntColumn[_d];
        _alt = new IntColumn[_d * _s];
        _work = new LongColumn[_d];
        _d0 = new LongColumn[_d];
        _hidden = new LongColumn[_d];
        _open = new LongColumn[_d];
        _found = new LongColumn[_d];
        _hours = new LongColumn[_d];
        _lineE = new LongColumn[_d];
        _rate = new LongColumn[_d];
        _cost = new LongColumn[_d];
        _cohort = new LongColumn[_d * _y];
        for (int d = 0; d < _d; d++)
        {
            string n = $"prd.d{d}";
            _stage[d] = table.AddInt($"{n}.stage");
            _bureauOf[d] = table.AddInt($"{n}.bureau");
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
            _cost[d] = table.AddLong($"{n}.unitCost");
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
        _need = table.AddLong("prd.upkeepNeed");
        _paid = table.AddLong("prd.paidRatio");
        _spent = table.AddLong("prd.spent");

        int rows = layout.SiteCount;
        var ints = new int[rows];
        var longs = new long[rows];
        for (int d = 0; d < _d; d++)
        {
            for (int a = 0; a < rows; a++)
                ints[a] = (int)start.Stage[a * _d + d];
            _stage[d].Load(ints);
            Array.Fill(ints, -1);
            _bureauOf[d].Load(ints);
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
        Array.Fill(longs, Fx6.Scale);
        _paid.Load(longs);
    }

    public int SystemId { get; }

    public Cadence Cadence => Cadence.Monthly;

    public ProductsLayout Layout => _l;

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

    public Fx6 WorkLeft(int actor, int design) => Fx6.FromRaw(_work[design].Current[actor]);
    public Fx6 InitialDefects(int actor, int design) => Fx6.FromRaw(_d0[design].Current[actor]);
    public Fx6 DefectsHidden(int actor, int design) => Fx6.FromRaw(_hidden[design].Current[actor]);
    public Fx6 DefectsOpen(int actor, int design) => Fx6.FromRaw(_open[design].Current[actor]);
    public Fx6 DefectsFound(int actor, int design) => Fx6.FromRaw(_found[design].Current[actor]);
    public Fx6 Hours(int actor, int design) => Fx6.FromRaw(_hours[design].Current[actor]);
    public Fx6 LineE(int actor, int design) => Fx6.FromRaw(_lineE[design].Current[actor]);

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

    /// <summary><c>unit_cost_floor</c> of a design (STATE_WORDS §3).</summary>
    public Fx6 UnitCostFloor(int actor, int design)
    {
        int g = ProductsLayout.GenOf(design);
        return ProductsMath.UnitCostFloor(_l.MaterialsFloor[g - 1], _l.HFloor[g - 1], _l.WageKh,
            DesignRules.BaseCostSum(_l, PrcSet(actor, design)), _l.ProcScale[g - 1]);
    }

    /// <summary>Upkeep per unit and month of a design (contract §4.4).</summary>
    public Fx6 Upkeep(int actor, int design) =>
        ProductsMath.Upkeep(_p.UpkeepShare, UnitCostFloor(actor, design), Complexity(actor, design), _p.UpkeepQ10, _l.Maintainability);

    /// <summary>
    /// <c>readiness</c> of a design's cohorts (spec §3.3) from the current state and the <c>q</c> of
    /// <c>production_profile</c> of month <paramref name="month"/>; 1 under L_P (§6.1).
    /// </summary>
    public Fx6 Readiness(int actor, int design, long month)
    {
        if (_mode == ProductsMode.CanonicalLp)
            return Fx6.One;
        ProductionProfile profile = _inputs.Profile(actor, DesignRules.Items(_l, PrcSet(actor, design)), month);
        return ProductsMath.Readiness(PaidRatio(actor), profile.Q, DefectsHidden(actor, design) + DefectsOpen(actor, design),
            InitialDefects(actor, design), _p.PsiD);
    }

    /// <summary>
    /// <c>n_v</c> and <c>FV = (n_v·readiness)·perf_rel</c> at equal budget (spec §5.5) for alternatives <paramref name="alts"/>
    /// of generation <paramref name="gen"/> at line experience <paramref name="e"/>, with <c>production_profile</c> frozen at
    /// <paramref name="month"/>. A pure function of the snapshot; not part of the tick.
    /// </summary>
    public (int Count, Fx6 Value) FleetValue(int actor, ReadOnlySpan<int> alts, int gen, Fx6 e, Fx6 readiness, Fx6 perfRel,
        Fx6 budget, long month)
    {
        int[] prc = DesignRules.PrcSet(_l, alts);
        ProductionProfile profile = _inputs.Profile(actor, DesignRules.Items(_l, prc), month);
        Fx6 baseSum = DesignRules.BaseCostSum(_l, prc);
        var card = Card(gen, baseSum, profile.UnitCostMult);
        Fx6 floor = ProductsMath.UnitCostFloor(_l.MaterialsFloor[gen - 1], _l.HFloor[gen - 1], _l.WageKh, baseSum, _l.ProcScale[gen - 1]);
        Fx6 upkeep = ProductsMath.Upkeep(_p.UpkeepShare, floor, DesignRules.Complexity(_l, prc), _p.UpkeepQ10, _l.Maintainability);
        int n = ProductsMath.FleetCount(card, e, upkeep, _p.FvHorizon, budget, _p.FvStep, _p.FvMaxUnits);
        return (n, ProductsMath.FleetValue(n, readiness, perfRel));
    }

    /// <summary>
    /// The alternatives a bureau would draw for (actor, gen, variant) in month <paramref name="month"/> (spec §1.4); false if the
    /// design cannot be made, or, for «технологичный», if it would equal «превосходящий» (F-PC3).
    /// </summary>
    public bool Variant(int actor, int gen, DesignVariant variant, long month, Span<int> alts) =>
        variant == DesignVariant.Superior
            ? DesignRules.Superior(_l, gen, n => _inputs.Knows(actor, n, month), alts)
            : DesignRules.Producible(_l, gen, n => _inputs.Knows(actor, n, month), p => _inputs.AdmittedYield(actor, p, month), _p.YProd, alts);

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
                for (int y = 0; y < _y; y++)
                {
                    if (_cohort[d * _y + y].Current[a] < 0)
                        throw new ProductsInvariantException(month, a, $"negative cohort of design {d}");
                }
            }
        }
    }

    // ---- compute parts (own row a only; reads and writes Next, which starts as a copy of the snapshot)

    private void StepFull(int chunk, int a, long t, int y)
    {
        Func<int, bool> knows = n => _inputs.Knows(a, n, t);
        AdvanceBureaus(a, t);
        PlaceOrder(a, t, knows);
        BureausRequestOrIdle(chunk, a);
        ApplyCommands(a, t);
        Defects(chunk, a, t);

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
        // Upkeep is paid before purchases (spec §3.3).
        Fx6 paid = need < budget ? need : budget;
        Fx6 left = budget - paid, spent = paid;
        Fx6 alloyLeft = _inputs.AlloySupply(a, t);
        for (int d = 0; d < _d; d++)
        {
            Fx6 e = Get(_lineE, d, a);
            Fx6 r = Fx6.Zero;
            if ((DesignStage)_stage[d].Next[a] == DesignStage.Series)
            {
                int g = ProductsLayout.GenOf(d);
                int[] prc = PrcNext(a, d);
                ProductionProfile profile = _inputs.Profile(a, DesignRules.Items(_l, prc), t);
                Fx6 cost = ProductsMath.UnitCost(Card(g, DesignRules.BaseCostSum(_l, prc), profile.UnitCostMult), e);
                Fx6 want = _inputs.LineRate(a, d, t);
                if (want < Fx6.Zero)
                    throw new ProductsInvariantException(t, a, $"negative line rate {want} for design {d}");
                if (profile.Admitted)
                {
                    // Spec §3.3: r = min(rate asked, line_capacity by alloy, money left / unit_cost); lines share alloy and
                    // money in ascending design_id (§1.4).
                    r = Min(Min(want, alloyLeft / _l.AlloyKg[g - 1]), left / cost);
                    alloyLeft -= r * _l.AlloyKg[g - 1];
                    Fx6 pay = r * cost;
                    left -= pay;
                    spent += pay;
                }
                Set(_cost, d, a, cost);
                if (r > Fx6.Zero)
                    Produce(chunk, a, d, y, r, prc);
            }
            Set(_rate, d, a, r);
            Set(_lineE, d, a, e - _p.DeltaE * e + r);
        }
        if (spent > budget || alloyLeft < Fx6.Zero)
            throw new ProductsInvariantException(t, a, $"spent {spent} of {budget} or alloy overdrawn (spec §3.4)");
        _need.Next[a] = need.Raw;
        _paid.Next[a] = ProductsMath.PaidRatio(paid, need).Raw;
        _spent.Next[a] = spent.Raw;
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
        {
            // L_P has no variants: the canonical project takes the (g, «технологичный») slot, so start fleets in the
            // (g, «превосходящий») slot keep their own alternatives [Предл.].
            int dc = ProductsLayout.DesignId(cur, DesignVariant.Producible);
            if ((DesignStage)_stage[dc].Next[a] is DesignStage.None or DesignStage.Withdrawn)
            {
                Span<int> alts = stackalloc int[_s];
                if (DesignRules.Canonical(_l, cur, n => _inputs.Knows(a, n, t), p => _inputs.AdmittedYield(a, p, t), alts))
                {
                    for (int d = 0; d < _d; d++)
                    {
                        if ((DesignStage)_stage[d].Next[a] == DesignStage.Series)
                            _stage[d].Next[a] = (int)DesignStage.InService;
                    }
                    SetAlts(a, dc, alts);
                    _stage[dc].Next[a] = (int)DesignStage.Series;
                }
            }
        }
        Fx6 alloyLeft = _inputs.AlloySupply(a, t);
        Fx6 budget = _inputs.ProcurementBudget(a, t);
        for (int d = 0; d < _d; d++)
        {
            Fx6 r = Fx6.Zero;
            if ((DesignStage)_stage[d].Next[a] == DesignStage.Series)
            {
                int g = ProductsLayout.GenOf(d);
                int[] prc = PrcNext(a, d);
                ProductionProfile profile = _inputs.Profile(a, DesignRules.Items(_l, prc), t);
                if (profile.Admitted)
                {
                    r = Min(_p.LpRho[g - 1] * budget, alloyLeft / _l.AlloyKg[g - 1]);
                    alloyLeft -= r * _l.AlloyKg[g - 1];
                }
                // §6.1: price = Σ n_p·base_cost_p·unit_cost_mult (diagnostic only; no budget is spent).
                Set(_cost, d, a, DesignRules.BaseCostSum(_l, prc) * profile.UnitCostMult);
                if (r > Fx6.Zero)
                    Produce(chunk, a, d, y, r, prc);
            }
            Set(_rate, d, a, r);
        }
        _need.Next[a] = 0;
        _paid.Next[a] = Fx6.Scale;
        _spent.Next[a] = 0;
    }

    // Cohorts of calendar year Y are written off when t − m0(Y) ≥ service_life, m0(Y) = month index of January of Y
    // [Предл.: the spec ages a (design, year) cohort as a whole; January is its earliest possible unit].
    private void Retire(int a, long t, TickCalendar calendar)
    {
        for (int y = 0; y < _y; y++)
        {
            long m0 = (long)(_l.StartYear + y - calendar.Start.Year) * 12 - (calendar.Start.Month - 1);
            if (t - m0 < _p.ServiceLife)
                continue;
            for (int d = 0; d < _d; d++)
                _cohort[d * _y + y].Next[a] = 0;
        }
    }

    // Bureau progress in t equals granted from t−1 (contract §4.6, spec §3.3); finishing W gives +1 bureau_exp and frees the bureau.
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
        }
    }

    // Requirement and variant choice in force (spec §4): the free bureau with the largest bureau_exp (tie: lower id) draws
    // the variant from the snapshot; W and D0 are fixed at the order. A «технологичный» order that equals «превосходящий»
    // (F-PC3) becomes the single superior design.
    private void PlaceOrder(int a, long t, Func<int, bool> knows)
    {
        if (_inputs.Order(a, t) is not { } order || order.Gen < 1 || order.Gen > _l.Generations)
            return;
        if (!knows(_l.GenApp[order.Gen - 1]))
            return;
        Span<int> alts = stackalloc int[_s];
        DesignVariant variant = order.Variant;
        bool ok = variant == DesignVariant.Superior
            ? DesignRules.Superior(_l, order.Gen, knows, alts)
            : DesignRules.Producible(_l, order.Gen, knows, p => _inputs.AdmittedYield(a, p, t), _p.YProd, alts);
        if (!ok && variant == DesignVariant.Producible)
        {
            variant = DesignVariant.Superior;
            ok = DesignRules.Superior(_l, order.Gen, knows, alts);
        }
        int d = ProductsLayout.DesignId(order.Gen, variant);
        if (!ok || (DesignStage)_stage[d].Next[a] != DesignStage.None)
            return;
        int bureau = -1;
        for (int b = 0; b < _b; b++)
        {
            if (_bureauDesign[b].Next[a] < 0 && (bureau < 0 || Get(_exp, b, a) > Get(_exp, bureau, a)))
                bureau = b;
        }
        if (bureau < 0)
            return;

        var used = new List<int>();
        for (int e = 0; e < _d; e++)
        {
            if ((DesignStage)_stage[e].Next[a] == DesignStage.None)
                continue;
            for (int s = 0; s < _s; s++)
                used.Add(_alt[e * _s + s].Next[a]);
        }
        Fx6 novelty = DesignRules.Novelty(_l, alts, used.ToArray());
        int complexity = DesignRules.Complexity(_l, DesignRules.PrcSet(_l, alts));
        Fx6 exp = Get(_exp, bureau, a);
        Fx6 d0 = ProductsMath.InitialDefects(_p.D0Factor, novelty, complexity, exp, _p.EB);
        SetAlts(a, d, alts);
        _stage[d].Next[a] = (int)DesignStage.Project;
        _bureauOf[d].Next[a] = bureau;
        _bureauDesign[bureau].Next[a] = d;
        Set(_work, d, a, ProductsMath.DesignWork(_p.W0, complexity, _l.DesignScale[order.Gen - 1], exp, _p.EB));
        Set(_d0, d, a, d0);
        Set(_hidden, d, a, d0);
        Set(_open, d, a, Fx6.Zero);
        Set(_found, d, a, Fx6.Zero);
        Set(_hours, d, a, Fx6.Zero);
        Set(_lineE, d, a, Fx6.Zero);
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
                    _l.BureauEngineers[ProductsLayout.GenOf(d) - 1]));
            }
            else if (_bureauDesign[b].Current[a] < 0)
            {
                Fx6 exp = Get(_exp, b, a);
                Set(_exp, b, a, exp - _p.DeltaB * exp);
            }
        }
    }

    // Commands in force (spec §4): launch a series from О or В; close a line in С (line_E → 0, contract §3).
    private void ApplyCommands(int a, long t)
    {
        for (int d = 0; d < _d; d++)
        {
            var stage = (DesignStage)_stage[d].Next[a];
            if (stage is (DesignStage.Prototype or DesignStage.InService) && _inputs.LaunchSeries(a, d, t))
                _stage[d].Next[a] = (int)DesignStage.Series;
            else if (stage == DesignStage.Series && _inputs.CloseLine(a, d, t))
            {
                _stage[d].Next[a] = (int)DesignStage.InService;
                Set(_lineE, d, a, Fx6.Zero);
            }
        }
    }

    // Defects (contract §4.4, spec §3.3): fixes by the team granted from t−1 (fix_hours each), then detection by cumulative
    // hours T += test_rate (О, С) + k·fleet_hours. defects_hidden falls at detection; found-but-unfixed stay open.
    private void Defects(int chunk, int a, long t)
    {
        for (int d = 0; d < _d; d++)
        {
            var stage = (DesignStage)_stage[d].Next[a];
            if (stage is not (DesignStage.Prototype or DesignStage.Series or DesignStage.InService))
                continue;
            Fx6 d0 = Get(_d0, d, a);
            if (d0 <= Fx6.Zero)
                continue;
            Fx6 open = Get(_open, d, a);
            Fx6 granted = Fx6.FromInt(_inputs.Granted(a, ProductsLayout.EngineerCategory, _l.FixRequester(d), t));
            Fx6 fixedNow = granted * _l.HPm / _p.FixHours;
            open -= fixedNow < open ? fixedNow : open;

            Fx6 test = stage == DesignStage.InService ? Fx6.Zero : _p.TestRate;
            Fx6 hours = Get(_hours, d, a) + test + _p.KFleet * (Units(a, d) * _p.FleetHoursPerUnit);
            Fx6 found = ProductsMath.DetectedTotal(hours, d0, _p.Lambda, _p.BetaQ10);
            Fx6 fresh = found - Get(_found, d, a);
            if (fresh < Fx6.Zero)
                throw new ProductsInvariantException(t, a, $"detected defects of design {d} fell");
            open += fresh;
            Set(_hours, d, a, hours);
            Set(_found, d, a, found);
            Set(_hidden, d, a, d0 - found);
            Set(_open, d, a, open);

            long team = ProductsMath.People(open * _p.FixHours, _l.HPm);
            long cap = _l.BureauEngineers[ProductsLayout.GenOf(d) - 1];
            if (team > 0)
                _mailbox.ReserveWorkforce(chunk, new WorkforceRequest(a, ProductsLayout.EngineerCategory, _l.FixRequester(d), team < cap ? team : cap));
        }
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

    private CostCard Card(int gen, Fx6 baseSum, Fx6 unitCostMult) =>
        new(_l.HFirst[gen - 1], _l.HFloor[gen - 1], _l.WageKh, _l.MaterialsFloor[gen - 1],
            ProductsMath.ProcessPart(baseSum, unitCostMult, _l.ProcScale[gen - 1]), _p.LearningQ10);

    private Fx6 UpkeepNext(int a, int d)
    {
        int g = ProductsLayout.GenOf(d);
        int[] prc = PrcNext(a, d);
        Fx6 floor = ProductsMath.UnitCostFloor(_l.MaterialsFloor[g - 1], _l.HFloor[g - 1], _l.WageKh,
            DesignRules.BaseCostSum(_l, prc), _l.ProcScale[g - 1]);
        return ProductsMath.Upkeep(_p.UpkeepShare, floor, DesignRules.Complexity(_l, prc), _p.UpkeepQ10, _l.Maintainability);
    }

    private int[] PrcNext(int a, int d)
    {
        Span<int> alts = stackalloc int[_s];
        for (int s = 0; s < _s; s++)
            alts[s] = _alt[d * _s + s].Next[a];
        return DesignRules.PrcSet(_l, alts);
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
}
