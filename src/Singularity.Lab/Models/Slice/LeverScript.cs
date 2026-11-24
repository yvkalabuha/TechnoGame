using System.Collections.Immutable;
using System.Globalization;
using Singularity.Core.Commands;
using Singularity.Core.Knowledge;
using Singularity.Core.Policy;
using Singularity.Kernel;
using Singularity.Session;

namespace Singularity.Lab.Models.Slice;

/// <summary>A visible budget item of the player levers probe P1 (PLAYER_LEVERS_WAVE1.md A2): Quality has no reader and is never shifted.</summary>
public enum LeverItem
{
    Research = 0,
    Capability = 1,
    Effort = 2,
    Procurement = 3,
}

/// <summary>The P1 shift of one item: +10 p.p., −10 p.p., or to zero (docs/simulation/levers-probes.md §3).</summary>
public enum LeverOp
{
    Plus10 = 0,
    Minus10 = 1,
    Zero = 2,
}

/// <summary>
/// Where the Lab reserve of P2 is spent: kept (a), on procurement (b, proxy of licences), on research (c, proxy of the programme). The two
/// direct values are the no-reserve controls of the same reallocation: the slot is paid into the item every month from month 0, no account.
/// </summary>
public enum ReserveSpend
{
    None = 0,
    Procurement = 1,
    Research = 2,
    ProcurementDirect = 3,
    ResearchDirect = 4,
}

/// <summary>
/// The player levers of TASK-0148 as a scripted strategy of one actor (keys &lt;c&gt;LEVER_ACTOR&lt;/c&gt;, &lt;c&gt;LEVER_SHIFT&lt;/c&gt;, &lt;c&gt;RESERVE&lt;/c&gt;,
/// &lt;c&gt;RESERVE_SPEND&lt;/c&gt;, &lt;c&gt;LEVER_DIAG&lt;/c&gt;). Null fields = the lever is off. Goals of P3 are not here: they go through &lt;c&gt;DISC_WEIGHT&lt;/c&gt;.
/// </summary>
public sealed record LeverSpec(string? Actor, LeverItem? Item, LeverOp Op, Fx6 Reserve, ReserveSpend Spend, bool Diag, bool Eta = false)
{
    /// <summary>The monthly keep factor of the reserve: 0,9^(1/12) = 0,99125… (10 % a year, A2), floored to Fx6.</summary>
    public static readonly Fx6 MonthlyKeep = Fx6.FromRaw(991_258);

    /// <summary>The reserve is spent from this month on (it accumulates for a year first, levers-probes.md §3).</summary>
    public const long SpendFromMonth = 12;

    /// <summary>Highest reserve share a manifest may ask for.</summary>
    public static readonly Fx6 ReserveMax = Fx6.FromRaw(900_000);

    public bool Active => Actor is not null || Diag || Eta;

    /// <summary>
    /// &lt;c&gt;lever_eta&lt;/c&gt; (LEVER_ETA, P3 forecast check): in every month t with t mod 12 = 0, after the step of t, one row per actor and frontier
    /// node: the months the node needs at the speed of the next month, &lt;c&gt;⌈(need − prog)/rate⌉&lt;/c&gt; (as &lt;c&gt;WhyNotAnalyzer&lt;/c&gt; computes &lt;c&gt;eta&lt;/c&gt;),
    /// empty when the rate is 0 or a hazard node already stands at its threshold (the discovery is then a draw).
    /// </summary>
    public static readonly MetricTableSpec EtaTable = new("lever_eta", ["t", "actor", "node", "hazard", "prog", "need", "rate", "eta"]);

    /// <summary>&lt;c&gt;R+10&lt;/c&gt;, &lt;c&gt;C-10&lt;/c&gt;, &lt;c&gt;E0&lt;/c&gt;, …: item letter R/C/E/P, then +10, -10 or 0.</summary>
    public static (LeverItem Item, LeverOp Op) ParseShift(string v)
    {
        if (v.Length < 2)
            throw new ParamException($"LEVER_SHIFT '{v}' is not R/C/E/P followed by +10, -10 or 0.");
        LeverItem item = v[0] switch
        {
            'R' => LeverItem.Research,
            'C' => LeverItem.Capability,
            'E' => LeverItem.Effort,
            'P' => LeverItem.Procurement,
            _ => throw new ParamException($"LEVER_SHIFT '{v}': item must be R, C, E or P."),
        };
        LeverOp op = v[1..] switch
        {
            "+10" => LeverOp.Plus10,
            "-10" => LeverOp.Minus10,
            "0" => LeverOp.Zero,
            _ => throw new ParamException($"LEVER_SHIFT '{v}': the change must be +10, -10 or 0."),
        };
        return (item, op);
    }

