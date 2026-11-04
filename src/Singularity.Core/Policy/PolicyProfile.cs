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

/// <summary>Ablation arms of the fleet rule (spec §6.10); a manifest switch, the mod data are untouched.</summary>
public enum FleetArm
{
    /// <summary>Main: the AI retires and sells by A42.4.</summary>
    Main = 0,
    /// <summary><c>NO_RETIRE</c>: the AI has no <c>retire_cohort</c> and <c>sell_cohort</c> (the behaviour of T6c2).</summary>
    NoRetire = 1,
    /// <summary><c>NO_SALE</c>: only <c>retire_cohort</c> (<c>s ≡ 0</c>).</summary>
    NoSale = 2,
    /// <summary><c>PURCHASE_CAP</c>: no commands; the line rate is capped so that the upkeep stays at <c>U_LO</c>, the rest of the money is lost (#118).</summary>
    PurchaseCap = 3,
}

/// <summary><c>FLEET_LINE_COND</c> (spec A42.4 п. 4): the condition on the line that makes a project a candidate.</summary>
public enum FleetLineCond
{
    /// <summary><c>gen(L) &gt; gen(d)</c> strictly (#147).</summary>
    Gen = 0,
    /// <summary>Spec v0.16: <c>perf_L·upkeep_d &gt; perf_d·upkeep_L</c> strictly. Diagnostic.</summary>
    Kappa = 1,
    /// <summary>No condition on the line. Diagnostic.</summary>
    None = 2,
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

    /// <summary>Shares of P2 during a quality programme (spec §3.5 A41, F13 closed): the 0,05 comes out of procurement.</summary>
    public BudgetShares MassProgrammeShares { get; init; } = new(F(200_000), F(180_000), F(120_000), F(450_000), F(50_000));

    /// <summary><c>m_keep</c> (spec §3.5 A30): the margin above the admission threshold the tooling plan keeps on the MECH process <c>p*</c>.</summary>
    public Fx6 MKeep { get; init; } = F(750_000);

    /// <summary>Length of a quality programme, months (spec A41).</summary>
    public int QpMonths { get; init; } = 84;

    /// <summary><c>qp_engineers = ⌊qp_share_eng·engineer_base_a⌋</c>: engineers a programme asks for (spec A41, [Предп.] 2%).</summary>
    public Fx6 QpEngineerShare { get; init; } = F(20_000);

    /// <summary>P2 starts a programme after this many consecutive months with a series below <see cref="QpQuality"/> (spec A41).</summary>
    public int QpTriggerMonths { get; init; } = 24;

    /// <summary>The quality <c>q</c> below which a series counts for the P2 trigger (spec A41).</summary>
    public Fx6 QpQuality { get; init; } = F(700_000);

    /// <summary><c>T_ab</c>: months in stage О without a series, or in С without output, after which the AI gives a project up (spec A40); 60, «З», not calibrated.</summary>
    public int TAb { get; init; } = 60;

    /// <summary>
    /// <c>k_P</c> by profile (index = <see cref="PolicyProfile"/>): the series starts when <c>gate_open ∧ D_hidden ≤ k_P·D_ref</c> (spec §4, A35):
    /// P1 0,40, P2 0,70 (= <c>k_gate</c>), P3 0,55.
    /// </summary>
    public ImmutableArray<Fx6> LaunchShare { get; init; } = [F(400_000), F(700_000), F(550_000)];

    /// <summary><c>y_prod</c> of the preset <c>producible</c> (spec §1.4): the value of <c>ProductsParams.YProd</c>, set by the assembly.</summary>
    public Fx6 YProd { get; init; } = F(500_000);

    /// <summary><c>n_service_min</c> (spec §5.1): the fleet the alloy supply must be able to deliver in <see cref="TField"/> months (preset <c>producible</c>); 50.</summary>
    public int NServiceMin { get; init; } = 50;

    /// <summary><c>T_field</c> = <c>H_build</c>, months (COMPONENT_SELECTION §6); 24, set by the assembly from <c>ProductsParams.HBuild</c>.</summary>
    public int TField { get; init; } = 24;

    /// <summary>
    /// <c>fv_budget</c> (spec §5.5): the budget of the hypothetical fleet in <c>FV</c> of the preset <c>adaptive</c>; the assembly sets it to
    /// 24 median monthly procurement budgets of the great powers at t0 (<see cref="FvBudgetOf"/>). Must be positive when the product AI runs.
    /// </summary>
    public Fx6 FvBudget { get; init; }

    /// <summary>Arm of the fleet rule A42.4 (spec §6.10): <see cref="FleetArm.Main"/> retires and sells; the others are ablations.</summary>
    public FleetArm FleetArm { get; init; } = FleetArm.Main;

    /// <summary>
    /// <c>FLEET_LINE_COND</c> (spec A42.4 п. 4, #147): the condition on the line for a candidate; <see cref="FleetLineCond.Gen"/> in main, the others diagnostic.
    /// </summary>
    public FleetLineCond FleetLineCond { get; init; } = FleetLineCond.Gen;

