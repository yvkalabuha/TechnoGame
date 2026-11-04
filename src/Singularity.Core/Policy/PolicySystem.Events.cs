using System.Collections.Immutable;
using Singularity.Core.Data;
using Singularity.Core.Events;
using Singularity.Core.Industry;
using Singularity.Core.Products;
using Singularity.Core.WhyNot;
using Singularity.Kernel;

namespace Singularity.Core.Policy;

/// <summary>
/// The events of T6d and the why_not-driven decisions of the AI (spec §3.5 A43; contract §3.1 r7, §5): licence state of the buyer, the yearly
/// offer and its acceptance, the payment through <c>licence_flow</c>, the alloy disruption, and the programme / licence / training-series
/// triggers read from <c>why_not</c>. Draws only the streams <c>EVT_LIC</c> and <c>EVT_DIS</c> (spec §9.1). A <c>take_licence</c> decided in
/// month t waits in the buyer's queue columns and is applied by <see cref="ApplyDeals"/> at the start of month t+1, after the commands of the player
/// (contract §5: the application is where the validator sees the shares in force, so a player's <c>set_budget_shares</c> of that month cannot make
/// the procurement budget negative); the session calls it before every monthly step. A deal touches the seller's row, so it is applied in buyer order.
/// </summary>
public sealed partial class PolicySystem
{
    private CounterRng? _events;
    private IPolicyEvents? _ev;
    private ImmutableArray<Fx6> _exposure;
    private IntColumn _licGen = null!, _licSeller = null!, _licStart = null!, _disStart = null!, _disEnd = null!, _qSeller = null!, _qGen = null!;
    private LongColumn _licMask = null!, _offerMask = null!, _licFlow = null!, _qMask = null!, _qPrice = null!;
    private LongColumn[] _bad = [];

    private void AddEventColumns(Table table, CounterRng? events, IPolicyEvents? inputs, ImmutableArray<Fx6> exposure, int rows)
    {
        _events = events;
        _ev = inputs;
        _exposure = exposure.IsDefault ? [.. Enumerable.Repeat(Fx6.One, rows)] : exposure;
        if (_exposure.Length != rows || _exposure.Any(e => e < Fx6.Zero || e > Fx6.One))
            throw new ArgumentException("One disruption exposure in [0; 1] per actor is required.", nameof(exposure));
        _licGen = table.AddInt("pol.lic.gen");
        _licSeller = table.AddInt("pol.lic.seller");
        _licMask = table.AddLong("pol.lic.mask");
        _licStart = table.AddInt("pol.lic.start");
        _offerMask = table.AddLong("pol.lic.offer");
        _licFlow = table.AddLong("pol.lic.flow");
        _disStart = table.AddInt("pol.dis.start");
        _disEnd = table.AddInt("pol.dis.end");
        // The queue of the deal decided in the last compute (row = buyer) and the counters of BAD_LICENCE by reason (SELF, EMPTY, GEN, MONEY).
        _qSeller = table.AddInt("pol.lic.q.seller");
        _qGen = table.AddInt("pol.lic.q.gen");
        _qMask = table.AddLong("pol.lic.q.mask");
        _qPrice = table.AddLong("pol.lic.q.price");
        _bad = [table.AddLong("pol.lic.bad.self"), table.AddLong("pol.lic.bad.empty"), table.AddLong("pol.lic.bad.gen"), table.AddLong("pol.lic.bad.money")];
    }

    private void InitializeEvents(int n)
    {
        _licGen.Load(new int[n]);
        _licSeller.Load([.. Enumerable.Repeat(-1, n)]);
        _licMask.Load(new long[n]);
        _licStart.Load([.. Enumerable.Repeat(-1, n)]);
        _offerMask.Load(new long[n]);
        _licFlow.Load(new long[n]);
        _disStart.Load([.. Enumerable.Repeat(-1, n)]);
        _disEnd.Load([.. Enumerable.Repeat(-1, n)]);
        _qSeller.Load([.. Enumerable.Repeat(-1, n)]);
        _qGen.Load(new int[n]);
        _qMask.Load(new long[n]);
        _qPrice.Load(new long[n]);
        foreach (LongColumn c in _bad)
            c.Load(new long[n]);
    }

    // ---- reads (state in force at the month being computed)

    /// <summary><c>lic_gen</c>: the engine generation of the buyer's licence, 0 without one (the licence may have lapsed: see the Session's <c>LicenceActive</c>).</summary>
    public int LicenceGen(int actorRow) => _licGen.Current[actorRow];

    public int LicenceSeller(int actorRow) => _licSeller.Current[actorRow];

    /// <summary><c>lic_mask</c>: PRC of the seller's engine and hot section as of the deal (bit <c>n − 1</c> for node <c>n</c>).</summary>
    public ulong LicenceMask(int actorRow) => unchecked((ulong)_licMask.Current[actorRow]);

    /// <summary><c>lic_start</c>: the month the licence began to act, −1 without one.</summary>
    public int LicenceStart(int actorRow) => _licStart.Current[actorRow];

