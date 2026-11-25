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

/// <summary>The licence market (TASK-0152): refusals by reason, the catalogue, the AI rules and the limit on what a player can drain from an AI.</summary>
public class LicenceMarketTests
{
    private const int Designs = 12;
    private static readonly Fx6 Cost = Fx6.FromInt(1000);     // reference price 1000 · 50 · 0.3 = 15 000
    private static readonly Fx6 Ref = Fx6.FromInt(15_000);

    private sealed class Prod : IPolicyProducts
    {
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
    }

    private sealed class Ev : IPolicyEvents
    {
        public int[] Gen = [2, 2, 0];
        public bool[] OwnProject = [true, true, false];   // false: the seller's serial project is a bought one, which the catalogue never lists
        public int? Eta;
        public bool SlotFree = true;
        public bool HasFreeDesignSlot(int actorRow) => SlotFree;
        public int EngineGen(int actorRow, long month) => Gen[actorRow];
        public LicenceSubject? Subject(int sellerRow, int gen, long month) => OwnProject[sellerRow] && gen <= Gen[sellerRow] ? new LicenceSubject(Cost, LicenceRules.Bit(54)) : null;
        public IReadOnlyList<Link> WhyNot(int actorRow, long month) => [new Link(WhyNotLayer.Knowledge, WhyNotCause.MissingNode, 58, Fx6.Zero, Fx6.One, [], Fx6.Zero, null)];
        public IReadOnlyList<Link> WhyNotDesign(int actorRow, DesignHandle design, long month) => WhyNot(actorRow, month);
        public int? OwnEtaMonths(int actorRow, int gen, long month) => Eta;
    }

    private sealed class NoInputs : IPolicyInputs
    {
        public DesignHandle? NewestSeriesDesign(int actorRow, long month) => null;
        public BottleneckLimit? Bottleneck(int actorRow, int design, long month) => null;
        public bool SeriesBelowQuality(int actorRow, Fx6 threshold, long month) => false;
    }

    // Three actors A (0), B (1) sellers and C (2) buyer. Row 2 is the player unless <external> says otherwise.
    private sealed class Rig
    {
        public readonly Prod Products = new();
        public readonly Ev Events = new();
        public readonly PolicySystem Policy;
        private readonly TickRunner _runner;
        private readonly TickCalendar _calendar = new(new CivilDate(1945, 9, 1));

        public Rig(PolicyProfile buyer, int[]? external = null, PolicyProfile seller = PolicyProfile.Adaptive, Fx6? buyerBudget = null, AllianceTable? alliances = null, Action<PolicyParams>? _ = null,
            Func<PolicyParams, PolicyParams>? tune = null)
        {
            var table = new Table(3);
            PolicyParams p = new PolicyParams
            {
                FvBudget = Fx6.FromInt(10), LicenceMarket = true, LicenceExternalRows = [.. external ?? [2]],
                MarketActorIds = ["AAA", "BBB", "CCC"], Alliances = alliances ?? AllianceTable.Empty, MarketStartYear = 1945, MarketStartMonth = 9,
            };
            p = tune?.Invoke(p) ?? p;
            Policy = new PolicySystem(table, p, [0, 1, 2], [Fx6.FromInt(100_000), Fx6.FromInt(100_000), buyerBudget ?? Fx6.FromInt(100_000)], [Fx6.Zero, Fx6.Zero, Fx6.Zero],
                new CounterRng(1), new NoInputs(), new IndustryMailbox(3), 99, [(int)seller, (int)seller, (int)buyer],
                products: Products, designs: Designs, generations: Probe.Generations, events: new CounterRng(5), eventInputs: Events);
            Policy.Initialize();
            _runner = new TickRunner(table, 1, [Policy], 1, 1, _calendar);
        }

        public Fx6 Funds(int b) => Policy.Shares(b).Procurement * Policy.Budget(b);

