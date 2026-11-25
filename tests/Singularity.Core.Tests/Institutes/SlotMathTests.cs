using Singularity.Core.Institutes;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Institutes;

/// <summary>Formulas of the slot science model (SCIENCE_SLOTS §1.2, §2a, §5; pilot TASK-0185).</summary>
public class SlotMathTests
{
    private static readonly SlotParams P = new();
    private static Fx6 F(double v) => Fx6.FromRaw((long)(v * 1_000_000));

    [Theory]
    [InlineData(3, 1, 27)]    // ENG of generation 1: 6·3·1.5
    [InlineData(1, 4, 18)]    // APP of generation 4: 6·1·3
    [InlineData(3, 4, 54)]    // ENG of generation 4
    public void NodeMonths_FollowThePilotFormula(int costType, int gen, int months) =>
        Assert.Equal(Fx6.FromInt(months), SlotMath.NodeMonths(P, Fx6.FromInt(costType), gen));

    [Fact]
    public void Speed_IsTheProductOfInstituteAndFocus_NeverAboveTheCap_S7()
    {
        Assert.Equal(F(1.2 * 1.25), SlotMath.Speed(P, F(1.2), 1));
        Assert.Equal(P.SpeedCap, SlotMath.Speed(P, F(1.9), 2));   // 1.9·1.5 = 2.85, cut to 2.5 (#568)
        Assert.Equal(Fx6.One, SlotMath.Speed(P, Fx6.One, 0));
    }

    [Fact]
    public void FocusRaisesTheAsk_ByTheSpeedAndItsSurcharge_O089a()
    {
        Fx6 c = Fx6.FromInt(10), teff = Fx6.FromInt(20);
        Fx6 plain = SlotMath.Rate(P, c, SlotMath.Speed(P, Fx6.One, 0), 0, teff);
        Fx6 two = SlotMath.Rate(P, c, SlotMath.Speed(P, Fx6.One, 2), 2, teff);
        Assert.Equal(F(0.5), plain);
        Assert.InRange(two.Raw, F(0.5 * 1.5 * 1.275425).Raw - 5, F(0.5 * 1.5 * 1.275425).Raw + 5);   // s^1.6
    }

    [Fact]
    public void Overlap_IsTheWeightedMain_NoOverlapIsZero_S10()
    {
        Assert.Equal(Fx6.Zero, SlotMath.OverlapLevel([0, 0, 0], P.MainWeight));
        Assert.Equal(Fx6.FromInt(3), SlotMath.OverlapLevel([3], P.MainWeight));
        Assert.Equal(F(0.5 * 4 + 0.5 * (2 + 0) / 2.0), SlotMath.OverlapLevel([4, 2, 0], P.MainWeight));
        Assert.Equal(F(0.5 * 0 + 0.5 * 4), SlotMath.OverlapLevel([0, 4], P.MainWeight));
    }

    [Fact]
    public void DirectionWeights_SumToOne()
    {
        foreach (int n in new[] { 1, 2, 3, 4 })
        {
            Fx6 sum = Fx6.Zero;
            for (int i = 0; i < n; i++)
                sum += SlotMath.DirectionWeight(i, n, P.MainWeight);
            Assert.InRange(Math.Abs(sum.Raw - Fx6.One.Raw), 0, n);
        }
    }

    [Fact]
    public void DirectionLevels_ReachFive_WhileTheAreaLevelsOfTheSharesModelStayAtFour()
    {
        var p = new InstituteParams();
        Assert.True(InstituteMath.Threshold(5, 40, p) > InstituteMath.Threshold(4, 40, p));
        Assert.Equal(5, InstituteMath.DirectionLevelOf(1000, 40, p));
        Assert.Equal(4, InstituteMath.DirectionLevelOf(InstituteMath.Threshold(5, 40, p) - 1, 40, p));
        Assert.Equal(4, InstituteMath.LevelOf(1000, 40, p));   // the old rule is untouched (S11)
    }

    [Fact]
    public void Speed_WithResearchBonus_IsStillCut_AtTheCap()
    {
        Assert.Equal(F(1.2 * 1.25), SlotMath.Speed(P, F(1.2), 1, Fx6.One));
        Assert.Equal(F(1.0 * 1.0 * 1.25), SlotMath.Speed(P, Fx6.One, 0, F(1.25)));
        Assert.Equal(F(1.25 * 1.5 * 1.25), SlotMath.Speed(P, F(1.25), 2, F(1.25)));   // the largest the data gives (level 5, focus 2, R cap) stays under 2.5
        Assert.Equal(P.SpeedCap, SlotMath.Speed(P, F(1.5), 2, F(1.25)));          // a value of the data above that is cut (#568)
    }

    [Fact]
    public void Coverage_IsOneWhenTheMoneySuffices_AndTheSameShareForEverySlot()
    {
        Assert.Equal(Fx6.One, SlotMath.Coverage(Fx6.FromInt(10), Fx6.FromInt(8)));
        Assert.Equal(F(0.5), SlotMath.Coverage(Fx6.FromInt(4), Fx6.FromInt(8)));
        Assert.Equal(Fx6.One, SlotMath.Coverage(Fx6.Zero, Fx6.Zero));
    }

    [Fact]
    public void DiffusionAndTheft_ShortenTheTime_ButNotBelowOneMonth_O089e()
    {
        Assert.Equal(Fx6.FromInt(9), SlotMath.EffectiveMonths(Fx6.FromInt(18), F(0.5)));
        Assert.Equal(Fx6.One, SlotMath.EffectiveMonths(Fx6.FromInt(1), F(0.5)));
    }
}
