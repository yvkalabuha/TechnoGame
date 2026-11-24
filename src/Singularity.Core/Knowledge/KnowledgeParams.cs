using Singularity.Core.Data;
using Singularity.Kernel;

namespace Singularity.Core.Knowledge;

/// <summary>
/// Knowledge-layer parameters of the jet slice (spec docs/simulation/slice-jet-aviation.md §8.1, all
/// per month). Defaults are the spec values; ranges marked "К" there are calibrated later.
/// </summary>
public sealed record KnowledgeParams
{
    private static Fx6 F(long raw) => Fx6.FromRaw(raw);

    /// <summary>Unit of <c>c_n</c> and <c>K_d</c>: 0.1 budget index per month (§3.5 A9).</summary>
    public Fx6 SRef { get; init; } = F(100_000);
    public Fx6 KRef { get; init; } = Fx6.One;
    public Fx6 KK { get; init; } = Fx6.One;
    public Fx6 KDiff { get; init; } = F(500_000);
    public Fx6 MuK { get; init; } = F(10_000);
    public Fx6 KNode { get; init; } = F(50_000);
    public Fx6 ThetaStart { get; init; } = F(600_000);
    public Fx6 HMax { get; init; } = F(80_000);
    public Fx6 YHalf { get; init; } = F(500_000);
    public Fx6 ProgCap { get; init; } = Fx6.FromInt(3);
    public Fx6 Eta { get; init; } = F(10_000);

    /// <summary>
    /// <c>V_ref</c> (contract §4.3). 1000 h is the reference fixture value, not a spec value: the real number
    /// comes from rule §3.5 A18 (median monthly labour of a mature-line process) once T2/T5 data exist.
    /// </summary>
    public Fx6 VRef { get; init; } = Fx6.FromInt(1000);
    public Fx6 ThetaInd { get; init; } = F(250_000);
    public Fx6 DeltaInd { get; init; } = F(3_000);

    /// <summary>Base <c>c_n</c> by <see cref="NodeType"/> (FND, PRN, ENG, CMP, PRC, APP), in S_ref·years.</summary>
    public Fx6[] CostByType { get; init; } = [Fx6.One, F(1_500_000), Fx6.FromInt(3), F(1_500_000), Fx6.FromInt(2), Fx6.One];

    /// <summary>
    /// Generation multiplier of <c>c_n</c>, index = gen: G0…G4 = 1.0/1.0/1.6/2.5/4.0 (§8.1, §3.5 A19). G5 and G6 are the first proposal of
    /// TASK-0128 (6.4 and 10.2, the G2→G4 step ×1.6 continued) until its calibration sets them; G7 and later have none and throw.
    /// </summary>
    public Fx6[] CostGenMultiplier { get; init; } = [Fx6.One, Fx6.One, F(1_600_000), F(2_500_000), Fx6.FromInt(4), F(6_400_000), F(10_200_000)];

    /// <summary>
    /// Ahead-of-time cost (owner decision #458): a node costs <c>1 + k·ahead^pow</c> times more (capped by <see cref="AheadCostMax"/>), where
    /// <c>ahead</c> is the number of years from the month of the step to the node's anchor year (0 once the anchor has passed). Defaults k 0.02, pow 2, max 10 (owner decision #465); k = 0 is off: the cost
    /// is exactly the pre-#458 one.
    /// </summary>
    public Fx6 AheadCostK { get; init; } = F(20_000);

    /// <summary>Integer exponent of the ahead-of-time cost, 1…4.</summary>
    public int AheadCostPow { get; init; } = 2;

    /// <summary>Ceiling of the ahead-of-time multiplier, at least 1.</summary>
    public Fx6 AheadCostMax { get; init; } = Fx6.FromInt(10);

    public void Validate()
    {
        if (AheadCostK < Fx6.Zero)
            throw new ArgumentException("The ahead-of-time cost k must not be negative.");
        if (AheadCostPow is < 1 or > 4)
            throw new ArgumentException("The ahead-of-time cost exponent must be in 1…4.");
        if (AheadCostMax < Fx6.One)
            throw new ArgumentException("The ahead-of-time cost ceiling must be at least 1.");
    }

    /// <summary><c>c_n = base(type)·mult(gen)</c>.</summary>
    public Fx6 CostN(NodeType type, int gen)
    {
        if (gen < 0 || gen >= CostGenMultiplier.Length)
            throw new ArgumentOutOfRangeException(nameof(gen), gen, "No c_n multiplier for this generation.");
        return CostByType[(int)type] * CostGenMultiplier[gen];
    }
}
