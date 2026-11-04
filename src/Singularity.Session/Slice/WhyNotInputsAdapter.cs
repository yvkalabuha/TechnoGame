using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Policy;
using Singularity.Core.WhyNot;
using Singularity.Kernel;

namespace Singularity.Session;

/// <summary>
/// Inputs of <c>why_not</c>: pure functions of the snapshot, no RNG. The procurement and training budgets are the product layer's own inputs,
/// the tooling rate is the growth of <c>tooling_M</c> under the tooling plan of the month (A39). One input has no rule behind it yet and says
/// by the policy's <c>offer_mask</c> (events, T6d).
/// </summary>
internal sealed class WhyNotInputsAdapter(SliceWorld world) : IWhyNotInputs
{
    // B_in (contract r6 §4.4f): the procurement item plus the sale revenue the snapshot already promises for the month (the AI's inc_exp, exact by FL7).
    public Fx6 ProcurementBudget(int actorRow, long month) => world.ProcurementBudget(actorRow) + world.Policy.ExpectedSaleIncome(actorRow);

    // Contract §5: a bit of offer_mask of the year, the process known. The mask is rewritten at the yearly step of month 12k and shows the offers of
    // the year that follows it; in month 12k itself it is still last year's, so it is not offered (no mask exists before the first step).
    public bool LicenceOffered(int siteRow, int processNumber, long month)
    {
        int actor = world.IndustryLayout.SiteOwner[siteRow];
        int row = world.Graph.RowOf(processNumber);
        return month % 12 != 0 && LicenceRules.Has(world.Policy.OfferMask(actor), processNumber) && row >= 0 && world.Knowledge.IsKnown(actor, row);
    }

    public Fx6 TrainingBudget(int actorRow, int design, long month) => world.ProductsInputs.TrainingBudget(actorRow, design, month);

    // Place of the cluster in the pass-2 queue (contract r8 §6): CAN in layout order (MET 1 … AERO 4), REV reversed, PROP and SEV have no permanent queue.
    public int PoolQueuePlace(int cluster)
    {
        int n = world.IndustryLayout.Clusters.Length;
        return world.PolicyParameters.ToolingTarget.PoolOrder switch
        {
            PoolOrder.Can => cluster + 1,
            PoolOrder.Rev => n - cluster,
            _ => 0,
        };
    }

    // Net growth of M in a month at the planned investment: gain − wear (spec A11, A39), never negative.
    public Fx6 ToolingRate(int siteRow, int cluster, Dimension dim, long month)
    {
        int actor = world.IndustryLayout.SiteOwner[siteRow];
        Fx6 invest = world.IndustryInputs.Plan(actor, month, world.Industry.RestActor(actor, month)).Applied(siteRow, cluster, dim);
        Fx6 m = world.Industry.ToolingM(siteRow, cluster, dim);
        IndustryParams p = world.IndustryParameters;
        Fx6 net = invest / p.CostM(IndustryMath.CostTier(m)) - p.DeltaM * m;
        return net > Fx6.Zero ? net : Fx6.Zero;
    }
}
