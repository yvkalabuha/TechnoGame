using Singularity.Kernel;

namespace Singularity.Session;

/// <summary>Step of the air power goal (#565): the ratio of own air power to the best rival crosses 1.0, 1.5, 2.0.</summary>
public enum GoalStage { None = 0, Parity = 1, Superiority = 2, Dominance = 3 }

/// <summary>
/// Pure rules of the air power goal (#565). Each step is "held" while the ratio stays at or above its line; the hold of a step restarts when the ratio falls below it.
/// A step counts as won after <see cref="HoldMonths"/> months of continuous hold. The "since" values are stored as month + 1 (0 = not at that step) so a zeroed column means "none".
/// </summary>
internal static class GoalMath
{
    public const int HoldMonths = 60;
    public static readonly Fx6 ParityLine = Fx6.One;
    public static readonly Fx6 SuperiorityLine = Fx6.FromRaw(Fx6.Scale * 3 / 2);
    public static readonly Fx6 DominanceLine = Fx6.FromInt(2);

    public static Fx6 Line(int step) => step switch { 1 => ParityLine, 2 => SuperiorityLine, _ => DominanceLine };

    /// <summary>The step the ratio stands at; an actor with no air power at all stands at none (the empty ratio 1 is not a parity).</summary>
    public static GoalStage StageOf(Fx6 ratio, bool hasPower)
    {
        if (!hasPower || ratio < ParityLine)
            return GoalStage.None;
        return ratio >= DominanceLine ? GoalStage.Dominance : ratio >= SuperiorityLine ? GoalStage.Superiority : GoalStage.Parity;
    }

    /// <summary>The since-value of one step after the month evaluated: kept while the ratio holds the line, set to the month when it first reaches it, cleared below it.</summary>
    public static long Advance(long sincePlusOne, int step, GoalStage now, long month) =>
        (int)now >= step ? (sincePlusOne > 0 ? sincePlusOne : month + 1) : 0;

    public static GoalStage Standing(long s1, long s2, long s3) => s3 > 0 ? GoalStage.Dominance : s2 > 0 ? GoalStage.Superiority : s1 > 0 ? GoalStage.Parity : GoalStage.None;

    /// <summary>Months the actor has been at (or above) the step at the month given; 0 when it is not there.</summary>
    public static int Held(long sincePlusOne, long month) => sincePlusOne > 0 ? (int)Math.Max(0, month - (sincePlusOne - 1)) : 0;

    /// <summary>The highest step held for the full <see cref="HoldMonths"/>.</summary>
    public static GoalStage Achieved(long s1, long s2, long s3, long month) =>
        Held(s3, month) >= HoldMonths ? GoalStage.Dominance : Held(s2, month) >= HoldMonths ? GoalStage.Superiority : Held(s1, month) >= HoldMonths ? GoalStage.Parity : GoalStage.None;
}
