using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.WhyNot;
using Singularity.Kernel;

namespace Singularity.Session;

/// <summary>
/// Inputs of <c>why_not</c>: pure functions of the snapshot, no RNG. The procurement and training budgets are the product layer's own inputs,
/// the tooling rate is the growth of <c>tooling_M</c> under the tooling plan of the month (A39). One input has no rule behind it yet and says
/// so with a constant, like the capability adapter: no licence is ever offered (events, T6d).
/// </summary>
internal sealed class WhyNotInputsAdapter(SliceWorld world) : IWhyNotInputs
{
    public Fx6 ProcurementBudget(int actorRow, long month) => world.ProcurementBudget(actorRow);

    public bool LicenceOffered(int siteRow, int processNumber, long month) => false;

    public Fx6 TrainingBudget(int actorRow, int design, long month) => world.ProductsInputs.TrainingBudget(actorRow, design, month);

    // Net growth of M in a month at the planned investment: gain − wear (spec A11, A39), never negative.
    public Fx6 ToolingRate(int siteRow, int cluster, Dimension dim, long month)
    {
        int actor = world.IndustryLayout.SiteOwner[siteRow];
        Fx6 invest = world.IndustryInputs.Plan(actor, month, world.Industry.RestActor(actor, month)).Invest(siteRow, cluster, dim);
        Fx6 m = world.Industry.ToolingM(siteRow, cluster, dim);
        IndustryParams p = world.IndustryParameters;
        Fx6 net = invest / p.CostM(IndustryMath.CostTier(m)) - p.DeltaM * m;
        return net > Fx6.Zero ? net : Fx6.Zero;
    }
}
