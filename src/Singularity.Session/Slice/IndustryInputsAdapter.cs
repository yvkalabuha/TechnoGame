using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session;

/// <summary>
/// Inputs of the capability layer: pure functions of the snapshot. No RNG here (the layer has none, #28, and nothing may draw on its
/// behalf; an architecture test checks this class too). The budget items come from the shares in force and <c>pol.budget</c>; the
/// planned volumes from <see cref="Singularity.Core.Products.ProductsSystem"/>; pools of people and alloy capacity follow the budget
/// (<see cref="BudgetMath.FollowBudget"/>).
///
/// <c>ToolingInvest</c> is the cell of the actor's tooling plan (A39, A30); <c>QualityProgramme</c> is the programme of the policy with its
/// engineers granted (A41). <c>LicenceShare</c> and <c>AlloyDeficit</c> are the state of the events of T6d in the policy columns (spec A43); <c>EffortMultiplier</c> is <c>mult_P</c> (A37).
/// </summary>
internal sealed class IndustryInputsAdapter(SliceWorld world) : IIndustryInputs
{
    public bool KnowsProcess(int siteRow, int processRow, long month)
    {
        int row = world.Graph.RowOf(world.IndustryLayout.Number[processRow]);
        return row >= 0 && world.Knowledge.IsKnown(siteRow, row);
    }

    public Fx6 W(int siteRow, int cluster, long month) => world.Scenario.W[siteRow];

    public Fx6 Hcap(int siteRow, Dimension dim, long month) => world.Scenario.Hcap[siteRow];

    // Spec A41: the programme is active and the engineers it asked for in the previous month were granted in full.
    public bool QualityProgramme(int siteRow, long month)
    {
        int actor = world.IndustryLayout.SiteOwner[siteRow];
        return world.Policy.QualityProgrammeActive(actor)
            && world.Industry.Granted(siteRow, ProductsLayout.EngineerCategory, world.QualityRequester) >= world.Policy.QualityEngineers(actor);
    }

    // Spec A39: one plan per actor and month, every call of the month recomputes the same plan; the cell of this site is returned.
    public Fx6 ToolingInvest(int siteRow, int cluster, Dimension dim, long month, Fx6 restActor) =>
        Plan(world.IndustryLayout.SiteOwner[siteRow], month, restActor).Applied(siteRow, cluster, dim);

    public Fx6 ToolingInvest(int siteRow, int cluster, Dimension dim, long month, Fx6 restActor, out Fx6 unmet)
    {
        ToolingPlanResult plan = Plan(world.IndustryLayout.SiteOwner[siteRow], month, restActor);
        unmet = plan.Unmet(siteRow, cluster, dim);
        return plan.Applied(siteRow, cluster, dim);
    }

    /// <summary><c>ToolingPlan(a, t, rest_a)</c> with <c>B_cap = ⌊k_cap·share_cap·budget_a(t)⌋</c> of the shares in force (<c>k_cap</c> = 1 outside the Lab stress set).</summary>
    public ToolingPlanResult Plan(int actor, long month, Fx6 restActor)
    {
        ToolingTargetSpec target = world.PolicyParameters.ToolingTarget;
        Fx6 share = world.SharesOf(actor).Capability, budget = world.Policy.Budget(actor);
        // k_cap ≠ 1 only in the Lab stress set: one floor over the whole product (spec), so the hash of a default run is untouched.
        Fx6 bCap = target.KCap == Fx6.One
            ? share * budget
            : Fx6.FromRaw((long)((Int128)target.KCap.Raw * share.Raw * budget.Raw / ((Int128)Fx6.Scale * Fx6.Scale)));
        return ToolingPlan.Compute(world.Industry, this, world.PolicyParameters.MKeep, actor, month, restActor, bCap, target);
    }

    public Fx6 EffortBudget(int actorRow, long month) => world.SharesOf(actorRow).Effort * world.Policy.Budget(actorRow);

    public int EffortFocus(int actorRow, int k, long month) => world.Commands.Focus(actorRow, k);