    /// <summary><c>TOOLING_TARGET</c> (spec §11 T6e v0.19, #156): pass 2 of the tooling plan; V0 until the owner chooses.</summary>
    public ToolingTargetSpec ToolingTarget { get; init; } = new();

    /// <summary><c>U_HI(P)</c> by profile (index = <see cref="PolicyProfile"/>; P1 / P2 / P3): the upkeep share of the procurement base from which the fleet rule acts (spec A42.8 «З»).</summary>
    public ImmutableArray<Fx6> FleetUHi { get; init; } = [F(250_000), F(800_000), F(500_000)];

    /// <summary><c>U_HI − U_LO</c>: the rule frees upkeep down to <c>U_LO = U_HI − 0,15</c> (spec A42.8).</summary>
    public Fx6 FleetUGap { get; init; } = F(150_000);

    /// <summary><c>A_PROT</c>: cohorts younger than this many months are never taken by the rule (spec A42.8); 60.</summary>
    public int FleetAProt { get; init; } = 60;

    /// <summary><c>U_LO(P) = U_HI(P) − gap</c>.</summary>
    public Fx6 FleetULo(PolicyProfile profile) => FleetUHi[(int)profile] - FleetUGap;

    /// <summary>Deficit of an alloy disruption (spec §4: 0,5); read by the manifest check L-C1 (the event rule is T6d).</summary>
    public Fx6 DisruptionDeficit { get; init; } = F(500_000);

    /// <summary>Longest alloy disruption, months (spec §4: 6…18); read by the manifest check L-C1.</summary>
    public int DisruptionMaxMonths { get; init; } = 18;

    /// <summary>
    /// <c>fv_budget</c> = 24 · the median of the great powers' start budgets · the median procurement share of the three profiles
    /// ([Предп.]: the spec says «24 медианных месячных бюджета закупок великих держав на t0»; the profile of an actor is random, so the
    /// median share of the profiles stands for it).
    /// </summary>
    public Fx6 FvBudgetOf(ReadOnlySpan<Fx6> greatPowerBudgets)
    {
        if (greatPowerBudgets.IsEmpty)
            throw new ArgumentException("At least one great power is needed.", nameof(greatPowerBudgets));
        Fx6[] sorted = greatPowerBudgets.ToArray();
        Array.Sort(sorted, (x, y) => x.CompareTo(y));
        Fx6 median = sorted[sorted.Length / 2];
        Fx6[] shares = [.. SharesByProfile.Select(s => s.Procurement)];
        Array.Sort(shares, (x, y) => x.CompareTo(y));
        return Fx6.FromInt(24) * shares[shares.Length / 2] * median;
    }

    public BudgetShares Shares(PolicyProfile profile) => SharesByProfile[(int)profile];

    /// <summary>Shares of the profile, with the programme terms of P2 while a quality programme runs.</summary>
    public BudgetShares Shares(PolicyProfile profile, bool programmeActive) =>
        programmeActive && profile == PolicyProfile.Mass ? MassProgrammeShares : Shares(profile);

    /// <summary><c>engineer_base_a = ⌊E_ref·budget_index⌋</c> at t0 (spec §1.1).</summary>
    public long EngineerBase(Fx6 budgetAtStart) => (EngineerRef * budgetAtStart.Raw) / Fx6.Scale;

    public void Validate()
    {
        if (EngineerRef < 0 || SkilledPerEngineer < 0)
            throw new ArgumentException("E_ref and the skilled-per-engineer ratio must not be negative.");
        if (QpMonths < 1 || QpTriggerMonths < 1 || QpEngineerShare < Fx6.Zero || MKeep < Fx6.Zero || !MassProgrammeShares.IsValid)
            throw new ArgumentException("Quality programme parameters are out of range.");
        if (TAb < 1 || NServiceMin < 1 || TField < 1 || DisruptionMaxMonths < 1 || YProd < Fx6.Zero || FvBudget < Fx6.Zero
            || DisruptionDeficit < Fx6.Zero || DisruptionDeficit > Fx6.One || LaunchShare.Length != 3 || LaunchShare.Any(k => k < Fx6.Zero || k > Fx6.One))
            throw new ArgumentException("Product-AI parameters are out of range (T_ab, k_P, y_prod, n_service_min, T_field, disruption).");
        if (FleetUHi.Length != 3 || FleetUHi.Any(u => u <= FleetUGap || u > Fx6.One) || FleetUGap < Fx6.Zero || FleetAProt < 0)
            throw new ArgumentException("Fleet rule: three U_HI in (gap; 1], gap and A_PROT non-negative (spec A42.8).");
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

    /// <summary><c>r_AI = ⌊B_proc/unit_cost_floor⌋</c> units a month (spec A41): <paramref name="budget"/> is the procurement item (plus the training money for the training series).</summary>
    public static Fx6 LineRate(Fx6 budget, Fx6 unitCostFloor) => budget / unitCostFloor;

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
