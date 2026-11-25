using System.Collections.Immutable;
using Singularity.Core.Commands;
using Singularity.Core.Data;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>The licence market in a running slice (TASK-0152): commands, the deal queue, AdoptLicensedDesign, replay, and the neutrality of the flag off.</summary>
public class LicenceMarketSessionTests
{
    private static AllianceTable Alliances => AllianceTable.Load(Path.Combine(SliceTestData.RepoRoot, "mods", "core", "scenarios", "1945", "alliances.yaml"));

    private static SliceRunConfig Config(int player, ulong seed = 7) => new()
    {
        Seed = seed, LicenceMarket = true, LicenceExternalActors = [player], Alliances = Alliances,
    };

    // A buyer, seller and generation the market lets through at the start (the player buys from an AI whose catalogue is open to everybody).
    private static (int Buyer, int Seller, int Gen) FindDeal(out SliceSession session)
    {
        var all = new List<string>();
        for (int buyer = 0; buyer < SliceTestData.Data.Scenario("jet_slice_1945").ActorCount; buyer++)
        {
            session = SliceTestData.Create(Config(buyer));
            session.StepThroughMonth(0);
            PolicySystem pol = session.World.Policy;
            for (int seller = 0; seller < session.World.Scenario.ActorCount; seller++)
            {
                for (int gen = 1; gen <= session.World.ProductsLayout.Generations; gen++)
                {
                    if (pol.Offer(seller, gen, 1) is null)
                        continue;
                    BadLicence r = pol.CheckBuy(buyer, seller, gen, 1, session.World.ProcurementBase(buyer), out _);
                    all.Add($"{buyer}<-{seller} g{gen}: {r}");
                    if (r == BadLicence.None)
                        return (buyer, seller, gen);
                }
            }
        }
        throw new InvalidOperationException("no open deal at the start: " + string.Join("; ", all));
    }

    [Fact]
    public void BuyLicence_PaysThroughTheQueue_CreatesALicensedProject_AndReplaysTheSame()
    {
        (int buyer, int seller, int gen) = FindDeal(out SliceSession s);
        Fx6 funds = s.World.ProcurementBase(buyer);
        Fx6 price = s.World.Policy.Offer(seller, gen, 1)!.Value.Price;
        CommandVerdict v = s.Submit(SourceClass.Player, buyer, 1, new BuyLicence(buyer, seller, gen));
        Assert.True(v.Accepted, v.Message);
        s.StepThroughMonth(1);
        Assert.Contains("accepted", s.Outcomes[^1], StringComparison.Ordinal);
        Assert.Contains(s.World.Policy.Licences(buyer), r => r.Seller == seller && r.Gen == gen);
        Assert.Equal(-price, s.World.Policy.LicenceFlow(buyer));
        Assert.True(funds + s.World.Policy.LicenceFlow(buyer) >= Fx6.Zero);
        // The licence is a project of the buyer: stage О, from the seller's own project, with its own gate month.
        s.StepThroughMonth(2);
        ActorView a = s.View.Actors[buyer];
        DesignView? licensed = a.Designs.FirstOrDefault(d => d.LicensedFrom == seller);
        Assert.NotNull(licensed);
        Assert.Equal(DesignStage.Prototype, licensed.Stage);
        Assert.True(licensed.Handle.Generation >= 1);
        Assert.Equal(1, licensed.StageOMonth);
        Assert.Contains(a.Market!.Held, h => h.Seller == seller && h.Gen == gen);
        // The seller's catalogue never lists the bought project (#477).
        Assert.Null(s.World.Policy.Offer(buyer, gen, 2));   // the buyer has no own serial project of that generation: the licensed one is not for sale

        string log = s.CommandLog;
        SliceSession replay = SliceSession.Replay(SliceTestData.Data, Config(buyer), log, s.Tick);
        Assert.Equal(s.LastHash, replay.LastHash);
    }

    [Fact]
    public void BuyLicence_Refusals_AreNamedImmediately()
    {
        (int buyer, int seller, int gen) = FindDeal(out SliceSession s);
        CommandVerdict self = s.Submit(SourceClass.Player, buyer, 1, new BuyLicence(buyer, buyer, gen));
        s.StepThroughMonth(1);
        Assert.Contains("Self", s.Outcomes[^1]);
        s.Submit(SourceClass.Player, buyer, 2, new BuyLicence(buyer, seller, 99));
        s.StepThroughMonth(2);
        Assert.Contains("Empty", s.Outcomes[^1]);
        Assert.True(self.Accepted);   // the form is fine; the state check names the reason when the command is applied
    }

    [Fact]
    public void LicenceCommands_AreRefusedForAnActorOutsideExternalControl_OrWithTheMarketOff()
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
    public void FlagOff_TheWorldIsTheWorldOfMain_WhateverTheMarketFieldsSay()
    {
        // The market fields without the flag change nothing in the state (no column, no rule): the hash is the plain run's.
        SliceSession plain = SliceTestData.Create(new SliceRunConfig { Seed = 7 });
        SliceSession withFields = SliceTestData.Create(new SliceRunConfig { Seed = 7, LicenceExternalActors = [0], Alliances = Alliances });
        plain.StepThroughMonth(36);
        withFields.StepThroughMonth(36);
        Assert.Equal(plain.LastHash, withFields.LastHash);
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
