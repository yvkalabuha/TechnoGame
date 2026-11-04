using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>
/// TASK-0028 (T-fleet, spec §11 п. 4, 5a): the AI rule A42.4 on the assembled slice. The state of USA at month 100 is rewritten by hand
/// (the table is poked between ticks, as in the other session tests) into a fleet that the rule must act on, then one month is run and the
/// commands of the AI are read from <c>pol.flt.*</c>. USA is Quality (P1): <c>U_HI</c> 0,25, <c>U_LO</c> 0,10.
/// </summary>
public class FleetRuleTests
{
    private const int Usa = 0;
    private const long AtMonth = 100;

    private sealed class Fixture
    {
        public required SliceSession Session;
        public SliceWorld World => Session.World;
        public ProductsSystem Products => World.Products;

        public void Int(string name, int value) => Poke.Int(Session, name, Usa, value);
        public void Long(string name, long value) => Poke.Long(Session, name, Usa, value);

        /// <summary>Sets a design: stage, platform generation, alternatives of the superior G<paramref name="gen"/> composition, cohorts by year index.</summary>
        public void Design(int d, DesignStage stage, int platformGen, int compositionGen, params double[] cohorts)
        {
            ProductsLayout l = World.ProductsLayout;
            var alts = new int[l.SlotCount];
            Assert.True(DesignRules.Superior(l, compositionGen, l.KnownUpTo(compositionGen), alts));
            Int($"prd.d{d}.stage", (int)stage);
            foreach (string col in new[] { "d0", "hidden", "found", "open", "dGate", "work", "retroPool", "lossAcc", "rate" })
                Long($"prd.d{d}.{col}", 0);
            Int($"prd.d{d}.forced", 0);
            Int($"prd.d{d}.idleC", 0);
            Int($"prd.d{d}.gp", platformGen);
            for (int s = 0; s < alts.Length; s++)
                Int($"prd.d{d}.alt.{s}", alts[s]);
            Int($"prd.d{d}.builtAny", 1);
            Int($"prd.d{d}.cancelled", 0);
            Int($"prd.d{d}.lineBind", stage == DesignStage.Series ? (int)LineBind.Money : 0);
            Int($"prd.d{d}.quotaBase", 100_000);
            Int($"prd.d{d}.soldY", 0);
            Long($"prd.d{d}.lineE", stage == DesignStage.Series ? 100_000_000 : 0);
            for (int y = 0; y < l.Years; y++)
                Long($"prd.d{d}.cohort.{l.StartYear + y}", y < cohorts.Length ? (long)Math.Round(cohorts[y] * Fx6.Scale) : 0);
        }

        public void FreeBureaus()
        {
            for (int b = 0; b < World.ProductsLayout.Bureaus; b++)
                Int($"prd.b{b}.design", -1);
        }

        public void Master(int d)
        {
            foreach (int p in Products.PrcSet(Usa, d))
                Poke.Int(Session, $"ind.mr.{p}", Usa, 6);
        }

        public IReadOnlyList<FleetCommand> Run(long monthToCompute)
        {
            Session.StepThroughMonth(monthToCompute);
            return World.Policy.AiFleetCommands(Usa, monthToCompute + 1);
        }

        /// <summary>The <c>n</c> of spec A42.4 п. 5 for a candidate, from the same snapshot (month t = <paramref name="t"/>).</summary>
        public long ExpectedN(int d, long t, double ulo, int keep, Fx6 alreadyFreed)
        {
            ProductsSystem p = Products;
            Fx6 budgetNext = BudgetMath.Next(World.Policy.Budget(Usa), World.Scenario.GrowthPa[Usa]);
            Fx6 baseNext = World.Policy.Shares(Usa).Procurement * budgetNext;
            Fx6 need = Fx6.Zero;
            for (int e = 0; e < World.ProductsLayout.Designs; e++)
            {
                if (p.Stage(Usa, e) is DesignStage.Series or DesignStage.InService)
                    need += Fx6.FromInt(p.InService(Usa, e).Raw / Fx6.Scale) * p.Upkeep(Usa, e);
            }
            Fx6 x = need - Fx6.FromRaw((long)(ulo * Fx6.Scale)) * baseNext - alreadyFreed;
            long wanted = (x.Raw + p.Upkeep(Usa, d).Raw - 1) / p.Upkeep(Usa, d).Raw;
            long avail = 0, whole = p.InService(Usa, d).Raw / Fx6.Scale;
            Fx6[] c = p.Cohorts(Usa, d);
            Fx6 sum = Fx6.Zero;
            for (int y = 0; y < c.Length; y++)
            {
                if (t + 1 - p.CohortMidyear(y) >= 60)
                    sum += c[y];
            }
            avail = sum.Raw / Fx6.Scale;
            return Math.Min(Math.Min(avail, whole - keep), wanted);
        }
    }