    /// <summary><c>offer_mask</c>: masks of the offers of the last yearly step that nobody took.</summary>
    public ulong OfferMask(int actorRow) => unchecked((ulong)_offerMask.Current[actorRow]);

    /// <summary><c>LicenceFlow(a, t)</c>: price received minus price paid by the deals applied at the start of month t; it is reset at the start of the next month (contract §3.1 r7).</summary>
    public Fx6 LicenceFlow(int actorRow) => Fx6.FromRaw(_licFlow.Current[actorRow]);

    /// <summary>Refused <c>take_licence</c> commands of the buyer by reason (contract §5); the AI's are never refused, so it is 0 unless a player's shares broke the decision.</summary>
    public long BadLicenceCount(int actorRow, BadLicence reason) => _bad[(int)reason - 1].Current[actorRow];

    /// <summary><c>dis_start</c>, <c>dis_end</c> (inclusive) of the actor's alloy disruption, −1 without one.</summary>
    public (int Start, int End) Disruption(int actorRow) => (_disStart.Current[actorRow], _disEnd.Current[actorRow]);

    /// <summary><c>AlloyDeficit(site, m)</c> of the owner: <c>deficit</c> when <c>dis_start ≤ m ≤ dis_end</c>, else 0.</summary>
    public Fx6 AlloyDeficit(int actorRow, long month) =>
        _disStart.Current[actorRow] >= 0 && month >= _disStart.Current[actorRow] && month <= _disEnd.Current[actorRow] ? _p.DisruptionDeficit : Fx6.Zero;

    // ---- the disruption (A43.6)

    private void ComputeDisruption(int row, long t)
    {
        if (_p.T6dOff || _events is null || t <= _disEnd.Current[row])
            return;
        Fx6 p = LicenceRules.DisruptionChance(_p.DisruptionHazard, _exposure[row]);
        if (!CounterRng.Chance(EventRng.DisruptionOnset(_events!.Value, t, _actorNumber[row]), p))
            return;
        long dur = CounterRng.UniformInt(EventRng.DisruptionDuration(_events!.Value, t, _actorNumber[row]), _p.DisruptionMinMonths, _p.DisruptionMaxMonths);
        _disStart.Next[row] = checked((int)t + 1);
        _disEnd.Next[row] = checked((int)(t + dur));
    }

    // ---- why_not lists (A43.7): computed once per actor and compute, only when a rule asks

    private sealed class WhyList(PolicySystem owner, int row, long t)
    {
        private IReadOnlyList<Link>? _list;

        public IReadOnlyList<Link> Get() => _list ??= owner._ev!.WhyNot(row, t);
    }

    /// <summary>A QC link (A43.7): a CAPABILITY link of <c>CultureH</c> in the dimension <c>quality_control</c>; the programme lifts only <c>α_eff</c>, tooling it does not (F28).</summary>
    private static bool IsQcLink(Link l) => l is { Layer: WhyNotLayer.Capability, Cause: WhyNotCause.CultureH, Dim: Dimension.QualityControl };

    private bool ProgrammeTrigger(PolicyProfile profile, WhyList w)
    {
        if (_p.T6dOff || _ev is null || profile == PolicyProfile.Mass)
            return false;
        IReadOnlyList<Link> list = w.Get();
        if (profile == PolicyProfile.Adaptive)
            return list.Count > 0 && IsQcLink(list[0]);
        for (int i = 0; i < list.Count && i < 3; i++)
        {
            if (IsQcLink(list[i]))
                return true;
        }
        return false;
    }

    // The training series of P3 (A43.7): the link of the bottleneck process of the newest line, culture-limited, that will not move within 36 months.
    private int TrainingTargetP3(int row, long t)
    {
        if (_p.T6dOff || _ev is null)
            return -1;
        int d = _inputs.NewestSeriesDesign(row, t);
        if (d < 0 || _inputs.Bottleneck(row, d, t) is not { Process: >= 0 } b)
            return -1;
        foreach (Link l in _ev.WhyNotDesign(row, d, t))
        {
            if (l.Layer == WhyNotLayer.Capability && l.Cause == WhyNotCause.CultureH && l.Process == b.Process)
                return (l.EtaMonths is null || l.EtaMonths > 36) && (l.Limiter != Industry.LimiterKind.Effort || !b.HasOutput) ? d : -1;
        }
        return -1;
    }

    // ---- the licence (A43.2, A43.3)

