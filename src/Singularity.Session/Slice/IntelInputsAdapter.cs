using Singularity.Core.Intelligence;
using Singularity.Kernel;

namespace Singularity.Session;

/// <summary>Inputs of the intelligence layer: the security share in force, the player's plan and what each actor knows; pure reads of the snapshot.</summary>
internal sealed class IntelInputsAdapter(SliceWorld world) : IIntelInputs
{
    public Fx6 SecurityShare(int actorRow) => world.SharesOf(actorRow).Security;

    public bool PlayerPlan(int actorRow, int slot, out int target, out IntelMode mode) => world.Commands.PlayerPlan(actorRow, slot, out target, out mode);

    public int KnownCount(int actorRow) => world.Knowledge.KnownCount(actorRow);

    public int NodeCount => world.Graph.Count;

    // The disciplines are the data's: one past the highest index a node carries (static, computed on first use).
    private int _disciplines = -1;

    public int DisciplineCount
    {
        get
        {
            if (_disciplines < 0)
            {
                int max = -1;
                for (int r = 0; r < world.Graph.Count; r++)
                    max = Math.Max(max, world.Graph.Discipline[r]);
                _disciplines = max + 1;
            }
            return _disciplines;
        }
    }

    public int DisciplineOf(int nodeRow) => world.Graph.Discipline[nodeRow];

    public bool Knows(int actorRow, int nodeRow) => world.Knowledge.IsKnown(actorRow, nodeRow);
}