    private static Fixture Start(FleetLineCond cond = FleetLineCond.Gen, FleetArm arm = FleetArm.Main, int cell = 0, int threads = 1, int chunk = 1)
    {
        var s = SliceTestData.Create(new SliceRunConfig { Seed = 7, ProfileCell = cell, FleetLineCond = cond, FleetArm = arm, Threads = threads, ChunkSize = chunk });
        s.StepThroughMonth(AtMonth);
        var f = new Fixture { Session = s };
        // The natural state of USA is not the subject: drop its other designs and the AI's pending decisions.
        for (int d = 0; d < s.World.ProductsLayout.Designs; d++)
            f.Design(d, DesignStage.None, 0, 1);
        for (int d = 0; d < s.World.ProductsLayout.Designs; d++)
        {
            f.Int($"prd.d{d}.cancelled", 0);
            f.Int($"prd.d{d}.builtAny", 0);
        }
        f.Long("prd.saleCap", 1_000_000_000_000);
        f.Long("prd.saleY", 0);
        f.FreeBureaus();
        return f;
    }

    // The heavy park d0 of G1: four old cohorts (ages at month 102: 103, 91, 79, 67) and a young one (age 55).
    private static double[] HeavyPark(Fixture f)
    {
        // u ≈ 0,6: the upkeep of the park alone is well above U_HI = 0,25 of the procurement base.
        Fx6 budgetNext = BudgetMath.Next(f.World.Policy.Budget(Usa), f.World.Scenario.GrowthPa[Usa]);
        Fx6 baseNext = f.World.Policy.Shares(Usa).Procurement * budgetNext;
        double units = baseNext.Raw * 0.6 / 29;
        return [units * 0.2, units * 0.2, units * 0.2, units * 0.2, units * 0.2];
    }

    [Fact]
    public void GenCondition_LineG2OverParkG1_RetiresTheOldestCohortsByTheFormula_AndNothingIsSoldWithoutAFrontRunner()
    {
        Fixture f = Start();
        f.Design(0, DesignStage.InService, 1, 1, HeavyPark(f));
        f.Design(2, DesignStage.Series, 2, 2, 10);   // line G2, not fielded (10 units), nobody is fielded: G_front = 0
        long expected = f.ExpectedN(0, 101, 0.10, 0, Fx6.Zero);
        IReadOnlyList<FleetCommand> cmds = f.Run(101);
        FleetCommand c = Assert.Single(cmds);
        Assert.Equal(new FleetCommand(FleetCommandKind.Retire, 0, expected), c);
        Assert.True(expected > 0);
        Assert.Equal(Fx6.Zero, f.World.Policy.ExpectedSaleIncome(Usa));
        Assert.Equal(1, f.World.Policy.FleetCommandsByPair(Usa, 2, 1));
    }

    [Fact]
    public void GenCondition_SameGeneration_NoCommand_AndNoneArmCommandsForAnyProjectInServiceBesideTheLine()
    {
        Fixture f = Start();
        f.Design(0, DesignStage.InService, 2, 2, HeavyPark(f));
        f.Design(2, DesignStage.Series, 2, 2, 10);
        Assert.Empty(f.Run(101));

        Fixture none = Start(FleetLineCond.None);
        none.Design(0, DesignStage.InService, 2, 2, HeavyPark(none));
        none.Design(2, DesignStage.Series, 2, 2, 10);
        Assert.Single(none.Run(101));

        Fixture newer = Start(FleetLineCond.None);        // gen(L) < gen(d): only NONE acts
        newer.Design(0, DesignStage.InService, 3, 3, HeavyPark(newer));
        newer.Design(2, DesignStage.Series, 2, 2, 10);
        Assert.Single(newer.Run(101));
        Fixture older = Start();
        older.Design(0, DesignStage.InService, 3, 3, HeavyPark(older));
        older.Design(2, DesignStage.Series, 2, 2, 10);
        Assert.Empty(older.Run(101));
    }

