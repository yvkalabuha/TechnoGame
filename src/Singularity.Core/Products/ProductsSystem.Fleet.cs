using Singularity.Kernel;

namespace Singularity.Core.Products;

/// <summary>What bound the output of a line in step 7 (contract r6 §3.1 <c>line_bind</c>, spec A42.6).</summary>
public enum LineBind
{
    /// <summary>The project has no line (not in С) or the line is not admitted.</summary>
    None = 0,
    /// <summary>The rate asked.</summary>
    Rate = 1,
    /// <summary>The alloy supply.</summary>
    Alloy = 2,
    /// <summary>Money: <c>(left + tb)/unit_cost</c> strictly the least of the three.</summary>
    Money = 3,
}

/// <summary>The key of the leader metric (contract §9.1): <c>K = (G, T, N, P)</c>, compared lexicographically.</summary>
public readonly record struct TechKey(int G, int T, int N, Fx6 P) : IComparable<TechKey>
{
    public int CompareTo(TechKey other)
    {
        if (G != other.G)
            return G.CompareTo(other.G);
        if (T != other.T)
            return T.CompareTo(other.T);
        if (N != other.N)
            return N.CompareTo(other.N);
        return P.CompareTo(other.P);
    }
}

/// <summary>Word of <c>u = Σ upkeep / budget</c> (STATE_WORDS §4, without the «недоплачено» override).</summary>
public enum UpkeepWord
{
    Light,
    Noticeable,
    Heavy,
    EatsProcurement,
}

/// <summary>
/// Forecast of a fleet command (spec A42.7): a pure function of the snapshot. Fields about money are per month of the next step.
/// </summary>
/// <param name="UpkeepDelta">Fall of the monthly upkeep, <c>n·upkeep</c>.</param>
/// <param name="UBefore">Upkeep share of the procurement base now.</param>
/// <param name="UAfter">The same after the command.</param>
/// <param name="WordAfter">Word of <paramref name="UAfter"/>.</param>
/// <param name="LineBind">What bound the line of the actor in the last month (the freed money is spent only if <see cref="LineBind.Money"/>).</param>
/// <param name="FreedMoneyIsSpent">False when the line is not bound by money: the freed money or the revenue will not be spent (#118).</param>
/// <param name="FieldedBefore">The design is <c>fielded</c> now.</param>
/// <param name="FieldedAfter">...and after the command.</param>
/// <param name="InDefK">The design is in <c>Def(K)</c> now.</param>
/// <param name="BelowKeep">The design is in <c>Def(K)</c> and the command leaves fewer than <c>keep</c> units (a warning; the command is allowed).</param>
/// <param name="OldestRemaining">Months of service left of the oldest cohort that would go first, 0 without a fleet.</param>
/// <param name="Sale">The sale quote; null for a retirement.</param>
public readonly record struct FleetForecast(Fx6 UpkeepDelta, Fx6 UBefore, Fx6 UAfter, UpkeepWord WordAfter, LineBind LineBind, bool FreedMoneyIsSpent,
    bool FieldedBefore, bool FieldedAfter, bool InDefK, bool BelowKeep, int OldestRemaining, SaleQuote? Sale);

// Fleet part of the product layer (contract r6 §4.4f, §9.1; spec A42): the commands retire_cohort and sell_cohort, the policy year, the
// leader key as pure functions of the snapshot, and the forecasts.
public sealed partial class ProductsSystem
{
    // ---- reads of the fleet columns (snapshot)

    /// <summary>Units taken off by <c>retire_cohort</c> so far, dust included (contract r6 §3.1).</summary>
    public Fx6 Retired(int actor, int design) => Fx6.FromRaw(_retired[design].Current[actor]);

    /// <summary>Units sold by <c>sell_cohort</c> so far, dust included.</summary>
    public Fx6 Sold(int actor, int design) => Fx6.FromRaw(_sold[design].Current[actor]);

    /// <summary><c>⌊Σcount⌋</c> at the start of the policy year.</summary>
    public int QuotaBase(int actor, int design) => _quotaBase[design].Current[actor];

    /// <summary>Whole units sold since the start of the policy year.</summary>
    public int SoldInYear(int actor, int design) => _soldY[design].Current[actor];

    /// <summary>What bound the line of the design in the last month (spec A42.6).</summary>
    public LineBind Bind(int actor, int design) => (LineBind)_lineBind[design].Current[actor];

    /// <summary>Sale revenue of the last month step.</summary>
    public Fx6 SaleIncome(int actor) => Fx6.FromRaw(_saleIncome.Current[actor]);

    /// <summary>Revenue ceiling of the policy year (<c>⌊χ·12·B_base⌋</c>).</summary>
    public Fx6 SaleCap(int actor) => Fx6.FromRaw(_saleCap.Current[actor]);

