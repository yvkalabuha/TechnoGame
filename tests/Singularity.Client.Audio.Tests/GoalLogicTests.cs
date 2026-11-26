using Singularity.Client.Spike;

namespace Singularity.Client.Audio.Tests;

public class GoalLogicTests
{
    [Theory]
    [InlineData(0, "goal.stage.0", "○")]
    [InlineData(1, "goal.stage.1", "◐")]
    [InlineData(2, "goal.stage.2", "●")]
    [InlineData(3, "goal.stage.3", "◆")]
    public void EachStep_HasAWordKeyAndADistinctMark(int stage, string key, string mark)
    {
        Assert.Equal(key, GoalLogic.StageKey(stage));
        Assert.Equal(mark, GoalLogic.StageMark(stage));
    }

    [Fact]
    public void RatioText_ShowsExact_Range_Unknown_AndCapsHugeValues()
    {
        Assert.Equal("1,25", GoalLogic.RatioText(1.254, false, true, 0, 0, ","));
        Assert.Equal("1.10–1.60", GoalLogic.RatioText(1.3, true, true, 1.1, 1.6, "."));
        Assert.Equal("?", GoalLogic.RatioText(1.3, true, false, 0, 0, "."));
        Assert.Equal("99+", GoalLogic.RatioText(100, false, true, 0, 0, "."));
    }

    [Fact]
    public void NextLine_And_HoldShare()
    {
        Assert.Equal(new[] { 1.0, 1.5, 2.0, 0 }, new[] { 0, 1, 2, 3 }.Select(GoalLogic.NextLine));
        Assert.Equal(0.5, GoalLogic.HoldShare(30, 60));
        Assert.Equal(1.0, GoalLogic.HoldShare(90, 60));
        Assert.Equal((2, 3), GoalLogic.Split(27));
    }

    [Fact]
    public void ChangeKey_AnnouncesAChangeForThreeMonths_UpOrDown()
    {
        Assert.Equal("goal.reached", GoalLogic.ChangeKey(0, 1, 10, 11));
        Assert.Equal("goal.lost", GoalLogic.ChangeKey(2, 1, 10, 12));
        Assert.Null(GoalLogic.ChangeKey(0, 1, 10, 13));
        Assert.Null(GoalLogic.ChangeKey(0, 0, 10, 10));
        Assert.Null(GoalLogic.ChangeKey(0, 1, -1, 0));
    }

    [Fact]
    public void EndKey_CoversEveryResult()
    {
        Assert.Equal("goal.end.0", GoalLogic.EndKey(0));
        Assert.Equal("goal.end.3", GoalLogic.EndKey(7));
    }
}
