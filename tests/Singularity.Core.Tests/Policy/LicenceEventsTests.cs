using System.Collections.Immutable;
using Singularity.Core.Data;
using Singularity.Core.Events;
using Singularity.Core.Industry;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Core.WhyNot;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Policy;

/// <summary>
/// The licence and disruption events and the why_not-driven decisions of the AI (spec §3.5 A43) on scripted snapshots: the pure rules, the draws against
/// their keys, the acceptance by profile and money, the deal and its payment, the programme and the training-series triggers.
/// </summary>
public class LicenceEventsTests
{
    private const int Designs = 12;
    private static readonly Fx6 Cost = Fx6.FromInt(1000);               // unit_cost_floor of the subject: the price is 1000·50·0,3 = 15 000
    private static readonly Fx6 Price = Fx6.FromInt(15_000);

    // ---- pure rules

    [Fact]
    public void Price_IsAFloorAtEachProduct_AndG1OfE1CostsTwentyThousandSevenHundredThirtyRaw()
    {
        Assert.Equal(20_730, LicenceRules.Price(Fx6.FromRaw(1_382), 50, Fx6.FromRaw(300_000)).Raw);
        Assert.Equal(Price, LicenceRules.Price(Cost, 50, Fx6.FromRaw(300_000)));
    }

    [Fact]
    public void Mask_Word0IsNodeNumberMinusOne_Word1IsNumberMinus64()
    {
        Assert.Equal(new LicenceMask(1UL, 0), LicenceRules.Bit(1));
        Assert.Equal(new LicenceMask(1UL << 53, 0), LicenceRules.Bit(54));
        Assert.Equal(new LicenceMask(1UL << 62, 0), LicenceRules.Bit(63));
        Assert.Equal(new LicenceMask(0, 1UL), LicenceRules.Bit(64));
        Assert.Equal(new LicenceMask(0, 1UL << 13), LicenceRules.Bit(77));
        Assert.True(LicenceRules.Has(LicenceRules.Bit(54) | LicenceRules.Bit(39), 39));
        Assert.False(LicenceRules.Has(LicenceRules.Bit(54), 44));
    }

    [Fact]
    public void Mask_Nodes64And77_AreHeldAndNotConfusedWithWord0()
    {
        LicenceMask m = LicenceRules.Bit(64) | LicenceRules.Bit(77) | LicenceRules.Bit(1);
        Assert.True(LicenceRules.Has(m, 64));
        Assert.True(LicenceRules.Has(m, 77));
        Assert.True(LicenceRules.Has(m, 1));
        Assert.False(LicenceRules.Has(m, 65));
        Assert.False(LicenceRules.Has(m, 14));   // 77 − 63: a one-word mask would alias it
    }