    /// <summary>Revenue since the start of the policy year.</summary>
    public Fx6 SaleInYear(int actor) => Fx6.FromRaw(_saleY.Current[actor]);

    /// <summary><c>ext_total</c>: licence flows plus sale revenue since t0 (contract r6 §3.1); a check column.</summary>
    public Fx6 ExternalTotal(int actor) => Fx6.FromRaw(_extTotal.Current[actor]);

    /// <summary>Cohort counts of a design by calendar year (index 0 = the first year of the layout) in the snapshot.</summary>
    public Fx6[] Cohorts(int actor, int design)
    {
        var counts = new Fx6[_y];
        for (int y = 0; y < _y; y++)
            counts[y] = Fx6.FromRaw(_cohort[design * _y + y].Current[actor]);
        return counts;
    }

    // ---- leader key as a function of the snapshot (contract §9.1; one implementation for the metric, the validator and the AI)

    /// <summary>Process <paramref name="processNumber"/> is mastered by the actor in the snapshot (<c>mastered_run ≥ 6</c> at a site).</summary>
    public bool Mastered(int actor, int processNumber, long month) => _inputs.Mastered(actor, processNumber, month);

    /// <summary>
    /// <c>fielded(a, t)</c> for a design: at least <c>n_service_min</c> whole units in service and every process of its <c>prc_set</c>
    /// mastered (contract §9.1). A design without a project is not fielded.
    /// </summary>
    public bool Fielded(int actor, int design, long month)
    {
        if (Stage(actor, design) == DesignStage.None)
            return false;
        Span<Fx6> counts = stackalloc Fx6[_y];
        for (int y = 0; y < _y; y++)
            counts[y] = Fx6.FromRaw(_cohort[design * _y + y].Current[actor]);
        if (FleetRules.Whole(counts) < _p.NServiceMin)
            return false;
        foreach (int p in PrcSet(actor, design))
        {
            if (!_inputs.Mastered(actor, p, month))
                return false;
        }
        return true;
    }

    /// <summary><c>G(a, t)</c>: the largest <c>gen(d)</c> over the fielded designs, 0 without any.</summary>
    public int FieldedGen(int actor, long month)
    {
        int g = 0;
        for (int d = 0; d < _d; d++)
        {
            if (Fielded(actor, d, month))
                g = Math.Max(g, Gen(actor, d));
        }
        return g;
    }

    /// <summary><c>K(a, t) = (G, T, N, P)</c> (contract §9.1): <c>T</c>, <c>N</c> over the processes of the class that the actor has mastered.</summary>
    public TechKey KeyOf(int actor, long month)
    {
        int g = 0;
        Fx6 perf = Fx6.Zero;
        for (int d = 0; d < _d; d++)
        {
            if (!Fielded(actor, d, month))
                continue;
            g = Math.Max(g, Gen(actor, d));
            perf = Max(perf, Perf(actor, d));
        }
        int tier = 0, count = 0;
        for (int k = 0; k < _l.ProcNumber.Length; k++)
        {
            if (!_inputs.Mastered(actor, _l.ProcNumber[k], month))
                continue;
            if (_l.ProcTier[k] > tier)
            {
                tier = _l.ProcTier[k];
                count = 1;
            }
            else if (_l.ProcTier[k] == tier)
            {
                count++;
            }
        }
        return new TechKey(g, tier, count, perf);
    }

    /// <summary><c>G_front(t) = max_a G(a, t)</c> over all actors (#146; contract §9.1); 0 if nobody has a fielded design. Not a column.</summary>
    public int GFront(long month)
    {
        int g = 0;
        for (int a = 0; a < _l.SiteCount; a++)
            g = Math.Max(g, FieldedGen(a, month));
        return g;
    }

    /// <summary>
    /// <c>Def(K)</c> of the actor (spec A42.3): the fielded designs with <c>gen(d) = G</c> or <c>perf(d) = P</c> (every design that reaches
    /// the value). <c>Def_T = Def_N = ∅</c>. One flag per design slot.
    /// </summary>
    public bool[] DefK(int actor, long month)
    {
        var def = new bool[_d];
        int g = 0;
        Fx6 perf = Fx6.Zero;
        var fielded = new bool[_d];
        for (int d = 0; d < _d; d++)
        {
            fielded[d] = Fielded(actor, d, month);
            if (!fielded[d])
                continue;
            g = Math.Max(g, Gen(actor, d));
            perf = Max(perf, Perf(actor, d));
        }
        for (int d = 0; d < _d; d++)
            def[d] = fielded[d] && (Gen(actor, d) == g || Perf(actor, d) == perf);
        return def;
    }

