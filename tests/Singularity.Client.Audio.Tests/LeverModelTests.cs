using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Singularity.Client.Spike;
using Singularity.Core.Commands;
using Singularity.Core.Data;
using Singularity.Core.Policy;
using Singularity.Kernel;
using Singularity.Session;
using Xunit;

namespace Singularity.Client.Audio.Tests;

/// <summary>TASK-0156 part 1: the display scale of money, the pure models of the budget and institute windows, the locale tables.</summary>
public class LeverModelTests
{
    static string Root()
    {
        string? d = AppContext.BaseDirectory;
        while (d != null && !File.Exists(Path.Combine(d, "Singularity.sln"))) d = Path.GetDirectoryName(d);
        return d ?? throw new InvalidOperationException("repo root not found");
    }

    static MoneyFormat.Scale Scale() => MoneyFormat.Parse(File.ReadAllText(Path.Combine(Root(), "src", "Singularity.Client.Spike", "data", "dollars_1990.json")));
    static string Nbsp => ((char)160).ToString();

    [Fact]
    public void K_IsInTheDataWithItsSource_AndMatchesTheDeflatorRatio()
    {
        var s = Scale();
        Assert.Equal(0.64828, s.K);
        Assert.Contains("BEA", s.Source);
        Assert.Equal(59.305 / 91.481, s.K, 4);
        Assert.Equal(389.04, s.Million1990PerIndex, 1);   // dollars-1990.md and budget-money-display.md §1
    }

    [Fact]
    public void Format_FollowsTheThresholdsOfTheDesignDocument()
    {
        Assert.Equal("$850" + Nbsp + "млн", MoneyFormat.Format(850, true, true));
        Assert.Equal("$30,6" + Nbsp + "млрд", MoneyFormat.Format(30_600, true, true));
        Assert.Equal("$30.6" + Nbsp + "B", MoneyFormat.Format(30_600, false, false));
        Assert.Equal("$31" + Nbsp + "млрд", MoneyFormat.Format(31_000, true, true));
        Assert.Equal("$150" + Nbsp + "млрд", MoneyFormat.Format(150_400, true, true));
        Assert.Equal("$1,5" + Nbsp + "трлн", MoneyFormat.Format(1_458_000, true, true));
        Assert.Equal("$1" + Nbsp + "млрд", MoneyFormat.Format(999.6, true, true));
    }

    [Fact]
    public void Format_RoundsBeforeChoosingTheUnit()
    {
        // 999.6 million is 1 000 million after rounding, so it reads in billions and never as «$1000 млн»
        Assert.DoesNotContain("1000", MoneyFormat.Format(999.6, true, true));
        Assert.Equal("$1" + Nbsp + "млрд", MoneyFormat.Format(999.6, true, true));
        Assert.Equal("$0" + Nbsp + "млн", MoneyFormat.Format(0.2, true, true));
    }

    [Fact]
    public void UsaBudget1945_IsAboutFourPointSevenBillionAYear()
    {
        // budget-money-display.md §1: one index a month = 389 million 1990 dollars; USA 1945 ≈ 1 index
        double m = MoneyFormat.Million1990PerYear(1.0, Scale());
        Assert.InRange(m, 4_600, 4_750);
    }

    static string Fmt(double m) => MoneyFormat.Format(m, true, true);

    static BudgetShares Shares(int r, int cap, int eff, int proc, int q) =>
        new(Fx6.FromRaw(r * 10_000L), Fx6.FromRaw(cap * 10_000L), Fx6.FromRaw(eff * 10_000L), Fx6.FromRaw(proc * 10_000L), Fx6.FromRaw(q * 10_000L));

    static ActorInputsView Inputs(BudgetShares s, double budget, ReserveView? reserve) =>
        new(0, Fx6.FromRaw((long)(budget * 1_000_000)), s, Fx6.Zero, default!, default, default, null, reserve);

    [Fact]
    public void Budget_NoInputs_IsNoData_NotZero()
    {
        var m = BudgetWindowModel.Build(null!, null, false, Scale(), Fmt);
        Assert.False(m.HasData);
        Assert.Equal("why.budget.nodata", m.ApplyDisabledKey);
    }

