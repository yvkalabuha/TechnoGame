using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Core.WhyNot;

namespace Singularity.Session;

/// <summary>Reads of the licence and why_not rules of the AI (spec A43): pure functions of the snapshot through the public functions of Core. No RNG here.</summary>
internal sealed class PolicyEventsAdapter(SliceWorld world) : IPolicyEvents
{
    public int EngineGen(int actorRow, long month) => world.EngineGen(actorRow);

    public LicenceSubject? Subject(int sellerRow, int gen, long month)
    {
        ProductsLayout l = world.ProductsLayout;
        for (int d = l.Designs - 1; d >= 0; d--)
        {
            if (world.Products.Stage(sellerRow, d) is not (DesignStage.Series or DesignStage.InService))
                continue;
            int[] alts = world.Products.Alternatives(sellerRow, d);
            if (l.AltEngGen[alts[l.BaseSlot]] != gen)
                continue;
            // The mask is the PRC of the engine and of the hot section as of the decision (spec A43.1); no reference to the project is kept.
            ulong mask = 0;
            foreach (int slot in (int[])[l.BaseSlot, l.AlloySlot])
            {
                if (alts[slot] < 0)
                    continue;
                foreach (int prc in l.PrcOf(alts[slot]))
                    mask |= LicenceRules.Bit(prc);
            }
            return new LicenceSubject(world.Products.UnitCostFloor(sellerRow, d), mask);
        }
        return null;
    }

    public IReadOnlyList<Link> WhyNot(int actorRow, long month)
    {
        // τ(a) (A43.7): the newest known APP not yet fielded; no APP known at all — the requirement of generation 1 (the link is the knowledge of node 58).
        ProductsLayout l = world.ProductsLayout;
        bool any = false;
        for (int g = 1; g <= l.Generations; g++)
        {
            int row = world.Graph.RowOf(l.GenApp[g - 1]);
            any |= row >= 0 && world.Knowledge.IsKnown(actorRow, row);
        }
        WhyNotTarget? target = any ? world.WhyNot.CurrentRequirement(actorRow) : WhyNotTarget.Requirement(1);
        return target is { } t ? world.WhyNot.WhyNot(actorRow, t, month) : [];
    }

    public IReadOnlyList<Link> WhyNotDesign(int actorRow, int design, long month) =>
        world.WhyNot.WhyNot(actorRow, WhyNotTarget.Design(design), month);
}
