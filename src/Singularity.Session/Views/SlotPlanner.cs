using System.Collections.Immutable;
using Singularity.Core.Commands;
using Singularity.Core.Institutes;
using Singularity.Core.Knowledge;
using Singularity.Kernel;

namespace Singularity.Session;

/// <summary>
/// Reads of the slot model for the tech map (SCIENCE_SLOTS §3, §4.3, §12): fit of the institutes, focus quote, path to a goal and a suggested layout of queues. Pure functions
/// of the snapshot of the month; nothing is written and nothing is started (§4.3: the game only shows, the player confirms).
/// </summary>
internal static class SlotPlanner
{
    public static ImmutableArray<InstituteFitView> Fit(SliceWorld w, int actor, int nodeRow, long month)
    {
        InstituteSystem inst = w.Institutes!;
        int slots = inst.Layout.SlotCount(actor);
        var node = new int[slots];
        inst.Resolve(actor, month, node, new byte[slots]);
        Fx6 prog = w.Knowledge.Prog(actor, nodeRow);
        Fx6 left = prog >= Fx6.One ? Fx6.Zero : Fx6.One - prog;
        Fx6 months = w.Knowledge.EffectiveMonths(actor, nodeRow, month);
        Fx6 research = w.Knowledge.ResearchFactor(actor, nodeRow);
        var list = ImmutableArray.CreateBuilder<InstituteFitView>(slots);
        for (int k = 0; k < slots; k++)
        {
            var h = new InstituteHandle(actor, k);
            bool standing = inst.Standing(h);
            Fx6 overlap = standing ? inst.Overlap(h, nodeRow) : Fx6.Zero;
            Fx6 speed = SlotMath.Speed(inst.SlotParameters, standing ? inst.InstituteMultiplier(h, nodeRow) : Fx6.One, 0, research);
            list.Add(new InstituteFitView(k, inst.Layout.IdOf(h), standing, overlap, speed, standing ? left * months / speed : Fx6.Zero, node[k] > 0 ? node[k] : 0, standing && overlap == Fx6.Zero));
        }
        return list.MoveToImmutable();
    }

    public static ImmutableArray<FocusQuoteView> Focus(SliceWorld w, int actor, int nodeRow, int slot, long month)
    {
        InstituteSystem inst = w.Institutes!;
        SlotParams p = inst.SlotParameters;
        var h = new InstituteHandle(actor, slot);
        Fx6 m = inst.Standing(h) ? inst.InstituteMultiplier(h, nodeRow) : Fx6.One;
        Fx6 research = w.Knowledge.ResearchFactor(actor, nodeRow);
        Fx6 months = w.Knowledge.EffectiveMonths(actor, nodeRow, month);
        Fx6 c = w.Knowledge.CEff(actor, nodeRow, month);
        Fx6 prog = w.Knowledge.Prog(actor, nodeRow);
        Fx6 left = prog >= Fx6.One ? Fx6.Zero : Fx6.One - prog;
        var list = ImmutableArray.CreateBuilder<FocusQuoteView>(3);
        for (int level = 0; level < 3; level++)
        {
            Fx6 speed = SlotMath.Speed(p, m, level, research);
            Fx6 flow = SlotMath.Rate(p, c, speed, level, months);
            Fx6 left2 = left * months / speed;
            list.Add(new FocusQuoteView(level, left2, flow, flow * left2));
        }
        return list.MoveToImmutable();
    }

    // The unknown nodes the goal needs, in the order of their requirements (a node after its requirements). A group of alternatives takes the member that is cheapest in time.
    private static List<int> Closure(SliceWorld w, int actor, int goalRow)
    {
        var order = new List<int>();
        var state = new Dictionary<int, byte>();   // lookup only, never enumerated
        var stack = new Stack<(int Row, int Group)>();
        var req = w.Graph.Requires;
        if (w.Knowledge.IsKnown(actor, goalRow))
            return order;
        stack.Push((goalRow, req.GroupStart[goalRow]));
        state[goalRow] = 1;
        while (stack.Count > 0)
        {
            (int row, int g) = stack.Pop();
            if (g < req.GroupStart[row + 1])
            {
                stack.Push((row, g + 1));
                bool satisfied = false;
                int pick = -1;
                foreach (int m in req.GroupMembers(g))
                {
                    satisfied |= w.Knowledge.IsKnown(actor, m);
                    if (pick < 0 || w.Graph.Number[m] < w.Graph.Number[pick] && w.Knowledge.EffectiveMonths(actor, m, 0) <= w.Knowledge.EffectiveMonths(actor, pick, 0))
                        pick = m;
                }
                if (!satisfied && !state.ContainsKey(pick))
                {
                    state[pick] = 1;
                    stack.Push((pick, req.GroupStart[pick]));
                }
                continue;
            }
            order.Add(row);
        }
        return order;
    }

    private static List<int> AllMembers(SliceWorld w, int row)
    {
        var list = new List<int>();
        var req = w.Graph.Requires;
        for (int g = req.GroupStart[row]; g < req.GroupStart[row + 1]; g++)
        {
            foreach (int m in req.GroupMembers(g))
                list.Add(m);
        }
        return list;
    }