    [Fact]
    public void Budget_Rows_CarryPercentAndMoney_AndTheRestIsTheReserve()
    {
        var inp = Inputs(Shares(28, 15, 10, 42, 5), 1.0, new ReserveView(Fx6.FromRaw(2_000_000), Fx6.Zero, Fx6.FromRaw(16_000)));
        var m = BudgetWindowModel.Build(inp, new[] { 28, 10, 30 }, false, Scale(), Fmt);
        Assert.True(m.HasData);
        Assert.Equal(20, m.OtherPct);        // tooling and quality: shown as one grey line
        Assert.Equal(12, m.ReservePct);      // 100 - 20 - 68
        Assert.Equal(3, m.Rows.Length);
        Assert.Equal(-12, m.Rows[BudgetWindowModel.Procurement].DeltaPp);
        Assert.True(m.Changed);
        Assert.Equal("", m.ApplyDisabledKey);
        Assert.Equal(m.TotalYearMillion * 0.28, m.Rows[0].YearMillion, 6);
        Assert.Equal(3, m.Forecast.Length);
        Assert.True(m.ReserveOn);
    }

    [Fact]
    public void Budget_Unchanged_DisablesApply_WithAReason()
    {
        var inp = Inputs(Shares(28, 15, 10, 42, 5), 1.0, null);
        var m = BudgetWindowModel.Build(inp, null, false, Scale(), Fmt);
        Assert.False(m.Changed);
        Assert.Equal("why.budget.nochange", m.ApplyDisabledKey);
        Assert.False(m.ReserveOn);
        Assert.Equal(0, m.ReservePct);
    }

    [Fact]
    public void Budget_Limit_StopsAtOneHundredPercentWithTheHiddenItems()
    {
        var s = Shares(28, 15, 10, 42, 5);
        Assert.Equal(28, BudgetWindowModel.MaxFor(s, new[] { 28, 10, 42 }, 0));   // 100 - 20 hidden - 10 - 42
        var m = BudgetWindowModel.Build(Inputs(s, 1.0, null), new[] { 28, 10, 42 }, false, Scale(), Fmt);
        Assert.True(m.Rows[0].AtLimit);
        Assert.Equal(0, m.ReservePct);
    }

    [Fact]
    public void Budget_ToShares_KeepsTheHiddenItemsExactly()
    {
        var s = new BudgetShares(Fx6.FromRaw(280_000), Fx6.FromRaw(153_333), Fx6.FromRaw(100_000), Fx6.FromRaw(420_000), Fx6.FromRaw(46_667));
        var n = BudgetWindowModel.ToShares(s, new[] { 30, 10, 40 });
        Assert.Equal(s.Capability, n.Capability);
        Assert.Equal(s.Quality, n.Quality);
        Assert.Equal(300_000, n.Research.Raw);
        Assert.True(n.IsValidWithReserve);
    }

    [Fact]
    public void Budget_EffortBelowTheThreshold_WarnsInAWordNotOnlyColour()
    {
        var m = BudgetWindowModel.Build(Inputs(Shares(28, 15, 10, 42, 5), 1.0, null), new[] { 28, 5, 42 }, false, Scale(), Fmt);
        Assert.True(m.EffortStop);
        Assert.True(m.Rows[BudgetWindowModel.Mastery].Stop);
        Assert.False(m.Rows[BudgetWindowModel.Science].Stop);
    }

    [Fact]
    public void Budget_ForeignPower_IsViewOnly()
    {
        var m = BudgetWindowModel.Build(Inputs(Shares(28, 15, 10, 42, 5), 1.0, null), new[] { 30, 10, 40 }, true, Scale(), Fmt);
        Assert.False(m.Editable);
        Assert.Equal("why.budget.foreign", m.ApplyDisabledKey);
    }

    [Theory]
    [InlineData("sig.alloy", true, 3, true, true)]
    [InlineData("sig.alloy", false, 3, true, false)]   // a signal going off pauses nothing
    [InlineData("sig.refusal", true, 0, true, false)]   // already paused
    [InlineData("sig.refusal", true, 2, false, false)]  // switched off
    [InlineData("sig.war", true, 2, true, false)]        // not an event of this list
    public void AutoPause_OnlyForANewEventOfARunningGame(string key, bool on, int speed, bool enabled, bool expected) =>
        Assert.Equal(expected, AutoPauseLogic.ShouldPause(key, on, speed, enabled));

