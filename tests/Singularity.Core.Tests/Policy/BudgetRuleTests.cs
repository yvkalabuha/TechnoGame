using System.Collections.Immutable;
using Singularity.Core.Industry;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Policy;

/// <summary>BUDGET_RULE variants A, B1, B2 (slice-budget-convergence.md §3, §4, tests T1–T7): a measurement switch, NOW is the budget of main.</summary>
public class BudgetRuleTests
{
    private static readonly string[] Ids = ["USA", "GBR", "SUN", "FRA", "SWE"];

    // Start budgets and growth of scenario_1945.yaml (budget_index, growth_pa).
    private static readonly Dictionary<string, (long Budget, long Growth)> Start = new()
    {
        ["USA"] = (1_000_000, 29_900),
        ["GBR"] = (239_000, 22_000),
        ["SUN"] = (229_000, 54_800),
        ["FRA"] = (70_000, 66_200),
        ["SWE"] = (24_000, 41_700),
    };

    private sealed class NoInputs : IPolicyInputs
    {
        public DesignHandle? NewestSeriesDesign(int actorRow, long month) => null;
        public BottleneckLimit? Bottleneck(int actorRow, int design, long month) => null;
        public bool SeriesBelowQuality(int actorRow, Fx6 threshold, long month) => false;
    }

    private sealed class Rig
    {
        private readonly TickRunner _runner;
        private readonly TickCalendar _calendar = new(new CivilDate(1945, 9, 1));
        private readonly string[] _ids;
        public readonly PolicySystem Policy;

        public Rig(BudgetRule rule, string[] ids, Fx6? usaGrowth = null)
        {
            _ids = ids;
            int n = ids.Length;
            var table = new Table(n);
            Fx6[] budget = [.. ids.Select(i => Fx6.FromRaw(Start[i].Budget))];
            Fx6[] growth = [.. ids.Select(i => i == "USA" && usaGrowth is { } g ? g : Fx6.FromRaw(Start[i].Growth))];
            Policy = new PolicySystem(table, new PolicyParams { FvBudget = Fx6.FromInt(10) }, [.. Enumerable.Range(0, n)], [.. budget], [.. growth],
                new CounterRng(1), new NoInputs(), new IndustryMailbox(TickRunner.ChunkCountFor(n, 1)), 99,
                [.. Enumerable.Repeat((int)PolicyProfile.Adaptive, n)], budgetRule: BudgetRulePlan.For(rule, [.. ids]));
            Policy.Initialize();
            _runner = new TickRunner(table, 1, [Policy], 1, 1, _calendar);
        }

        /// <summary>Budget of the actor in the month the next step computes.</summary>
        public Fx6 Budget(string id) => Policy.Budget(Array.IndexOf(_ids, id));

        public void Month(long month)
        {
            while (_runner.Tick <= _calendar.TickOfMonth(month))
                _runner.RunTick();
        }
    }

    private static double D(Fx6 v) => v.Raw / 1e6;

    // ---- plumbing

    [Theory]
    [InlineData("NOW", BudgetRule.Now)]
    [InlineData("A", BudgetRule.A)]
    [InlineData("B1", BudgetRule.B1)]
    [InlineData("B2", BudgetRule.B2)]
    public void Parse_KnownValues(string text, BudgetRule expected) => Assert.Equal(expected, BudgetRules.Parse(text));

    [Fact]
    public void Parse_UnknownValue_IsAnError() => Assert.Throws<ArgumentException>(() => BudgetRules.Parse("C"));

    [Fact]
    public void Plan_NeedsTheFrontierActor_ExceptUnderNow()
    {
        Assert.Throws<ArgumentException>(() => BudgetRulePlan.For(BudgetRule.A, ["GBR", "SUN"]));
        Assert.Equal(-1, BudgetRulePlan.For(BudgetRule.Now, ["GBR", "SUN"]).FrontierRow);
        Assert.Equal(2, BudgetRulePlan.For(BudgetRule.B1, ["GBR", "SUN", "USA"]).FrontierRow);
    }

    [Fact]
    public void Plan_PresetsAreKeyedByActorId_NotByRow()
    {
        BudgetRulePlan a = BudgetRulePlan.For(BudgetRule.A, ["SWE", "USA", "SUN"]);
        Assert.Equal(Fx6.FromRaw(27_000), a.Share[0]);
        Assert.Equal(Fx6.Zero, a.Share[1]);
        Assert.Equal(Fx6.FromRaw(440_000), a.Share[2]);
        Assert.Equal(Fx6.FromRaw(60_000), a.LambdaPerYear[2]);
        Assert.Equal(Fx6.FromRaw(520_000), BudgetRulePlan.For(BudgetRule.B1, ["USA", "SUN"]).Ceiling[1]);
        Assert.Equal(Fx6.FromRaw(430_000), BudgetRulePlan.For(BudgetRule.B2, ["USA", "SUN"]).Ceiling[1]);
    }