    private void ComputeLicence(int row, long t, PolicyDesign[] designs, Decisions dec, BudgetShares sharesNext, Fx6 budgetNext, WhyList w)
    {
        if (_p.T6dOff || _p.LicOff || _events is null || _ev is null || _prod is null || t % 12 != 0)
            return;
        var profile = (PolicyProfile)_profile.Current[row];
        int gBuyer = _ev.EngineGen(row, t), held = _licGen.Current[row];
        ulong unaccepted = 0;
        bool taken = false;
        Fx6 baseNext = sharesNext.Procurement * budgetNext;
        // The money left after the upkeep of the book of t+1 (A43.3 (б)): computed once, only if an offer exists.
        Fx6? room = null;
        for (int s = 0; s < _actorNumber.Length; s++)
        {
            if (s == row)
                continue;
            int gSeller = _ev.EngineGen(s, t);
            if (gSeller <= gBuyer || gSeller <= held)
                continue;
            if (!CounterRng.Chance(EventRng.LicenceOffer(_events!.Value, t, _actorNumber[s], _actorNumber[row]), _p.LicenceOfferChance))
                continue;
            if (_ev.Subject(s, gSeller, t) is not { } subject)
                continue;   // cannot happen: g_s > 0 means a project in С/В of that generation
            Fx6 price = LicenceRules.Price(subject.UnitCostFloor, _p.NServiceMin, _p.LicenceShare);
            bool accept = !taken && profile switch
            {
                PolicyProfile.Quality => false,
                PolicyProfile.Mass => true,
                _ => LicenceWanted(w),
            };
            if (accept)
            {
                room ??= baseNext - ProjectedNeed(row, t, designs, dec, sharesNext, budgetNext);
                accept = room.Value >= price;
            }
            if (accept)
            {
                _qSeller.Next[row] = s;
                _qGen.Next[row] = gSeller;
                _qMask.Next[row] = unchecked((long)subject.Mask);
                _qPrice.Next[row] = price.Raw;
                taken = true;
            }
            else
            {
                unaccepted |= subject.Mask;
            }
        }
        _offerMask.Next[row] = unchecked((long)unaccepted);
    }

    // P3 (A43.3 (а), A43.7): the first link of W cannot be closed by waiting — no estimate, or more than 60 months.
    private static bool LicenceWanted(WhyList w)
    {
        IReadOnlyList<Link> list = w.Get();
        return list.Count > 0 && (list[0].EtaMonths is null || list[0].EtaMonths > 60);
    }

    // need_proj(b, t+1): the upkeep of the fleet on the projection of the book to the step 2 of t+1 (A42.4 п. 2); 0 without a fleet layer.
    private Fx6 ProjectedNeed(int row, long t, PolicyDesign[] designs, Decisions dec, BudgetShares sharesNext, Fx6 budgetNext) =>
        ProjectBook(row, t, designs, dec, sharesNext, budgetNext) is { } p ? p.Book.Need() : Fx6.Zero;

    /// <summary>
    /// The application of the queued <c>take_licence</c> commands at the start of month <paramref name="month"/> (contract §5, A43.5), after the commands of the
    /// player: resets <c>licence_flow</c>, then in buyer order validates each deal (<see cref="LicenceRules.Validate"/>, the money against
    /// <paramref name="procurementBase"/> = <c>ProcurementBase(b, t)</c> of the shares in force) and writes the state of the buyer and the flows of both sides.
    /// A refused deal leaves both sides untouched and counts in <c>BAD_LICENCE</c>. Writes the Current columns: it runs between ticks, never inside one.
    /// </summary>
    public void ApplyDeals(long month, Func<int, Fx6> procurementBase)
    {
        ArgumentNullException.ThrowIfNull(procurementBase);
        int n = _actorNumber.Length;
        bool queued = false, flowing = false;
        for (int b = 0; b < n; b++)
        {
            queued |= _qSeller.Current[b] >= 0;
            flowing |= _licFlow.Current[b] != 0;
        }
        if (!queued && !flowing)
            return;
        long[] flow = new long[n];
        int[] gen = _licGen.Current.ToArray(), seller = _licSeller.Current.ToArray(), start = _licStart.Current.ToArray(), qSeller = _qSeller.Current.ToArray();
        long[] mask = _licMask.Current.ToArray();
        var bad = new long[_bad.Length][];
        for (int k = 0; k < bad.Length; k++)
            bad[k] = _bad[k].Current.ToArray();
        for (int b = 0; b < n; b++)
        {
            int s = _qSeller.Current[b];
            if (s < 0)
                continue;
            qSeller[b] = -1;
            ulong dealMask = unchecked((ulong)_qMask.Current[b]);
            Fx6 price = Fx6.FromRaw(_qPrice.Current[b]);
            int dealGen = _qGen.Current[b];
            // «Active» is the lapse rule of A43.4 on the snapshot of the month: the licence of the buyer in force, not just a non-zero column.
            BadLicence reason = LicenceRules.Validate(b, s, dealGen, dealMask, price, gen[b], gen[b] > 0 && (_ev?.EngineGen(b, month) ?? 0) <= gen[b], procurementBase(b));
            if (reason != BadLicence.None)
            {
                bad[(int)reason - 1][b]++;
                continue;
            }
            gen[b] = dealGen;
            seller[b] = s;
            mask[b] = unchecked((long)dealMask);
            start[b] = checked((int)month);
            flow[b] -= price.Raw;
            flow[s] += price.Raw;
        }
        _licGen.Load(gen);
        _licSeller.Load(seller);
        _licMask.Load(mask);
        _licStart.Load(start);
        _licFlow.Load(flow);
        _qSeller.Load(qSeller);
        for (int k = 0; k < bad.Length; k++)
            _bad[k].Load(bad[k]);
    }
}