    [Fact]
    public void Mask_OutOfRange_IsLoud_ForBitAndHas()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LicenceRules.Bit(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => LicenceRules.Bit(128));
        Assert.Throws<ArgumentOutOfRangeException>(() => LicenceRules.Has(LicenceRules.Bit(54), 128));   // used to answer false
        Assert.Throws<ArgumentOutOfRangeException>(() => LicenceRules.Has(LicenceRules.Bit(54), -1));
    }

    [Fact]
    public void Validator_NamesTheFirstFailedCheck_AndAcceptsAtTheBoundary()
    {
        LicenceMask m = LicenceRules.Bit(54);
        Assert.Equal(BadLicence.Self, LicenceRules.Validate(2, 2, 1, m, Price, 0, false, Price));
        Assert.Equal(BadLicence.Empty, LicenceRules.Validate(2, 0, 0, m, Price, 0, false, Price));
        Assert.Equal(BadLicence.Empty, LicenceRules.Validate(2, 0, 1, default, Price, 0, false, Price));
        Assert.Equal(BadLicence.Empty, LicenceRules.Validate(2, 0, 1, m, Fx6.FromRaw(-1), 0, false, Price));
        Assert.Equal(BadLicence.Gen, LicenceRules.Validate(2, 0, 1, m, Price, 1, true, Price));
        Assert.Equal(BadLicence.None, LicenceRules.Validate(2, 0, 1, m, Price, 1, false, Price));   // a lapsed licence is replaced by the same generation
        Assert.Equal(BadLicence.None, LicenceRules.Validate(2, 0, 2, m, Price, 1, true, Price));    // a newer generation replaces an active one
        Assert.Equal(BadLicence.Money, LicenceRules.Validate(2, 0, 1, m, Price, 0, false, Price - Fx6.FromRaw(1)));
        Assert.Equal(BadLicence.None, LicenceRules.Validate(2, 0, 1, m, Price, 0, false, Price));
    }

    [Fact]
    public void DisruptionChance_ExposureHalf_IsFourThousandOneHundredSixtySixRaw()
    {
        Assert.Equal(4_166, LicenceRules.DisruptionChance(Fx6.FromRaw(8_333), Fx6.FromRaw(500_000)).Raw);
        Assert.Equal(8_333, LicenceRules.DisruptionChance(Fx6.FromRaw(8_333), Fx6.One).Raw);
    }

    // ---- the draws (item 2, item 5): the keys of §9.1 and the frequencies on 10⁴ keys

    [Fact]
    public void Draws_UseTheKeysOfSection91_OfStreamE()
    {
        var e = new CounterRng(0x5EED);
        Assert.Equal(e.NextU64(12, 4, 0 * 8 + 3, 0), EventRng.LicenceOffer(e, 12, 0, 3));
        Assert.Equal(e.NextU64(7, 3, 2, 0), EventRng.DisruptionOnset(e, 7, 2));
        Assert.Equal(e.NextU64(7, 3, 2, 1), EventRng.DisruptionDuration(e, 7, 2));
        // Golden values of the keys (seed 0x5EED): a change of a key changes every event of every run.
        Assert.Equal(3117558943129824545UL, EventRng.LicenceOffer(e, 0, 0, 3));
        Assert.Equal(16282391738246191407UL, EventRng.LicenceOffer(e, 12, 1, 4));
        Assert.Equal(7724618808872053217UL, EventRng.DisruptionOnset(e, 7, 2));
        Assert.Equal(3298076512927289543UL, EventRng.DisruptionDuration(e, 7, 2));
    }

    // 99% two-sided normal bound of a binomial count: mean ± 2,576·σ.
    private static void AssertInBinomialCi(long count, long n, double p)
    {
        double sd = Math.Sqrt(n * p * (1 - p));
        Assert.InRange(count, n * p - 2.576 * sd, n * p + 2.576 * sd);
    }

    [Fact]
    public void OfferFrequency_OnTenThousandKeys_IsInTheBinomialCiAround0_2()
    {
        var e = new CounterRng(1);
        int hits = 0;
        for (int t = 0; t < 10_000; t++)
            hits += CounterRng.Chance(EventRng.LicenceOffer(e, t * 12L, t % 5, 5 + t % 3), Fx6.FromRaw(200_000)) ? 1 : 0;
        AssertInBinomialCi(hits, 10_000, 0.2);
    }

    [Theory]
    [InlineData(500_000, 0.004166)]
    [InlineData(1_000_000, 0.008333)]
    public void OnsetFrequency_OnTenThousandKeys_IsInTheBinomialCi_ForExposureHalfAndOne(long exposureRaw, double p)
    {
        var e = new CounterRng(2);
        Fx6 chance = LicenceRules.DisruptionChance(Fx6.FromRaw(8_333), Fx6.FromRaw(exposureRaw));
        int hits = 0;
        for (int t = 0; t < 10_000; t++)
            hits += CounterRng.Chance(EventRng.DisruptionOnset(e, t, t % 5), chance) ? 1 : 0;
        AssertInBinomialCi(hits, 10_000, p);
    }

    [Fact]
    public void Duration_CoversEveryValueFrom6To18_EachInTheBinomialCi()
    {
        var e = new CounterRng(3);
        var count = new int[19];
        for (int t = 0; t < 10_000; t++)
            count[CounterRng.UniformInt(EventRng.DisruptionDuration(e, t, t % 5), 6, 18)]++;
        for (int dur = 6; dur <= 18; dur++)
            AssertInBinomialCi(count[dur], 10_000, 1.0 / 13);
    }

    // ---- the deal: a rig of three actors (0, 1 sellers; 2 buyer; the buyer's profile is the parameter)

    private sealed class Prod : IPolicyProducts
    {
        public long FleetUpkeep;   // need of the book of actor 2: 100 units · upkeep
        public ProductsLayout Layout => Probe;
        public Fx6 DRef => Fx6.FromInt(740);
        public PolicyDesign Design(int actorRow, int design, long month) => default;
        public int[] Alternatives(int actorRow, int design) => [.. Enumerable.Repeat(0, Probe.SlotCount)];
        public bool Knows(int actorRow, int nodeNumber, long month) => false;
        public bool Admitted(int actorRow, int design, long month) => true;
        public Fx6 AdmittedYield(int actorRow, int processNumber, long month) => Fx6.Zero;
        public Fx6 AlloySupply(int actorRow, long month) => Fx6.Zero;
        public int FreeBureaus(int actorRow) => 0;
        public Fx6 CandidateValue(int actorRow, ReadOnlySpan<int> alts, Fx6 budget, long month) => Fx6.One;

        public FleetBook? Fleet(int actorRow, long month)
        {
            if (actorRow != 2 || FleetUpkeep == 0)
                return null;
            var book = new FleetBook(new ProductsParams(), [0], Designs);
            book.Stage[0] = DesignStage.InService;
            book.Counts[0][0] = Fx6.FromInt(100);
            book.Upkeep[0] = Fx6.FromRaw(FleetUpkeep * Fx6.Scale / 100);
            return book;
        }
    }

    private sealed class Ev : IPolicyEvents
    {
        public int[] Gen = [1, 1, 0];
        public Func<int, IReadOnlyList<Link>> List = _ => [];
        public LicenceMask[] Mask = [LicenceRules.Bit(54), LicenceRules.Bit(39), default];
        public int EngineGen(int actorRow, long month) => Gen[actorRow];
        public LicenceSubject? Subject(int sellerRow, int gen, long month) => new LicenceSubject(Cost, Mask[sellerRow]);
        public IReadOnlyList<Link> WhyNot(int actorRow, long month) => List(actorRow);
        public IReadOnlyList<Link> WhyNotDesign(int actorRow, DesignHandle design, long month) => List(actorRow);
    }

    private sealed class NoInputs : IPolicyInputs
    {
        public DesignHandle? NewestSeriesDesign(int actorRow, long month) => null;
        public BottleneckLimit? Bottleneck(int actorRow, int design, long month) => null;
        public bool SeriesBelowQuality(int actorRow, Fx6 threshold, long month) => false;
    }

    private static Link First(int? eta) =>
        new(WhyNotLayer.Knowledge, WhyNotCause.MissingNode, 58, Fx6.Zero, Fx6.One, [], Fx6.Zero, eta);

    // A seed of stream E at which both sellers offer to the buyer at t = 0 (the keys are fixed, so the search is deterministic).
    private static ulong SeedWhere(bool from0, bool from1)
    {
        for (ulong seed = 1; seed < 100_000; seed++)
        {
            var e = new CounterRng(seed);
            if (CounterRng.Chance(EventRng.LicenceOffer(e, 0, 0, 2), Fx6.FromRaw(200_000)) == from0
                && CounterRng.Chance(EventRng.LicenceOffer(e, 0, 1, 2), Fx6.FromRaw(200_000)) == from1)
                return seed;
        }
        throw new InvalidOperationException("no seed");
    }

    private sealed class Rig
    {
        public readonly Prod Products = new();
        public readonly Ev Events = new();
        public readonly PolicySystem Policy;
        public readonly Table Table;
        private readonly TickRunner _runner;
        private readonly TickCalendar _calendar = new(new CivilDate(1945, 9, 1));

        public Rig(PolicyProfile buyer, ulong seed, Fx6? buyerBudget = null, long fleetUpkeep = 0, PolicyParams? parameters = null, bool wide = false)
        {
            Products.FleetUpkeep = fleetUpkeep;
            var table = Table = new Table(3);
            PolicyParams p = (parameters ?? new PolicyParams()) with { FvBudget = Fx6.FromInt(10) };
            Policy = new PolicySystem(table, p, [0, 1, 2], [Fx6.FromInt(100_000), Fx6.FromInt(100_000), buyerBudget ?? Fx6.FromInt(100_000)], [Fx6.Zero, Fx6.Zero, Fx6.Zero],
                new CounterRng(1), new NoInputs(), new IndustryMailbox(3), 99, [(int)PolicyProfile.Mass, (int)PolicyProfile.Mass, (int)buyer],
                products: Products, designs: Designs, generations: Probe.Generations, events: new CounterRng(seed), eventInputs: Events, wideLicenceMask: wide);
            Policy.Initialize();
            _runner = new TickRunner(table, 1, [Policy], 1, 1, _calendar);
        }

        // The session applies the queued deals at the start of every monthly step (SliceSession), with the procurement base of the shares in force.
        public void Month(long month)
        {
            while (_runner.Tick <= _calendar.TickOfMonth(month))
            {
                if (_calendar.IsDue(Cadence.Monthly, _runner.Tick))
                    Policy.ApplyDeals(_calendar.MonthIndex(_runner.Tick), b => Policy.Shares(b).Procurement * Policy.Budget(b));
                _runner.RunTick();
            }
        }
    }

    [Fact]
    public void WideMask_Nodes64And77_SurviveTheDeal_AndTheSnapshot()
    {
        ulong seed = SeedWhere(true, true);
        var rig = new Rig(PolicyProfile.Mass, seed, wide: true);
        rig.Events.Mask = [LicenceRules.Bit(77) | LicenceRules.Bit(54), LicenceRules.Bit(64) | LicenceRules.Bit(39), default];
        rig.Month(0);
        Assert.Equal(LicenceRules.Bit(64) | LicenceRules.Bit(39), rig.Policy.OfferMask(2));

        // Snapshot between months 0 and 1: the deal is still queued (pol.lic.q.mask.1 holds node 77), not yet applied.
        var queued = new MemoryStream();
        TableSnapshot.Write(rig.Table, 1, 1, queued);
        rig.Month(1);
        LicenceMask expected = LicenceRules.Bit(77) | LicenceRules.Bit(54);
        Assert.Equal(expected, rig.Policy.LicenceMask(2));
        Assert.True(rig.Policy.LicenceMask(2).Has(77));

        var other = new Rig(PolicyProfile.Mass, seed, wide: true);
        queued.Position = 0;
        TableSnapshot.Read(other.Table, 1, queued);
        Assert.Equal(0, other.Policy.LicenceGen(2));
        other.Policy.ApplyDeals(1, _ => Fx6.FromInt(1_000_000));   // the loaded queue is applied like the original's
        Assert.Equal(expected, other.Policy.LicenceMask(2));
        Assert.Equal(LicenceRules.Bit(64) | LicenceRules.Bit(39), other.Policy.OfferMask(2));

        // Snapshot after the deal: word 1 of the held mask is saved too.
        var applied = new MemoryStream();
        TableSnapshot.Write(rig.Table, 1, 2, applied);
        var third = new Rig(PolicyProfile.Mass, seed, wide: true);
        applied.Position = 0;
        TableSnapshot.Read(third.Table, 1, applied);
        Assert.Equal(expected, third.Policy.LicenceMask(2));
    }

    [Fact]
    public void MaskReadFromANarrowPolicy_KnowsItsWidth_HasThrowsAbove63()
    {
        var narrow = new Rig(PolicyProfile.Mass, 1);
        LicenceMask m = narrow.Policy.LicenceMask(2);
        Assert.Equal(63, m.Capacity);
        Assert.False(LicenceRules.Has(m, 63));
        Assert.Throws<ArgumentOutOfRangeException>(() => LicenceRules.Has(m, 64));   // used to answer false
        Assert.Throws<ArgumentOutOfRangeException>(() => narrow.Policy.OfferMask(2).Has(77));
        Assert.Equal(127, new Rig(PolicyProfile.Mass, 1, wide: true).Policy.LicenceMask(2).Capacity);
        Assert.False(LicenceRules.Has(new Rig(PolicyProfile.Mass, 1, wide: true).Policy.LicenceMask(2), 64));
    }

    [Fact]
    public void Licensable_FollowsTheCapacityOfTheMask_AndDoesNotThrowAboveIt()
    {
        var narrow = new Rig(PolicyProfile.Mass, 1);
        var wide = new Rig(PolicyProfile.Mass, 1, wide: true);
        Assert.Equal(63, narrow.Policy.LicenceCapacity);
        Assert.Equal(127, wide.Policy.LicenceCapacity);
        Assert.True(narrow.Policy.Licensable(63));
        Assert.All((int[])[0, 64, 138, 500], n => Assert.False(narrow.Policy.Licensable(n)));   // synthetic worlds reach node 180 and more: no licence, no exception
        Assert.True(wide.Policy.Licensable(64) && wide.Policy.Licensable(127));
        Assert.All((int[])[0, 128, 138], n => Assert.False(wide.Policy.Licensable(n)));
    }

    [Fact]
    public void NarrowPolicy_HasNoSecondWordColumns_AndRefusesANodeAbove63()
    {
        ulong seed = SeedWhere(true, true);
        var narrow = new Rig(PolicyProfile.Mass, seed);
        Assert.DoesNotContain(narrow.Table.Columns, c => c.Name.EndsWith(".mask.1", StringComparison.Ordinal) || c.Name.EndsWith(".offer.1", StringComparison.Ordinal));
        Assert.Contains(new Rig(PolicyProfile.Mass, seed, wide: true).Table.Columns, c => c.Name == "pol.lic.mask.1");
        narrow.Events.Mask = [LicenceRules.Bit(77), LicenceRules.Bit(39), default];
        Assert.ThrowsAny<Exception>(() => narrow.Month(0));
    }

    [Fact]
    public void P2_TakesTheFirstOfferBySellerIndex_PaysThePrice_TheOtherOneIsOnlyVisible_ActsFromMonthOne()
    {
        var rig = new Rig(PolicyProfile.Mass, SeedWhere(true, true));
        Assert.Equal(0, rig.Policy.LicenceGen(2));                       // A8: nothing is decided before tick 0
        rig.Month(0);
        PolicySystem p = rig.Policy;
        Assert.Equal(0, p.LicenceGen(2));                                // decided in compute 0, applied at the start of month 1
        Assert.Equal(LicenceRules.Bit(39), p.OfferMask(2));              // the second offer is not taken and stays visible
        rig.Month(1);
        Assert.Equal(1, p.LicenceGen(2));
        Assert.Equal(0, p.LicenceSeller(2));
        Assert.Equal(LicenceRules.Bit(54), p.LicenceMask(2));
        Assert.Equal(1, p.LicenceStart(2));
        Assert.Equal(-Price, p.LicenceFlow(2));
        Assert.Equal(Price, p.LicenceFlow(0));
        Assert.Equal(Fx6.Zero, p.LicenceFlow(1));
        Assert.Equal(Fx6.Zero, p.LicenceFlow(0) + p.LicenceFlow(1) + p.LicenceFlow(2));   // L-M1
        rig.Month(2);
        Assert.Equal(Fx6.Zero, p.LicenceFlow(2));                          // the flow lasts one month
        Assert.Equal(Fx6.Zero, p.LicenceFlow(0));
        Assert.Equal(1, p.LicenceGen(2));                                  // the state stays
        Assert.Equal(LicenceRules.Bit(39), p.OfferMask(2));                // the offers stay until the next yearly step
    }

    [Fact]
    public void ADealRefusedAtTheApplication_ForMoney_LeavesBothSidesUntouched_AndCounts()
    {
        var rig = new Rig(PolicyProfile.Mass, SeedWhere(true, false));
        rig.Month(0);
        PolicySystem p = rig.Policy;
        // The player's shares of month 1 left less than the price: the AI could not know it when it decided.
        p.ApplyDeals(1, _ => Price - Fx6.FromRaw(1));
        Assert.Equal(0, p.LicenceGen(2));
        Assert.Equal(Fx6.Zero, p.LicenceFlow(0) + p.LicenceFlow(2));
        Assert.Equal(1, p.BadLicenceCount(2, BadLicence.Money));
        p.ApplyDeals(2, _ => Price);                                        // the queue is empty now: nothing is applied later
        Assert.Equal(0, p.LicenceGen(2));
    }

    [Fact]
    public void P1_NeverTakes_ButEveryOfferIsVisibleUntilTheNextYear()
    {
        var rig = new Rig(PolicyProfile.Quality, SeedWhere(true, true));
        rig.Month(0);
        Assert.Equal(0, rig.Policy.LicenceGen(2));
        Assert.Equal(LicenceRules.Bit(54) | LicenceRules.Bit(39), rig.Policy.OfferMask(2));
        Assert.Equal(Fx6.Zero, rig.Policy.LicenceFlow(2));
    }

    [Fact]
    public void NoOffer_NoDeal_AndNoOfferForAnEqualOrNewerEngineOfTheBuyer()
    {
        var none = new Rig(PolicyProfile.Mass, SeedWhere(false, false));
        none.Month(1);
        Assert.Equal(0, none.Policy.LicenceGen(2));
        Assert.Equal(default(LicenceMask), none.Policy.OfferMask(2));
        // g_s = g_b: no pair, so no draw and no offer even where the draw would succeed.
        var equal = new Rig(PolicyProfile.Mass, SeedWhere(true, true));
        equal.Events.Gen = [1, 1, 1];
        equal.Month(1);
        Assert.Equal(0, equal.Policy.LicenceGen(2));
        Assert.Equal(default(LicenceMask), equal.Policy.OfferMask(2));
    }

    [Fact]
    public void Money_TheOfferIsRefusedWhenTheBaseLessTheProjectedUpkeepIsBelowThePrice_Boundary()
    {
        // Buyer budget 100 000, P2 procurement share 0,5: B_base(1) = 50 000; the book needs 100 · upkeep.
        ulong seed = SeedWhere(true, false);
        var ok = new Rig(PolicyProfile.Mass, seed, fleetUpkeep: 50_000 - 15_000);     // room = 15 000 = the price: bought
        ok.Month(1);
        Assert.Equal(1, ok.Policy.LicenceGen(2));
        var refuse = new Rig(PolicyProfile.Mass, seed, fleetUpkeep: 50_000 - 15_000 + 1);   // one unit above: refused, and the offer stays visible
        refuse.Month(1);
        Assert.Equal(0, refuse.Policy.LicenceGen(2));
        Assert.Equal(LicenceRules.Bit(54), refuse.Policy.OfferMask(2));
        // Without a fleet the condition is that of the base alone.
        var noFleet = new Rig(PolicyProfile.Mass, seed, buyerBudget: Fx6.FromInt(30_000));   // base 15 000 = the price
        noFleet.Month(1);
        Assert.Equal(1, noFleet.Policy.LicenceGen(2));
        var poor = new Rig(PolicyProfile.Mass, seed, buyerBudget: Fx6.FromRaw(30_000L * Fx6.Scale - 2));
        poor.Month(1);
        Assert.Equal(0, poor.Policy.LicenceGen(2));
    }

    [Fact]
    public void MoneyRefusal_IsTracedForTheWantedOfferTheBuyerCannotPay_AndNotCountedAsABadLicence()
    {
        ulong seed = SeedWhere(true, false);
        var refuse = new Rig(PolicyProfile.Mass, seed, fleetUpkeep: 50_000 - 15_000 + 1);
        refuse.Month(0);
        MoneyRefusal r = Assert.Single(refuse.Policy.LastMoneyRefusals(2));
        Assert.Equal((0, 1), (r.Seller, r.Gen));
        Assert.Equal(Price, r.Price);
        Assert.Equal(Price - Fx6.One, r.Room);
        Assert.Equal(0, refuse.Policy.BadLicenceCount(2, BadLicence.Money));
        Assert.Empty(refuse.Policy.LastMoneyRefusals(0));
        refuse.Month(1);                                                 // the trace is of the last step: month 1 draws no offer
        Assert.Empty(refuse.Policy.LastMoneyRefusals(2));
        var ok = new Rig(PolicyProfile.Mass, seed, fleetUpkeep: 50_000 - 15_000);
        ok.Month(0);
        Assert.Empty(ok.Policy.LastMoneyRefusals(2));
    }

    [Fact]
    public void MoneyRefusal_QualityNeverWantsOne_AndEveryPoorMassOfferIsListedBySellerRow()
    {
        var quality = new Rig(PolicyProfile.Quality, SeedWhere(true, true), fleetUpkeep: 50_000);   // the room is 0 < price, but P1 never asks for a licence
        quality.Month(0);
        Assert.Empty(quality.Policy.LastMoneyRefusals(2));
        var poorMass = new Rig(PolicyProfile.Mass, SeedWhere(true, true), fleetUpkeep: 50_000);
        poorMass.Month(0);
        Assert.Equal([0, 1], poorMass.Policy.LastMoneyRefusals(2).Select(x => x.Seller).ToArray());   // ascending seller row
    }

    [Theory]
    [InlineData(61, true)]
    [InlineData(60, false)]
    [InlineData(null, true)]
    public void P3_BuysWhenTheFirstLinkOfWhyNotWillNotCloseWithin60Months(int? eta, bool buys)
    {
        var rig = new Rig(PolicyProfile.Adaptive, SeedWhere(true, false));
        rig.Events.List = actor => actor == 2 ? [First(eta)] : [];
        rig.Month(1);
        Assert.Equal(buys ? 1 : 0, rig.Policy.LicenceGen(2));
    }

    [Fact]
    public void P3_DoesNotBuyWithoutATarget_TheListIsEmpty()
    {
        var rig = new Rig(PolicyProfile.Adaptive, SeedWhere(true, false));
        rig.Month(1);
        Assert.Equal(0, rig.Policy.LicenceGen(2));
    }

    [Fact]
    public void LicOffAndT6dOff_DrawNothingAndChangeNothing()
    {
        foreach (PolicyParams p in new[] { new PolicyParams { LicOff = true }, new PolicyParams { T6dOff = true } })
        {
            var rig = new Rig(PolicyProfile.Mass, SeedWhere(true, true), parameters: p);
            rig.Month(1);
            Assert.Equal(0, rig.Policy.LicenceGen(2));
            Assert.Equal(default(LicenceMask), rig.Policy.OfferMask(2));
        }
    }

    [Fact]
    public void ANewerEngineOfASellerReplacesTheLicence_AtAYearlyStep_AndTheGenerationNeverFalls()
    {
        var rig = new Rig(PolicyProfile.Mass, SeedWhere(true, false));
        rig.Month(1);
        Assert.Equal(1, rig.Policy.LicenceGen(2));
        rig.Events.Gen = [2, 1, 0];                                       // seller 0 now has the next engine
        // Each yearly step draws once at p = 0,2: the generation only rises, and in forty years the draw has hit.
        for (long t = 2; t <= 12 * 40; t++)
        {
            rig.Month(t);
            Assert.InRange(rig.Policy.LicenceGen(2), 1, 2);
        }
        Assert.Equal(2, rig.Policy.LicenceGen(2));
        Assert.Equal(0, rig.Policy.LicenceSeller(2));
    }

    // ---- the disruption (A43.6)

    [Fact]
    public void Disruption_StartsNextMonth_LastsTheDrawnMonths_AndIsNotDrawnWhileInForce()
    {
        // exposure 1 → p = 8 333 raw per month: in 400 months every actor has windows; the checks are the same as of the session test, on this rig.
        var rig = new Rig(PolicyProfile.Mass, 1);
        int windows = 0, lastEnd = -1;
        for (int m = 0; m < 400; m++)
        {
            rig.Month(m);
            (int start, int end) = rig.Policy.Disruption(0);
            Assert.Equal(start >= 0 && m + 1 >= start && m + 1 <= end, rig.Policy.AlloyDeficit(0, m + 1) == Fx6.FromRaw(500_000));
            if (end != lastEnd)
            {
                windows++;
                Assert.Equal(m + 1, start);
                Assert.InRange(end - m, 6, 18);
                Assert.True(lastEnd < m);
                lastEnd = end;
            }
        }
        Assert.True(windows >= 1);
    }

    // ---- the triggers (A43.7)

    private static Link Qc(WhyNotCause cause = WhyNotCause.CultureH, Dimension dim = Dimension.QualityControl, int process = 54, int? eta = null, LimiterKind? lim = null) =>
        new(WhyNotLayer.Capability, cause, process, Fx6.One, Fx6.FromInt(4), [], Fx6.Zero, eta, process, dim, lim);

    [Theory]
    [InlineData(PolicyProfile.Quality, 0, true)]     // P1: among the first three
    [InlineData(PolicyProfile.Quality, 2, true)]
    [InlineData(PolicyProfile.Quality, 3, false)]    // the fourth is not
    [InlineData(PolicyProfile.Adaptive, 0, true)]    // P3: only the first
    [InlineData(PolicyProfile.Adaptive, 1, false)]
    [InlineData(PolicyProfile.Mass, 0, false)]       // P2 has its own counter
    public void ProgrammeTrigger_P1Top3_P3First_AtTheYearlyStep(PolicyProfile profile, int position, bool starts)
    {
        var rig = new Rig(profile, 1);
        var list = new List<Link> { First(5), First(6), First(7), First(8) };
        list[position] = Qc();
        rig.Events.List = a => a == 2 ? list : [];
        // P1 has a programme from t0 (A8) for 84 months; the trigger is read only for a programme that is not active, so at the first yearly step after its end.
        long step = profile == PolicyProfile.Quality ? 84 : 12;
        rig.Month(step);
        Assert.Equal(starts, rig.Policy.QualityProgrammeActive(2) && rig.Policy.QualityProgrammeStart(2) == step + 1);
    }

    [Theory]
    [InlineData(WhyNotCause.ToolingM, Dimension.QualityControl)]     // the programme lifts only α_eff of the culture, tooling not (F28)
    [InlineData(WhyNotCause.CultureH, Dimension.Precision)]          // another dimension
    public void ProgrammeTrigger_AQcLinkIsOnlyCultureHOfQualityControl(WhyNotCause cause, Dimension dim)
    {
        var rig = new Rig(PolicyProfile.Adaptive, 1);
        rig.Events.List = a => a == 2 ? [Qc(cause, dim)] : [];
        rig.Month(12);
        Assert.False(rig.Policy.QualityProgrammeActive(2));
    }

    [Fact]
    public void ProgrammeTrigger_IsOffUnderT6dOff()
    {
        var rig = new Rig(PolicyProfile.Adaptive, 1, parameters: new PolicyParams { T6dOff = true });
        rig.Events.List = a => a == 2 ? [Qc()] : [];
        rig.Month(12);
        Assert.False(rig.Policy.QualityProgrammeActive(2));
    }
}