    [Fact]
    public void GenCondition_LineG3OverParkG2_ActsUnderGen_NotUnderKappa()
    {
        // κ(G3) < κ(G2) in the data (spec A42.9): the v0.16 condition would never act on this pair.
        Fixture gen = Start();
        gen.Design(0, DesignStage.InService, 2, 2, HeavyPark(gen));
        gen.Design(2, DesignStage.Series, 3, 3, 10);
        Assert.Single(gen.Run(101));
        Assert.Equal(1, gen.World.Policy.FleetCommandsByPair(Usa, 3, 2));

        Fixture kappa = Start(FleetLineCond.Kappa);
        kappa.Design(0, DesignStage.InService, 2, 2, HeavyPark(kappa));
        kappa.Design(2, DesignStage.Series, 3, 3, 10);
        Assert.Empty(kappa.Run(101));
    }

    [Fact]
    public void GenCondition_UsesGenOf_NotThePlatformGeneration()
    {
        // The park has g_p = 3 on a G1 engine: gen = min(3, 1 + 1) = 2, so a G3 line is newer than it although g_p are equal.
        Fixture f = Start();
        f.Design(0, DesignStage.InService, 3, 1, HeavyPark(f));
        f.Design(2, DesignStage.Series, 3, 3, 10);
        Assert.Equal(2, f.Products.Gen(Usa, 0));
        Assert.Single(f.Run(101));
    }

    [Fact]
    public void TheRuleIsSilent_WithoutALine_BelowUHi_OrWhenMoneyDoesNotBindTheLine()
    {
        Fixture noLine = Start();
        noLine.Design(0, DesignStage.InService, 1, 1, HeavyPark(noLine));
        Assert.Empty(noLine.Run(101));

        Fixture light = Start();
        light.Design(0, DesignStage.InService, 1, 1, 5, 5, 5, 5, 5);
        light.Design(2, DesignStage.Series, 2, 2, 10);
        long armedBefore = light.World.Policy.FleetArmedMonths(Usa);
        Assert.Empty(light.Run(101));
        Assert.Equal(armedBefore, light.World.Policy.FleetArmedMonths(Usa));

        Fixture alloy = Start();
        alloy.Design(0, DesignStage.InService, 1, 1, HeavyPark(alloy));
        alloy.Design(2, DesignStage.Series, 2, 2, 10);
        alloy.Int("prd.d2.lineBind", (int)LineBind.Alloy);
        long armed = alloy.World.Policy.FleetArmedMonths(Usa), silent = alloy.World.Policy.FleetSilentByBindMonths(Usa);
        Assert.Empty(alloy.Run(101));
        Assert.Equal(armed + 1, alloy.World.Policy.FleetArmedMonths(Usa));
        Assert.Equal(silent + 1, alloy.World.Policy.FleetSilentByBindMonths(Usa));
    }

    [Fact]
    public void ProjectsInSeries_AndCohortsYoungerThanAProt_AreNeverTaken()
    {
        Fixture f = Start();
        double[] park = HeavyPark(f);
        f.Design(0, DesignStage.InService, 1, 1, park);
        f.Design(1, DesignStage.Series, 1, 1, [.. park]);   // a second project in С: not a candidate
        f.Design(2, DesignStage.Series, 2, 2, 10);          // the line (highest id in С)
        IReadOnlyList<FleetCommand> cmds = f.Run(101);
        Assert.All(cmds, c => Assert.Equal(0, c.Design));

        // Every cohort of the park is younger than 60 months at month 102: nothing is taken.
        Fixture young = Start();
        young.Design(0, DesignStage.InService, 1, 1, 0, 0, 0, 0, 0, 0, 0, HeavyPark(young)[0] * 5);   // year index 7 = 1952: age 20
        young.Design(2, DesignStage.Series, 2, 2, 10);
        Assert.Empty(young.Run(101));
    }

