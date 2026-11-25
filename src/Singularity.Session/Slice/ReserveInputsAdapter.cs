using Singularity.Core.Reserve;
using Singularity.Kernel;

namespace Singularity.Session;

/// <summary>Inputs of the reserve account: the share the five items leave times the budget, and the programme the knowledge layer funded this month.</summary>
internal sealed class ReserveInputsAdapter(SliceWorld world) : IReserveInputs
{
    // A licence fee received enters the reserve, a fee paid leaves it (O-055); the flow is that of the deals applied at the start of the month.
    public Fx6 Inflow(int actorRow, long month) =>
        world.ReserveShare(actorRow) * world.Policy.Budget(actorRow) + (world.FlowViaReserve(actorRow) ? Max(world.Policy.LicenceFlow(actorRow), Fx6.Zero) : Fx6.Zero);

    public Fx6 Spend(int actorRow, long month) =>
        world.Knowledge.ProgrammeSpend(actorRow, month) + (world.FlowViaReserve(actorRow) ? Max(-world.Policy.LicenceFlow(actorRow), Fx6.Zero) : Fx6.Zero);

    private static Fx6 Max(Fx6 a, Fx6 b) => a > b ? a : b;
}