        public void Month(long month)
        {
            while (_runner.Tick <= _calendar.TickOfMonth(month))
            {
                if (_calendar.IsDue(Cadence.Monthly, _runner.Tick))
                    Policy.ApplyDeals(_calendar.MonthIndex(_runner.Tick), Funds);
                _runner.RunTick();
            }
        }

        // The player's command of month m: queued between ticks, applied at the start of the next monthly step.
        public void Buy(int buyer, int seller, int gen, long month)
        {
            Assert.True(Policy.QueueBuy(buyer, seller, gen, month));
            Month(month + 1);
        }
    }

    // ---- refusals: each reason has its own test

    [Fact]
    public void Buy_Succeeds_RecordsTheKey_PaysFromTheBuyer_AndKeepsTheLegacyAggregate()
    {
        var rig = new Rig(PolicyProfile.Adaptive);
        rig.Month(0);
        Fx6 price = rig.Policy.Offer(0, 2, 0)!.Value.Price;
        Assert.Equal(Ref, price);   // an Adaptive AI seller asks m = 1
        rig.Buy(2, 0, 2, 0);
        Assert.Single(rig.Policy.Licences(2));
        Assert.Equal(new LicenceRecord(0, 2, 1, LicenceRules.Bit(54)), rig.Policy.Licences(2)[0]);
        Assert.Equal(-price, rig.Policy.LicenceFlow(2));
        Assert.Equal(price, rig.Policy.LicenceFlow(0));
        Assert.Equal((2, 0), (rig.Policy.LicenceGen(2), rig.Policy.LicenceSeller(2)));
        Assert.True(rig.Policy.LicenceMask(2).Has(54));
        Assert.Equal(1, rig.Policy.NewLicences(2, 1, stackalloc (int, int)[4]));
    }

    [Fact]
    public void Buy_Self_IsRefused()
    {
        var rig = new Rig(PolicyProfile.Adaptive, external: [0, 2]);
        rig.Month(0);
        Assert.Equal(BadLicence.Self, rig.Policy.CheckBuy(0, 0, 2, 0, rig.Funds(0), out _));
    }

    [Fact]
    public void Buy_Empty_WhenTheSellerHasNoOwnSerialProjectOfThatGeneration()
    {
        var rig = new Rig(PolicyProfile.Adaptive);
        rig.Events.OwnProject[1] = false;   // seller B only has a bought project: not in the catalogue (#477)
        rig.Month(0);
        Assert.Null(rig.Policy.Offer(1, 2, 0));
        Assert.Equal(BadLicence.Empty, rig.Policy.CheckBuy(2, 1, 2, 0, rig.Funds(2), out _));
        Assert.Equal(BadLicence.Empty, rig.Policy.CheckBuy(2, 0, 3, 0, rig.Funds(2), out _));   // generation 3: the seller has not reached it
        rig.Buy(2, 1, 2, 0);
        Assert.Equal(1L, rig.Policy.BadLicenceCount(2, BadLicence.Empty));
        Assert.Empty(rig.Policy.Licences(2));
    }

    [Fact]
    public void Buy_Gen_WhenTheKeyIsHeld_OrTheBuyerAlreadyBuildsTheGeneration()
    {
        var rig = new Rig(PolicyProfile.Adaptive);
        rig.Month(0);
        rig.Buy(2, 0, 2, 0);
        Assert.Equal(BadLicence.Gen, rig.Policy.CheckBuy(2, 0, 2, 1, rig.Funds(2), out _));
        // A second seller of the same generation is another key, but the buyer's own engine already reaches it.
        rig.Events.Gen[2] = 2;
        Assert.Equal(BadLicence.Gen, rig.Policy.CheckBuy(2, 1, 2, 1, rig.Funds(2), out _));
        rig.Month(2);
        Assert.Single(rig.Policy.Licences(2));
    }