    [Fact]
    public void Keep_HoldsFiftyUnitsOfAFieldedProjectThatDefinesG_UntilTheLineIsFielded()
    {
        Fixture f = Start();
        f.Design(0, DesignStage.InService, 1, 1, HeavyPark(f));
        f.Master(0);
        f.Design(2, DesignStage.Series, 2, 2, 10);       // the line is not fielded: the G1 park defines G
        Assert.Equal(1, f.Products.FieldedGen(Usa, 100));
        long expectedKeep = f.ExpectedN(0, 101, 0.10, 50, Fx6.Zero);
        long wholeBefore = f.Products.InService(Usa, 0).Raw / Fx6.Scale;
        IReadOnlyList<FleetCommand> cmds = f.Run(101);
        FleetCommand c = Assert.Single(cmds);
        Assert.Equal(expectedKeep, c.N);
        Assert.True(wholeBefore - c.N >= 50);

        // The line becomes fielded (60 units, processes mastered): the G1 park leaves Def(K) and may go below 50.
        Fixture after = Start();
        double[] park = HeavyPark(after);
        after.Design(0, DesignStage.InService, 1, 1, park);
        after.Master(0);
        after.Design(2, DesignStage.Series, 2, 2, 60);
        after.Master(2);
        Assert.Equal(2, after.Products.FieldedGen(Usa, 100));
        long expected = after.ExpectedN(0, 101, 0.10, 0, Fx6.Zero);
        IReadOnlyList<FleetCommand> cmds2 = after.Run(101);
        Assert.Equal(expected, cmds2.Sum(x => x.N));
        Assert.True(cmds2.Sum(x => x.N) > 0);
    }

    [Fact]
    public void WhenAFrontRunnerExists_TheAiSellsUpToTheQuotaAndRetiresTheRest_AndPromisesTheRevenue()
    {
        Fixture f = Start();
        double[] park = HeavyPark(f);
        f.Design(0, DesignStage.InService, 1, 1, park);
        f.Int("prd.d0.quotaBase", 120);                  // quota ⌈0,1·120⌉ = 12 units
        f.Design(2, DesignStage.Series, 2, 2, 60);       // fielded G2 line: G_front = 2, gap = 1 for the G1 park
        f.Master(2);
        f.Int("prd.d0.quotaBase", 120);
        IReadOnlyList<FleetCommand> cmds = f.Run(101);
        Assert.Equal(2, cmds.Count);
        Assert.Equal(new FleetCommand(FleetCommandKind.Sell, 0, 12), cmds[0]);
        Assert.Equal(FleetCommandKind.Retire, cmds[1].Kind);
        Assert.Equal(0, cmds[1].Design);
        Fx6 promised = f.World.Policy.ExpectedSaleIncome(Usa);
        Assert.True(promised > Fx6.Zero);
        // FL7: the month after, the layer pays exactly the promised revenue, and refuses nothing. (The month's merge recomputes mastered_run from the
        // natural yields of USA, which are below 0,7; the fixture keeps the front runner fielded.)
        f.Master(2);
        f.Session.StepThroughMonth(102);
        Assert.Equal(promised, f.Products.SaleIncome(Usa));
        Assert.Equal(0, Enum.GetValues<CommandRejection>().Where(r => (int)r >= 9).Sum(r => f.Products.RejectionCount(Usa, r)));
        Assert.Equal(Fx6.FromInt(12), f.Products.Sold(Usa, 0));
    }

    [Fact]
    public void L2_TheSaleAndRetireMonths_GiveOneHash_ForOneAndEightThreads()
    {
        string Hash(int threads, int chunk)
        {
            Fixture f = Start(threads: threads, chunk: chunk);
            f.Design(0, DesignStage.InService, 1, 1, HeavyPark(f));
            f.Int("prd.d0.quotaBase", 120);
            f.Design(2, DesignStage.Series, 2, 2, 60);
            f.Master(2);
            f.Run(101);
            f.Master(2);
            f.Session.StepThroughMonth(102);
            Assert.True(f.Products.Sold(Usa, 0) > Fx6.Zero);   // the month really sold and retired
            return f.Session.LastHash;
        }
        Assert.Equal(Hash(1, 1), Hash(8, 3));
    }

