using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session;

/// <summary>Reads of the product AI: pure functions of the snapshot through the public functions of Core. No RNG here.</summary>
internal sealed class PolicyProductsAdapter(SliceWorld world) : IPolicyProducts
{
    public ProductsLayout Layout => world.ProductsLayout;

    public Fx6 DRef => world.Products.DRef;

    public PolicyDesign Design(int actorRow, int design, long month)
    {
        ProductsSystem p = world.Products;
        DesignStage stage = p.Stage(actorRow, design);
        bool exists = stage != DesignStage.None;
        return new PolicyDesign(stage, p.PlatformGen(actorRow, design), p.Cancelled(actorRow, design), p.BuiltAny(actorRow, design),
            p.IdleMonths(actorRow, design), p.StageOMonth(actorRow, design), p.Parent(actorRow, design),
            stage == DesignStage.Prototype && p.GateOpen(actorRow, design, month), p.DefectsHidden(actorRow, design),
            exists ? p.UnitCostFloor(actorRow, design) : Fx6.One);
    }

    public int[] Alternatives(int actorRow, int design) => world.Products.Alternatives(actorRow, design);

    public bool Knows(int actorRow, int nodeNumber, long month)
    {
        int row = world.Graph.RowOf(nodeNumber);
        return row >= 0 && world.Knowledge.IsKnown(actorRow, row);
    }

    public bool Admitted(int actorRow, int design, long month)
    {
        int[] prc = world.Products.PrcSet(actorRow, design);
        return world.Industry.Profile(actorRow, DesignRules.Items(world.ProductsLayout, prc), month).Admitted;
    }

    public Fx6 AdmittedYield(int actorRow, int processNumber, long month) => world.ProductsInputs.AdmittedYield(actorRow, processNumber, month);

    public Fx6 AlloySupply(int actorRow, long month) => world.Industry.Alloy(actorRow, month).Supply;

    public int FreeBureaus(int actorRow) => world.Products.FreeBureaus(actorRow);

    public FleetBook? Fleet(int actorRow, long month) => world.Products.SnapshotBook(actorRow, month);

    public int GFront(long month) => world.Products.GFront(month - 1);

    public Fx6 CandidateValue(int actorRow, ReadOnlySpan<int> alts, Fx6 budget, long month) =>
        world.Products.CandidateValue(actorRow, alts, budget, month).Value;
}
