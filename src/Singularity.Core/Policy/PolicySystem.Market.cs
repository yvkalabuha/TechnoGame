using Singularity.Kernel;

namespace Singularity.Core.Policy;

/// <summary>One line of a seller's catalogue: the generation, the reference price, the multiplier and the asking price (all in the units of the core), the circle.</summary>
public readonly record struct LicenceOffer(int Seller, int Gen, Fx6 Reference, Fx6 M, Fx6 Price, SaleCircle Circle, LicenceMask Mask);

/// <summary>One licence a country holds: the key is (<see cref="Seller"/>, <see cref="Gen"/>).</summary>
public readonly record struct LicenceRecord(int Seller, int Gen, int Start, LicenceMask Mask);

/// <summary>Why an <c>OfferLicence</c> was refused.</summary>
public enum OfferRefusal
{
    None = 0,

    /// <summary>The generation is outside the layout.</summary>
    BadGen,

    /// <summary>The seller has no serial project of its own with this generation (only own projects are sold, #477).</summary>
    NoOwnProject,

    /// <summary>The multiplier is outside [<c>SellerMMin</c>; <c>SellerMMax</c>].</summary>
    BadMultiplier,
}

/// <summary>
/// The licence market (TASK-0152, flag <see cref="PolicyParams.LicenceMarket"/>): up to <see cref="MarketSlots"/> licences per country keyed (seller, gen),
/// a permanent catalogue per seller and generation (multiplier and circle; the reference price is a pure function of the seller's own serial project),
/// <c>BuyLicence</c> through the deal queue (<c>pol.lic.q.*</c>) and <see cref="ApplyDeals"/>. Ownership of the queue: the AI of an actor decides in
/// <see cref="ComputeLicence"/> (yearly, into Next, never for an externally controlled row); a command of the player is written between ticks into Current
/// (<see cref="QueueBuy"/>), only for an externally controlled row. Both are validated by the same function at the start of the month. The legacy
/// single-licence columns stay and hold the aggregate (the highest generation, its seller and start, the union of the masks), so the consumers of the mask
/// work unchanged.
/// </summary>
public sealed partial class PolicySystem
{
    public const int MarketSlots = 8;

    private IntColumn[] _mkSeller = [], _mkGen = [], _mkStart = [], _catCircle = [];
    private LongColumn[] _mkMask = [], _mkMask1 = [], _catM = [];
    private IntColumn? _incYear, _lastBad, _lastBadSeller, _lastBadGen, _lastBadMonth;
    private LongColumn? _incAmt;
    private bool[] _external = [];
    private int _marketGens;

    private void AddMarketColumns(Table table, int rows, bool wide, int generations)
    {
        if (generations < 1)
            throw new ArgumentException("The licence market needs the product layout (generations).", nameof(generations));
        _marketGens = generations;
        _external = new bool[rows];
        foreach (int r in _p.LicenceExternalRows)
        {
            if (r < 0 || r >= rows)
                throw new ArgumentOutOfRangeException(nameof(_p), r, "An external licence row is outside the table.");
            _external[r] = true;
        }
        if (_p.MarketActorIds.Length != rows)
            throw new ArgumentException("The licence market needs one actor id per row.", nameof(_p));
        _mkSeller = new IntColumn[MarketSlots];
        _mkGen = new IntColumn[MarketSlots];
        _mkStart = new IntColumn[MarketSlots];
        _mkMask = new LongColumn[MarketSlots];
        _mkMask1 = wide ? new LongColumn[MarketSlots] : [];
        for (int k = 0; k < MarketSlots; k++)
        {
            _mkSeller[k] = table.AddInt($"pol.lm.{k}.seller");
            _mkGen[k] = table.AddInt($"pol.lm.{k}.gen");
            _mkStart[k] = table.AddInt($"pol.lm.{k}.start");
            _mkMask[k] = table.AddLong($"pol.lm.{k}.mask");
            if (wide)
                _mkMask1[k] = table.AddLong($"pol.lm.{k}.mask.1");
        }
        _catM = new LongColumn[generations];
        _catCircle = new IntColumn[generations];
        for (int g = 0; g < generations; g++)
        {
            _catM[g] = table.AddLong($"pol.cat.g{g + 1}.m");
            _catCircle[g] = table.AddInt($"pol.cat.g{g + 1}.circle");
        }
        _incAmt = table.AddLong("pol.cat.income");
        _incYear = table.AddInt("pol.cat.income.year");
        _lastBad = table.AddInt("pol.lic.lastbad");
        _lastBadSeller = table.AddInt("pol.lic.lastbad.seller");
        _lastBadGen = table.AddInt("pol.lic.lastbad.gen");
        _lastBadMonth = table.AddInt("pol.lic.lastbad.month");
    }

