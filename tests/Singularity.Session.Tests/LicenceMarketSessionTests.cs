using System.Collections.Immutable;
using Singularity.Core.Commands;
using Singularity.Core.Data;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>The licence market in a running slice (TASK-0152): commands, the deal queue paid from the reserve, AdoptLicensedDesign, production access, replay, threads, and the neutrality of the flag off.</summary>
public class LicenceMarketSessionTests
{
    private static AllianceTable Alliances => AllianceTable.Load(Path.Combine(SliceTestData.RepoRoot, "mods", "core", "scenarios", "1945", "alliances.yaml"));

    private static SliceRunConfig Config(int player, ulong seed = 7, int threads = 1, bool reserve = true) => new()
    {
        Seed = seed, Threads = threads, Reserve = reserve, LicenceMarket = true, LicenceExternalActors = [player], Alliances = Alliances,
    };

    // Half of the budget goes to the reserve: the player has money for a licence after two years.
    private static readonly BudgetShares Half = new(Fx6.FromRaw(150_000), Fx6.FromRaw(100_000), Fx6.FromRaw(50_000), Fx6.FromRaw(200_000), Fx6.Zero);

    private static SliceSession Prepared(int buyer, int threads = 1)
    {
        SliceSession s = SliceTestData.Create(Config(buyer, threads: threads));
        Assert.True(s.Submit(SourceClass.Player, buyer, 1, new SetBudgetShares(buyer, Half)).Accepted);
        s.StepThroughMonth(24);
        return s;
    }

    // A buyer, seller and generation the market lets through after two years of saving (the player buys from an AI whose catalogue is open to everybody).
    private static (int Buyer, int Seller, int Gen) FindDeal(out SliceSession session)
    {
        var all = new List<string>();
        for (int buyer = 0; buyer < SliceTestData.Data.Scenario("jet_slice_1945").ActorCount; buyer++)
        {
            session = Prepared(buyer);
            PolicySystem pol = session.World.Policy;
            for (int seller = 0; seller < session.World.Scenario.ActorCount; seller++)
            {
                for (int gen = 1; gen <= session.World.ProductsLayout.Generations; gen++)
                {
                    if (pol.Offer(seller, gen, 25) is null)
                        continue;
                    BadLicence r = pol.CheckBuy(buyer, seller, gen, 25, session.World.LicenceFunds(buyer, 25), out _);
                    all.Add($"{buyer}<-{seller} g{gen}: {r}");
                    if (r == BadLicence.None)
                        return (buyer, seller, gen);
                }
            }
        }
        throw new InvalidOperationException("no open deal after two years: " + string.Join("; ", all));
    }

    private static List<int> MaskedUnknown(SliceSession s, int buyer)
    {
        var list = new List<int>();
        LicenceMask mask = s.World.Policy.LicenceMask(buyer);
        for (int n = 1; n <= s.World.Policy.LicenceCapacity; n++)
        {
            if (s.World.Policy.Licensable(n) && mask.Has(n) && s.World.IndustryLayout.RowOf(n) >= 0 && !s.World.Knowledge.IsKnown(buyer, s.World.Graph.RowOf(n)))
                list.Add(n);
        }
        return list;
    }

