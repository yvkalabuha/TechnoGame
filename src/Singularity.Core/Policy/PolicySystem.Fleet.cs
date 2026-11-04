using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Core.Policy;

/// <summary>
/// The fleet rule of the AI (spec A42.4, contract r6 §4.4f «Сторона ИИ»): <c>retire_cohort</c> and <c>sell_cohort</c> chosen by the
/// upkeep share of the procurement base, on a projection of the state in which the commands will run. Decisions of month t are written to
/// <c>pol.flt.*</c> with the stamp of month t+1 and run in the step 2 of t+1. No RNG.
/// </summary>
public sealed partial class PolicySystem
{
    private IntColumn[] _fltStamp = [], _fltKind = [], _fltDesign = [], _fltN = [];
    private LongColumn _incExp = null!;
    private LongColumn _nSell = null!, _nRetire = null!, _armed = null!, _silentBind = null!;
    private LongColumn[] _pair = [];

    private void AddFleetColumns(Table table, int designs, int generations)
    {
        int slots = 2 * designs;
        _fltStamp = new IntColumn[slots];
        _fltKind = new IntColumn[slots];
        _fltDesign = new IntColumn[slots];
        _fltN = new IntColumn[slots];
        for (int k = 0; k < slots; k++)
        {
            _fltStamp[k] = table.AddInt($"pol.flt.{k}.stamp");
            _fltKind[k] = table.AddInt($"pol.flt.{k}.kind");
            _fltDesign[k] = table.AddInt($"pol.flt.{k}.design");
            _fltN[k] = table.AddInt($"pol.flt.{k}.n");
        }
        _incExp = table.AddLong("pol.flt.incExp");
        // Counters of the print (spec §6.10): units sold / retired, months the rule was armed (u ≥ U_HI with a line), months it was silent
        // because the line was not bound by money, and the commands by (gen(L), gen(d)).
        _nSell = table.AddLong("pol.flt.nSell");
        _nRetire = table.AddLong("pol.flt.nRetire");
        _armed = table.AddLong("pol.flt.armed");
        _silentBind = table.AddLong("pol.flt.silentBind");
        _pair = new LongColumn[generations * generations];
        for (int gl = 0; gl < generations; gl++)
        {
            for (int gd = 0; gd < generations; gd++)
                _pair[gl * generations + gd] = table.AddLong($"pol.flt.pair.{gl + 1}.{gd + 1}");
        }
    }

    /// <summary>The AI's <c>sell_cohort</c>/<c>retire_cohort</c> consumed at <paramref name="month"/>, in the order they were decided.</summary>
    public IReadOnlyList<FleetCommand> AiFleetCommands(int actor, long month)
    {
        var list = new List<FleetCommand>();
        for (int k = 0; k < _fltStamp.Length; k++)
        {
            if (_fltStamp[k].Current[actor] == Stamp(month))
                list.Add(new FleetCommand((FleetCommandKind)_fltKind[k].Current[actor], _fltDesign[k].Current[actor], _fltN[k].Current[actor]));
        }
        return list;
    }

    /// <summary><c>inc_exp</c>: the sale revenue the decisions of the previous month promise for the month being computed (FL7); 0 without sales.</summary>
    public Fx6 ExpectedSaleIncome(int actor) => _incExp.Current[actor] == 0 ? Fx6.Zero : Fx6.FromRaw(_incExp.Current[actor]);

    /// <summary>Units sold by the AI's commands so far.</summary>
    public long FleetUnitsSold(int actor) => _nSell.Current[actor];

    /// <summary>Units retired by the AI's commands so far.</summary>
    public long FleetUnitsRetired(int actor) => _nRetire.Current[actor];

    /// <summary>Months with <c>u ≥ U_HI</c> and a line (the rule was armed).</summary>
    public long FleetArmedMonths(int actor) => _armed.Current[actor];

    /// <summary>Armed months the rule kept silent because <c>line_bind</c> was not <c>MONEY</c>.</summary>
    public long FleetSilentByBindMonths(int actor) => _silentBind.Current[actor];

    /// <summary>AI commands by the pair (<c>gen(L)</c>, <c>gen(d)</c>), generations from 1.</summary>
    public long FleetCommandsByPair(int actor, int lineGen, int designGen) => _pair[(lineGen - 1) * _gens + designGen - 1].Current[actor];