    private void InitializeMarket(int n)
    {
        for (int k = 0; k < MarketSlots; k++)
        {
            _mkSeller[k].Load([.. Enumerable.Repeat(-1, n)]);
            _mkGen[k].Load(new int[n]);
            _mkStart[k].Load([.. Enumerable.Repeat(-1, n)]);
            _mkMask[k].Load(new long[n]);
            if (_mkMask1.Length > 0)
                _mkMask1[k].Load(new long[n]);
        }
        for (int g = 0; g < _marketGens; g++)
        {
            _catM[g].Load(new long[n]);
            _catCircle[g].Load(new int[n]);
        }
        _incAmt!.Load(new long[n]);
        _incYear!.Load([.. Enumerable.Repeat(-1, n)]);
        _lastBad!.Load(new int[n]);
        _lastBadSeller!.Load([.. Enumerable.Repeat(-1, n)]);
        _lastBadGen!.Load(new int[n]);
        _lastBadMonth!.Load([.. Enumerable.Repeat(-1, n)]);
    }

    // ---- reads

    /// <summary>The market is on in this policy.</summary>
    public bool MarketOn => _p.LicenceMarket;

    /// <summary>The licences of the buyer in slot order (an empty slot is skipped). Allocates: a view read, not the tick path.</summary>
    public IReadOnlyList<LicenceRecord> Licences(int actorRow)
    {
        var list = new List<LicenceRecord>();
        for (int k = 0; k < _mkSeller.Length; k++)
        {
            if (_mkSeller[k].Current[actorRow] >= 0)
                list.Add(new LicenceRecord(_mkSeller[k].Current[actorRow], _mkGen[k].Current[actorRow], _mkStart[k].Current[actorRow], ReadMask(_mkMask[k], _mkMask1.Length > 0 ? _mkMask1[k] : null, actorRow)));
        }
        return list;
    }

    /// <summary>
    /// The licences that begin in the month being computed, written to <paramref name="into"/> (the product layer turns each into a project, <c>AdoptLicensedDesign</c>);
    /// returns the count. Allocates nothing.
    /// </summary>
    public int NewLicences(int actorRow, long month, Span<(int Seller, int Gen)> into)
    {
        int n = 0;
        for (int k = 0; k < _mkSeller.Length && n < into.Length; k++)
        {
            if (_mkSeller[k].Current[actorRow] >= 0 && _mkStart[k].Current[actorRow] == month)
                into[n++] = (_mkSeller[k].Current[actorRow], _mkGen[k].Current[actorRow]);
        }
        return n;
    }

    /// <summary>The last refused deal of the buyer: reason, seller, generation, month (Reason None before any).</summary>
    public (BadLicence Reason, int Seller, int Gen, int Month) LastRefusal(int actorRow) =>
        _lastBad is null ? default : ((BadLicence)_lastBad.Current[actorRow], _lastBadSeller!.Current[actorRow], _lastBadGen!.Current[actorRow], _lastBadMonth!.Current[actorRow]);

    /// <summary>Licence income of the seller in the calendar year of <paramref name="month"/> so far (0 when the stored year is another one).</summary>
    public Fx6 LicenceIncome(int actorRow, long month) => _incAmt is null || _incYear!.Current[actorRow] != YearOf(month) ? Fx6.Zero : Fx6.FromRaw(_incAmt.Current[actorRow]);

    /// <summary>Calendar year of a month index of the run.</summary>
    public int YearOf(long month) => _p.MarketStartYear + checked((int)((_p.MarketStartMonth - 1 + month) / 12));

    public bool IsExternal(int actorRow) => _external.Length > actorRow && _external[actorRow];