    [Fact]
    public void BuyLicence_PaysFromTheReserve_CreatesALicensedProject_GrantsProduction_AndReplaysTheSame()
    {
        (int buyer, int seller, int gen) = FindDeal(out SliceSession s);
        PolicySystem pol = s.World.Policy;
        Fx6 price = pol.Offer(seller, gen, 25)!.Value.Price;
        Fx6 reserveBefore = s.World.Reserve!.Balance(buyer);
        CommandVerdict v = s.Submit(SourceClass.Player, buyer, 2, new BuyLicence(buyer, seller, gen));
        Assert.True(v.Accepted, v.Message);
        s.StepThroughMonth(25);
        Assert.Contains("accepted", s.Outcomes[^1], StringComparison.Ordinal);
        Assert.Contains(pol.Licences(buyer), r => r.Seller == seller && r.Gen == gen);
        Assert.Equal(-price, pol.LicenceFlow(buyer));
        Assert.Equal(Fx6.Zero, s.World.ProcurementBudget(buyer) - s.World.ProcurementBase(buyer));   // the fee is not in the procurement item
        Assert.True(s.World.Reserve.Balance(buyer) <= reserveBefore - price + s.World.ReserveShare(buyer) * pol.Budget(buyer) + Fx6.One, "the reserve did not pay the fee");
        // The licence is a project of the buyer: stage О, from the seller's own project, with its own gate month.
        s.StepThroughMonth(26);
        ActorView a = s.View.Actors[buyer];
        DesignView? licensed = a.Designs.FirstOrDefault(d => d.LicensedFrom == seller);
        Assert.NotNull(licensed);
        Assert.Equal(DesignStage.Prototype, licensed.Stage);
        Assert.True(licensed.Handle.Generation >= 1);
        Assert.Equal(25, licensed.StageOMonth);
        Assert.Contains(a.Market!.Held, h => h.Seller == seller && h.Gen == gen);
        Assert.Null(pol.Offer(buyer, gen, 26));   // the licensed project is not for sale (#477)
        // LP-1 (#158): production right without knowledge. A masked process the buyer has not researched is producible, a research-only check is not.
        List<int> unknown = MaskedUnknown(s, buyer);
        Assert.NotEmpty(unknown);
        Assert.All(unknown, n => Assert.True(s.World.IndustryInputs.KnowsProcess(buyer, s.World.IndustryLayout.RowOf(n), 26), $"process {n} stays unusable after the licence"));
        Assert.All(unknown, n => Assert.False(s.World.Knowledge.IsKnown(buyer, s.World.Graph.RowOf(n)), "the licence must not teach the node"));
        // The whole project is licensed, not only the engine and the hot section: every process of the licensed design can be produced.
        Assert.All(licensed.PrcSet, n => Assert.True(s.World.IndustryInputs.KnowsProcess(buyer, s.World.IndustryLayout.RowOf(n), 26), $"process {n} of the licensed design is unusable"));
        // A licensed project in series is not the buyer's own engine: it neither raises g_a nor lapses the licence it came with (A43.4).
        int genBefore = s.World.EngineGen(buyer);
        s.Submit(SourceClass.Player, buyer, 3, new StartSeries(buyer, licensed.Handle, Fx6.FromInt(1), true));
        s.StepThroughMonth(30);
        Assert.Equal(DesignStage.Series, s.View.Actors[buyer].Designs.First(d => d.LicensedFrom == seller).Stage);
        Assert.Equal(genBefore, s.World.EngineGen(buyer));
        Assert.Contains(pol.Licences(buyer), r => r.Seller == seller && r.Gen == gen);

        SliceSession replay = SliceSession.Replay(SliceTestData.Data, Config(buyer), s.CommandLog, s.Tick);
        HashAssert.Equal(s.LastHash, replay.LastHash);
    }

    [Fact]
    public void SecondBuyOfTheSameMonth_IsRefused_AndAProgrammeCannotTakeTheMoneyOfAnAcceptedDeal()
    {
        (int buyer, int seller, int gen) = FindDeal(out SliceSession s);
        Assert.True(s.Submit(SourceClass.Player, buyer, 2, new BuyLicence(buyer, seller, gen)).Accepted);
        Assert.True(s.Submit(SourceClass.Player, buyer, 3, new BuyLicence(buyer, seller, gen)).Accepted);
        int row = Enumerable.Range(0, s.World.Graph.Count).First(r => s.World.Knowledge.InFrontier(buyer, r) && !s.World.Graph.IsHazard[r]);
        Fx6 all = s.World.Reserve!.Spendable(buyer);
        Assert.True(s.Submit(SourceClass.Player, buyer, 4, new FundProgramme(buyer, s.World.Graph.Number[row], all)).Accepted);
        s.StepThroughMonth(25);
        string[] out3 = [.. s.Outcomes.TakeLast(3)];
        Assert.Contains("accepted", out3[0], StringComparison.Ordinal);
        Assert.Contains("SlotOverflow", out3[1], StringComparison.Ordinal);
        Assert.Contains("ProgrammeNotFundable", out3[2], StringComparison.Ordinal);
        Assert.Single(s.World.Policy.Licences(buyer));
    }