    // ---- T1: the budget stays positive

    [Theory]
    [InlineData(1, 0, 0)]                 // smallest share, no pull, no growth
    [InlineData(1, 1_000_000, 29_900)]    // λ = 1
    [InlineData(27_000, 60_000, 0)]
    [InlineData(1_000_000, 1_000_000, 200_000)]
    [InlineData(500_000, 25_000, 29_900)]
    public void T1_BudgetStaysPositive_On1024Months(long shareRaw, long lambdaRaw, long growthRaw)
    {
        Fx6 usa = Fx6.One, g = Fx6.FromRaw(growthRaw);
        Fx6 b = Fx6.FromRaw(24_000);
        for (int t = 0; t < 1024; t++)
        {
            b = BudgetMath.NextConverging(b, usa, g, Fx6.FromRaw(shareRaw), Fx6.FromRaw(lambdaRaw));
            usa = BudgetMath.Next(usa, g);
            Assert.True(b > Fx6.Zero, $"month {t}");
        }
    }

    // ---- T2: monotone, never crosses s (exactly), never above the frontier

    // share(b, f) >= s in real numbers: b / f >= s / Scale, by cross-multiplication.
    private static bool AtOrAbove(Fx6 b, Fx6 f, Fx6 s) => (Int128)b.Raw * Fx6.Scale >= (Int128)s.Raw * f.Raw;

    [Theory]
    [InlineData("SUN")]   // r0 0.229 < s 0.44: the share does not fall
    [InlineData("GBR")]   // r0 0.239 > s 0.12: the share does not rise
    [InlineData("FRA")]
    [InlineData("SWE")]
    public void T2_ShareIsMonotoneTowardsS_NeverCrossesIt_AndStaysBelowTheFrontier(string id)
    {
        var rig = new Rig(BudgetRule.A, Ids);
        BudgetRulePlan plan = BudgetRulePlan.For(BudgetRule.A, [.. Ids]);
        Fx6 s = plan.Share[Array.IndexOf(Ids, id)];
        bool rising = !AtOrAbove(rig.Budget(id), rig.Budget("USA"), s);
        for (long t = 0; t < 1024; t++)
        {
            Fx6 b = rig.Budget(id), f = rig.Budget("USA");
            rig.Month(t);
            Fx6 bn = rig.Budget(id), fn = rig.Budget("USA");
            // The share stays on its side of s, with no allowance for rounding: a share at or above s stays at or above, one below stays at most s.
            if (AtOrAbove(b, f, s))
                Assert.True(AtOrAbove(bn, fn, s), $"{id} fell below s at month {t}");
            else
                Assert.True((Int128)bn.Raw * Fx6.Scale <= (Int128)s.Raw * fn.Raw, $"{id} rose above s at month {t}");
            // Monotone by cross-multiplication: bn/fn against b/f.
            Int128 after = (Int128)bn.Raw * f.Raw, before = (Int128)b.Raw * fn.Raw;
            Assert.True(rising ? after >= before : after <= before, $"{id} share moved the wrong way at month {t}");
            Assert.True(bn <= fn);
        }
    }

    [Fact]
    public void T2_NearTarget_FromAbove_ShareStaysAtOrAboveS()
    {
        // GBR preset: s = 0,12, λ_pa = 0,025. B_GBR 120 001 raw against B_USA 1 000 000 raw is just above s.
        Fx6 f = Fx6.FromRaw(1_000_000), g = Fx6.FromRaw(29_900), s = Fx6.FromRaw(120_000), lambda = Fx6.FromRaw(25_000);
        Fx6 fn = BudgetMath.Next(f, g);
        Assert.Equal(Fx6.FromRaw(1_002_491), fn);
        Fx6 bn = BudgetMath.NextConverging(Fx6.FromRaw(120_001), f, g, s, lambda);
        Assert.True(AtOrAbove(bn, fn, s), $"{bn.Raw} / {fn.Raw} is below 0,12");
        Assert.Equal(120_299, bn.Raw);   // ⌈0,12·1 002 491⌉; the unclamped step gave 120 298 (ratio 0,119999)
    }