    /// <summary>
    /// P1: the AI shares with one item shifted. +10 takes 0,10 (at most what the other three have) from the other three visible items in
    /// proportion to their shares; −10 gives min(0,10, item) and 0 gives the whole item to the other three in proportion. Every part is floored;
    /// the rounding residue goes to procurement, or to research when procurement is the shifted item. Quality is kept as it is.
    /// </summary>
    public static BudgetShares Shift(BudgetShares ai, LeverItem item, LeverOp op)
    {
        long[] v = [ai.Research.Raw, ai.Capability.Raw, ai.Effort.Raw, ai.Procurement.Raw];
        int i = (int)item;
        long others = v.Sum() - v[i];
        const long step = 100_000;
        long d = op switch
        {
            LeverOp.Plus10 => Math.Min(step, others),
            LeverOp.Minus10 => Math.Min(step, v[i]),
            _ => v[i],
        };
        if (others <= 0 && op != LeverOp.Plus10 && d > 0)
            throw new InvalidOperationException($"LEVER_SHIFT: the other items of {ai} are all zero, nothing receives the share of {item}.");
        long[] n = (long[])v.Clone();
        for (int j = 0; j < 4; j++)
        {
            if (j == i || others == 0)
                continue;
            long part = (long)((Int128)d * v[j] / others);
            n[j] = op == LeverOp.Plus10 ? v[j] - part : v[j] + part;
        }
        n[i] = op == LeverOp.Plus10 ? v[i] + d : v[i] - d;
        int sink = item == LeverItem.Procurement ? 0 : 3;
        n[sink] += Fx6.Scale - ai.Quality.Raw - n.Sum();
        return Valid(new BudgetShares(Fx6.FromRaw(n[0]), Fx6.FromRaw(n[1]), Fx6.FromRaw(n[2]), Fx6.FromRaw(n[3]), ai.Quality), "LEVER_SHIFT");
    }

    /// <summary>
    /// P2: the AI shares with the reserve withheld. Each visible item is floored to &lt;c&gt;item·(1 − r)&lt;/c&gt;; &lt;paramref name="spent"/&gt; (≤ r, the
    /// part of this month's budget paid out of the reserve) is added to the spend item; the rest — the AI quality share, the reserve and the
    /// rounding residue — sits in the Quality slot, which no system reads (A2), so that money is not used.
    /// </summary>
    public static bool IsDirect(ReserveSpend s) => s is ReserveSpend.ProcurementDirect or ReserveSpend.ResearchDirect;

    public static BudgetShares Withhold(BudgetShares ai, Fx6 reserve, ReserveSpend spend, Fx6 spent)
    {
        if (reserve < Fx6.Zero || reserve > ReserveMax || spent < Fx6.Zero || spent > reserve)
            throw new ArgumentOutOfRangeException(nameof(spent), $"reserve {reserve}, spent {spent}: need 0 ≤ spent ≤ reserve ≤ {ReserveMax}.");
        long keep = Fx6.Scale - reserve.Raw;
        long K(Fx6 x) => (long)((Int128)x.Raw * keep / Fx6.Scale);
        long r = K(ai.Research), c = K(ai.Capability), e = K(ai.Effort), p = K(ai.Procurement);
        if (spend is ReserveSpend.Research or ReserveSpend.ResearchDirect)
            r += spent.Raw;
        else if (spend is ReserveSpend.Procurement or ReserveSpend.ProcurementDirect)
            p += spent.Raw;
        else if (spent > Fx6.Zero)
            throw new ArgumentException("RESERVE_SPEND = NONE spends nothing.", nameof(spent));
        long q = Fx6.Scale - r - c - e - p;
        return Valid(new BudgetShares(Fx6.FromRaw(r), Fx6.FromRaw(c), Fx6.FromRaw(e), Fx6.FromRaw(p), Fx6.FromRaw(q)), "RESERVE");
    }

    private static BudgetShares Valid(BudgetShares s, string key) =>
        s.IsValid ? s : throw new InvalidOperationException($"{key}: computed shares {s} are not valid (non-negative, sum 1).");
}