    [Fact]
    public void Buy_SeveralLicencesAtOnce_AreKeptByTheKey()
    {
        var rig = new Rig(PolicyProfile.Adaptive);
        rig.Events.Gen = [3, 2, 0];
        rig.Month(0);
        rig.Buy(2, 0, 3, 0);
        rig.Buy(2, 1, 2, 1);
        Assert.Equal(2, rig.Policy.Licences(2).Count);
        Assert.Equal(3, rig.Policy.LicenceGen(2));   // the aggregate is the highest generation
    }

    [Fact]
    public void Buy_Money_WhenTheFundsAreBelowThePrice_AndNothingIsPaid()
    {
        var rig = new Rig(PolicyProfile.Adaptive, buyerBudget: Fx6.FromInt(1000));
        rig.Month(0);
        Assert.Equal(BadLicence.Money, rig.Policy.CheckBuy(2, 0, 2, 0, rig.Funds(2), out _));
        rig.Buy(2, 0, 2, 0);
        Assert.Equal(1L, rig.Policy.BadLicenceCount(2, BadLicence.Money));
        Assert.Equal(Fx6.Zero, rig.Policy.LicenceFlow(2));
        Assert.Equal((BadLicence.Money, 0, 2), (rig.Policy.LastRefusal(2).Reason, rig.Policy.LastRefusal(2).Seller, rig.Policy.LastRefusal(2).Gen));
    }

    [Fact]
    public void Buy_Circle_FollowsTheBlocAndTheFriendsOfTheYear_AndTheClosedCatalogue()
    {
        AllianceTable alliances = AllianceTable.Parse("schema: 1\nblocs:\n  - id: x\n    members:\n      - {actor: AAA, from: 1946}\n      - {actor: CCC, from: 1946}\nfriends:\n  - {a: BBB, b: CCC, from: 1945, to: 1945}\n", "t.yaml");
        var rig = new Rig(PolicyProfile.Adaptive, alliances: alliances);
        rig.Month(0);
        Assert.Equal(SaleCircle.All, rig.Policy.Offer(0, 2, 0)!.Value.Circle);   // an AI seller sells to everybody by default
        Assert.Equal(OfferRefusal.None, rig.Policy.SetOffer(0, 2, Fx6.One, SaleCircle.Bloc, 0));
        // 1945: A and C are not yet in the bloc.
        Assert.Equal(BadLicence.Circle, rig.Policy.CheckBuy(2, 0, 2, 0, rig.Funds(2), out _));
        rig.Month(12);   // September 1946
        Assert.Equal(BadLicence.None, rig.Policy.CheckBuy(2, 0, 2, 12, rig.Funds(2), out _));
        // Friends are not the bloc: B closes its circle to the bloc only and C is its friend in 1945 only.
        Assert.Equal(OfferRefusal.None, rig.Policy.SetOffer(1, 2, Fx6.One, SaleCircle.Friends, 12));
        Assert.Equal(BadLicence.Circle, rig.Policy.CheckBuy(2, 1, 2, 12, rig.Funds(2), out _));
        Assert.Equal(OfferRefusal.None, rig.Policy.SetOffer(0, 2, Fx6.One, SaleCircle.Nobody, 12));
        Assert.Equal(BadLicence.Circle, rig.Policy.CheckBuy(2, 0, 2, 12, rig.Funds(2), out _));
    }

    [Fact]
    public void Buy_Capacity_WhenNoProjectSlotIsFree_BeforeAnythingIsPaid()
    {
        var rig = new Rig(PolicyProfile.Adaptive);
        rig.Month(0);
        rig.Events.SlotFree = false;
        rig.Buy(2, 0, 2, 0);
        Assert.Equal(1L, rig.Policy.BadLicenceCount(2, BadLicence.Capacity));
        Assert.Equal(Fx6.Zero, rig.Policy.LicenceFlow(2));
        Assert.Empty(rig.Policy.Licences(2));
    }