    [Fact]
    public void LicenceMarket_WithoutTheAlliancesTable_IsRefused_NotSilentlyEmpty()
    {
        Assert.Throws<ArgumentException>(() => SliceTestData.Create(new SliceRunConfig { Seed = 7, LicenceMarket = true }));
    }

    [Fact]
    public void OwnEta_IsZeroForAGenerationWhoseApplicationIsKnown_WhateverTheDesignLinksSay()
    {
        SliceSession s = SliceTestData.Create(Config(0));
        s.StepThroughMonth(0);
        var events = new PolicyEventsAdapter(s.World);
        int checkedPairs = 0;
        for (int a = 0; a < s.World.Scenario.ActorCount; a++)
        {
            for (int g = 1; g <= s.World.ProductsLayout.Generations; g++)
            {
                int row = s.World.Graph.RowOf(s.World.ProductsLayout.GenApp[g - 1]);
                if (row < 0 || !s.World.Knowledge.IsKnown(a, row))
                    continue;
                checkedPairs++;
                Assert.Equal(0, events.OwnEtaMonths(a, g, 1));
            }
        }
        Assert.True(checkedPairs > 0, "no actor knows an application at the start");
    }

    [Fact]
    public void BuyLicence_Refusals_AreNamedWhenTheCommandIsApplied()
    {
        (int buyer, int seller, int gen) = FindDeal(out SliceSession s);
        Assert.True(s.Submit(SourceClass.Player, buyer, 2, new BuyLicence(buyer, buyer, gen)).Accepted);   // the form is fine
        s.StepThroughMonth(25);
        Assert.Contains("Self", s.Outcomes[^1]);
        s.Submit(SourceClass.Player, buyer, 3, new BuyLicence(buyer, seller, 99));
        s.StepThroughMonth(26);
        Assert.Contains("Empty", s.Outcomes[^1]);
    }

    [Fact]
    public void LicenceCommands_AreRefusedForAnActorOutsideExternalControl_WithTheMarketOff_AndWithoutTheReserve()
    {
        SliceSession s = SliceTestData.Create(Config(0));
        s.StepThroughMonth(0);
        s.Submit(SourceClass.Player, 1, 1, new BuyLicence(1, 0, 1));   // actor 1 is an AI
        s.StepThroughMonth(1);
        Assert.Contains("LicenceNotAllowed", s.Outcomes[^1]);
        SliceSession off = SliceTestData.Create(new SliceRunConfig { Seed = 7 });
        off.StepThroughMonth(0);
        off.Submit(SourceClass.Player, 0, 1, new OfferLicence(0, 1, Fx6.One, SaleCircle.All));
        off.StepThroughMonth(1);
        Assert.Contains("LicenceNotAllowed", off.Outcomes[^1]);
        // O-055: the fee is paid from the reserve, so a run without one has no market for the player.
        SliceSession noReserve = SliceTestData.Create(Config(0, reserve: false));
        noReserve.StepThroughMonth(0);
        noReserve.Submit(SourceClass.Player, 0, 1, new BuyLicence(0, 1, 1));
        noReserve.Submit(SourceClass.Player, 0, 2, new OfferLicence(0, 1, Fx6.One, SaleCircle.All));
        noReserve.StepThroughMonth(1);
        Assert.Equal(2, noReserve.Outcomes.Count(o => o.Contains("ReserveOff", StringComparison.Ordinal)));
        Assert.Equal(Fx6.Zero, noReserve.World.LicenceFunds(0, 2));
    }