    /// <summary><c>keep_d</c> (spec A42.3): <c>n_service_min</c> for a design in <c>Def(K)</c>, else 0.</summary>
    public int Keep(bool inDefK) => inDefK ? _p.NServiceMin : 0;

    /// <summary>Word of <c>u</c> (STATE_WORDS §4).</summary>
    public static UpkeepWord WordOf(Fx6 u) =>
        u < Fx6.FromRaw(250_000) ? UpkeepWord.Light : u < Fx6.FromRaw(500_000) ? UpkeepWord.Noticeable : u < Fx6.FromRaw(800_000) ? UpkeepWord.Heavy : UpkeepWord.EatsProcurement;

    // ---- SaleQuote and forecasts on the snapshot

    /// <summary>
    /// <c>SaleQuote</c> of selling <paramref name="n"/> units of a stage В design of the snapshot, executed at <paramref name="month"/>
    /// (contract r6 §4.4f): the policy-year columns are those of the snapshot, so use it for the month the next step handles.
    /// </summary>
    public SaleQuote Quote(int actor, int design, long n, long month)
    {
        int gap = GFront(month - 1) - Gen(actor, design);
        return FleetRules.Quote(Cohorts(actor, design), n, month, _m0, UnitCostFloor(actor, design), gap, _p, QuotaBase(actor, design),
            SoldInYear(actor, design), SaleInYear(actor), SaleCap(actor));
    }

    /// <summary>Forecast of <c>retire_cohort(design, n)</c> (spec A42.7) for the month <paramref name="month"/> the next step handles.</summary>
    public FleetForecast RetireForecast(int actor, int design, long n, long month) => Forecast(actor, design, n, month, sell: false);

    /// <summary>Forecast of <c>sell_cohort(design, n)</c>, with the quote (revenue by base · generation · age, quota and ceiling left, the refusal).</summary>
    public FleetForecast SellForecast(int actor, int design, long n, long month) => Forecast(actor, design, n, month, sell: true);

    private FleetForecast Forecast(int actor, int design, long n, long month, bool sell)
    {
        long snapshot = month - 1;
        Fx6[] counts = Cohorts(actor, design);
        Fx6 upkeep = Upkeep(actor, design);
        Fx6 need = Fx6.Zero;
        int line = -1;
        for (int d = 0; d < _d; d++)
        {
            need += InService(actor, d) * Upkeep(actor, d);
            if (Stage(actor, d) == DesignStage.Series)
                line = d;
        }
        Fx6 basis = _inputs.ProcurementBase(actor, month);
        Fx6 u0 = basis > Fx6.Zero ? need / basis : Fx6.Zero;
        Fx6 delta = Fx6.FromInt(n) * upkeep;
        Fx6 u1 = basis > Fx6.Zero ? (need - delta) / basis : Fx6.Zero;
        LineBind bind = line >= 0 ? Bind(actor, line) : LineBind.None;
        bool wasFielded = Fielded(actor, design, snapshot);
        bool inDef = DefK(actor, snapshot)[design];
        Span<Fx6> work = stackalloc Fx6[_y];
        counts.CopyTo(work);
        Span<Fx6> taken = stackalloc Fx6[_y];
        FleetRules.Take(work, Fx6.FromInt(n), taken);
        FleetRules.SweepDust(work);
        long left = FleetRules.Whole(work);
        bool stillFielded = wasFielded && left >= _p.NServiceMin;
        int oldest = 0;
        for (int y = 0; y < _y; y++)
        {
            if (taken[y] > Fx6.Zero)
            {
                oldest = (int)Math.Max(0, _p.ServiceLife - (month - _m0[y]));
                break;
            }
        }
        SaleQuote? quote = sell ? Quote(actor, design, n, month) : null;
        return new FleetForecast(delta, u0, u1, WordOf(u1), bind, bind == LineBind.Money, wasFielded, stillFielded, inDef,
            inDef && left < _p.NServiceMin, oldest, quote);
    }

    // ---- compute: steps 2a, 2d, 2e of the contract (r6 §4.4d)

    // Step 2a: the sale income of the month starts at 0; at the start of a policy year (t mod 12 = 0, after step 0) the quota base, the
    // sold-in-year counters and the ceiling are set (contract r6 §4.4f).
    private void BeginFleetMonth(int a, long t)
    {
        _saleIncome.Next[a] = 0;
        if (t % 12 != 0)
            return;
        for (int d = 0; d < _d; d++)
        {
            _quotaBase[d].Next[a] = checked((int)Whole(a, d));
            _soldY[d].Next[a] = 0;
        }
        _saleCap.Next[a] = FleetRules.SaleCap(_p.SaleChi, _inputs.ProcurementBase(a, t)).Raw;
        _saleY.Next[a] = 0;
    }

    private long Whole(int a, int d) => Units(a, d).Raw / Fx6.Scale;

