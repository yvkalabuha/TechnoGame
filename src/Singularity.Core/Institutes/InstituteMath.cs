using Singularity.Kernel;

namespace Singularity.Core.Institutes;

/// <summary>Pure formulas of the priorities (research-institutes-priorities rev 4 §3, §5).</summary>
public static class InstituteMath
{
    /// <summary>
    /// The common money multiplier of the priority groups: <c>max(1, min(kappa, (1 − rho)·wsum / W))</c>. At least 1, so a node of a group is never slower than
    /// without the priority (P-8); the others keep at least rho of the money while it is above 1.
    /// </summary>
    public static Fx6 KappaEff(Fx6 wsum, Fx6 groupWeight, InstituteParams p)
    {
        if (groupWeight <= Fx6.Zero || wsum <= Fx6.Zero)
            return Fx6.One;
        Fx6 share = (Fx6.One - p.Rho) * wsum;
        // The quotient can leave the Fx6 range with a tiny group weight; above kappa the cap does not bind, so decide in Int128 first.
        Int128 quotient = (Int128)share.Raw * Fx6.Scale / groupWeight.Raw;
        Fx6 k = quotient >= p.Kappa.Raw ? p.Kappa : share / groupWeight;
        return k > Fx6.One ? k : Fx6.One;
    }

    /// <summary>Speed multiplier of the goal node from the level of its institute in the area: <c>1 + bonus·level</c>.</summary>
    public static Fx6 LevelMultiplier(int level, InstituteParams p) => Fx6.One + p.LevelBonus * Fx6.FromInt(level);

    /// <summary>
    /// Experience at which the area reaches <paramref name="level"/> (1..4; 5 only for the directions of the slot model): <c>ceil(q·N)</c> of the nodes of the area, forced to rise by at least 1 per level
    /// so that a small area (N = 6) still has four distinct levels (the rule of rev 4 gives 1/2/3/3 there; the start experience would read as a higher level).
    /// </summary>
    public static int Threshold(int level, int nodesInArea, InstituteParams p)
    {
        if (level <= 0)
            return 0;
        int previous = 0;
        int result = 0;
        for (int l = 1; l <= level; l++)
        {
            int raw = ((l <= 4 ? p.ThresholdPercent[l - 1] : p.Level5Percent) * nodesInArea + 99) / 100;
            result = raw > previous ? raw : previous + 1;
            previous = result;
        }
        return result;
    }

    /// <summary>Months a project of <paramref name="levels"/> takes: <c>12·(base + per_level·Σ)</c> rounded up.</summary>
    public static int BuildMonths(int levels, InstituteParams p)
    {
        long raw = ((p.BuildYearsBase + p.BuildYearsPerLevel * Fx6.FromInt(levels)) * Fx6.FromInt(12)).Raw;
        return checked((int)((raw + Fx6.Scale - 1) / Fx6.Scale));
    }

    /// <summary>Price multiplier of opening <paramref name="yearsAhead"/> before the historical year: <c>1 + k·y²</c>, y in [0, window].</summary>
    public static Fx6 AheadMultiplier(int yearsAhead, InstituteParams p)
    {
        int y = yearsAhead < 0 ? 0 : yearsAhead > p.WindowYears ? p.WindowYears : yearsAhead;
        return Fx6.One + p.AheadK * Fx6.FromInt(y * y);
    }

    /// <summary>Share of the research money a running project takes per month of its run (a flow share, not divided by the calendar).</summary>
    public static Fx6 BuildRate(int levels, int yearsAhead, InstituteParams p) =>
        p.BuildRatePerLevel * Fx6.FromInt(levels) * AheadMultiplier(yearsAhead, p);

    /// <summary>
    /// Safety rule: the speed of a project is multiplied by <c>min(1, 1/take)</c>, where take is the sum of the shares the projects and the upkeep take. The data never
    /// reaches 100 % (TASK-0173: at most 13.4 %), but without the rule a larger list of candidates would run projects at full speed on money that is not there.
    /// </summary>
    public static Fx6 OverloadScale(Fx6 take) => take > Fx6.One ? Fx6.One / take : Fx6.One;

    /// <summary>The highest level (0..5) whose threshold the experience reaches: the levels of the directions of the slot model (data levels 1..5).</summary>
    public static int DirectionLevelOf(int experience, int nodesInDirection, InstituteParams p)
    {
        int level = 0;
        while (level < MaxDirectionLevel && experience >= Threshold(level + 1, nodesInDirection, p))
            level++;
        return level;
    }

    /// <summary>Highest level of a direction (#571 data: 1..5).</summary>
    public const int MaxDirectionLevel = 5;

    /// <summary>The highest level (0..4) whose threshold the experience reaches.</summary>
    public static int LevelOf(int experience, int nodesInArea, InstituteParams p)
    {
        int level = 0;
        while (level < 4 && experience >= Threshold(level + 1, nodesInArea, p))
            level++;
        return level;
    }
}