    private Fx6 DefaultM(int seller) => IsExternal(seller)
        ? Fx6.One
        : (PolicyProfile)_profile.Current[seller] switch { PolicyProfile.Quality => _p.SellerMQuality, PolicyProfile.Mass => _p.SellerMMass, _ => Fx6.One };

    private SaleCircle CircleOf(int seller, int gen)
    {
        var c = (SaleCircle)_catCircle[gen - 1].Current[seller];
        return c != SaleCircle.Default ? c : IsExternal(seller) ? SaleCircle.Friends : SaleCircle.All;
    }

    /// <summary>
    /// The line of the seller's catalogue for a generation, or null when the seller has no serial project of its own with that generation (a bought project
    /// is never sold, #477) or the generation is outside the layout. The price is <c>m × ref</c> with <c>ref = LicenceRules.Price</c> of that project.
    /// </summary>
    public LicenceOffer? Offer(int seller, int gen, long month)
    {
        if (!_p.LicenceMarket || _ev is null || gen < 1 || gen > _marketGens)
            return null;
        if (_ev.Subject(seller, gen, month) is not { } subject)
            return null;
        Fx6 reference = LicenceRules.Price(subject.UnitCostFloor, _p.NServiceMin, _p.LicenceShare);
        Fx6 m = _catM[gen - 1].Current[seller] == 0 ? DefaultM(seller) : Fx6.FromRaw(_catM[gen - 1].Current[seller]);
        return new LicenceOffer(seller, gen, reference, m, reference * m, CircleOf(seller, gen), subject.Mask);
    }

    /// <summary>
    /// <c>WorldAvg(gen, t)</c> (licence-prices.md §5): the mean asking price over the catalogues of the AI sellers, null with fewer than <c>ThinMarketSellers</c>.
    /// A pure function of the catalogues; the catalogue of an externally controlled seller is left out, so a player cannot move it.
    /// </summary>
    public Fx6? WorldAverage(int gen, long month)
    {
        Span<Fx6> prices = stackalloc Fx6[Math.Min(_actorNumber.Length, 64)];
        int n = 0;
        for (int s = 0; s < _actorNumber.Length && n < prices.Length; s++)
        {
            if (IsExternal(s) || Offer(s, gen, month) is not { } o || o.Circle == SaleCircle.Nobody)
                continue;
            prices[n++] = o.Price;
        }
        return LicenceRules.WorldAverage(prices[..n], _p.ThinMarketSellers);
    }

    private bool KeyHeld(int buyer, int seller, int gen)
    {
        for (int k = 0; k < _mkSeller.Length; k++)
        {
            if (_mkSeller[k].Current[buyer] == seller && _mkGen[k].Current[buyer] == gen)
                return true;
        }
        return false;
    }

    /// <summary>
    /// The refusal a purchase would meet now (what the screen shows before the button is pressed, and what <see cref="ApplyDeals"/> decides): the same
    /// function. <paramref name="funds"/> is what the buyer may spend (the procurement base now, the reserve once it exists).
    /// </summary>
    public BadLicence CheckBuy(int buyer, int seller, int gen, long month, Fx6 funds, out Fx6 price)
    {
        LicenceOffer? offer = seller == buyer ? null : Offer(seller, gen, month);
        price = offer?.Price ?? Fx6.Zero;
        int year = YearOf(month);
        bool inCircle = offer is { } o && o.Circle != SaleCircle.Nobody && _p.Alliances.InCircle(_p.MarketActorIds[seller], _p.MarketActorIds[buyer], year, o.Circle);
        bool held = KeyHeld(buyer, seller, gen) || gen >= 1 && gen <= (_ev?.EngineGen(buyer, month) ?? 0);
        Fx6 limit = IsExternal(buyer) ? funds : AiLimit(funds);
        bool hasSlot = (_prod?.HasFreeDesignSlot(buyer) ?? true) && FreeLicenceSlot(buyer) >= 0;
        bool overCap = IsExternal(seller) && offer is not null
            && LicenceIncome(seller, month) + price > _p.IncomeCapShare * Fx6.FromInt(12) * Budget(seller);
        return LicenceRules.ValidateMarket(buyer, seller, gen, offer is not null, held, price, funds, limit, inCircle, hasSlot, overCap);
    }

