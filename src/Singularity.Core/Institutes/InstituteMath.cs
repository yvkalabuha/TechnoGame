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
        Fx6 cap = (Fx6.One - p.Rho) * wsum / groupWeight;
        Fx6 k = cap < p.Kappa ? cap : p.Kappa;
        return k > Fx6.One ? k : Fx6.One;
    }

    /// <summary>Speed multiplier of the goal node from the level of its institute in the area: <c>1 + bonus·level</c>.</summary>
    public static Fx6 LevelMultiplier(int level, InstituteParams p) => Fx6.One + p.LevelBonus * Fx6.FromInt(level);

    /// <summary>
    /// Experience at which the area reaches <paramref name="level"/> (1..4): <c>ceil(q·N)</c> of the nodes of the area, forced to rise by at least 1 per level
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
            int raw = (p.ThresholdPercent[l - 1] * nodesInArea + 99) / 100;
            result = raw > previous ? raw : previous + 1;
            previous = result;
        }
        return result;
    }

    /// <summary>The highest level (0..4) whose threshold the experience reaches.</summary>
    public static int LevelOf(int experience, int nodesInArea, InstituteParams p)
    {
        int level = 0;
        while (level < 4 && experience >= Threshold(level + 1, nodesInArea, p))
            level++;
        return level;
    }
}