    [Fact]
    public void DisabledButtonText_HasFourPointFiveToOneOnPaperAndOnTheTexture()
    {
        // DEBT-0095 / TASK-0149 remainder: the text of a grey button (the focus order button) must read. The paper texture darkens the paper up to about 6 %.
        var text = ColorMath.FromHex(Palette.DisabledText);
        foreach (double k in new[] { 1.0, 0.94 })
        {
            Assert.True(ColorMath.Contrast(text, ColorMath.FromHex(Palette.C.Paper, k)) >= 4.5, $"paper x{k}");
            Assert.True(ColorMath.Contrast(text, ColorMath.FromHex(Palette.C.Sheet, k)) >= 4.5, $"sheet x{k}");
        }
    }

    [Fact]
    public void EngineGenerationLimit_NamesTheNodeToStudy()
    {
        var ll = new Singularity.Core.WhyNot.Link(Singularity.Core.WhyNot.WhyNotLayer.Product, Singularity.Core.WhyNot.WhyNotCause.EngineGenLimit, 42, Fx6.One, Fx6.One, default, Fx6.Zero, null, Slot: 1);
        Assert.Equal(42, WhyLines.SubjectNode(ll));
        var kn = new Singularity.Core.WhyNot.Link(Singularity.Core.WhyNot.WhyNotLayer.Knowledge, Singularity.Core.WhyNot.WhyNotCause.MissingNode, 7, Fx6.One, Fx6.One, default, Fx6.Zero, null);
        Assert.Equal(7, WhyLines.SubjectNode(kn));
    }

    [Fact]
    public void ToShares_KeepsAnUntouchedItemExactly_AndMaxForCountsItExactly()
    {
        var s = new BudgetShares(Fx6.FromRaw(284_000), Fx6.FromRaw(150_000), Fx6.FromRaw(100_000), Fx6.FromRaw(420_000), Fx6.FromRaw(46_000));
        var n = BudgetWindowModel.ToShares(s, new[] { 28, 8, 42 });   // science 28.4 % shows as 28 % and is not touched
        Assert.Equal(284_000, n.Research.Raw);
        Assert.Equal(80_000, n.Effort.Raw);
        Assert.Equal(420_000, n.Procurement.Raw);
        Assert.True(n.IsValidWithReserve);
        // the 0.4 pp the display rounds away still counts against the 100 % limit
        Assert.Equal(10, BudgetWindowModel.MaxFor(s, new[] { 28, 10, 42 }, 1));   // 100 - 19.6 hidden - 28.4 - 42
    }

    // The price of a project in the window is the BuildRate of the view.
    [Fact]
    public void InstituteQuote_EqualsTheCoreRateOfTheStartedProject_ForAProjectOpeningBeforeItsHistoricalYear()
    {
        GameData data = ModLoader.Load(Path.Combine(Root(), "mods", "core"));
        var sc = data.Scenarios[0];
        int usa = sc.ActorIndex("USA");
        var s = SliceSession.Create(data, ClientRun.Config(data, Root(), 7, "USA"));
        for (long t0 = 0; t0 < 40; t0++) s.StepThroughMonth(t0);
        var scale = Scale();
        ActorView a = s.View.Actors.First(x => x.Actor == usa);
        var ctx = new InstituteContext(sc.StartYear, sc.StartMonth, InstitutesWindowModel.IsSmallCountry(sc, usa));
        var p = new Singularity.Core.Institutes.InstituteParams();
        long month = s.View.Month;
        var pick = a.Institutes.FirstOrDefault(i => !i.Standing && i.BuildStart < 0
            && InstitutesWindowModel.CheckBuild(i, ctx, month + InstitutesWindowModel.StartLag, p) == Singularity.Core.Institutes.BuildCheck.Ok
            && i.RefYear > InstitutesWindowModel.YearOf(ctx, month + 60));
        Assert.NotNull(pick);   // a candidate that opens years before its historical year
        // a candidate shows the price of the view (core quote), not a client copy
        var cand = InstitutesWindowModel.Build(a, month, 1.0, scale, ctx, n => "n", n => 0).Candidates.First(c => c.Slot == pick!.Slot);
        Assert.Equal(InstituteState.Candidate, cand.State);
        Assert.Equal((int)((pick!.BuildRate.Raw + 500) / 1000), cand.RateTenthPercent);
        Assert.True(s.Submit(Singularity.Core.Commands.SourceClass.Player, 0, 1, new BuildInstitute(usa, pick.Slot)).Accepted);
        for (long t1 = 40; t1 < 43; t1++) s.StepThroughMonth(t1);
        var started = s.View.Actors.First(x => x.Actor == usa).Institutes.First(i => i.Slot == pick.Slot);
        Assert.True(started.BuildStart >= 0);
        Assert.Equal(month + InstitutesWindowModel.StartLag, started.BuildStart);
        Assert.True(started.BuildRate.Raw > p.BuildRatePerLevel.Raw * pick.TotalLevels, "the project opens ahead of its year: the price is above the base rate");
        // and the model shows the view's own rate for a running project
        var research = 1.0;
        var run = InstitutesWindowModel.Build(s.View.Actors.First(x => x.Actor == usa), s.View.Month, research, scale, ctx, n => "n", n => 0)
            .Candidates.First(c => c.Slot == pick.Slot);
        Assert.Equal(InstituteState.Building, run.State);
        Assert.Equal((int)((started.BuildRate.Raw + 500) / 1000), run.RateTenthPercent);
    }