/// <summary>
/// The Lab reserve account of P2 in Fx6 (levers-probes.md §3): each month &lt;c&gt;balance ← ⌊balance·keep⌋&lt;/c&gt; (leak), then
/// &lt;c&gt;+ ⌊r·budget⌋&lt;/c&gt;, then from month 12 on a payout of at most &lt;c&gt;r·budget&lt;/c&gt; (the share sum of the Core is exactly 1, so a month can not
/// spend more than the slot it withholds). Instance state of one run, never static.
/// </summary>
public sealed class ReserveLedger(Fx6 reserve, ReserveSpend spend)
{
    public long Balance { get; private set; }
    public long Accrued { get; private set; }
    public long Leaked { get; private set; }
    public long Spent { get; private set; }

    /// <summary>One month with budget &lt;paramref name="budget"/&gt;; returns the share of that budget paid out of the reserve (0 … r).</summary>
    public Fx6 Month(long month, Fx6 budget)
    {
        long kept = (Fx6.FromRaw(Balance) * LeverSpec.MonthlyKeep).Raw;
        Leaked += Balance - kept;
        long add = (reserve * budget).Raw;
        Balance = kept + add;
        Accrued += add;
        if (spend == ReserveSpend.None || month < LeverSpec.SpendFromMonth || budget <= Fx6.Zero || Balance <= 0)
            return Fx6.Zero;
        long money = Math.Min(Balance, add);
        // The share is floored, and the money actually paid is that share of the budget (floored), so the account never pays more than the slot.
        Fx6 share = Fx6.FromRaw((long)((Int128)money * Fx6.Scale / budget.Raw));
        if (share > reserve)
            share = reserve;
        long paid = (share * budget).Raw;
        Balance -= paid;
        Spent += paid;
        return share;
    }
}

/// <summary>
/// Issues the lever commands of &lt;see cref="LeverSpec"/&gt; for one actor (source Harness, id 1, so it never shares a local number with the FORCE
/// proxy, id 0) and reads the research allocation trace for the P3 calibration. Called on the owner thread between monthly ticks only; it
/// reads the world through Session getters (internal, as &lt;see cref="SupplyProbe"/&gt;) and writes nothing but commands. Buffers are per instance.
/// </summary>
internal sealed class LeverScript
{
    private const int SourceId = 1;
    private const long DiagSplit = 420; // 1980-09: the P3 calibration window 1945-1980

    private readonly SliceSession _s;
    private readonly LeverSpec _spec;
    private readonly IMetricSink? _sink;
    private readonly int _etaTable;
    private readonly int _actor = -1;
    private readonly ReserveLedger? _ledger;
    private readonly ImmutableArray<string> _ids;
    private readonly long[] _alloc80, _allocAll; // actor × discipline, raw
    private readonly int _disc;
    private int _local;
    private BudgetShares? _sent;

    public LeverScript(SliceSession s, LeverSpec spec, IMetricSink? sink = null, int etaTable = -1)
    {
        _s = s;
        _spec = spec;
        if (spec.Eta && (sink is null || etaTable < 0))
            throw new ArgumentException("LEVER_ETA needs the sink and the index of the lever_eta table.", nameof(sink));
        _sink = sink;
        _etaTable = etaTable;
        _ids = s.Layout.ActorIds;
        if (spec.Actor is { } id)
        {
            _actor = _ids.IndexOf(id);
            if (_actor < 0)
                throw new ParamException($"LEVER_ACTOR '{id}' is not an actor of the scenario ({string.Join(", ", _ids)}).");
        }
        if (spec.Reserve > Fx6.Zero && !LeverSpec.IsDirect(spec.Spend))
            _ledger = new ReserveLedger(spec.Reserve, spec.Spend);
        _disc = s.World.Graph.DisciplineNames.Length;
        _alloc80 = new long[_ids.Length * _disc];
        _allocAll = new long[_ids.Length * _disc];
    }

    public int Commands { get; private set; }