    [Fact]
    public void Buy_Cap_StopsTheLicenceIncomeOfAPlayerSellerAtTenPercentOfTheYearlyBudget()
    {
        // Seller 0 is the player: income cap = 10 % · 12 · budget(0). Budget 1000 → 1200 a year; one licence costs 15 000.
        var rig = new Rig(PolicyProfile.Adaptive, external: [0], tune: p => p);
        rig.Month(0);
        rig.Policy.SetOffer(0, 2, Fx6.One, SaleCircle.All, 0);
        Assert.Equal(BadLicence.None, rig.Policy.CheckBuy(2, 0, 2, 0, rig.Funds(2), out _));   // 15 000 ≤ 0,1 · 12 · 100 000
        var poor = new Rig(PolicyProfile.Adaptive, external: [0, 2], buyerBudget: Fx6.FromInt(100_000));
        poor.Month(0);
        poor.Policy.SetOffer(0, 2, Fx6.One, SaleCircle.All, 0);
        // The cap is relative to the seller's budget: 120 000 here, so two licences of 15 000 fit and the income is counted per year.
        poor.Buy(2, 0, 2, 0);
        Assert.Equal(Ref, poor.Policy.LicenceIncome(0, 1));
        Fx6 cap = poor.Policy.Budget(0) * Fx6.FromInt(12) / Fx6.FromInt(10);
        Assert.True(poor.Policy.LicenceIncome(0, 1) + Ref <= cap);
        var tight = new Rig(PolicyProfile.Adaptive, external: [0], tune: p => p with { IncomeCapShare = Fx6.FromRaw(10_000) });   // 1 %: 12 000 a year, below one licence
        tight.Month(0);
        tight.Policy.SetOffer(0, 2, Fx6.One, SaleCircle.All, 0);
        Assert.Equal(BadLicence.Cap, tight.Policy.CheckBuy(2, 0, 2, 0, tight.Funds(2), out _));
    }

    [Fact]
    public void Buy_Cap_CountsTheDealsOfTheSameMonth_TwoAiBuyersEachUnderTheCapAndTogetherOverIt()
    {
        // The player's catalogue is open to all; the cap allows 20 000 a year and one licence costs 15 000, so the second buyer of the month must meet Cap.
        var rig = new Rig(PolicyProfile.Mass, external: [0], seller: PolicyProfile.Mass, tune: p => p with { IncomeCapShare = Fx6.FromRaw(16_667) });
        rig.Events.Gen = [2, 0, 0];
        rig.Events.OwnProject = [true, false, false];
        Assert.Equal(OfferRefusal.None, rig.Policy.SetOffer(0, 2, Fx6.One, SaleCircle.All, 0));
        rig.Month(1);
        int held = rig.Policy.Licences(1).Count + rig.Policy.Licences(2).Count;
        Assert.Equal(1, held);
        Assert.Equal(1L, rig.Policy.BadLicenceCount(1, BadLicence.Cap) + rig.Policy.BadLicenceCount(2, BadLicence.Cap));
        Assert.Equal(Ref, rig.Policy.LicenceIncome(0, 1));
    }

    [Fact]
    public void AiDeal_IsRefusedAtSettlement_WhenTheSellerRaisedThePriceAboveTheCeilingAfterTheDecision()
    {
        // Month 0: the AI buyer decides at m = 1.5 (the ceiling). Before the deal is settled the player's offer goes to m = 2, which the funds still cover.
        var rig = new Rig(PolicyProfile.Mass, external: [0], buyerBudget: Fx6.FromInt(200_000));
        rig.Events.OwnProject[1] = false;
        Assert.Equal(OfferRefusal.None, rig.Policy.SetOffer(0, 2, Fx6.FromRaw(1_500_000), SaleCircle.All, 0));
        rig.Month(0);
        Assert.Equal(OfferRefusal.None, rig.Policy.SetOffer(0, 2, Fx6.FromInt(2), SaleCircle.All, 1));
        rig.Month(1);
        Assert.Empty(rig.Policy.Licences(2));
        Assert.Equal(1L, rig.Policy.BadLicenceCount(2, BadLicence.Price));
        Assert.Equal(Fx6.Zero, rig.Policy.LicenceFlow(0));
    }