    [Fact]
    public void DataOrLoad_LoadsOnlyWhenThereIsNoData()   // the shot/bench start passes null to BuildClient
    {
        int calls = 0;
        GameData loaded = null!;
        Assert.Null(Record.Exception(() => ClientRun.DataOrLoad(null!, () => { calls++; return loaded; })));
        Assert.Equal(1, calls);
        GameData data = ModLoader.Load(Path.Combine(Root(), "mods", "core"));
        Assert.Same(data, ClientRun.DataOrLoad(data, () => { calls++; return null!; }));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(Singularity.Core.Institutes.BuildCheck.AlreadyStanding, "why.inst.standing")]
    [InlineData(Singularity.Core.Institutes.BuildCheck.AlreadyBuilding, "why.inst.building")]
    [InlineData(Singularity.Core.Institutes.BuildCheck.TooEarly, "why.inst.early")]
    [InlineData(Singularity.Core.Institutes.BuildCheck.SmallCountryCap, "why.inst.cap")]
    [InlineData(Singularity.Core.Institutes.BuildCheck.Ok, "")]
    public void EveryBuildCheck_HasAReasonInWords(Singularity.Core.Institutes.BuildCheck c, string key)
    {
        Assert.Equal(key, InstitutesWindowModel.BuildWhyKey(c));
        var keys = Parse(File.ReadAllText(Path.Combine(Root(), "src", "Singularity.Client.Spike", "locale", "ui.csv"), Encoding.UTF8)).Select(r => r[0]).ToHashSet();
        if (key != "") Assert.Contains(key, keys);
    }

    [Fact]
    public void SmallCountryCap_IsCheckedBeforeTheClick()
    {
        var p = new Singularity.Core.Institutes.InstituteParams();
        var big = new InstituteView(0, "x", false, 0, System.Collections.Immutable.ImmutableArray.Create(2, 2), -1, 0, Fx6.Zero, 1946, 4);
        Assert.Equal(Singularity.Core.Institutes.BuildCheck.SmallCountryCap, InstitutesWindowModel.CheckBuild(big, new InstituteContext(1945, 9, true), 0, p));
        Assert.Equal(Singularity.Core.Institutes.BuildCheck.Ok, InstitutesWindowModel.CheckBuild(big, new InstituteContext(1945, 9, false), 0, p));
    }

