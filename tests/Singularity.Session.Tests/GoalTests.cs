using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>The air power goal (#565): steps, hold and the view of it.</summary>
public class GoalTests
{
    private static Fx6 R(double v) => Fx6.FromRaw((long)(v * Fx6.Scale));

    [Theory]
    [InlineData(0.99, GoalStage.None)]
    [InlineData(1.0, GoalStage.Parity)]
    [InlineData(1.49, GoalStage.Parity)]
    [InlineData(1.5, GoalStage.Superiority)]
    [InlineData(1.99, GoalStage.Superiority)]
    [InlineData(2.0, GoalStage.Dominance)]
    [InlineData(100, GoalStage.Dominance)]
    public void StageOf_UsesTheLinesOfTheDecision(double ratio, GoalStage expected) =>
        Assert.Equal(expected, GoalMath.StageOf(R(ratio), true));

    [Fact]
    public void AnActorWithNoAirPower_IsAtNoStep_EvenAtTheEmptyRatioOfOne() =>
        Assert.Equal(GoalStage.None, GoalMath.StageOf(Fx6.One, false));

    private sealed class Track
    {
        public long S1, S2, S3;
        public void Month(long m, double ratio)
        {
            GoalStage now = GoalMath.StageOf(R(ratio), true);
            S1 = GoalMath.Advance(S1, 1, now, m);
            S2 = GoalMath.Advance(S2, 2, now, m);
            S3 = GoalMath.Advance(S3, 3, now, m);
        }
    }

    [Fact]
    public void ParityHeldFiveYears_IsAchieved_AndTheHoldRestartsWhenTheRatioDipsBelowTheLine()
    {
        var t = new Track();
        for (long m = 0; m < 59; m++) t.Month(m, 1.2);
        Assert.Equal(GoalStage.None, GoalMath.Achieved(t.S1, t.S2, t.S3, 59));   // 59 months of hold is not yet five years
        Assert.Equal(GoalStage.Parity, GoalMath.Achieved(t.S1, t.S2, t.S3, 60));
        t.Month(59, 0.9);                                                         // a dip below the line clears the hold
        Assert.Equal(GoalStage.None, GoalMath.Standing(t.S1, t.S2, t.S3));
        t.Month(60, 1.2);
        Assert.Equal(GoalStage.None, GoalMath.Achieved(t.S1, t.S2, t.S3, 61));
        Assert.Equal(1, GoalMath.Held(t.S1, 61));
    }

    [Fact]
    public void TheSteps_AreHeldIndependently_ADipToParityKeepsTheParityHold()
    {
        var t = new Track();
        for (long m = 0; m < 30; m++) t.Month(m, 2.5);
        for (long m = 30; m < 60; m++) t.Month(m, 1.2);   // dominance and superiority lost, parity kept
        Assert.Equal(GoalStage.Parity, GoalMath.Standing(t.S1, t.S2, t.S3));
        Assert.Equal(60, GoalMath.Held(t.S1, 60));
        Assert.Equal(GoalStage.Parity, GoalMath.Achieved(t.S1, t.S2, t.S3, 60));
    }

    [Fact]
    public void TheView_CarriesTheGoalOfEveryActor_ConsistentWithTheRatio_AndTheColumnsAreInTheState()
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 6, Threads = 1 });
        Assert.Contains(s.World.Table.Columns, c => c.Name == "goal.since1");
        for (long t = 0; t < 36; t++)
        {
            s.StepThroughMonth(t);
            foreach (ActorView av in s.View.Actors)
            {
                GoalView g = av.Slots!.Goal!;
                Assert.Equal(av.Slots.MpiRatio, g.Ratio);
                Assert.Equal(GoalMath.StageOf(g.Ratio, av.Slots.Mpi > Fx6.Zero), g.Stage);
                Assert.True(g.HeldMonths >= 0 && g.HeldMonths <= t + 1);
            }
        }
    }
}