    [Fact]
    public void Licence_LapsesWhenTheBuyersOwnEnginePassesIt_AndTheSlotAndTheMaskAreFreed()
    {
        var rig = new Rig(PolicyProfile.Adaptive, external: [2]);
        rig.Events.Gen = [4, 2, 0];
        rig.Month(0);
        rig.Buy(2, 0, 4, 0);
        rig.Buy(2, 1, 2, 1);
        Assert.Equal(2, rig.Policy.Licences(2).Count);
        rig.Events.Gen[2] = 3;   // the buyer's own engine passes G2 but not G4
        rig.Month(3);
        LicenceRecord left = Assert.Single(rig.Policy.Licences(2));
        Assert.Equal(4, left.Gen);
        Assert.Equal(4, rig.Policy.LicenceGen(2));
        rig.Events.Gen[2] = 5;   // now G4 too
        rig.Month(4);
        Assert.Empty(rig.Policy.Licences(2));
        Assert.Equal(0, rig.Policy.LicenceGen(2));
        Assert.Equal(-1, rig.Policy.LicenceSeller(2));
        Assert.True(rig.Policy.LicenceMask(2).IsEmpty);
    }

    // ---- the catalogue

    [Fact]
    public void Offer_RejectsAMultiplierOutsideTheRange_AndABoughtProject()
    {
        var rig = new Rig(PolicyProfile.Adaptive, external: [0, 2]);
        rig.Month(0);
        Assert.Equal(OfferRefusal.BadMultiplier, rig.Policy.SetOffer(0, 2, Fx6.FromRaw(499_999), SaleCircle.All, 0));
        Assert.Equal(OfferRefusal.BadMultiplier, rig.Policy.SetOffer(0, 2, Fx6.FromRaw(2_000_001), SaleCircle.All, 0));
        Assert.Equal(OfferRefusal.None, rig.Policy.SetOffer(0, 2, Fx6.FromRaw(2_000_000), SaleCircle.All, 0));
        Assert.Equal(Ref * Fx6.FromInt(2), rig.Policy.Offer(0, 2, 0)!.Value.Price);
        Assert.Equal(OfferRefusal.NoOwnProject, rig.Policy.SetOffer(0, 3, Fx6.One, SaleCircle.All, 0));
        rig.Events.OwnProject[0] = false;
        Assert.Equal(OfferRefusal.NoOwnProject, rig.Policy.SetOffer(0, 2, Fx6.One, SaleCircle.All, 0));
        Assert.Equal(OfferRefusal.BadGen, rig.Policy.SetOffer(0, 99, Fx6.One, SaleCircle.All, 0));
    }

    [Fact]
    public void AiSellers_AskByProfile_QualityDearMassCheap()
    {
        Assert.Equal(Ref * Fx6.FromRaw(1_300_000), PriceOf(PolicyProfile.Quality));
        Assert.Equal(Ref * Fx6.FromRaw(800_000), PriceOf(PolicyProfile.Mass));
        Assert.Equal(Ref, PriceOf(PolicyProfile.Adaptive));
        static Fx6 PriceOf(PolicyProfile p)
        {
            var rig = new Rig(PolicyProfile.Adaptive, seller: p);
            rig.Month(0);
            return rig.Policy.Offer(0, 2, 0)!.Value.Price;
        }
    }

    [Fact]
    public void WorldAverage_NeedsTwoAiCatalogues_AndLeavesThePlayerOut()
    {
        var rig = new Rig(PolicyProfile.Adaptive, external: [1, 2]);
        rig.Month(0);
        Assert.Null(rig.Policy.WorldAverage(2, 0));   // only A is an AI seller: a thin market
        Assert.Equal(Ref, new Rig(PolicyProfile.Adaptive).Also(r => r.Month(0)).Policy.WorldAverage(2, 0));
        Assert.Equal(OfferRefusal.None, rig.Policy.SetOffer(1, 2, Fx6.FromInt(2), SaleCircle.All, 0));
        Assert.Null(rig.Policy.WorldAverage(2, 0));   // the player's price does not enter the average
    }