    /// <summary><c>U_LO</c> of the actor's profile.</summary>
    public Fx6 FleetULo(int actor) => _pp.FleetULo(Profile(actor));

    /// <summary>
    /// The rate of an AI line (spec A41, A42.5): <c>⌊(B_base + inc_exp)/unit_cost_floor⌋</c>, <paramref name="baseBudget"/> already including the
    /// training money. Under <c>PURCHASE_CAP</c> it is capped by the room under <c>U_LO·B_base</c>: <c>⌊(U_LO·B_base − need)/upkeep_L⌋</c>.
    /// </summary>
    public Fx6 AiLineRate(int actor, Fx6 baseBudget, Fx6 baseProcurement, Fx6 unitCostFloor, Fx6 need, Fx6 upkeepOfLine)
    {
        Fx6 rate = PolicyRules.LineRate(baseBudget + ExpectedSaleIncome(actor), unitCostFloor);
        if (_pp.FleetArm != FleetArm.PurchaseCap)
            return rate;
        Fx6 room = FleetULo(actor) * baseProcurement - need;
        if (upkeepOfLine <= Fx6.Zero)
            return rate;   // a line without upkeep leaves no room to cap
        // ⌊room/upkeep_L⌋ in whole units (spec §6.10).
        Fx6 cap = room > Fx6.Zero ? Fx6.FromInt(room.Raw / upkeepOfLine.Raw) : Fx6.Zero;
        return rate < cap ? rate : cap;
    }

    // ---- decision

    private sealed class FleetDecision
    {
        public readonly List<FleetCommand> Commands = [];
        public Fx6 IncomeExpected;
    }