    [Fact]
    public void Forecasts_ArePureFunctionsOfTheSnapshot_AndShowUpkeepFreedRevenueAndWarnings()
    {
        Fixture f = Start();
        f.Design(0, DesignStage.InService, 1, 1, HeavyPark(f));
        f.Master(0);
        f.Design(2, DesignStage.Series, 2, 2, 10);
        f.Session.StepThroughMonth(100);
        string before = f.Session.LastHash;
        // The park defines G (fielded G1); retiring all but 10 units leaves it below keep: the warning, not a refusal.
        long whole = f.Products.InService(Usa, 0).Raw / Fx6.Scale;
        FleetForecast r = f.Products.RetireForecast(Usa, 0, whole - 10, 101);
        Assert.True(r.FieldedBefore && r.InDefK && r.BelowKeep && !r.FieldedAfter);
        Assert.Equal(Fx6.FromInt(whole - 10) * f.Products.Upkeep(Usa, 0), r.UpkeepDelta);
        Assert.True(r.UAfter < r.UBefore);
        Assert.Equal(r.LineBind == LineBind.Money, r.FreedMoneyIsSpent);
        FleetForecast sell = f.Products.SellForecast(Usa, 0, 1, 101);
        Assert.NotNull(sell.Sale);
        Assert.Equal(SaleReject.NotSurplus, sell.Sale!.Value.Reject);   // nobody is ahead of the park
        Assert.Equal(before, f.Session.LastHash);   // a forecast changes nothing
    }

    [Fact]
    public void LineRate_AddsTheExpectedSaleRevenueToTheBase_AndPurchaseCapKeepsTheUpkeepAtULo()
    {
        Fixture f = Start();
        f.Design(0, DesignStage.InService, 1, 1, HeavyPark(f));
        f.Int("prd.d0.quotaBase", 120);
        f.Design(2, DesignStage.Series, 2, 2, 60);
        f.Master(2);
        f.Run(101);
        PolicySystem pol = f.World.Policy;
        Fx6 inc = pol.ExpectedSaleIncome(Usa);
        Assert.True(inc > Fx6.Zero);
        Fx6 b = Fx6.FromInt(1000), floor = Fx6.FromRaw(2256);
        Assert.Equal((b + inc) / floor, pol.AiLineRate(Usa, b, b, floor, Fx6.Zero, Fx6.FromRaw(29)));

        Fixture cap = Start(arm: FleetArm.PurchaseCap);
        PolicySystem p2 = cap.World.Policy;
        Fx6 lo = p2.FleetULo(Usa);
        Assert.Equal(Fx6.FromRaw(100_000), lo);   // U_HI(P1) 0,25 − 0,15
        Fx6 need = Fx6.FromRaw(40_000), up = Fx6.FromRaw(29), basis = Fx6.FromInt(1);
        Fx6 room = lo * basis - need;
        Assert.Equal(room / up, p2.AiLineRate(Usa, Fx6.FromInt(1_000_000), basis, Fx6.FromRaw(1), need, up));       // the cap binds
        Assert.Equal(Fx6.Zero, p2.AiLineRate(Usa, Fx6.FromInt(1_000_000), basis, Fx6.FromRaw(1), Fx6.FromRaw(200_000), up));   // no room at all
        Assert.Equal(Fx6.FromInt(2), p2.AiLineRate(Usa, Fx6.FromInt(2), basis, Fx6.FromInt(1), Fx6.Zero, Fx6.FromRaw(1)));   // the rate itself is lower than the room
    }

    [Fact]
    public void NoSaleArm_OnlyRetires_AndTheOtherArmsIssueNothing()
    {
        Fixture f = Start(arm: FleetArm.NoSale);
        f.Design(0, DesignStage.InService, 1, 1, HeavyPark(f));
        f.Design(2, DesignStage.Series, 2, 2, 60);
        f.Master(2);
        IReadOnlyList<FleetCommand> cmds = f.Run(101);
        Assert.All(cmds, c => Assert.Equal(FleetCommandKind.Retire, c.Kind));
        Assert.NotEmpty(cmds);

        foreach (FleetArm arm in new[] { FleetArm.NoRetire, FleetArm.PurchaseCap })
        {
            Fixture g = Start(arm: arm);
            g.Design(0, DesignStage.InService, 1, 1, HeavyPark(g));
            g.Design(2, DesignStage.Series, 2, 2, 60);
            g.Master(2);
            Assert.Empty(g.Run(101));
        }
    }