    // Contract §4.1a r4, spec A37: v_exp = max(proc_v, V_C) + V_PO on home(a), proc_v elsewhere. The formula is a function of Core; the adapter passes proc_v.
    public Fx6 PlannedVolume(int siteRow, int processRow, long month)
    {
        int actor = world.IndustryLayout.SiteOwner[siteRow];
        return world.Products.ExpectedVolume(actor, world.IndustryLayout.Number[processRow], world.Industry.ProcV(siteRow, processRow),
            world.IndustryLayout.HomeSite(actor) == siteRow);
    }

    public bool Planned(int siteRow, int processRow, long month) =>
        world.Products.Planned(world.IndustryLayout.SiteOwner[siteRow], world.IndustryLayout.Number[processRow]);

    // Spec A43.1, contract §5: l_lic while the owner's licence is active, the process is in its mask and known to the owner in the snapshot; else 0.
    public Fx6 LicenceShare(int siteRow, int processRow, long month)
    {
        int actor = world.IndustryLayout.SiteOwner[siteRow];
        int number = world.IndustryLayout.Number[processRow];
        return LicenceRules.Has(world.Policy.LicenceMask(actor), number) && world.LicenceActive(actor)
            && KnowsProcess(siteRow, processRow, month) ? world.PolicyParameters.LicenceProcessShare : Fx6.Zero;
    }

    // mult_P (spec A37, DEBT-0019): P1 doubles the weight of the processes of the project of its last Requirement (the newest platform generation among
    // its live projects), P2 those of the line N (the newest platform generation among its series; with one line, that line), P3 does not. T6D_OFF has none.
    public Fx6 EffortMultiplier(int actorRow, int processRow, long month)
    {
        if (world.PolicyParameters.T6dOff)
            return Fx6.One;
        PolicyProfile profile = world.Policy.Profile(actorRow);
        if (profile == PolicyProfile.Adaptive)
            return Fx6.One;
        int best = -1;
        for (int d = 0; d < world.ProductsLayout.Designs; d++)
        {
            DesignStage stage = world.Products.Stage(actorRow, d);
            bool counts = profile == PolicyProfile.Mass
                ? stage == DesignStage.Series
                : stage is DesignStage.Project or DesignStage.Prototype or DesignStage.Series or DesignStage.InService;
            // A tie of the platform generation goes to the larger design id (the later in the loop).
            if (counts && (best < 0 || world.Products.PlatformGen(actorRow, d) >= world.Products.PlatformGen(actorRow, best)))
                best = d;
        }
        return best >= 0 && Array.IndexOf(world.Products.PrcSet(actorRow, best), world.IndustryLayout.Number[processRow]) >= 0 ? Fx6.FromInt(2) : Fx6.One;
    }

    // Spec §1.1: engineer_base = ⌊E_ref·budget_index⌋, skilled = 10·engineer_base, the base follows the budget; trophy engineers are a constant addition.
    public long WorkforceAvailable(int siteRow, int category, long month)
    {
        int actor = world.IndustryLayout.SiteOwner[siteRow];
        PolicyParams p = world.PolicyParameters;
        Fx6 b0 = world.BudgetAtStart[actor];
        long engineers = p.EngineerBase(b0);
        long baseNow = BudgetMath.FollowBudget(category == world.IndustryLayout.SkilledCategory ? p.SkilledPerEngineer * engineers : engineers,
            world.Policy.Budget(actor), b0);
        return category == world.IndustryLayout.SkilledCategory ? baseNow : baseNow + world.Scenario.TrophyEngineers[actor];
    }

    public Fx6 AlloyCap(int siteRow, long month)
    {
        int actor = world.IndustryLayout.SiteOwner[siteRow];
        Fx6 b0 = world.BudgetAtStart[actor];
        return Fx6.FromInt(BudgetMath.FollowBudget(world.Scenario.AlloyCapAtStart(actor).Raw / Fx6.Scale, world.Policy.Budget(actor), b0));
    }

    public Fx6 AlloyDeficit(int siteRow, long month) => world.Policy.AlloyDeficit(world.IndustryLayout.SiteOwner[siteRow], month);

    // Only the L_C control reads it (spec §6.1); this world runs the full capability model.
    public long KnownSince(int siteRow, int processRow, long month) => -1;
}