    public static PathView Path(SliceWorld w, int actor, int goalRow, long month)
    {
        List<int> nodes = Closure(w, actor, goalRow);
        InstituteSystem inst = w.Institutes!;
        int slots = 0;
        for (int k = 0; k < inst.Layout.SlotCount(actor); k++)
            slots += inst.Standing(new InstituteHandle(actor, k)) ? 1 : 0;
        slots = Math.Max(1, slots);
        var view = ImmutableArray.CreateBuilder<PathNodeView>(nodes.Count);
        var months = new Fx6[nodes.Count];
        Fx6 sum = Fx6.Zero;
        for (int i = 0; i < nodes.Count; i++)
        {
            int r = nodes[i];
            Fx6 prog = w.Knowledge.Prog(actor, r);
            Fx6 left = prog >= Fx6.One ? Fx6.Zero : Fx6.One - prog;
            Fx6 best = Fx6.Zero;
            for (int k = 0; k < inst.Layout.SlotCount(actor); k++)
            {
                var h = new InstituteHandle(actor, k);
                if (!inst.Standing(h))
                    continue;
                Fx6 speed = SlotMath.Speed(inst.SlotParameters, inst.InstituteMultiplier(h, r), 0, w.Knowledge.ResearchFactor(actor, r));
                best = speed > best ? speed : best;
            }
            months[i] = best == Fx6.Zero ? Fx6.Zero : left * w.Knowledge.EffectiveMonths(actor, r, month) / best;
            sum += months[i];
            view.Add(new PathNodeView(w.Graph.Number[r], months[i], left * w.Knowledge.CEff(actor, r, month), w.Knowledge.InFrontier(actor, r)));
        }
        // Greedy schedule on the standing institutes (nodes in requirement order, each on the earliest free one, after its requirements); the lower bound is the longest chain or the work over the slots.
        var finish = new Fx6[nodes.Count];
        var free = new Fx6[slots];
        Fx6 longest = Fx6.Zero, makespan = Fx6.Zero;
        int longestEnd = 0;
        for (int i = 0; i < nodes.Count; i++)
        {
            Fx6 ready = Fx6.Zero;
            foreach (int m in AllMembers(w, nodes[i]))
            {
                int j = nodes.IndexOf(m);
                if (j >= 0 && finish[j] > ready)
                    ready = finish[j];
            }
            int slot = 0;
            for (int k = 1; k < slots; k++)
                slot = free[k] < free[slot] ? k : slot;
            Fx6 start = free[slot] > ready ? free[slot] : ready;
            finish[i] = start + months[i];
            free[slot] = finish[i];
            makespan = finish[i] > makespan ? finish[i] : makespan;
            Fx6 chain = Fx6.Zero;
            foreach (int m in AllMembers(w, nodes[i]))
            {
                int j = nodes.IndexOf(m);
                if (j >= 0 && finish[j] > chain)
                    chain = finish[j];
            }
            if (finish[i] - start + chain > longest)
            {
                longest = finish[i] - start + chain;
                longestEnd = w.Graph.Number[nodes[i]];
            }
        }
        Fx6 bound = sum / Fx6.FromInt(slots);
        Fx6 min = longest > bound ? longest : bound;
        return new PathView(view.MoveToImmutable(), min, makespan > min ? makespan : min, longestEnd);
    }

    /// <summary>
    /// A layout of the path over the standing institutes for the player to confirm (<c>ApplyQueues</c>): nodes in requirement order, each to the institute where it ends first
    /// (the overlap makes it faster), at most the list length per institute; nodes that do not fit are left out.
    /// </summary>
    public static ImmutableArray<QueuePlan> Suggest(SliceWorld w, int actor, int goalRow, long month)
    {
        List<int> nodes = Closure(w, actor, goalRow);
        InstituteSystem inst = w.Institutes!;
        int slots = inst.Layout.SlotCount(actor);
        var lists = new List<int>[slots];
        var free = new Fx6[slots];
        for (int k = 0; k < slots; k++)
            lists[k] = [];
        foreach (int r in nodes)
        {
            int best = -1;
            Fx6 bestEnd = Fx6.Zero;
            Fx6 prog = w.Knowledge.Prog(actor, r);
            Fx6 left = prog >= Fx6.One ? Fx6.Zero : Fx6.One - prog;
            for (int k = 0; k < slots; k++)
            {
                var h = new InstituteHandle(actor, k);
                if (!inst.Standing(h) || lists[k].Count >= inst.ListLength)
                    continue;
                Fx6 speed = SlotMath.Speed(inst.SlotParameters, inst.InstituteMultiplier(h, r), 0, w.Knowledge.ResearchFactor(actor, r));
                Fx6 end = free[k] + left * w.Knowledge.EffectiveMonths(actor, r, month) / speed;
                if (best < 0 || end < bestEnd)
                {
                    best = k;
                    bestEnd = end;
                }
            }
            if (best < 0)
                continue;
            lists[best].Add(w.Graph.Number[r]);
            free[best] = bestEnd;
        }
        var plan = ImmutableArray.CreateBuilder<QueuePlan>();
        for (int k = 0; k < slots; k++)
        {
            if (lists[k].Count > 0)
                plan.Add(new QueuePlan(k, [.. lists[k]]));
        }
        return plan.ToImmutable();
    }
}
