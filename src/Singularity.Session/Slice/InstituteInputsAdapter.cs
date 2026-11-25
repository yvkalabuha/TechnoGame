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

    public long PlayerBuildMonth(InstituteHandle slot) => world.InstituteOrders!.BuildMonth(slot);

    public int OrderNode(InstituteHandle slot) => world.InstituteOrders!.OrderNode(slot);
}