    // ---- the AI buys

    [Fact]
    public void AiBuyer_TakesAnOfferAtOrBelowOnePointFiveTimesTheReference_AndRefusesAbove()
    {
        // Buyer C is an AI (Mass takes every offer that passes the rules); the seller is the player and sets the price.
        var cheap = new Rig(PolicyProfile.Mass, external: [0]);
        cheap.Events.OwnProject[1] = false;   // B sells nothing: only the player's price is on offer
        Assert.Equal(OfferRefusal.None, cheap.Policy.SetOffer(0, 2, Fx6.FromRaw(1_500_000), SaleCircle.All, 0));
        cheap.Month(1);
        Assert.Single(cheap.Policy.Licences(2));

        var dear = new Rig(PolicyProfile.Mass, external: [0]);
        dear.Events.OwnProject[1] = false;
        dear.Month(0);
        Assert.Equal(OfferRefusal.None, dear.Policy.SetOffer(0, 2, Fx6.FromInt(2), SaleCircle.All, 0));
        dear.Month(12);
        Assert.Empty(dear.Policy.Licences(2));
    }

    [Fact]
    public void AiBuyer_QualityDoesNotBuy_AndNobodyBuysWhatItReachesWithinAYear()
    {
        var quality = new Rig(PolicyProfile.Quality, external: [0]);
        quality.Month(1);
        Assert.Empty(quality.Policy.Licences(2));
        var near = new Rig(PolicyProfile.Mass, external: [0]) { };
        near.Events.Eta = 11;
        near.Month(1);
        Assert.Empty(near.Policy.Licences(2));
        var far = new Rig(PolicyProfile.Mass, external: [0]);
        far.Events.Eta = 12;
        far.Month(1);
        Assert.Single(far.Policy.Licences(2));
    }

    [Fact]
    public void PlayerCannotDrainAnAi_ByPrice_OverTwentyYears()
    {
        // Whatever multiplier the player sets in [0.5; 2], over 20 yearly decisions the AI buyer pays at most min(1.5 · ref, L) per licence, once per key,
        // and its procurement base never falls below half a month's worth in the month of a payment.
        foreach (long raw in (long[])[500_000, 1_000_000, 1_500_000, 1_500_001, 2_000_000])
        {
            var rig = new Rig(PolicyProfile.Mass, external: [0]);
            rig.Events.OwnProject[1] = false;
            rig.Events.Gen = [6, 2, 0];
            Fx6 paid = Fx6.Zero;
            Fx6 minAfter = Fx6.FromInt(1_000_000);
            for (long t = 0; t < 240; t++)
            {
                if (t % 12 == 0)
                {
                    for (int gen = 3; gen <= 6; gen++)
                        rig.Policy.SetOffer(0, gen, Fx6.FromRaw(raw), SaleCircle.All, t);
                    rig.Events.Gen[2] = 2;   // the buyer's engine does not advance: it always wants the licence
                }
                rig.Month(t);
                Fx6 flow = rig.Policy.LicenceFlow(2);
                if (flow < Fx6.Zero)
                {
                    paid += -flow;
                    minAfter = Fx6.FromRaw(Math.Min(minAfter.Raw, (rig.Funds(2) + flow).Raw));
                }
            }
            Fx6 bound = Fx6.Zero;
            foreach (LicenceRecord r in rig.Policy.Licences(2))
                bound += Fx6.FromRaw(Math.Min((rig.Policy.Offer(0, r.Gen, 240)!.Value.Reference * Fx6.FromRaw(1_500_000)).Raw, rig.Funds(2).Raw * 6));
            Assert.True(paid <= bound, $"m={raw}: paid {paid} above {bound}");
            Assert.True(raw > 1_500_000 ? rig.Policy.Licences(2).Count == 0 : rig.Policy.Licences(2).Count > 0, $"m={raw}: {rig.Policy.Licences(2).Count} licences");
            if (paid > Fx6.Zero)
                Assert.True(minAfter >= rig.Funds(2) / Fx6.FromInt(2), $"m={raw}: left {minAfter}");
        }
    }

