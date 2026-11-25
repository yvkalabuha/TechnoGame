using Singularity.Core.Institutes;
using Singularity.Kernel;

namespace Singularity.Session;

/// <summary>Inputs of the institutes: the AI picks goals by the same node weight the knowledge layer uses.</summary>
internal sealed class InstituteInputsAdapter(SliceWorld world) : IInstituteInputs
{
    private readonly KnowledgeInputsAdapter _knowledge = new(world);

    public Fx6 Priority(int actorRow, int nodeRow, long month) => _knowledge.Priority(actorRow, nodeRow, month);

    public bool IsKnown(int actorRow, int nodeRow) => world.Knowledge.IsKnown(actorRow, nodeRow);

    public bool InFrontier(int actorRow, int nodeRow) => world.Knowledge.InFrontier(actorRow, nodeRow);

    public bool DiscoveredByResearch(int actorRow, int nodeRow) => world.Knowledge.DiscoveredByResearch(actorRow, nodeRow);

    public bool PlayerControlled(int actorRow) => world.Config.PlayerActor == actorRow;

    // The player's research share against the one of the AI profile (the money the project's rate was priced on); the AI is always fully funded.
    public Fx6 BuildFunding(int actorRow)
    {
        if (world.Commands.SharesOverride(actorRow) is not { } s)
            return Fx6.One;
        Fx6 plan = world.Policy.Shares(actorRow).Research;
        if (plan <= Fx6.Zero)
            return s.Research > Fx6.Zero ? Fx6.One : Fx6.Zero;
        return s.Research >= plan ? Fx6.One : s.Research / plan;
    }

    public long PlayerBuildMonth(InstituteHandle slot) => world.InstituteOrders!.BuildMonth(slot);

    public int OrderNode(InstituteHandle slot) => world.InstituteOrders!.OrderNode(slot);
}
