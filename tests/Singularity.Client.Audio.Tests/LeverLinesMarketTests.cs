using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Singularity.Client.Spike;
using Singularity.Core.Commands;
using Singularity.Core.Data;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;
using Singularity.Session;
using Xunit;

namespace Singularity.Client.Audio.Tests;

/// <summary>TASK-0156 part 2: the pure models of the production-line window and the licence-market window, and the same models over a real session.</summary>
public class LeverLinesMarketTests
{
    static string Root()
    {
        string? d = AppContext.BaseDirectory;
        while (d != null && !File.Exists(Path.Combine(d, "Singularity.sln"))) d = Path.GetDirectoryName(d);
        return d ?? throw new InvalidOperationException("repo root not found");
    }

    static MoneyFormat.Scale Scale() => MoneyFormat.Parse(File.ReadAllText(Path.Combine(Root(), "src", "Singularity.Client.Spike", "data", "dollars_1990.json")));
    static string Fmt(double m) => MoneyFormat.Format(m, true, true);
    static string Seller(int i) => "S" + i;
    static Fx6 F(double v) => Fx6.FromRaw((long)Math.Round(v * 1_000_000));

    static DesignView Design(int n, DesignStage st, int gen = 2, double rate = 0, double cost = 0.001, int licensed = -1) =>
        new(n, st, gen, gen, -1, 0, true, false, F(rate), F(cost), F(cost), F(12), F(0.8), F(1), LineBind.Rate, false, ImmutableArray<int>.Empty, 1, licensed);

    static ProposalView Proposal(ProposalKind k, int design, double rate = 6, LineCloseReason r = LineCloseReason.None) =>
        new(k, design, new DesignHandle(design, 1), F(rate), r);

    [Fact]
    public void Lines_RowsCarryUnitsAndTheLimitSaysWhy()
    {
        var designs = ImmutableArray.Create(Design(0, DesignStage.Series, rate: 6), Design(1, DesignStage.Series, rate: 4), Design(2, DesignStage.Prototype), Design(3, DesignStage.None));
        var m = LinesWindowModel.Build(designs, ImmutableArray.Create(Proposal(ProposalKind.Open, 2)), 2, Scale(), Fmt, Seller);
        Assert.Equal(3, m.Lines.Length);            // an empty slot is no row
        Assert.Equal(2, m.SeriesLines);
        Assert.True(m.AtLimit);
        Assert.Equal(80, m.Lines[0].ReadinessPct);   // a percent, not a bare 0.8
        Assert.Equal("bind.Rate", m.Lines[0].BindKey);
        Assert.True(m.Lines[0].CanClose); Assert.True(m.Lines[2].CanCancel); Assert.False(m.Lines[2].CanClose);
        var p = Assert.Single(m.Proposals);
        Assert.Equal("why.line.limit", p.AcceptDisabledKey);   // the third line: grey Accept with the reason in words
        Assert.Equal(2, p.AcceptDisabledArg);
        Assert.Equal("line.reason.open", p.ReasonKey);
        Assert.NotEqual("", p.MonthlyMoney);
    }

    [Fact]
    public void Lines_BelowTheLimit_AcceptGoes_AndACloseIsNeverLimited()
    {
        var designs = ImmutableArray.Create(Design(0, DesignStage.Series, rate: 6), Design(1, DesignStage.Prototype));
        var open = LinesWindowModel.Build(designs, ImmutableArray.Create(Proposal(ProposalKind.Open, 1)), 2, Scale(), Fmt, Seller);
        Assert.Equal("", open.Proposals[0].AcceptDisabledKey);
        var full = ImmutableArray.Create(Design(0, DesignStage.Series), Design(1, DesignStage.Series));
        var close = LinesWindowModel.Build(full, ImmutableArray.Create(Proposal(ProposalKind.Close, 0, r: LineCloseReason.YProd)), 2, Scale(), Fmt, Seller);
        Assert.Equal("", close.Proposals[0].AcceptDisabledKey);
        Assert.Equal("line.reason.YProd", close.Proposals[0].ReasonKey);
    }