    [Fact]
    public void PlayerCannotDrainAnAi_TheTestFailsWithoutTheCeilingK()
    {
        // The same deal with the ceiling removed (k = 10): the AI pays 2 × ref, so the bound of the test above would break.
        var rig = new Rig(PolicyProfile.Mass, external: [0], tune: p => p with { AiPriceK = Fx6.FromInt(10) });
        rig.Events.Gen = [6, 2, 0];
        rig.Events.OwnProject[1] = false;
        for (int gen = 3; gen <= 6; gen++)
            Assert.Equal(OfferRefusal.None, rig.Policy.SetOffer(0, gen, Fx6.FromRaw(1_600_000), SaleCircle.All, 0));
        rig.Month(1);
        Assert.Single(rig.Policy.Licences(2));   // with k = 1.5 the same offer is refused (see the test above)
        Assert.Equal(Ref * Fx6.FromRaw(1_600_000), -rig.Policy.LicenceFlow(2));
    }

    // ---- the pure rules and the data

    [Fact]
    public void AiRule_NamesTheFirstFailedRule()
    {
        Fx6 k = Fx6.FromRaw(1_500_000), L = Fx6.FromInt(100);
        Assert.Equal(AiDecline.None, LicenceRules.AiRule(Fx6.FromInt(15), Fx6.FromInt(10), k, null, 12, L));
        Assert.Equal(AiDecline.PriceAboveK, LicenceRules.AiRule(Fx6.FromRaw(15_000_001), Fx6.FromInt(10), k, null, 12, L));
        Assert.Equal(AiDecline.OwnEtaShort, LicenceRules.AiRule(Fx6.FromInt(10), Fx6.FromInt(10), k, 3, 12, L));
        Assert.Equal(AiDecline.Reserve, LicenceRules.AiRule(Fx6.FromInt(10), Fx6.FromInt(10), k, 40, 12, Fx6.FromInt(9)));
    }

    [Fact]
    public void Alliances_RealFile_LoadsAndAnswersByYear()
    {
        AllianceTable t = AllianceTable.Load(Path.Combine(Singularity.Core.Tests.Data.TestMods.CoreDir, "scenarios", "1945", "alliances.yaml"));
        Assert.Equal("", t.BlocOf("FRA", 1948));
        Assert.Equal("nato", t.BlocOf("FRA", 1949));
        Assert.Equal("", t.BlocOf("SWE", 2023));
        Assert.Equal("nato", t.BlocOf("SWE", 2024));
        Assert.Equal("warsaw", t.BlocOf("SUN", 1991));
        Assert.Equal("", t.BlocOf("SUN", 1992));
        Assert.True(t.InCircle("USA", "GBR", 1950, SaleCircle.Bloc));
        Assert.False(t.InCircle("USA", "SUN", 1950, SaleCircle.Friends));
        Assert.True(t.InCircle("GBR", "SUN", 1950, SaleCircle.Friends));
        Assert.False(t.InCircle("GBR", "SUN", 1950, SaleCircle.Bloc));
        Assert.False(t.InCircle("GBR", "SUN", 1956, SaleCircle.Friends));
        Assert.False(t.InCircle("USA", "USA", 1950, SaleCircle.All));
        Assert.True(t.InCircle("USA", "SUN", 1950, SaleCircle.All));
    }

    [Fact]
    public void Alliances_TwoBlocsInOneYear_IsALoadError()
    {
        const string text = "schema: 1\nblocs:\n  - id: a\n    members:\n      - {actor: AAA, from: 1950}\n  - id: b\n    members:\n      - {actor: AAA, from: 1960}\nfriends: []\n";
        Assert.Throws<DataLoadException>(() => AllianceTable.Parse(text, "t.yaml"));
    }
}

internal static class RigExtensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
