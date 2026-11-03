using Singularity.Core.Data;
using Singularity.Core.WhyNot;
using Singularity.Kernel;

namespace Singularity.Session;

/// <summary>
/// Inputs of <c>why_not</c>: pure functions of the snapshot, no RNG. The procurement budget is the product layer's own input. Two inputs have
/// no rule behind them in this build and say so with a constant, like the capability adapter: no licence is ever offered (events, T6d) and no
/// tooling investment is planned (tooling plan, T6b), so an M-source link has no <c>eta</c> until T6b wires <see cref="ToolingRate"/> to the plan.
/// </summary>
internal sealed class WhyNotInputsAdapter(SliceWorld world) : IWhyNotInputs
{
    public Fx6 ProcurementBudget(int actorRow, long month) => world.SharesOf(actorRow).Procurement * world.Policy.Budget(actorRow);

    public bool LicenceOffered(int siteRow, int processNumber, long month) => false;

    public Fx6 ToolingRate(int siteRow, int cluster, Dimension dim, long month) => Fx6.Zero;
}