    /// <summary>
    /// A42.4 for the month t compute. The book is the snapshot (state after t−1) projected to the step 2 of t+1: step 0 of t, the year reset of t,
    /// the AI's own commands decided in t−1 (they run in t), step 0 of t+1, the year reset of t+1 (contract r6 §4.4f (а)–(в)).
    /// </summary>
    private FleetDecision DecideFleet(int row, long t, PolicyDesign[] designs, Decisions dec, BudgetShares sharesNext, Fx6 budgetNext)
    {
        var result = new FleetDecision();
        if (_pp.FleetArm is FleetArm.NoRetire or FleetArm.PurchaseCap || _prod!.Fleet(row, t) is not { } book)
            return result;
        int gFront = _prod.GFront(t);
        book.AgeOut(t);
        // B_base of the year reset comes from the AI's own shares (Shares(row)), not from a player's set_budget_shares override: for an actor
        // driven by the player with the AI on, the ceiling may differ from the one the layer sets (deviation 11 of TASK-0028).
        if (t % 12 == 0)
            book.ResetYear(Shares(row).Procurement * Budget(row));
        book.NewMonth();
        IReadOnlyList<FleetCommand> pending = AiFleetCommands(row, t);
        foreach (FleetCommandKind kind in (FleetCommandKind[])[FleetCommandKind.Sell, FleetCommandKind.Retire])
        {
            foreach (FleetCommand c in pending)
            {
                if (c.Kind == kind)
                    _ = kind == FleetCommandKind.Sell ? book.Sell(c.Design, c.N, t, gFront) : book.Retire(c.Design, c.N);
            }
        }
        book.AgeOut(t + 1);
        Fx6 baseNext = sharesNext.Procurement * budgetNext;
        if ((t + 1) % 12 == 0)
            book.ResetYear(baseNext);
        book.NewMonth();

        // The line L of t+1: the lines of the snapshot with the closes and starts decided in t−1 (they run in t) and in this compute.
        var series = new bool[_designs];
        for (int d = 0; d < _designs; d++)
            series[d] = designs[d].Stage == DesignStage.Series;
        for (int d = 0; d < _designs; d++)
        {
            if (_closeStamp[d].Current[row] == Stamp(t))
                series[d] = false;
            if (_startStamp[d].Current[row] == Stamp(t))
                series[d] = true;
        }
        foreach (int d in dec.Closes)
            series[d] = false;
        foreach ((int d, _) in dec.Starts)
            series[d] = true;
        // A project that is, or is about to be, a line is never a candidate: the stage on the book is С. One that has just closed is left out for this month.
        int line = -1;
        for (int d = 0; d < _designs; d++)
        {
            bool closing = designs[d].Stage == DesignStage.Series && !series[d];
            if (series[d] || closing)
                book.Stage[d] = DesignStage.Series;
            if (series[d])
                line = d;
        }
        if (line < 0)
            return result;

        var profile = (PolicyProfile)_profile.Current[row];
        Fx6 need = book.Need();
        Fx6 u = baseNext > Fx6.Zero ? need / baseNext : Fx6.Zero;
        if (u < _pp.FleetUHi[(int)profile])
            return result;
        _armed.Next[row] = _armed.Current[row] + 1;
        if (book.Bind[line] != LineBind.Money)
        {
            _silentBind.Next[row] = _silentBind.Current[row] + 1;
            return result;
        }
        Fx6 excess = need - FleetULo(row) * baseNext;
        var candidates = new List<int>();
        for (int d = 0; d < _designs; d++)
        {
            if (d != line && book.Stage[d] == DesignStage.InService && !book.Excess[d] && LineCondition(book, line, d))
                candidates.Add(d);
        }
        // κ ascending: perf_d·upkeep_e < perf_e·upkeep_d puts d first; a tie goes to the lower design_id (List.Sort is not stable, so the id is the key).
        candidates.Sort((d, e) =>
        {
            int c = ((Int128)book.Perf[d].Raw * book.Upkeep[e].Raw).CompareTo((Int128)book.Perf[e].Raw * book.Upkeep[d].Raw);
            return c != 0 ? c : d.CompareTo(e);
        });
        int genLine = book.Gen[line];
        foreach (int d in candidates)
        {
            if (excess <= Fx6.Zero)
                break;
            long avail = book.Aged(d, t + 1, _pp.FleetAProt);
            long keep = book.InDefK[d] ? book.Parameters.NServiceMin : 0;
            long wanted = (excess.Raw + book.Upkeep[d].Raw - 1) / book.Upkeep[d].Raw;
            long n = Math.Min(Math.Min(avail, book.Whole(d) - keep), wanted);
            if (n < 1)
                continue;
            long s = _pp.FleetArm == FleetArm.NoSale ? 0 : book.SaleMax(d, n, t + 1, gFront);
            if (s >= 1)
            {
                CommandRejection? bad = book.Sell(d, s, t + 1, gFront, out Fx6 income);
                if (bad is not null)
                    throw new PolicyInvariantException(t, row, $"the AI's sell_cohort of design {d} would be refused: {bad}");
                result.Commands.Add(new FleetCommand(FleetCommandKind.Sell, d, s));
                result.IncomeExpected += income;
            }
            if (n - s >= 1)
            {
                CommandRejection? bad = book.Retire(d, n - s);
                if (bad is not null)
                    throw new PolicyInvariantException(t, row, $"the AI's retire_cohort of design {d} would be refused: {bad}");
                result.Commands.Add(new FleetCommand(FleetCommandKind.Retire, d, n - s));
            }
            excess -= Fx6.FromInt(n) * book.Upkeep[d];
            CountPair(row, genLine, book.Gen[d], s >= 1 ? 1 : 0, n - s >= 1 ? 1 : 0, s, n - s);
        }
        return result;
    }

    // FLEET_LINE_COND (spec A42.4 п. 4).
    private bool LineCondition(FleetBook book, int line, int d) => _pp.FleetLineCond switch
    {
        FleetLineCond.Gen => book.Gen[line] > book.Gen[d],
        FleetLineCond.Kappa => (Int128)book.Perf[line].Raw * book.Upkeep[d].Raw > (Int128)book.Perf[d].Raw * book.Upkeep[line].Raw,
        _ => true,
    };

    private void CountPair(int row, int genLine, int genDesign, int sellCmds, int retireCmds, long sold, long retired)
    {
        if (genLine >= 1 && genLine <= _gens && genDesign >= 1 && genDesign <= _gens)
        {
            LongColumn pair = _pair[(genLine - 1) * _gens + genDesign - 1];
            pair.Next[row] += sellCmds + retireCmds;
        }
        _nSell.Next[row] += sold;
        _nRetire.Next[row] += retired;
    }
}