    // L (months of the stock) and the share the AI keeps in the month of the payment: the lower bound wins.
    private Fx6 AiLimit(Fx6 funds)
    {
        Fx6 stock = funds * Fx6.FromInt(_p.AiStockMonths), keep = funds * (Fx6.One - _p.AiKeepShare);
        return stock < keep ? stock : keep;
    }

    private int FreeLicenceSlot(int buyer)
    {
        for (int k = 0; k < _mkSeller.Length; k++)
        {
            if (_mkSeller[k].Current[buyer] < 0)
                return k;
        }
        return -1;
    }

    // ---- commands of the player (between ticks, Current)

    /// <summary>
    /// <c>BuyLicence</c>: puts the deal into the queue of the buyer, to be validated and paid by <see cref="ApplyDeals"/> at the start of the month. Only for an
    /// externally controlled row (the AI queues its own in <see cref="ComputeLicence"/>); a later call of the same month replaces the earlier one.
    /// </summary>
    public bool QueueBuy(int buyer, int seller, int gen, long month)
    {
        if (!_p.LicenceMarket || !IsExternal(buyer) || seller < 0 || seller >= _actorNumber.Length)
            return false;
        Fx6 price = Offer(seller, gen, month)?.Price ?? Fx6.Zero;
        LicenceMask mask = Offer(seller, gen, month)?.Mask ?? default;
        SetCurrent(_qSeller, buyer, seller);
        SetCurrent(_qGen, buyer, gen);
        SetCurrent(_qPrice, buyer, price.Raw);
        SetCurrent(_qMask, buyer, unchecked((long)mask.Lo));
        if (_qMask1 is not null)
            SetCurrent(_qMask1, buyer, unchecked((long)mask.Hi));
        return true;
    }

    /// <summary>
    /// <c>OfferLicence</c>: sets the multiplier and the circle of the seller's catalogue line. Own serial projects only (#477), <c>m</c> inside the declared range.
    /// </summary>
    public OfferRefusal SetOffer(int seller, int gen, Fx6 m, SaleCircle circle, long month)
    {
        if (!_p.LicenceMarket || gen < 1 || gen > _marketGens)
            return OfferRefusal.BadGen;
        if (_ev?.Subject(seller, gen, month) is null)
            return OfferRefusal.NoOwnProject;
        if (!LicenceRules.MultiplierInRange(m, _p.SellerMMin, _p.SellerMMax))
            return OfferRefusal.BadMultiplier;
        SetCurrent(_catM[gen - 1], seller, m.Raw);
        SetCurrent(_catCircle[gen - 1], seller, (int)circle);
        return OfferRefusal.None;
    }

    // ---- the AI buys (yearly, in the compute of month t; the deal waits in the queue for ApplyDeals)

    private void ComputeMarketBuy(int row, long t, WhyList w, PolicyDesign[] designs, Decisions dec, BudgetShares sharesNext, Fx6 budgetNext)
    {
        if (IsExternal(row))
            return;
        var profile = (PolicyProfile)_profile.Current[row];
        int gBuyer = _ev!.EngineGen(row, t), year = YearOf(t);
        Fx6 baseNext = sharesNext.Procurement * budgetNext;
        Fx6 limit = AiLimit(baseNext);
        Fx6? room = null;   // the money left after the upkeep of the book of t+1, computed once and only if an offer survives the rules
        for (int s = 0; s < _actorNumber.Length; s++)
        {
            if (s == row)
                continue;
            for (int gen = Math.Min(_marketGens, _ev.EngineGen(s, t)); gen > gBuyer; gen--)
            {
                if (Offer(s, gen, t) is not { } o || KeyHeld(row, s, gen))
                    continue;
                if (o.Circle == SaleCircle.Nobody || !_p.Alliances.InCircle(_p.MarketActorIds[s], _p.MarketActorIds[row], year, o.Circle))
                    continue;
                if (profile == PolicyProfile.Quality || profile != PolicyProfile.Mass && !LicenceWanted(w))
                    continue;
                if (LicenceRules.AiRule(o.Price, o.Reference, _p.AiPriceK, _ev.OwnEtaMonths(row, gen, t), _p.AiMinOwnEtaMonths, limit) != AiDecline.None)
                    continue;
                Fx6 free = room ??= baseNext - ProjectedNeed(row, t, designs, dec, sharesNext, budgetNext);
                if (o.Price > free)
                {
                    _moneyRefusals[row].Add(new MoneyRefusal(s, gen, o.Price, free));
                    continue;
                }
                _qSeller.Next[row] = s;
                _qGen.Next[row] = gen;
                WriteNextMask(_qMask, _qMask1, row, o.Mask);
                _qPrice.Next[row] = o.Price.Raw;
                return;
            }
        }
    }

