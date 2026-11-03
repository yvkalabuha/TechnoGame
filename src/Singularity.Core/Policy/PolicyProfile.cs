using System.Collections.Immutable;
using Singularity.Core.Data;
using Singularity.Kernel;

namespace Singularity.Core.Policy;

/// <summary>AI doctrine of an actor for the whole run (slice spec §4): P1, P2, P3.</summary>
public enum PolicyProfile
{
    /// <summary>P1 «качество».</summary>
    Quality = 0,

    /// <summary>P2 «массовость».</summary>
    Mass = 1,

    /// <summary>P3 «адаптивный».</summary>
    Adaptive = 2,
}

/// <summary>Budget split of an actor: research, capability, process development (D), procurement, quality programme (spec §3.4).</summary>
public readonly record struct BudgetShares(Fx6 Research, Fx6 Capability, Fx6 Effort, Fx6 Procurement, Fx6 Quality)
{
    public Fx6 Sum => Research + Capability + Effort + Procurement + Quality;

    /// <summary>Every share is non-negative and the sum is exactly 1 in <see cref="Fx6"/> (the <c>set_budget_shares</c> validator).</summary>
    public bool IsValid => Research >= Fx6.Zero && Capability >= Fx6.Zero && Effort >= Fx6.Zero
        && Procurement >= Fx6.Zero && Quality >= Fx6.Zero && Sum == Fx6.One;
}

/// <summary>
/// Policy parameters of the slice (spec §4 table, v0.11 shares): the budget shares of each profile. All "З" values, never
/// calibrated; changing them is a new experiment.
/// </summary>
public sealed record PolicyParams
{
    private static Fx6 F(long raw) => Fx6.FromRaw(raw);

    /// <summary>Shares by profile (index = <see cref="PolicyProfile"/>): R / capability / D / procurement / QP.</summary>
    public ImmutableArray<BudgetShares> SharesByProfile { get; init; } =
    [
        new(F(350_000), F(120_000), F(80_000), F(400_000), F(50_000)),
        new(F(200_000), F(180_000), F(120_000), F(500_000), F(0)),
        new(F(280_000), F(150_000), F(100_000), F(420_000), F(50_000)),
    ];

    /// <summary><c>E_ref</c>: engineers of the aviation sector of a budget-index-1 actor at t0 (spec §1.1, [Предп.], "З").</summary>
    public long EngineerRef { get; init; } = 20_000;

    /// <summary>Skilled workers per engineer in the base pool (spec §1.1, [Предп.]).</summary>
    public int SkilledPerEngineer { get; init; } = 10;

    public BudgetShares Shares(PolicyProfile profile) => SharesByProfile[(int)profile];

    public void Validate()
    {
        if (EngineerRef < 0 || SkilledPerEngineer < 0)
            throw new ArgumentException("E_ref and the skilled-per-engineer ratio must not be negative.");
        if (SharesByProfile.Length != 3)
            throw new ArgumentException("One share set per profile (P1, P2, P3) is required.");
        for (int i = 0; i < SharesByProfile.Length; i++)
        {
            if (!SharesByProfile[i].IsValid)
                throw new ArgumentException($"Budget shares of profile {i} must be non-negative and sum to exactly 1.");
        }
    }
}

/// <summary>Pure rules of the AI policy: functions of the profile and of data, no state, no RNG.</summary>
public static class PolicyRules
{
    /// <summary>
    /// <c>prio(profile, node)</c> (spec §4, §3.5 A41): factor 2 when the node fits the doctrine, else 1; a ★ engine is ×2, not ×4.
    /// P1: ★ (key hazard) or ENG; P2: PRC; P3: all ×1.
    /// </summary>
    public static Fx6 Priority(PolicyProfile profile, NodeType type, Hazard hazard)
    {
        bool boosted = profile switch
        {
            PolicyProfile.Quality => hazard == Hazard.Key || type == NodeType.ENG,
            PolicyProfile.Mass => type == NodeType.PRC,
            _ => false,
        };
        return boosted ? Fx6.FromInt(2) : Fx6.One;
    }

    /// <summary>
    /// Profiles of the great powers under a factorial cell (spec §6.6): cell = <c>9u + 3g + s</c>, u, g, s ∈ {0, 1, 2} the profiles
    /// of USA, GBR, SUN (scenario order, §3.5 A7).
    /// </summary>
    public static int CellOf(PolicyProfile usa, PolicyProfile gbr, PolicyProfile sun) => 9 * (int)usa + 3 * (int)gbr + (int)sun;

    public static (PolicyProfile Usa, PolicyProfile Gbr, PolicyProfile Sun) ProfilesOfCell(int cell)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cell);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(cell, 26);
        return ((PolicyProfile)(cell / 9), (PolicyProfile)(cell / 3 % 3), (PolicyProfile)(cell % 3));
    }
}

/// <summary>Budget arithmetic of spec §3.4 and the scaling rules that follow the budget (§1.1).</summary>
public static class BudgetMath
{
    /// <summary>
    /// <c>budget(t+1) = ⌊budget(t)·(1 + g_m)⌋</c>, <c>g_m = ⌊g/12⌋</c> once per actor (spec §3.5 A12): the rounding of
    /// <c>g/12</c> comes first, then one floored product.
    /// </summary>
    public static Fx6 Next(Fx6 budget, Fx6 growthPerYear)
    {
        long q = growthPerYear.Raw / 12;
        if (growthPerYear.Raw % 12 < 0)
            q--;
        return budget * (Fx6.One + Fx6.FromRaw(q));
    }

    /// <summary><c>⌊value·budget(t)/budget(t0)⌋</c> in one 128-bit step: a pool that follows the budget (workforce base, alloy capacity; §1.1, §1.4).</summary>
    public static long FollowBudget(long valueAtStart, Fx6 budgetNow, Fx6 budgetAtStart)
    {
        if (budgetAtStart <= Fx6.Zero)
            throw new ArgumentOutOfRangeException(nameof(budgetAtStart), budgetAtStart, "The start budget must be positive.");
        return (long)((Int128)valueAtStart * budgetNow.Raw / budgetAtStart.Raw);
    }
}
