using Singularity.Core.Data;
using Singularity.Core.Institutes;
using Singularity.Core.Knowledge;
using Singularity.Core.Products;
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

    // ---- slot model: the player's lists and focus are columns; progress and the money returned come from the knowledge snapshot

    public int QueueEntry(InstituteHandle slot, int index) => world.SlotOrders is { } o && !IsKnownNumber(slot.Actor, o.Entry(slot, index)) ? o.Entry(slot, index) : 0;

    public int FocusOrder(InstituteHandle slot, out int level, out long month)
    {
        SlotOrders o = world.SlotOrders!;
        level = o.FocusLevel(slot);
        month = o.FocusMonth(slot);
        return o.FocusNode(slot);
    }

    public bool FocusFromReserve(InstituteHandle slot) => world.SlotOrders!.FocusFromReserve(slot);

    public int SlotProgress(int actorRow, int slot, out Fx6 added) => world.Knowledge.SlotProgress(actorRow, slot, out added);

    public Fx6 Progress(int actorRow, int nodeRow) => world.Knowledge.Prog(actorRow, nodeRow);

    public bool Released(int actorRow, int nodeRow) => world.Knowledge.Released(actorRow, nodeRow);

    public Fx6 AirPowerRatio(int actorRow, long month) => world.AirPowerRatio(actorRow);   // read from the column of the last refresh: no fleet value is computed on the tick path

    // Returned money over the research money of the last step; what the slots left out of a budget that is the share of the actor's income.
    public Fx6 SurplusShare(int actorRow, long month)
    {
        // The money the slots saw in that step (after projects and upkeep): the same figure their coverage was computed against.
        Fx6 returned = world.Knowledge.ReturnedToBudget(actorRow);
        Fx6 budget = _knowledge.ResearchBudget(actorRow, month);
        return budget > Fx6.Zero ? returned / budget : Fx6.Zero;
    }

    public Fx6 NodeMonths(int nodeRow) => world.Knowledge.NodeMonths(nodeRow);

    private bool IsKnownNumber(int actor, int number) => number == 0 || world.Graph.RowOf(number) is not (>= 0 and int row) || world.Knowledge.IsKnown(actor, row);


    // The PATH rule of ai-institute-goals.md §2 (#559). g* = the lowest generation the actor cannot yet compose: its APP node is unknown, or a slot has no available alternative that fits g*
    // (airframe rank >= platform_min_rank, engine eng_gen >= g*-1, max_eng_gen of the others). Seeds: the APP node and, for each slot without a fitting
    // available alternative, the requires and processes of the lowest-rank fitting one. The path = the unknown requirement closure of the seeds.
    public void GoalPath(int actorRow, long month, Span<byte> tier)
    {
        ProductsLayout l = world.ProductsLayout;
        KnowledgeGraph kg = world.Graph;
        Span<int> stack = kg.Count <= 512 ? stackalloc int[kg.Count] : new int[kg.Count];   // heap only above 512 nodes
        const byte level = 1;   // only g*: the rule never reads a second tier (ai-institute-goals.md §2 item 4, PATH)
        for (int g = 1; g <= l.Generations; g++)
        {
            if (MarkGeneration(actorRow, l, g, level, tier, stack) > 0)
            {
                // The slot model also uses the generation after (the fallback tier 2 of the AI, SCIENCE_SLOTS §8); the shares model reads tier 1 only.
                if (world.SlotsOn && g < l.Generations)
                    MarkGeneration(actorRow, l, g + 1, 2, tier, stack);
                return;
            }
        }
    }

    // The requirement closure of what the actor lacks for generation g (its APP node, the engine, each slot without a fitting available alternative), marked with the level.
    private int MarkGeneration(int actorRow, ProductsLayout l, int g, byte level, Span<byte> tier, Span<int> stack)
    {
        int marked = 0;
        marked += Seed(actorRow, l.GenApp[g - 1], level, tier, stack);
        int need = g - 1 < 1 ? 1 : g - 1;
        int engine = Fitting(actorRow, l, l.BaseSlot, g, -1, need, out bool engineOk);
        if (!engineOk && engine >= 0)
            marked += SeedAlt(actorRow, l, engine, level, tier, stack);
        for (int s = 0; s < l.SlotCount; s++)
        {
            if (s == l.BaseSlot)
                continue;
            int alt = Fitting(actorRow, l, s, g, engine, need, out bool ok);
            if (!ok && alt >= 0)
                marked += SeedAlt(actorRow, l, alt, level, tier, stack);
        }
        return marked;
    }

    // The lowest-rank available alternative of the slot that fits generation g (ok = true), else the lowest-rank fitting one (ok = false); -1 = none fits.
    private int Fitting(int a, ProductsLayout l, int s, int g, int engine, int need, out bool ok)
    {
        int first = -1;
        for (int alt = l.AltStart[s]; alt < l.AltStart[s + 1]; alt++)
        {
            bool fits = s == l.BaseSlot ? l.AltEngGen[alt] >= need
                : s == l.AirframeSlot ? l.AltRank[alt] >= l.PlatformMinRank[g - 1]
                : true;
            if (fits && engine >= 0 && s != l.BaseSlot)
                fits = l.AltMaxEngGen[alt] == 0 || l.AltEngGen[engine] <= l.AltMaxEngGen[alt];
            if (!fits)
                continue;
            if (first < 0)
                first = alt;
            if (Available(a, l, alt))
            {
                ok = true;
                return alt;
            }
        }
        ok = false;
        return first;
    }

    private bool Available(int a, ProductsLayout l, int alt)
    {
        for (int g = l.AltGroupStart[alt]; g < l.AltGroupStart[alt + 1]; g++)
        {
            bool any = false;
            for (int m = l.GroupMemberStart[g]; m < l.GroupMemberStart[g + 1] && !any; m++)
                any = Knows(a, l.GroupMembers[m]);
            if (!any)
                return false;
        }
        foreach (int p in l.PrcOf(alt))
        {
            if (!Knows(a, p))
                return false;
        }
        return true;
    }

    private bool Knows(int a, int number) => world.Graph.RowOf(number) is not (>= 0 and int row) || world.Knowledge.IsKnown(a, row);

    private int SeedAlt(int a, ProductsLayout l, int alt, byte level, Span<byte> tier, Span<int> stack)
    {
        int n = 0;
        for (int g = l.AltGroupStart[alt]; g < l.AltGroupStart[alt + 1]; g++)
        {
            bool any = false;
            for (int m = l.GroupMemberStart[g]; m < l.GroupMemberStart[g + 1] && !any; m++)
                any = Knows(a, l.GroupMembers[m]);
            if (any)
                continue;
            for (int m = l.GroupMemberStart[g]; m < l.GroupMemberStart[g + 1]; m++)
                n += Seed(a, l.GroupMembers[m], level, tier, stack);
        }
        foreach (int p in l.PrcOf(alt))
            n += Seed(a, p, level, tier, stack);
        return n;
    }

    // Marks the unknown requirement closure of the node with the level (a node already marked keeps its lower level); returns the count newly marked.
    private int Seed(int a, int number, byte level, Span<byte> tier, Span<int> stack)
    {
        int root = world.Graph.RowOf(number);
        if (root < 0 || world.Knowledge.IsKnown(a, root) || tier[root] != 0)
            return 0;
        RequirementLists req = world.Graph.Requires;
        int n = 0, top = 0;
        tier[root] = level;
        stack[top++] = root;
        while (top > 0)
        {
            int from = stack[--top];
            n++;
            for (int g = req.GroupStart[from]; g < req.GroupStart[from + 1]; g++)
            {
                bool satisfied = false;
                foreach (int m in req.GroupMembers(g))
                    satisfied |= world.Knowledge.IsKnown(a, m);
                if (satisfied)
                    continue;
                foreach (int m in req.GroupMembers(g))
                {
                    if (tier[m] != 0 || world.Knowledge.IsKnown(a, m))
                        continue;
                    tier[m] = level;
                    stack[top++] = m;
                }
            }
        }
        return n;
    }
}
