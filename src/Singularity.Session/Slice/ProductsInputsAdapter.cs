using Singularity.Core.Commands;
using Singularity.Core.Industry;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session;

/// <summary>
/// Inputs of the product layer. Reads of the capability and knowledge layers are their public pure functions; the design commands,
/// <c>start_series</c> and <c>close_line</c> of month t are the external commands in <c>cmd.*</c> whose stamp is t. [Extension point,
/// T6c] AI decisions will live in <c>pol.*</c> and join the external ones here in command-key order; <c>force</c> is taken only from
/// <c>cmd.*</c>, and an AI decision never has it. No RNG here.
/// </summary>
internal sealed class ProductsInputsAdapter(SliceWorld world) : IProductsInputs
{
    public bool Knows(int actorRow, int nodeNumber, long month) => KnowsNode(actorRow, nodeNumber);

    public Fx6 AdmittedYield(int siteRow, int processNumber, long month) =>
        KnowsNode(siteRow, processNumber) ? world.Industry.Yield(siteRow, world.IndustryLayout.RowOf(processNumber), month).Yield : Fx6.Zero;

    public ProductionProfile Profile(int siteRow, ReadOnlySpan<PrcSetItem> prcSet, long month) => world.Industry.Profile(siteRow, prcSet, month);

    public Fx6 AlloySupply(int siteRow, long month) => world.Industry.Alloy(siteRow, month).Supply;

    public long Granted(int siteRow, int category, int requesterId, long month) => world.Industry.Granted(siteRow, category, requesterId);

    public Fx6 ProcurementBudget(int actorRow, long month) => world.ProcurementBudget(actorRow);

    public Fx6 ProcurementBase(int actorRow, long month) => world.ProcurementBase(actorRow);

    // mastered(a, p): mastered_run ≥ 6 at some site of the actor (contract r6 §9.1; in the slice a site is an actor).
    public bool Mastered(int actorRow, int processNumber, long month)
    {
        int row = world.IndustryLayout.RowOf(processNumber);
        if (row < 0)
            return false;
        for (int s = 0; s < world.IndustryLayout.SiteCount; s++)
        {
            if (world.IndustryLayout.SiteOwner[s] == actorRow && world.Industry.MasteredRun(s, row) >= IndustryParams.MasteredMonths)
                return true;
        }
        return false;
    }

    // External commands first, then the AI's (key order: SourceClass.Ai is last).
    public IReadOnlyList<FleetCommand> FleetCommands(int actorRow, long month)
    {
        var list = new List<FleetCommand>();
        foreach ((CommandKind kind, int design, int n) in world.Commands.FleetCommands(actorRow, month))
            list.Add(new FleetCommand(kind == CommandKind.SellCohort ? FleetCommandKind.Sell : FleetCommandKind.Retire, design, n));
        list.AddRange(world.Policy.AiFleetCommands(actorRow, month));
        return list;
    }

    public IReadOnlyList<DesignCommand> DesignCommands(int actorRow, long month)
    {
        var list = new List<DesignCommand>();
        // Key order: the external sources (player, harness) first, then the AI (SourceClass.Ai is last).
        foreach ((CommandKind kind, int target, var alts) in world.Commands.DesignCommands(actorRow, month))
        {
            DesignCommandKind k = kind switch
            {
                CommandKind.ComposeDesign => DesignCommandKind.Compose,
                CommandKind.DeriveDesign => DesignCommandKind.Derive,
                _ => DesignCommandKind.Cancel,
            };
            list.Add(new DesignCommand(k, target, kind == CommandKind.CancelDesign ? [] : alts));
        }
        list.AddRange(world.Policy.AiDesignCommands(actorRow, month));
        return list;
    }

    // An external command wins; the AI's start never carries force (spec A36).
    public SeriesStart? StartSeries(int actorRow, int design, long month) =>
        world.Commands.Series(actorRow, design, month) is { } s ? new SeriesStart(s.Rate, s.Force)
            : world.Policy.AiStart(actorRow, design, month) is { } r ? new SeriesStart(r, false) : null;

    public bool CloseLine(int actorRow, int design, long month) =>
        world.Commands.CloseLine(actorRow, design, month) || world.Policy.AiClose(actorRow, design, month);

    // Spec A39, A41 (F15): T_a of this month's plan on the line the policy chose as the training series, else 0.
    public Fx6 TrainingBudget(int actorRow, int design, long month) =>
        world.Policy.TrainingTarget(actorRow) == design
            ? world.IndustryInputs.Plan(actorRow, month, world.Industry.RestActor(actorRow, month)).TrainingBudget
            : Fx6.Zero;

    // Spec A41: a line the AI launched runs at r_AI = ⌊B_proc(t)/unit_cost_floor⌋, recomputed from the budget of the month; the training series adds
    // the capability item (B_proc + B_cap). A line started by a command keeps the rate of that command.
    public Fx6? LineRate(int actorRow, int design, long month)
    {
        if (!world.Policy.AiLine(actorRow, design))
            return null;
        BudgetShares shares = world.SharesOf(actorRow);
        Fx6 budget = shares.Procurement * world.Policy.Budget(actorRow);
        if (world.Policy.TrainingTarget(actorRow) == design)
            budget += shares.Capability * world.Policy.Budget(actorRow);
        // Spec A42.5: the expected sale revenue of the month joins the base; PURCHASE_CAP keeps the upkeep at U_LO.
        Fx6 need = Fx6.Zero;
        for (int d = 0; d < world.ProductsLayout.Designs; d++)
        {
            if (world.Products.Stage(actorRow, d) is DesignStage.Series or DesignStage.InService)
                need += Fx6.FromInt(world.Products.InService(actorRow, d).Raw / Fx6.Scale) * world.Products.Upkeep(actorRow, d);
        }
        return world.Policy.AiLineRate(actorRow, budget, shares.Procurement * world.Policy.Budget(actorRow),
            world.Products.UnitCostFloor(actorRow, design), need, world.Products.Upkeep(actorRow, design));
    }

    // Only the L_P control reads it (spec §6.1).
    public long AppKnownSince(int actorRow, int gen, long month) => -1;

    private bool KnowsNode(int actorRow, int nodeNumber)
    {
        int row = world.Graph.RowOf(nodeNumber);
        return row >= 0 && world.Knowledge.IsKnown(actorRow, row);
    }
}