    [Fact]
    public void Projection_ACohortThatReachesItsServiceLifeNextMonthIsNotCounted()
    {
        // Month 189 decides for 190: the 1946 cohort (midyear 10) is 180 months old then and is written off by step 0 before the commands.
        var s = SliceTestData.Create(new SliceRunConfig { Seed = 7, ProfileCell = 0 });
        s.StepThroughMonth(188);
        var f = new Fixture { Session = s };
        for (int d = 0; d < s.World.ProductsLayout.Designs; d++)
            f.Design(d, DesignStage.None, 0, 1);
        f.Long("prd.saleCap", 1_000_000_000_000);
        f.FreeBureaus();
        Fx6 budgetNext = BudgetMath.Next(s.World.Policy.Budget(Usa), s.World.Scenario.GrowthPa[Usa]);
        double units = BudgetMath.Next(budgetNext, s.World.Scenario.GrowthPa[Usa]).Raw * 0.6 / 29 * 0.35 * 0.5;
        f.Design(0, DesignStage.InService, 1, 1, 0, units, units, units);   // years 1946 (leaves), 1947, 1948
        f.Design(2, DesignStage.Series, 2, 2, 10);
        IReadOnlyList<FleetCommand> cmds = f.Run(189);
        Assert.NotEmpty(cmds);
        long alive = (long)((units * 2) / 1);   // the 1947 and 1948 cohorts only
        Assert.True(cmds.Sum(c => c.N) <= alive, $"commands {cmds.Sum(c => c.N)} above the {alive} units alive at month 190");
        s.StepThroughMonth(190);
        Assert.Equal(0, Enum.GetValues<CommandRejection>().Where(r => (int)r >= 9).Sum(r => f.Products.RejectionCount(Usa, r)));
    }

    [Fact]
    public void Projection_AtTheStartOfAPolicyYear_TheSaleIsPricedAgainstTheNewQuotaBase()
    {
        // Month 191 decides for 192 = 16·12, a year start: quota_base is reset from the cohorts, and the old year's counters do not apply.
        var s = SliceTestData.Create(new SliceRunConfig { Seed = 7, ProfileCell = 0 });
        s.StepThroughMonth(190);
        var f = new Fixture { Session = s };
        for (int d = 0; d < s.World.ProductsLayout.Designs; d++)
            f.Design(d, DesignStage.None, 0, 1);
        f.FreeBureaus();
        f.Long("prd.saleCap", 0);   // the ceiling of the old year is spent; the reset gives a fresh one
        f.Long("prd.saleY", 0);
        Fx6 b2 = BudgetMath.Next(BudgetMath.Next(s.World.Policy.Budget(Usa), s.World.Scenario.GrowthPa[Usa]), s.World.Scenario.GrowthPa[Usa]);
        double units = b2.Raw * 0.6 / 29 * 0.5;
        f.Design(0, DesignStage.InService, 1, 1, 0, 0, units, units);
        f.Int("prd.d0.quotaBase", 0);
        f.Int("prd.d0.soldY", 0);
        f.Design(2, DesignStage.Series, 2, 2, [.. new double[15], 60]);   // 1960 cohort: alive at month 192
        f.Master(2);
        f.Int("prd.d0.quotaBase", 0);
        IReadOnlyList<FleetCommand> cmds = f.Run(191);
        FleetCommand sell = Assert.Single(cmds, c => c.Kind == FleetCommandKind.Sell);
        Assert.True(sell.N > 0);
        Fx6 promised = s.World.Policy.ExpectedSaleIncome(Usa);
        f.Master(2);   // the merge of 191 recomputed mastered_run from the natural yields; keep the front runner fielded for the step
        s.StepThroughMonth(192);
        Assert.Equal(promised, f.Products.SaleIncome(Usa));
        Assert.Equal(0, Enum.GetValues<CommandRejection>().Where(r => (int)r >= 9).Sum(r => f.Products.RejectionCount(Usa, r)));
        Assert.Equal(sell.N, f.Products.SoldInYear(Usa, 0));
        Assert.True(f.Products.SaleInYear(Usa) <= f.Products.SaleCap(Usa));
    }
}

/// <summary>Writes one cell of a column between ticks (the Kernel has no single-cell writer; the tables here are tiny).</summary>
internal static class Poke
{
    public static void Int(SliceSession s, string name, int row, int value)
    {
        IntColumn c = s.World.Table.GetInt(name);
        int[] all = c.Current.ToArray();
        all[row] = value;
        c.Load(all);
    }

    public static void Long(SliceSession s, string name, int row, long value)
    {
        LongColumn c = s.World.Table.GetLong(name);
        long[] all = c.Current.ToArray();
        all[row] = value;
        c.Load(all);
    }
}
