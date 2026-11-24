using Singularity.Core.Reserve;
using Singularity.Kernel;

namespace Singularity.Session;

/// <summary>Inputs of the reserve account: the share the five items leave times the budget, and the programme the knowledge layer funded this month.</summary>
internal sealed class ReserveInputsAdapter(SliceWorld world) : IReserveInputs
{
    public Fx6 Inflow(int actorRow, long month) => world.SharesOf(actorRow).Reserve * world.Policy.Budget(actorRow);

    public Fx6 Spend(int actorRow, long month) => world.Knowledge.ProgrammeSpend(actorRow, month);
}
