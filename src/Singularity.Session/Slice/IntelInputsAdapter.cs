using Singularity.Core.Intelligence;
using Singularity.Kernel;

namespace Singularity.Session;

/// <summary>Inputs of the intelligence layer: the security share in force, the player's plan and what each actor knows; pure reads of the snapshot.</summary>
internal sealed class IntelInputsAdapter(SliceWorld world) : IIntelInputs
{
    public Fx6 SecurityShare(int actorRow) => world.SharesOf(actorRow).Security;

    public bool PlayerPlan(int actorRow, int slot, out int target, out IntelMode mode) => world.Commands.PlayerPlan(actorRow, slot, out target, out mode);

    public int KnownCount(int actorRow)
    {
        int n = 0;
        for (int r = 0; r < world.Graph.Count; r++)
            n += world.Knowledge.IsKnown(actorRow, r) ? 1 : 0;
        return n;
    }

    public bool Knows(int actorRow, int nodeRow) => world.Knowledge.IsKnown(actorRow, nodeRow);
}