    /// <summary>Before month 0 and after the step of each month t: sets the shares the step of &lt;paramref name="nextMonth"/&gt; reads.</summary>
    public void Issue(long nextMonth)
    {
        if (_actor < 0)
            return;
        SliceWorld w = _s.World;
        BudgetShares ai = w.Policy.Shares(_actor);
        BudgetShares want = ai;
        if (_spec.Item is { } item)
            want = LeverSpec.Shift(ai, item, _spec.Op);
        if (_ledger is not null)
        {
            Fx6 spent = _ledger.Month(nextMonth, w.Policy.Budget(_actor));
            want = LeverSpec.Withhold(ai, _spec.Reserve, _spec.Spend, spent);
        }
        else if (LeverSpec.IsDirect(_spec.Spend))
        {
            want = LeverSpec.Withhold(ai, _spec.Reserve, _spec.Spend, _spec.Reserve);
        }
        if (_sent == want)
            return;
        CommandVerdict v = _s.Submit(SourceClass.Harness, SourceId, _local++, new SetBudgetShares(_actor, want));
        if (!v.Accepted)
            throw new InvalidOperationException($"the lever script was refused at month {nextMonth}: {v.Message}");
        _sent = want;
        Commands++;
    }

    /// <summary>After the monthly step of t: adds the research money each actor allocated to each discipline (KnowledgeSystem trace).</summary>
    public void Observe(long month)
    {
        if (_spec.Eta && month % 12 == 0)
            Forecast(month);
        if (!_spec.Diag)
            return;
        SliceWorld w = _s.World;
        ImmutableArray<int> disc = w.Graph.Discipline;
        for (int a = 0; a < _ids.Length; a++)
        {
            for (int r = 0; r < disc.Length; r++)
            {
                int d = disc[r];
                if (d < 0)
                    continue;
                long alloc = w.Knowledge.LastStep(a, r).Alloc.Raw;
                _allocAll[a * _disc + d] += alloc;
                if (month < DiagSplit)
                    _alloc80[a * _disc + d] += alloc;
            }
        }
    }

    // The forecast rows of lever_eta for the state after the step of month (Knowledge getters of the snapshot; the speed is that of month + 1).
    private void Forecast(long month)
    {
        SliceWorld w = _s.World;
        KnowledgeSystem k = w.Knowledge;
        ImmutableArray<int> numbers = w.Graph.Number;
        ImmutableArray<bool> hazard = w.Graph.IsHazard;
        for (int a = 0; a < _ids.Length; a++)
        {
            for (int r = 0; r < numbers.Length; r++)
            {
                if (!k.InFrontier(a, r))
                    continue;
                Fx6 prog = k.Prog(a, r), need = k.NeedOf(r), rate = k.ProgressRate(a, r, month + 1);
                string eta = "";
                if (rate > Fx6.Zero && !(hazard[r] && prog >= need))
                    eta = MetricFormat.Int(prog >= need ? 0 : (long)(((Int128)(need - prog).Raw + rate.Raw - 1) / rate.Raw));
                _sink!.Row(_etaTable, MetricFormat.Int(month), _ids[a], MetricFormat.Int(numbers[r]), hazard[r] ? "1" : "0",
                    MetricFormat.Fx6(prog.Raw), MetricFormat.Fx6(need.Raw), MetricFormat.Fx6(rate.Raw), eta);
            }
        }
    }

    /// <summary>model_info entries of the run (levers-probes.md §5): commands sent, the reserve account, licence money refusals, allocations.</summary>
    public IEnumerable<KeyValuePair<string, string>> Counters()
    {
        static string I(long v) => v.ToString(CultureInfo.InvariantCulture);
        if (_actor >= 0)
        {
            yield return new("lever_commands", I(Commands));
            yield return new("lever_licence_money_refusals", I(_s.World.Policy.BadLicenceCount(_actor, BadLicence.Money)));
        }
        if (_ledger is not null)
        {
            yield return new("reserve_accrued", I(_ledger.Accrued));
            yield return new("reserve_leaked", I(_ledger.Leaked));
            yield return new("reserve_spent", I(_ledger.Spent));
            yield return new("reserve_end", I(_ledger.Balance));
        }
        if (!_spec.Diag)
            yield break;
        ImmutableArray<string> names = _s.World.Graph.DisciplineNames;
        for (int a = 0; a < _ids.Length; a++)
        {
            for (int d = 0; d < _disc; d++)
            {
                yield return new($"alloc_1980_{_ids[a]}_{names[d]}", I(_alloc80[a * _disc + d]));
                yield return new($"alloc_all_{_ids[a]}_{names[d]}", I(_allocAll[a * _disc + d]));
            }
        }
    }
}