    [Fact]
    public void Lines_Empty_IsEmpty_NotZeroRows()
    {
        var m = LinesWindowModel.Build(ImmutableArray<DesignView>.Empty, ImmutableArray<ProposalView>.Empty, 2, Scale(), Fmt, Seller);
        Assert.Empty(m.Lines); Assert.Empty(m.Proposals); Assert.False(m.AtLimit);
        var none = LinesWindowModel.Build(default, default, 2, Scale(), Fmt, Seller);   // a view without designs must not throw
        Assert.Empty(none.Lines);
    }

    [Fact]
    public void Lines_LicencedProject_NamesTheSeller()
    {
        var m = LinesWindowModel.Build(ImmutableArray.Create(Design(0, DesignStage.Series, licensed: 3)), default, 2, Scale(), Fmt, Seller);
        Assert.Equal("S3", m.Lines[0].LicensedFrom);
    }

    static CatalogueLineView Offer(int seller, int gen, double refPrice, double m, BadLicence r = BadLicence.None, SaleCircle c = SaleCircle.All) =>
        new(seller, gen, F(refPrice), F(m), F(refPrice * m), c, r);

    static LicenceMarketView Market(CatalogueLineView[] offers, WorldPriceView[]? world = null, CatalogueLineView[]? own = null) =>
        new(ImmutableArray<LicenceHeldView>.Empty, own == null ? ImmutableArray<CatalogueLineView>.Empty : ImmutableArray.CreateRange(own), ImmutableArray.CreateRange(offers),
            world == null ? ImmutableArray<WorldPriceView>.Empty : ImmutableArray.CreateRange(world), BadLicence.None, 0, 0, 0, F(0.1), F(0.5), true);

    [Fact]
    public void Market_NoMarketView_IsDisabled_NotEmpty()
    {
        var m = MarketWindowModel.Build(null, null, Scale(), Fmt, Seller);
        Assert.False(m.Enabled);
    }

    [Fact]
    public void Market_Offer_ShowsPriceAgainstReferenceAndTheWorldPrice()
    {
        var mk = Market(new[] { Offer(1, 2, 0.3, 1.3), Offer(2, 2, 0.3, 0.8) }, new[] { new WorldPriceView(2, F(0.31), 2) });
        var m = MarketWindowModel.Build(mk, new ReserveView(F(1.0), Fx6.Zero, Fx6.Zero), Scale(), Fmt, Seller);
        Assert.True(m.Enabled);
        Assert.Equal(2, m.Offers.Length);
        var up = m.Offers.Single(o => o.Seller == 1); var down = m.Offers.Single(o => o.Seller == 2);
        Assert.Equal(30, up.PremiumPct); Assert.Equal(-20, down.PremiumPct);
        Assert.False(up.FewOffers);
        Assert.NotEqual("", up.WorldPrice);
        Assert.True(up.CanBuy);
        Assert.NotEqual("", up.ReserveAfter);   // what stays on the account is shown before the click
    }

    [Fact]
    public void Market_FewSellers_HasNoWorldAverage()
    {
        var mk = Market(new[] { Offer(1, 3, 0.3, 1.0) }, new[] { new WorldPriceView(3, null, 1) });
        var o = MarketWindowModel.Build(mk, null, Scale(), Fmt, Seller).Offers.Single();
        Assert.True(o.FewOffers);
        Assert.Equal("", o.WorldPrice);
        Assert.Equal("", o.ReserveAfter);   // no reserve in the view: no invented balance
    }

    [Theory]
    [InlineData(BadLicence.Self)] [InlineData(BadLicence.Empty)] [InlineData(BadLicence.Gen)] [InlineData(BadLicence.Money)]
    [InlineData(BadLicence.Circle)] [InlineData(BadLicence.Capacity)] [InlineData(BadLicence.Cap)] [InlineData(BadLicence.Price)]
    public void Market_EveryRefusalOfTheCore_HasAKeyInTheLocale_AndBlocksTheButton(BadLicence r)
    {
        var o = MarketWindowModel.Build(Market(new[] { Offer(1, 2, 0.3, 1.0, r) }), null, Scale(), Fmt, Seller).Offers.Single();
        Assert.False(o.CanBuy);
        Assert.Equal("licence.bad." + r, o.RefusalKey);
        Assert.Contains(o.RefusalKey, Keys());
    }