    [Fact]
    public void T2_NearTarget_FromBelow_ShareStaysAtMostS()
    {
        Fx6 f = Fx6.FromRaw(1_000_000), g = Fx6.FromRaw(29_900), s = Fx6.FromRaw(120_000);
        Fx6 fn = BudgetMath.Next(f, g);
        foreach (long raw in (long[])[119_999, 119_990, 119_000])
        {
            foreach (long lambdaRaw in (long[])[25_000, 1_000_000])
            {
                Fx6 bn = BudgetMath.NextConverging(Fx6.FromRaw(raw), f, g, s, Fx6.FromRaw(lambdaRaw));
                Assert.True((Int128)bn.Raw * Fx6.Scale <= (Int128)s.Raw * fn.Raw, $"{raw} λ {lambdaRaw}: {bn.Raw}");
            }
        }
    }

    // ---- T3: no overflow over 85 years under any rule (Fx6 is checked)

    [Theory]
    [InlineData(BudgetRule.Now)]
    [InlineData(BudgetRule.A)]
    [InlineData(BudgetRule.B1)]
    [InlineData(BudgetRule.B2)]
    public void T3_EightyFiveYears_NoOverflow_BudgetsPositive_PostInvariantHolds(BudgetRule rule)
    {
        var rig = new Rig(rule, Ids);
        for (long t = 0; t < 1020; t++)
            rig.Month(t);   // Post throws on a non-positive budget; a checked overflow throws too
        Assert.InRange(rig.Budget("USA").Raw, 1_000_000L, 20_000_000L);
    }

    // ---- T4: the order of the actors does not matter; B_USA is read from Current

    [Theory]
    [InlineData(BudgetRule.A)]
    [InlineData(BudgetRule.B1)]
    [InlineData(BudgetRule.B2)]
    public void T4_PermutingTheRows_GivesTheSameBudgetPerActor(BudgetRule rule)
    {
        string[][] orders = [Ids, [.. Ids.Reverse()], ["SUN", "SWE", "USA", "FRA", "GBR"], ["FRA", "GBR", "SWE", "SUN", "USA"]];
        var rigs = orders.Select(o => new Rig(rule, o)).ToArray();
        for (long t = 0; t < 600; t++)
        {
            foreach (Rig r in rigs)
                r.Month(t);
            if (t % 50 != 0)
                continue;
            foreach (string id in Ids)
                Assert.All(rigs.Skip(1), r => Assert.Equal(rigs[0].Budget(id), r.Budget(id)));
        }
    }

    // ---- T5: USA is BudgetMath.Next bit for bit

    [Theory]
    [InlineData(BudgetRule.A)]
    [InlineData(BudgetRule.B1)]
    [InlineData(BudgetRule.B2)]
    public void T5_Usa_IsBudgetMathNext_BitForBit(BudgetRule rule)
    {
        var rig = new Rig(rule, Ids);
        Fx6 expected = Fx6.FromRaw(Start["USA"].Budget);
        for (long t = 0; t < 1020; t++)
        {
            Assert.Equal(expected, rig.Budget("USA"));
            rig.Month(t);
            expected = BudgetMath.Next(expected, Fx6.FromRaw(Start["USA"].Growth));
        }
    }

    [Fact]
    public void Now_EveryActor_IsBudgetMathNext_AndTheSameAsWithoutAPlan()
    {
        var rig = new Rig(BudgetRule.Now, Ids);
        var expected = Ids.Select(i => Fx6.FromRaw(Start[i].Budget)).ToArray();
        for (long t = 0; t < 500; t++)
        {
            for (int i = 0; i < Ids.Length; i++)
                Assert.Equal(expected[i], rig.Budget(Ids[i]));
            rig.Month(t);
            for (int i = 0; i < Ids.Length; i++)
                expected[i] = BudgetMath.Next(expected[i], Fx6.FromRaw(Start[Ids[i]].Growth));
        }
    }

    [Fact]
    public void RuleB_ActorWithoutCeiling_GrowsAsNow()
    {
        string[] ids = Ids;
        var rigB = new Rig(BudgetRule.B1, ids);
        var now = new Rig(BudgetRule.Now, ids);
        for (long t = 0; t < 700; t++)
        {
            rigB.Month(t);
            now.Month(t);
        }
        Assert.Equal(now.Budget("GBR"), rigB.Budget("GBR"));   // GBR has no ceiling
    }

    // ---- T6: closed form of the share