    [Fact]
    public void LicenceMarket_IsRefusedWithTheCanonicalProductLayer_SoNoFeeIsEverPaidWithoutAProject()
    {
        Assert.Throws<ArgumentException>(() => SliceTestData.CreateLegacy(new SliceRunConfig { Seed = 7, Layer = LayerControl.Lp, LicenceMarket = true }));
    }

    [Fact]
    public void OfferLicence_SetsThePriceOfTheOwnCatalogue_AndRefusesAMultiplierOutOfRange()
    {
        SliceSession s = SliceTestData.Create(Config(0));
        s.StepThroughMonth(0);
        PolicySystem pol = s.World.Policy;
        int gen = Enumerable.Range(1, s.World.ProductsLayout.Generations).First(g => pol.Offer(0, g, 1) is not null);
        s.Submit(SourceClass.Player, 0, 1, new OfferLicence(0, gen, Fx6.FromRaw(1_800_000), SaleCircle.Bloc));
        s.Submit(SourceClass.Player, 0, 2, new OfferLicence(0, gen, Fx6.FromRaw(2_100_000), SaleCircle.All));
        s.StepThroughMonth(1);
        LicenceOffer o = pol.Offer(0, gen, 2)!.Value;
        Assert.Equal(Fx6.FromRaw(1_800_000), o.M);
        Assert.Equal(SaleCircle.Bloc, o.Circle);
        Assert.Contains(s.Outcomes, x => x.Contains("BadOffer", StringComparison.Ordinal));
        Assert.Contains(s.View.Actors[0].Market!.Own, l => l.Gen == gen && l.M == Fx6.FromRaw(1_800_000));
    }

    [Fact]
    public void MarketRun_GivesTheSameStateHashesOnOneAndOnFourThreads()
    {
        // L2 (ADR-0002): the same scripted market run, AI deals and the player's deal, on 1 and on 4 threads, month by month.
        (int buyer, int seller, int gen) = FindDeal(out SliceSession probe);
        Assert.NotNull(probe);
        var hashes = new List<string>[2];
        int i = 0;
        foreach (int threads in new[] { 1, 4 })
        {
            SliceSession s = Prepared(buyer, threads);
            s.Submit(SourceClass.Player, buyer, 2, new BuyLicence(buyer, seller, gen));
            hashes[i] = [];
            for (long m = 25; m < 72; m++)
            {
                s.StepThroughMonth(m);
                hashes[i].Add(s.LastHash);
            }
            Assert.NotEmpty(s.World.Policy.Licences(buyer));
            i++;
        }
        for (int m = 0; m < hashes[0].Count; m++)
            HashAssert.Equal(hashes[0][m], hashes[1][m], $"month {25 + m}: 1 thread and 4 threads differ");
    }

    [Fact]
    public void FlagOff_TheWorldIsTheWorldOfMain_WhateverTheMarketFieldsSay()
    {
        // The market fields without the flag change nothing in the state (no column, no rule): the hash is the plain run's.
        SliceSession plain = SliceTestData.Create(new SliceRunConfig { Seed = 7 });
        SliceSession withFields = SliceTestData.Create(new SliceRunConfig { Seed = 7, LicenceExternalActors = [0], Alliances = Alliances });
        plain.StepThroughMonth(36);
        withFields.StepThroughMonth(36);
        HashAssert.Equal(plain.LastHash, withFields.LastHash);
        Assert.DoesNotContain(plain.World.Table.Columns, c => c.Name.StartsWith("pol.lm.", StringComparison.Ordinal) || c.Name.StartsWith("pol.cat.", StringComparison.Ordinal));
    }

    [Fact]
    public void FlagOn_AiMarketRuns_ForTwentyYears_WithNoInvariantBroken()
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 3, LicenceMarket = true, Alliances = Alliances });
        for (long m = 0; m < 240; m++)
            s.StepThroughMonth(m);
        Assert.False(string.IsNullOrEmpty(s.LastHash));
    }
}