    // The run-time check of the rule «fixture and live path share one function» (TASK-0111): a real session, the real view.
    [Fact]
    public void RealSession_FeedsBothModels_AndTheFirstGoalOrderIsAccepted()
    {
        GameData data = ModLoader.Load(Path.Combine(Root(), "mods", "core"));
        int usa = data.Scenarios[0].ActorIndex("USA");
        var cfg = ClientRun.Config(data, Root(), 7, "USA");
        Assert.Equal(usa, cfg.PlayerActor);
        var s = SliceSession.Create(data, cfg);
        for (long t = 0; t < 14; t++) s.StepThroughMonth(t);
        var scale = Scale();
        ActorView a = s.View.Actors.First(x => x.Actor == usa);
        ActorInputsView inp = s.View.Inputs!.Actors.First(x => x.Actor == usa);

        BudgetModel b = BudgetWindowModel.Build(inp, null, false, scale, Fmt);
        Assert.True(b.HasData);
        Assert.True(b.ReserveOn);
        Assert.True(b.TotalYearMillion > 1_000, "the USA budget is billions of 1990 dollars a year");

        double research = inp.Budget.ToDouble() * inp.Shares.Research.ToDouble();
        var sc = data.Scenarios[0];
        var ctx = new InstituteContext(sc.StartYear, sc.StartMonth, InstitutesWindowModel.IsSmallCountry(sc, usa));
        var m = InstitutesWindowModel.Build(a, s.View.Month, research, scale, ctx, n => "node" + n, n => 0);
        Assert.True(m.Enabled);
        Assert.NotEmpty(m.Standing);
        foreach (var c in m.Candidates.Where(c => c.State == InstituteState.Candidate))
        {
            Assert.True(c.CostTotalMillion > 0, c.Id);
            Assert.True(c.BuildMonths >= 24, c.Id);
            Assert.True(c.CostPerYearMillion > 0, c.Id);
        }
        var row = m.Standing.FirstOrDefault(r => r.Options.Length > 0);
        if (row != null)
        {
            var v = s.Submit(Singularity.Core.Commands.SourceClass.Player, 0, 1, new SetPriorityNode(usa, row.Slot, row.Options[0].Node));
            Assert.True(v.Accepted, v.Message);
        }
    }

    // DEBT-0095: a comma inside a field needs quotes, else the Russian text is cut at the comma (two messages were).
    [Theory]
    [InlineData("ui.csv")]
    [InlineData("data.csv")]
    public void LocaleTables_EveryRowHasKeyEnAndRu(string name)
    {
        string text = File.ReadAllText(Path.Combine(Root(), "src", "Singularity.Client.Spike", "locale", name), Encoding.UTF8);
        var rows = Parse(text);
        foreach (var r in rows.Skip(1))
        {
            if (r.Count == 1 && r[0] == "") continue;
            Assert.True(r.Count == 3, $"{name}: key '{r[0]}' has {r.Count} fields (a comma without quotes?)");
        }
    }

    [Fact]
    public void LocaleTables_HaveTheKeysOfTheNewWindows()
    {
        var rows = Parse(File.ReadAllText(Path.Combine(Root(), "src", "Singularity.Client.Spike", "locale", "ui.csv"), Encoding.UTF8));
        var keys = rows.Select(r => r[0]).ToHashSet();
        foreach (var k in new[] { "why.budget.nochange", "why.inst.early", "why.inst.building", "refusal.PriorityTooSoon", "refusal.BuildRefused", "lever.budget.title", "lever.inst.title" })
            Assert.Contains(k, keys);
    }

