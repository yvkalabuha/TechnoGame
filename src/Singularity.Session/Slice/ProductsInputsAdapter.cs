using Singularity.Core.Commands;
using Singularity.Core.Industry;
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

    public IReadOnlyList<DesignCommand> DesignCommands(int actorRow, long month)
    {
        var list = new List<DesignCommand>();
        foreach ((CommandKind kind, int target, var alts) in world.Commands.DesignCommands(actorRow, month))
            list.Add(new DesignCommand(kind == CommandKind.ComposeDesign ? DesignCommandKind.Compose : DesignCommandKind.Derive, target, alts));
        return list;
    }

    public SeriesStart? StartSeries(int actorRow, int design, long month) =>
        world.Commands.Series(actorRow, design, month) is { } s ? new SeriesStart(s.Rate, s.Force) : null;

    public bool CloseLine(int actorRow, int design, long month) => world.Commands.CloseLine(actorRow, design, month);

    // Spec A39, A41 (F15): T_a of this month's plan on the line the policy chose as the training series, else 0.
    public Fx6 TrainingBudget(int actorRow, int design, long month) =>
        world.Policy.TrainingTarget(actorRow) == design
            ? world.IndustryInputs.Plan(actorRow, month, world.Industry.RestActor(actorRow, month)).TrainingBudget
            : Fx6.Zero;

    // The AI's line rate r_AI is a T6c decision; an external start_series sets the rate of the line.
    public Fx6? LineRate(int actorRow, int design, long month) => null;

    // Only the L_P control reads it (spec §6.1).
    public long AppKnownSince(int actorRow, int gen, long month) => -1;

    private bool KnowsNode(int actorRow, int nodeNumber)
    {
        int row = world.Graph.RowOf(nodeNumber);
        return row >= 0 && world.Knowledge.IsKnown(actorRow, row);
    }
}