    // Steps 2d–2e: all sell_cohort of the actor, then all retire_cohort, each type in command order, on the live (Next) state. A refused command
    // changes nothing and leaves its slot free; a second command of the same type for a project in the month is refused with SLOT.
    private void ApplyFleetCommands(int a, long t)
    {
        IReadOnlyList<FleetCommand> commands = _inputs.FleetCommands(a, t);
        if (commands.Count == 0)
            return;
        FleetBook book = NewBook(a, next: true);
        book.SaleY = Fx6.FromRaw(_saleY.Next[a]);
        book.SaleCap = Fx6.FromRaw(_saleCap.Next[a]);
        // G_front is a function of the snapshot (contract r6 §4.4f): the same value for every sale of the month.
        int gFront = commands.Any(c => c.Kind == FleetCommandKind.Sell) ? GFront(t - 1) : 0;
        foreach (FleetCommandKind kind in (FleetCommandKind[])[FleetCommandKind.Sell, FleetCommandKind.Retire])
        {
            foreach (FleetCommand c in commands)
            {
                if (c.Kind != kind)
                    continue;
                CommandRejection? rejected = kind == FleetCommandKind.Sell
                    ? book.Sell(c.Design, c.N, t, gFront)
                    : book.Retire(c.Design, c.N);
                if (rejected is { } reason)
                    Reject(a, reason);
            }
        }
        for (int d = 0; d < _d; d++)
        {
            StoreNext(a, d, book.Counts[d]);
            Set(_retired, d, a, book.Retired[d]);
            Set(_sold, d, a, book.Sold[d]);
            _soldY[d].Next[a] = book.SoldY[d];
            // Contract r6 §4.4d step 3: no fleet, no phantom accidents.
            if (Units(a, d) == Fx6.Zero)
                Set(_lossAcc, d, a, Fx6.Zero);
        }
        _saleY.Next[a] = book.SaleY.Raw;
        _saleIncome.Next[a] = book.SaleIncome.Raw;
    }

    // A book of the fleet of the actor: from the live state of the step (Next) or from the snapshot (Current, with the facts the AI reads).
    private FleetBook NewBook(int a, bool next)
    {
        var book = new FleetBook(_p, _m0, _d);
        for (int d = 0; d < _d; d++)
        {
            for (int y = 0; y < _y; y++)
                book.Counts[d][y] = Fx6.FromRaw((next ? _cohort[d * _y + y].Next[a] : _cohort[d * _y + y].Current[a]));
            book.Stage[d] = (DesignStage)(next ? _stage[d].Next[a] : _stage[d].Current[a]);
            book.Retired[d] = Fx6.FromRaw(next ? _retired[d].Next[a] : _retired[d].Current[a]);
            book.Sold[d] = Fx6.FromRaw(next ? _sold[d].Next[a] : _sold[d].Current[a]);
            book.QuotaBase[d] = next ? _quotaBase[d].Next[a] : _quotaBase[d].Current[a];
            book.SoldY[d] = next ? _soldY[d].Next[a] : _soldY[d].Current[a];
            if (book.Stage[d] == DesignStage.None)
                continue;
            int[] alts = next ? AltsNext(a, d) : Alternatives(a, d);
            book.Gen[d] = DesignRules.Gen(_l, next ? _gp[d].Next[a] : _gp[d].Current[a], alts);
            book.UnitCostFloor[d] = FloorOf(alts);
            if (!next)
            {
                book.Perf[d] = DesignRules.Perf(_l, alts);
                book.Upkeep[d] = UpkeepOf(alts);
                book.Bind[d] = Bind(a, d);
                book.Excess[d] = ExcessDefects(a, d) > Fx6.Zero;
            }
        }
        book.SaleY = Fx6.FromRaw(next ? _saleY.Next[a] : _saleY.Current[a]);
        book.SaleCap = Fx6.FromRaw(next ? _saleCap.Next[a] : _saleCap.Current[a]);
        return book;
    }

    /// <summary>
    /// The fleet of the actor as the snapshot holds it, for the AI (spec A42.4): cohorts, stages, <c>gen</c>, <c>perf</c>, upkeep, price floor,
    /// <c>line_bind</c>, the policy-year counters and <c>Def(K)</c>. The caller projects it to the month of execution.
    /// </summary>
    public FleetBook SnapshotBook(int actor, long month)
    {
        FleetBook book = NewBook(actor, next: false);
        bool[] def = DefK(actor, month - 1);
        for (int d = 0; d < _d; d++)
            book.InDefK[d] = def[d];
        return book;
    }

    private void StoreNext(int a, int d, ReadOnlySpan<Fx6> counts)
    {
        for (int y = 0; y < _y; y++)
            _cohort[d * _y + y].Next[a] = counts[y].Raw;
    }
}
