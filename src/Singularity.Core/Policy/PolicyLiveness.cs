using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Core.Policy;

/// <summary>A manifest that fails a liveness check of the rule A40 (spec §3.5 A40): the code names the check.</summary>
public sealed class LivenessException(string code, string message) : ArgumentException($"{code}: {message}")
{
    public string Code { get; } = code;
}

/// <summary>
/// The liveness checks of the rule A40, run when a manifest or a world is assembled, before any run (spec §3.5 A40 «Проверка живости»):
/// the launch bound <c>t_launch_max &lt; T_ab</c> for stage О, and L-C1…L-C3 for stage С. They exclude every cause of a series without
/// output that is not culture, so that each firing of the stage-С branch in main has a cultural cause. Pure functions of the parameters.
/// </summary>
public static class PolicyLiveness
{
    /// <summary>
    /// The month of the AI's launch counted from entry into stage О, tests only: the first <c>m</c> with <c>m ≥ t_test_min</c> and
    /// <c>D_hidden(m) ≤ k·D_ref</c>, <c>D_hidden(m) = D0 − detected(m·test_rate)</c> (contract §4.4b, spec A35 «эталон»). −1 if there is none within the cap.
    /// </summary>
    public static int LaunchMonth(ProductsParams products, Fx6 d0, Fx6 dRef, Fx6 k, int cap = 1000)
    {
        Fx6 hours = Fx6.Zero;
        for (int m = 1; m <= cap; m++)
        {
            hours += products.TestRate;
            Fx6 hidden = d0 - ProductsMath.DetectedTotal(hours, d0, products.LambdaD, products.BetaQ10);
            if (ProductsMath.GateOpen(hidden, k, dRef, m, products.TTestMin) && hidden <= k * dRef)
                return m;
        }
        return -1;
    }

    /// <summary>
    /// <c>t_launch_max</c>: the launch month of the doctrine with the smallest <c>k_P</c> at <c>D0 = d0·c_max</c> (novelty 1, bureau experience 0);
    /// −1 if the doctrine never launches.
    /// </summary>
    public static int LaunchMonthMax(ProductsParams products, PolicyParams policy, ProductsLayout layout)
    {
        Fx6 dRef = ProductsMath.DRef(products.D0Factor, layout.CRef);
        Fx6 d0 = products.D0Factor * Fx6.FromInt(ProductsSystem.MaxComplexity(layout));
        Fx6 k = policy.LaunchShare[0];
        foreach (Fx6 x in policy.LaunchShare)
        {
            if (x < k)
                k = x;
        }
        return LaunchMonth(products, d0, dRef, k);
    }

    /// <summary>
    /// Throws <see cref="LivenessException"/> for the first failed check: <c>LAUNCH</c> (<c>t_launch_max ≥ T_ab</c> or never), <c>L-C1</c> (an alloy
    /// disruption can last <c>T_ab</c> months or leaves nothing: <c>1 − deficit = 0</c>), <c>L-C2</c> (a procurement share or a start budget is zero),
    /// <c>L-C3</c> (the largest one-off fall of the procurement item is not covered by the budget growth over <c>T_ab</c> months, or a budget falls).
    /// </summary>
    /// <param name="budgetAtStart"><c>budget_a(t0)</c> by actor.</param>
    /// <param name="growthPerYear"><c>g_a</c> by actor.</param>
    public static void Validate(ProductsParams products, PolicyParams policy, ProductsLayout layout, ReadOnlySpan<Fx6> budgetAtStart,
        ReadOnlySpan<Fx6> growthPerYear)
    {
        int launch = LaunchMonthMax(products, policy, layout);
        if (launch < 0 || launch >= policy.TAb)
            throw new LivenessException("LAUNCH", $"t_launch_max = {launch} must be below T_ab = {policy.TAb}: the stage-О branch of A40 would become part of main.");
        if (policy.DisruptionDeficit >= Fx6.One || policy.DisruptionMaxMonths >= policy.TAb)
            throw new LivenessException("L-C1", $"an alloy disruption (deficit {policy.DisruptionDeficit}, up to {policy.DisruptionMaxMonths} months) can idle a line for T_ab = {policy.TAb} months.");
        foreach (BudgetShares s in policy.SharesByProfile.Append(policy.MassProgrammeShares))
        {
            if (s.Procurement <= Fx6.Zero)
                throw new LivenessException("L-C2", "a profile has a zero procurement share.");
        }
        foreach (Fx6 b in budgetAtStart)
        {
            if (b <= Fx6.Zero)
                throw new LivenessException("L-C2", "an actor has a zero start budget.");
        }
        // The largest fall of the item: a programme moves the share from `before` to `after`; the budget must grow by before/after within T_ab.
        Fx6 before = Fx6.Zero, after = Fx6.One;
        BudgetShares[] outside = [.. policy.SharesByProfile];
        for (int i = 0; i < outside.Length; i++)
        {
            BudgetShares inside = (PolicyProfile)i == PolicyProfile.Mass ? policy.MassProgrammeShares : outside[i];
            if (inside.Procurement < outside[i].Procurement && outside[i].Procurement * after > before * inside.Procurement)
            {
                before = outside[i].Procurement;
                after = inside.Procurement;
            }
        }
        for (int a = 0; a < growthPerYear.Length; a++)
        {
            if (growthPerYear[a] < Fx6.Zero)
                throw new LivenessException("L-C3", $"actor {a} has a falling budget (g = {growthPerYear[a]}).");
            Fx6 factor = Fx6.One;
            for (int m = 0; m < policy.TAb; m++)
                factor = BudgetMath.Next(factor, growthPerYear[a]);
            if (before > Fx6.Zero && factor * after < before)
                throw new LivenessException("L-C3", $"actor {a}: growth over T_ab months ({factor}) does not cover the fall {before}→{after} of the procurement item.");
        }
    }

    /// <summary>
    /// L-C3a (spec A40, v0.15, E1): the premise of L-C3 for the start line, <c>need(0) &lt; B_proc(0)</c> for every actor and profile, where
    /// <c>need(0) = Σ upkeep·count</c> of the start fleet and <c>B_proc(0) = share_P·budget_a(t0)</c>. Throws <see cref="LivenessException"/> <c>L-C3a</c>.
    /// </summary>
    /// <param name="startUpkeep"><c>need(0)</c> by actor (0 for an actor without a start fleet).</param>
    /// <param name="budgetAtStart"><c>budget_a(t0)</c> by actor.</param>
    public static void ValidateStartUpkeep(PolicyParams policy, ReadOnlySpan<Fx6> startUpkeep, ReadOnlySpan<Fx6> budgetAtStart)
    {
        Fx6 share = policy.MassProgrammeShares.Procurement;
        foreach (BudgetShares s in policy.SharesByProfile)
        {
            if (s.Procurement < share)
                share = s.Procurement;
        }
        for (int a = 0; a < startUpkeep.Length; a++)
        {
            if (startUpkeep[a] >= share * budgetAtStart[a])
                throw new LivenessException("L-C3a", $"actor {a}: the upkeep of the start fleet ({startUpkeep[a]}) is not below the procurement item at t0 ({share * budgetAtStart[a]}).");
        }
    }
}
