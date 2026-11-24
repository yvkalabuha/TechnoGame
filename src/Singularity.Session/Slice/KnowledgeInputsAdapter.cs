using Singularity.Core.Data;
using Singularity.Core.Knowledge;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session;

/// <summary>
/// Inputs of the knowledge layer: pure functions of the snapshot (the table state after month t−1) and of keys. Thin delegation:
/// the budget is <c>share_R·pol.budget</c>, <c>prio</c> is <see cref="PolicyRules.Priority"/>, <c>j</c> is <see cref="KnowledgeRng.J"/>
/// on the K stream (A15: the year of month t is 0 for t = 0, else ⌊(t−1)/12⌋).
/// </summary>
internal sealed class KnowledgeInputsAdapter(SliceWorld world) : IKnowledgeInputs
{
    public Fx6 ResearchBudget(int actorRow, long month) => world.SharesOf(actorRow).Research * world.Policy.Budget(actorRow);

    public Fx6 Priority(int actorRow, int nodeRow, long month)
    {
        Fx6 prio = PolicyRules.Priority(world.Policy.Profile(actorRow), world.Graph.Type[nodeRow], world.Data.Nodes.Hazard[nodeRow]);
        // Per-discipline research weight (TASK-0147); empty by default, so the default config computes exactly the old value.
        int d = world.Graph.Discipline[nodeRow];
        return world.ResearchWeight.Length == 0 || d < 0 ? prio : prio * world.ResearchWeight[d];
    }

    public int PriorityGoals(int actorRow, long month, Span<int> nodeRows, Span<Fx6> multipliers) =>
        world.Institutes?.Goals(actorRow, nodeRows, multipliers) ?? 0;

    public Fx6 J(int actorRow, int nodeRow, long month) =>
        KnowledgeRng.J(world.Streams.Know, month == 0 ? 0 : (int)((month - 1) / 12), actorRow, world.Graph.Number[nodeRow]);

    // The player's fund_programme of month t, cut to the reserve of the snapshot: the same value the reserve spends (one function in the knowledge layer).
    public bool FundProgramme(int actorRow, long month, out int nodeRow, out Fx6 amount)
    {
        nodeRow = -1;
        amount = Fx6.Zero;
        if (world.Reserve is null || world.Commands.Fund(actorRow, month) is not { } f)
            return false;
        nodeRow = world.Graph.RowOf(f.Node);
        Fx6 balance = world.Reserve.Balance(actorRow);
        amount = f.Amount < balance ? f.Amount : balance;
        return nodeRow >= 0;
    }

    public Fx6 DisciplineVolume(int actorRow, int discipline, long month) => world.Industry.DisciplineVolume(actorRow, discipline);

    // lic_knows (spec §6.7): the merge of month t grants what the licence bought in compute t−1 names (it began to act in t): the ENG node of the seller's
    // engine of the licensed generation and the PRC nodes of the mask. Without the diagnostic the knowledge system never asks.
    public ReadOnlySpan<int> LicensedEngines(int actorRow, long month)
    {
        if (world.Policy.LicenceStart(actorRow) != month || world.Policy.LicenceGen(actorRow) <= 0)
            return ReadOnlySpan<int>.Empty;
        ProductsLayout l = world.ProductsLayout;
        var rows = new List<int>();
        for (int a = l.AltStart[l.BaseSlot]; a < l.AltStart[l.BaseSlot + 1]; a++)
        {
            if (l.AltEngGen[a] != world.Policy.LicenceGen(actorRow))
                continue;
            for (int g = l.AltGroupStart[a]; g < l.AltGroupStart[a + 1]; g++)
            {
                for (int m = l.GroupMemberStart[g]; m < l.GroupMemberStart[g + 1]; m++)
                {
                    int row = world.Graph.RowOf(l.GroupMembers[m]);
                    if (row >= 0 && world.Graph.Type[row] == NodeType.ENG)
                        rows.Add(row);
                }
            }
        }
        LicenceMask mask = world.Policy.LicenceMask(actorRow);
        for (int n = 1; n <= world.Policy.LicenceCapacity; n++)
        {
            if (LicenceRules.Has(mask, n) && world.Graph.RowOf(n) is >= 0 and int row)
                rows.Add(row);
        }
        return rows.ToArray();
    }
}
