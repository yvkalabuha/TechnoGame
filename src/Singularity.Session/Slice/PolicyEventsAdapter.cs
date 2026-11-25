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
            if (world.Products.Stage(sellerRow, d) is not (DesignStage.Series or DesignStage.InService) || world.Products.LicensedFrom(sellerRow, d) >= 0)
                continue;   // a bought project is never the subject of a licence (#477)
            if (l.AltEngGen[world.Products.AltAt(sellerRow, d, l.BaseSlot)] != gen)
                continue;
            // The mask is the PRC of the engine and of the hot section as of the decision (spec A43.1); no reference to the project is kept.
            // The market sells the whole project (LP-1, #158), so its mask covers every slot: the buyer must be able to produce all of it.
            LicenceMask mask = default;
            foreach (int slot in world.Config.LicenceMarket ? Enumerable.Range(0, l.SlotCount) : (int[])[l.BaseSlot, l.AlloySlot])
            {
                int alt = world.Products.AltAt(sellerRow, d, slot);
                if (alt < 0)
                    continue;
                foreach (int prc in l.PrcOf(alt))
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
        WhyNotTarget? target = any ? world.WhyNot.CurrentRequirement(actorRow, month) : WhyNotTarget.Requirement(1);
        return target is { } t ? world.WhyNot.WhyNot(actorRow, t, month) : [];
    }

    // The longest estimate over the links of the requirement of the generation; any link without an estimate means "never by itself".
    public int? OwnEtaMonths(int actorRow, int gen, long month)
    {
        // The ETA is to the APP node of the generation (O-062): once it is known the buyer reaches the generation by itself, whatever else its requirement lists.
        ProductsLayout layout = world.ProductsLayout;
        int appRow = gen >= 1 && gen <= layout.Generations ? world.Graph.RowOf(layout.GenApp[gen - 1]) : -1;
        if (appRow >= 0 && world.Knowledge.IsKnown(actorRow, appRow))
            return 0;
        IReadOnlyList<Link> links = world.WhyNot.WhyNot(actorRow, WhyNotTarget.Requirement(gen), month);
        int worst = 0;
        foreach (Link l in links)
        {
            // Only the research of the generation counts: a design link («no project yet») has no ETA of its own and says nothing about how soon the APP is known.
            if (l.Layer != WhyNotLayer.Knowledge)
                continue;
            if (l.EtaMonths is not { } eta)
                return null;
            worst = Math.Max(worst, eta);
        }
        return worst;
    }

    public bool HasFreeDesignSlot(int actorRow) => world.Products.HasFreeSlot(actorRow);

    public IReadOnlyList<Link> WhyNotDesign(int actorRow, DesignHandle design, long month) =>
        world.WhyNot.WhyNot(actorRow, WhyNotTarget.Design(design), month);
}
