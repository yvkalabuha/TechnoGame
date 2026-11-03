using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Policy;
using Singularity.Kernel;

namespace Singularity.Session;

/// <summary>
/// Inputs of the capability layer: pure functions of the snapshot. No RNG here (the layer has none, #28, and nothing may draw on its
/// behalf; an architecture test checks this class too). The budget items come from the shares in force and <c>pol.budget</c>; the
/// planned volumes from <see cref="Singularity.Core.Products.ProductsSystem"/>; pools of people and alloy capacity follow the budget
/// (<see cref="BudgetMath.FollowBudget"/>).
///
/// Four inputs have no rule behind them in this build and say so with a constant: <c>ToolingInvest</c> (the tooling plan of A39/A30 is
/// T6b), <c>QualityProgramme</c> (T6b), <c>LicenceShare</c> and <c>AlloyDeficit</c> (events, T6d). They are not decisions imitated
/// by a placeholder: until those tasks land there is no tooling investment, no programme, no licence and no disruption.
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

    public bool QualityProgramme(int siteRow, long month) => false;

    public Fx6 ToolingInvest(int siteRow, int cluster, Dimension dim, long month, Fx6 restActor) => Fx6.Zero;

    public Fx6 EffortBudget(int actorRow, long month) => world.SharesOf(actorRow).Effort * world.Policy.Budget(actorRow);

    public int EffortFocus(int actorRow, int k, long month) => world.Commands.Focus(actorRow, k);

    public Fx6 PlannedVolume(int siteRow, int processRow, long month) =>
        world.Products.PlannedVolume(world.IndustryLayout.SiteOwner[siteRow], world.IndustryLayout.Number[processRow]);

    public bool Planned(int siteRow, int processRow, long month) =>
        world.Products.Planned(world.IndustryLayout.SiteOwner[siteRow], world.IndustryLayout.Number[processRow]);

    public Fx6 LicenceShare(int siteRow, int processRow, long month) => Fx6.Zero;

    // Spec §1.1: engineer_base = ⌊E_ref·budget_index⌋, skilled = 10·engineer_base, the base follows the budget; trophy engineers are a constant addition.
    public long WorkforceAvailable(int siteRow, int category, long month)
    {
        int actor = world.IndustryLayout.SiteOwner[siteRow];
        PolicyParams p = world.PolicyParameters;
        Fx6 b0 = world.BudgetAtStart[actor];
        long engineers = (p.EngineerRef * b0.Raw) / Fx6.Scale;
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

    public Fx6 AlloyDeficit(int siteRow, long month) => Fx6.Zero;

    // Only the L_C control reads it (spec §6.1); this world runs the full capability model.
    public long KnownSince(int siteRow, int processRow, long month) => -1;
}