    static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>(); var row = new List<string>(); var sb = new StringBuilder(); bool q = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (q) { if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; } else q = false; } else sb.Append(c); }
            else if (c == '"') q = true;
            else if (c == ',') { row.Add(sb.ToString()); sb.Clear(); }
            else if (c == '\n') { row.Add(sb.ToString()); sb.Clear(); rows.Add(row); row = new List<string>(); }
            else if (c != '\r') sb.Append(c);
        }
        if (sb.Length > 0 || row.Count > 0) { row.Add(sb.ToString()); rows.Add(row); }
        return rows;
    }

    // ---- ui-art-director round 1 (TASK-0156) ----

    [Fact]
    public void LeverText_JoinSkipsEmptyParts_NoDoubleOrTrailingSeparator()
    {
        Assert.Equal("a · c", LeverText.Join("a", "", "c"));
        Assert.Equal("основан 1990", LeverText.Join("основан 1990", ""));   // an institute without levels (DARPA): no trailing « · »
        Assert.Equal("", LeverText.Join("", " "));
    }

    [Fact]
    public void MarkNumbers_KeepsTheDecimalSeparatorOutOfTheMonoFace()
    {
        Assert.Equal("$[code]19[/code],[code]3[/code] млрд", LeverText.MarkNumbers("$19,3 млрд"));
        Assert.Equal("[code]97%[/code] [lb]x]", LeverText.MarkNumbers("97% [x]"));
    }

    [Theory]
    [InlineData("cmd.budget", 0, true)]
    [InlineData("cmd.budget", 3, false)]
    [InlineData("cmd.buy", 3, true)]
    [InlineData("cmd.buy", 1, false)]
    [InlineData("cmd.priority", 1, true)]
    [InlineData("cmd.close", 2, true)]
    [InlineData("cmd.close", 0, false)]
    public void TheLastOrderIsToldOnlyOnItsOwnTab(string key, int tab, bool shown) => Assert.Equal(shown, LeverText.ResultBelongsToTab(key, tab));

    [Fact]
    public void MapLegend_IsHiddenWhileTheLeverWindowIsOpen()
    {
        Assert.True(LeverText.LegendHidden(false, false, true));
        Assert.False(LeverText.LegendHidden(false, false, false));
    }

    static OfferRow Offer(bool canBuy, string refusal = "") =>
        new(1, "S", 2, "$6 млн", "$8 млн", -20, "", true, Singularity.Core.Policy.SaleCircle.All, refusal, canBuy, "$19,3 млрд", 19_294, true);

    [Fact]
    public void MarketReason_ShowsOnlyWhileBuyIsInactive_AndNamesTheCoreReason()
    {
        Assert.Equal("", MarketWindowModel.ReasonKey(Offer(true), true));                              // an active button has no reason under it
        Assert.Equal("licence.bad.Gen", MarketWindowModel.ReasonKey(Offer(false, "licence.bad.Gen"), true));
        Assert.Equal("mkt.notyours.short", MarketWindowModel.ReasonKey(Offer(true), false));            // an open deal in a closed market
        Assert.False(MarketWindowModel.CanBuy(Offer(true), false));
    }

    [Fact]
    public void DefaultOffer_PrefersADealThatCanBeBought()
    {
        var offers = System.Collections.Immutable.ImmutableArray.Create(Offer(false, "licence.bad.Gen"), Offer(true));
        Assert.Equal(1, MarketWindowModel.DefaultOffer(offers, true));
        Assert.Equal(0, MarketWindowModel.DefaultOffer(offers, false));
        Assert.Equal(-1, MarketWindowModel.DefaultOffer(System.Collections.Immutable.ImmutableArray<OfferRow>.Empty, true));
    }

    [Fact]
    public void MarketOffer_OnALivePlayer_ReasonAgreesWithTheHeldLicences()
    {
        // the live Session view: a refusal "you already have it" must go with a licence or an own engine of that generation; with no licences held the text must not claim a licence
        GameData data = ModLoader.Load(Path.Combine(Root(), "mods", "core"));
        var s = SliceSession.Create(data, ClientRun.Config(data, Root(), 7, "USA"));
        for (long t0 = 0; t0 < 40; t0++) s.StepThroughMonth(t0);
        var a = s.View.Actors.First(x => x.Actor == data.Scenarios[0].ActorIndex("USA"));
        var m = MarketWindowModel.Build(a.Market, s.View.Inputs?.Actors.First(x => x.Actor == a.Actor).Reserve, Scale(), x => x.ToString("F1", System.Globalization.CultureInfo.InvariantCulture), i => "S" + i);
        Assert.True(m.Enabled);
        foreach (var o in m.Offers)
        {
            Assert.Equal(o.CanBuy && m.External, MarketWindowModel.ReasonKey(o, m.External) == "");
            if (o.RefusalKey == "licence.bad.Gen") Assert.Equal("licence.bad.Gen", MarketWindowModel.ReasonKey(o, m.External));
        }
        Assert.Contains("двигатель", Loc_RuGenText());   // not «licence» only
    }

    // the reason text of the core's Gen refusal covers an own engine too (PolicySystem.Market: KeyHeld || Covered || engine gen), so it may not say «licence» only
    static string Loc_RuGenText()
    {
        foreach (var line in File.ReadLines(Path.Combine(Root(), "src", "Singularity.Client.Spike", "locale", "ui.csv")))
            if (line.StartsWith("licence.bad.Gen,")) return line;
        return "";
    }

    [Fact]
    public void ReserveSame_WhenThePriceVanishesInTheRoundingOfTheBalance()
    {
        var scale = Scale();
        var mk = new LicenceMarketView(System.Collections.Immutable.ImmutableArray<LicenceHeldView>.Empty, System.Collections.Immutable.ImmutableArray<CatalogueLineView>.Empty,
            System.Collections.Immutable.ImmutableArray.Create(new CatalogueLineView(1, 1, Fx6.FromRaw(20_000), Fx6.One, Fx6.FromRaw(20_000), Singularity.Core.Policy.SaleCircle.All, Singularity.Core.Policy.BadLicence.None)),
            System.Collections.Immutable.ImmutableArray<WorldPriceView>.Empty, Singularity.Core.Policy.BadLicence.None, 0, 0, 0, Fx6.Zero, Fx6.FromInt(1), true);
        var big = MarketWindowModel.Build(mk, new ReserveView(Fx6.FromInt(50), Fx6.Zero, Fx6.Zero), scale, x => ((long)(x / 1000)).ToString(), i => "S");
        Assert.True(big.Offers[0].ReserveSame);   // a few million off tens of billions: «почти не изменится»
        var small = MarketWindowModel.Build(mk, new ReserveView(Fx6.FromRaw(30_000), Fx6.Zero, Fx6.Zero), scale, x => ((long)x).ToString(), i => "S");
        Assert.False(small.Offers[0].ReserveSame);
    }

    [Fact]
    public void BudgetNotes_OneLimitLine_PlusDimmedAtTheLimit_FooterWithoutRepeat()
    {
        var row = new BudgetRow("science", 11, 11, 11, 0, "", 0, "", true, false);
        Assert.False(BudgetWindowModel.PlusEnabled(row, true));
        Assert.True(BudgetWindowModel.MinusEnabled(row, true));
        Assert.False(BudgetWindowModel.MinusEnabled(row with { Pct = 0 }, true));
        Assert.False(BudgetWindowModel.PlusEnabled(row with { AtLimit = false }, false));
        Assert.Equal("Изменений нет", BudgetWindowModel.FooterNone("Изменений нет", "why.budget.nochange", "Ничего не изменено"));
        Assert.Equal("Изменений нет · Чужая держава", BudgetWindowModel.FooterNone("Изменений нет", "why.budget.foreign", "Чужая держава"));
    }

    [Fact]
    public void LineCells_AreUnitedColumns_AndAnOpenProjectHasNoOutput()
    {
        var series = new LineRow(0, new Singularity.Core.Products.DesignHandle(0, 1), 3, (int)Singularity.Core.Products.DesignStage.Series, "$1 млн", 97, 12, 4.5, "bind.Money", "", true, false);
        string[] c = LinesWindowModel.Cells(series, (k, a) => a.Length == 0 ? k : k + ":" + string.Join(",", a), (v, d) => v.ToString("F" + d, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(LinesWindowModel.ColumnKeys.Length, c.Length);
        Assert.Equal("97 %", c[3]);
        Assert.Equal("lines.cell.rate:4.5", c[5]);
        Assert.Equal("bind.Money", c[6]);
        var proj = LinesWindowModel.Cells(series with { StageNo = (int)Singularity.Core.Products.DesignStage.Project }, (k, a) => k, (v, d) => "");
        Assert.Equal("—", proj[5]); Assert.Equal("—", proj[6]);
    }

    [Fact]
    public void InactiveAndSubtitleText_Reads_ThreeToOneFocusRing_OnEveryFill()
    {
        // A7 / DEBT-0095: numbers, not an eye estimate. The measured pixels of a disabled button in a frame: 6.5:1 (55575A on F8F3E3).
        foreach (double k in new[] { 1.0, 0.94 })   // the texture darkens the paper by up to about 6 %
            foreach (string fill in new[] { Palette.C.Paper, Palette.C.Sheet })
            {
                var bg = ColorMath.FromHex(fill, k);
                Assert.True(ColorMath.Contrast(ColorMath.FromHex(Palette.DisabledText), bg) >= 4.5, $"disabled text on {fill} x{k}");
                Assert.True(ColorMath.Contrast(ColorMath.FromHex(Palette.Ink2), bg) >= 4.5, $"ink-2 subtitle on {fill} x{k}");
                Assert.True(ColorMath.Contrast(ColorMath.FromHex(Palette.Ink3), bg) >= 4.5, $"ink-3 caption on {fill} x{k}");
            }
        // the two-tone ring: one of its two rings (ink outside, paper inside) reads 3:1 on every fill a button can have
        foreach (string fill in new[] { Palette.C.Paper, Palette.C.Sheet, Palette.C.Command })
        {
            var bg = ColorMath.FromHex(fill);
            double best = System.Math.Max(ColorMath.Contrast(ColorMath.FromHex(Palette.Ink), bg), ColorMath.Contrast(ColorMath.FromHex(Palette.C.Paper), bg));
            Assert.True(best >= 3.0, $"focus ring on {fill}: {best:F2}");
        }
    }
}
