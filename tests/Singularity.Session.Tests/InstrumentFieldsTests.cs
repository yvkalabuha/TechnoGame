using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>Research in progress and alloy coverage in the actor view (TASK-0119): each field is checked against a brute-force read of Core on the same state.</summary>
public sealed class InstrumentFieldsTests
{
    private const int Months = 120;

    [Fact]
    public void Research_IsTheFrontierWithProgress_NearestToCompletionFirst_AndEveryActorHasSomeInTheWalk()
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 7 });
        SliceWorld w = s.World;
        int actors = w.Scenario.ActorCount;
        var months = new int[actors];
        for (long t = 0; t < Months; t++)
        {
            s.StepThroughMonth(t);
            for (int a = 0; a < actors; a++)
            {
                var expected = new List<(int Row, Fx6 Share)>();
                for (int r = 0; r < w.Graph.Count; r++)
                {
                    Fx6 prog = w.Knowledge.Prog(a, r);
                    if (prog > Fx6.Zero && w.Knowledge.InFrontier(a, r))
                        expected.Add((r, prog / w.Knowledge.NeedOf(r)));
                }
                // share descending, row ascending on ties
                var top = expected.OrderByDescending(e => e.Share.Raw).ThenBy(e => e.Row).Take(5).ToList();
                var got = s.View.Actors[a].Research;
                Assert.Equal(top.Count, got.Length);
                for (int i = 0; i < top.Count; i++)
                {
                    Assert.Equal(w.Graph.Number[top[i].Row], got[i].Node);
                    Assert.Equal(w.Knowledge.Prog(a, top[i].Row), got[i].Have);
                    Assert.Equal(w.Knowledge.NeedOf(top[i].Row), got[i].Need);
                    Assert.Equal(w.Graph.IsHazard[top[i].Row] && got[i].Have >= got[i].Need, got[i].Threshold);
                }
                if (got.Length > 0)
                    months[a]++;
            }
        }
        for (int a = 0; a < actors; a++)
            Assert.True(months[a] * 100 >= Months * 80, $"actor {a}: research in progress in {months[a]} of {Months} months");
    }

    [Fact]
    public void Alloy_IsSupplyOfTheMonthAndTheAlloyAskedInThatStep()
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 7 });
        SliceWorld w = s.World;
        bool needSeen = false;
        for (long t = 0; t < Months; t++)
        {
            s.StepThroughMonth(t);
            for (int a = 0; a < w.Scenario.ActorCount; a++)
            {
                AlloyCoverage c = s.View.Actors[a].Alloy;
                Assert.Equal(w.Industry.Alloy(a, t).Supply, c.Supply);
                Assert.Equal(w.Policy.AlloyDeficit(a, t), c.DeficitFraction);
                Assert.Equal(w.Products.LastAlloyAsk(a), c.Need);
                needSeen |= c.Need > Fx6.Zero;
            }
        }
        Assert.True(needSeen, "no actor ran an alloy-using line in the walk: the test does not exercise the need");
    }

    [Fact]
    public void ALineBoundByAlloy_ShowsNeedAboveSupply()
    {
        // The produced rate is already cut by the alloy; the need must come from the asked rate, so a line stopped for lack of alloy still counts.
        int bound = 0;
        foreach (int seed in new[] { 1, 2, 3, 4, 5, 6, 7, 8 })
        {
            SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = (ulong)seed });
            SliceWorld w = s.World;
            for (long t = 0; t < 200; t++)
            {
                s.StepThroughMonth(t);
                for (int a = 0; a < w.Scenario.ActorCount; a++)
                {
                    bool alloyBound = false;
                    for (int d = 0; d < w.ProductsLayout.Designs; d++)
                        alloyBound |= w.Products.Stage(a, d) == DesignStage.Series && w.Products.Bind(a, d) == LineBind.Alloy;
                    if (!alloyBound)
                        continue;
                    bound++;
                    AlloyCoverage c = s.View.Actors[a].Alloy;
                    Assert.True(c.Need > c.Supply, $"seed {seed} month {t} actor {a}: a line is bound by alloy but need {c.Need} <= supply {c.Supply}");
                    Assert.True(c.Ratio < Fx6.One);
                }
            }
        }
        Assert.True(bound > 0, "no line was bound by alloy in the walks: the test does not exercise the case");
    }

    [Theory]
    [InlineData(40, 100, 0.4)]
    [InlineData(100, 100, 1)]
    [InlineData(150, 100, 1)]
    [InlineData(0, 0, 1)]
    [InlineData(5, 0, 1)]
    public void Ratio_IsSupplyOverNeedCappedAtOne_AndOneWithoutNeed(double supply, double need, double ratio) =>
        Assert.Equal(Fx6.FromDouble(ratio), new AlloyCoverage(Fx6.FromDouble(supply), Fx6.FromDouble(need), Fx6.Zero).Ratio);
}