    [Theory]
    [InlineData("SUN", 60)]
    [InlineData("SUN", 360)]
    [InlineData("GBR", 1020)]
    [InlineData("FRA", 600)]
    [InlineData("SWE", 1020)]
    public void T6_ShareFollowsTheClosedForm_Within1e4(string id, int months)
    {
        var rig = new Rig(BudgetRule.A, Ids);
        BudgetRulePlan plan = BudgetRulePlan.For(BudgetRule.A, [.. Ids]);
        int row = Array.IndexOf(Ids, id);
        double s = D(plan.Share[row]);
        double lambda = D(BudgetMath.Monthly(plan.LambdaPerYear[row]));
        double r0 = D(rig.Budget(id) / rig.Budget("USA"));
        for (long t = 0; t < months; t++)
            rig.Month(t);
        double actual = D(rig.Budget(id) / rig.Budget("USA"));
        double expected = s - (s - r0) * System.Math.Pow(1 - lambda, months);
        Assert.InRange(System.Math.Abs(actual - expected), 0, 1e-4);
    }

    // ---- T7: the ceiling first binds in an exact month, and holds afterwards

    // Convention: month 0 = 1945-09, the month of the start budgets. Budget(m) is the budget of month m; month m is capped when
    // Budget(m) equals the ceiling value c * B_USA(m), and "first capped month" is the smallest such m.
    private static readonly (BudgetRule Rule, string Id, int FirstCapped, int Table)[] Ceilings =
    [
        (BudgetRule.B1, "SUN", 397, 397),   // note: Oct 1978
        (BudgetRule.B1, "FRA", 396, 395),   // Aug 1978
        (BudgetRule.B1, "SWE", 418, 413),   // Feb 1980
        (BudgetRule.B2, "SUN", 305, 305),   // Feb 1971
        (BudgetRule.B2, "FRA", 343, 342),   // Mar 1974
        (BudgetRule.B2, "SWE", 298, 293),   // Feb 1970
    ];

    public static IEnumerable<object[]> CeilingCases => Ceilings.Select(c => new object[] { c.Rule, c.Id, c.FirstCapped });

    [Theory]
    [MemberData(nameof(CeilingCases))]
    public void T7_CeilingFirstBindsInTheExactMonth_AndHoldsAfter(BudgetRule rule, string id, int firstCapped)
    {
        var rig = new Rig(rule, Ids);
        Fx6 c = BudgetRulePlan.For(rule, [.. Ids]).Ceiling[Array.IndexOf(Ids, id)];
        int first = -1;
        for (int m = 1; m <= 1000; m++)
        {
            rig.Month(m - 1);   // after the step of month m - 1 the columns hold month m
            bool capped = rig.Budget(id) == c * rig.Budget("USA");
            if (first < 0 && capped)
                first = m;
            if (first >= 0)
                Assert.True(capped, $"{id} leaves the ceiling at month {m}");
            else
                Assert.True(rig.Budget(id) < c * rig.Budget("USA"), $"{id} above the ceiling at month {m}");
        }
        Assert.Equal(firstCapped, first);
    }

    [Theory]
    [MemberData(nameof(CeilingCases))]
    public void T7_TheFixedPointRecurrenceGivesTheSameMonth(BudgetRule rule, string id, int firstCapped)
    {
        Fx6 c = BudgetRulePlan.For(rule, [.. Ids]).Ceiling[Array.IndexOf(Ids, id)];
        Fx6 b = Fx6.FromRaw(Start[id].Budget), usa = Fx6.FromRaw(Start["USA"].Budget);
        Fx6 gb = Fx6.FromRaw(Start[id].Growth), gu = Fx6.FromRaw(Start["USA"].Growth);
        int m = 0;
        while (b != c * usa && m < 1000)
        {
            b = BudgetMath.NextCapped(b, gb, usa, gu, c);
            usa = BudgetMath.Next(usa, gu);
            m++;
        }
        Assert.Equal(firstCapped, m);
    }

    [Fact]
    public void T7_DistanceToTheNotesUnroundedTable_SunAndFraWithinOne_SweFourOrFiveLater()
    {
        // The note's table is continuous-growth arithmetic. Fx6 floors every step, and at a budget near 24 000 raw (SWE) that loses about 2e-5
        // of the budget per step, so SWE binds 4 (B1) and 5 (B2) months after the table; SUN and FRA agree within a month.
        foreach ((BudgetRule _, string id, int exact, int table) in Ceilings)
        {
            int distance = exact - table;
            if (id == "SWE")
                Assert.InRange(distance, 4, 5);
            else
                Assert.InRange(distance, -1, 1);
        }
    }
}