    // ---- the application at the start of month t+1 (the same entry as the legacy deals)

    private void ApplyMarketDeals(long month, Func<int, Fx6> funds)
    {
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
        int[] qSeller = _qSeller.Current.ToArray();
        int year = YearOf(month);
        var bad = new long[_bad.Length][];
        for (int k = 0; k < bad.Length; k++)
            bad[k] = _bad[k].Current.ToArray();
        int[] lastBad = _lastBad!.Current.ToArray(), lastSeller = _lastBadSeller!.Current.ToArray(), lastGen = _lastBadGen!.Current.ToArray(), lastMonth = _lastBadMonth!.Current.ToArray();
        long[] income = _incAmt!.Current.ToArray();
        int[] incomeYear = _incYear!.Current.ToArray();
        // The aggregate is written last from the records; the records themselves are copied only when a deal goes through.
        for (int b = 0; b < n; b++)
        {
            int s = _qSeller.Current[b];
            if (s < 0)
                continue;
            qSeller[b] = -1;
            int gen = _qGen.Current[b];
            BadLicence reason = CheckBuy(b, s, gen, month, funds(b), out Fx6 price);
            if (reason != BadLicence.None)
            {
                bad[(int)reason - 1][b]++;
                lastBad[b] = (int)reason;
                lastSeller[b] = s;
                lastGen[b] = gen;
                lastMonth[b] = checked((int)month);
                continue;
            }
            LicenceOffer offer = Offer(s, gen, month)!.Value;
            int slot = FreeLicenceSlot(b);
            SetCurrent(_mkSeller[slot], b, s);
            SetCurrent(_mkGen[slot], b, gen);
            SetCurrent(_mkStart[slot], b, checked((int)month));
            SetCurrent(_mkMask[slot], b, unchecked((long)offer.Mask.Lo));
            if (_mkMask1.Length > 0)
                SetCurrent(_mkMask1[slot], b, unchecked((long)offer.Mask.Hi));
            RefreshAggregate(b);
            flow[b] -= price.Raw;
            flow[s] += price.Raw;
            if (incomeYear[s] != year)
            {
                incomeYear[s] = year;
                income[s] = 0;
            }
            income[s] += price.Raw;
        }
        _licFlow.Load(flow);
        _qSeller.Load(qSeller);
        for (int k = 0; k < bad.Length; k++)
            _bad[k].Load(bad[k]);
        _lastBad.Load(lastBad);
        _lastBadSeller.Load(lastSeller);
        _lastBadGen.Load(lastGen);
        _lastBadMonth.Load(lastMonth);
        _incAmt.Load(income);
        _incYear.Load(incomeYear);
    }

    // The legacy columns hold the aggregate of the records: the highest generation with its seller and start, the union of all masks.
    private void RefreshAggregate(int b)
    {
        int best = -1;
        ulong lo = 0, hi = 0;
        for (int k = 0; k < _mkSeller.Length; k++)
        {
            if (_mkSeller[k].Current[b] < 0)
                continue;
            lo |= unchecked((ulong)_mkMask[k].Current[b]);
            if (_mkMask1.Length > 0)
                hi |= unchecked((ulong)_mkMask1[k].Current[b]);
            if (best < 0 || _mkGen[k].Current[b] > _mkGen[best].Current[b])
                best = k;
        }
        SetCurrent(_licGen, b, _mkGen[best].Current[b]);
        SetCurrent(_licSeller, b, _mkSeller[best].Current[b]);
        SetCurrent(_licStart, b, _mkStart[best].Current[b]);
        SetCurrent(_licMask, b, unchecked((long)lo));
        if (_licMask1 is not null)
            SetCurrent(_licMask1, b, unchecked((long)hi));
    }
}