    [Theory]
    [InlineData(OfferRefusal.BadGen)] [InlineData(OfferRefusal.NoOwnProject)] [InlineData(OfferRefusal.BadMultiplier)]
    public void Market_EveryOfferRefusal_HasAKey(OfferRefusal r) => Assert.Contains(MarketWindowModel.OfferRefusalKey(r), Keys());

    [Theory]
    [InlineData(LineCloseReason.None)] [InlineData(LineCloseReason.A40C)] [InlineData(LineCloseReason.YProd)]
    [InlineData(LineCloseReason.LaunchAll)] [InlineData(LineCloseReason.LaunchCandidate)] [InlineData(LineCloseReason.CandidateBelow)]
    public void EveryCloseReason_HasWordsInTheLocale(LineCloseReason r) => Assert.Contains("line.reason." + r, Keys());

    [Fact]
    public void Market_OwnOffer_StepStaysInsideTheRangeTheCoreTakes()
    {
        Assert.Equal(2.0, MarketWindowModel.StepM(1.95, 1));
        Assert.Equal(0.5, MarketWindowModel.StepM(0.5, -1));
        Assert.Equal(1.3, MarketWindowModel.StepM(1.2, 1));
        var mk = Market(new CatalogueLineView[0], own: new[] { Offer(0, 2, 0.3, 1.2, c: SaleCircle.Friends) });
        var own = MarketWindowModel.Build(mk, null, Scale(), Fmt, Seller).Own.Single();
        Assert.Equal(1.2, own.M, 6); Assert.Equal(SaleCircle.Friends, own.Circle);
    }

    [Theory]
    [InlineData("LineLimit")] [InlineData("NoProposal")] [InlineData("StaleHandle")] [InlineData("LicenceRefused")] [InlineData("BadOffer")] [InlineData("LicenceNotAllowed")]
    public void RefusalsOfTheNewCommands_AreInWords(string reason)
    {
        string key = InstitutesWindowModel.RefusalKey(reason);
        Assert.NotNull(key);
        Assert.Contains(key, Keys());
    }

    // The run-time check (TASK-0111): the same models over a real session, and the commands the buttons send are accepted by the session.
    [Fact]
    public void RealSession_FeedsBothModels()
    {
        GameData data = ModLoader.Load(Path.Combine(Root(), "mods", "core"));
        int usa = data.Scenarios[0].ActorIndex("USA");
        var s = SliceSession.Create(data, ClientRun.Config(data, Root(), 7, "USA"));
        for (long t = 0; t < 60; t++) s.StepThroughMonth(t);
        var a = s.View.Actors.First(x => x.Actor == usa);
        var scale = Scale();
        var lines = LinesWindowModel.Build(a.Designs, a.Decisions.Proposals, new SliceRunConfig().AdviserMaxLines, scale, Fmt, Seller);
        Assert.Equal(a.Designs.Count(d => d.Stage != DesignStage.None), lines.Lines.Length);
        foreach (var p in lines.Proposals)
        {
            var v = s.Submit(SourceClass.Player, 0, 1, p.Source.Refuse(usa));   // the refuse button of a real proposal is accepted
            Assert.True(v.Accepted, v.Message);
            break;
        }
        var mk = MarketWindowModel.Build(a.Market, s.View.Inputs?.Actors.FirstOrDefault(x => x.Actor == usa)?.Reserve, scale, Fmt, Seller);
        Assert.True(mk.Enabled);   // the client run has the market
        Assert.True(mk.External);
        foreach (var o in mk.Offers) Assert.True(o.CanBuy || o.RefusalKey != "");
    }

    static System.Collections.Generic.HashSet<string> Keys()
    {
        var rows = File.ReadAllLines(Path.Combine(Root(), "src", "Singularity.Client.Spike", "locale", "ui.csv"), Encoding.UTF8);
        return rows.Select(l => l.Split(',')[0]).ToHashSet();
    }
}
