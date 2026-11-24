using Singularity.Core.Knowledge;
using Singularity.Kernel;

namespace Singularity.Core.Institutes;

/// <summary>What the institutes read from the other layers: pure functions of the snapshot.</summary>
public interface IInstituteInputs
{
    /// <summary>The weight of the node for the actor in month t (the AI picks the heaviest frontier node as the goal).</summary>
    Fx6 Priority(int actorRow, int nodeRow, long month);

    /// <summary>The node is known to the actor (snapshot).</summary>
    bool IsKnown(int actorRow, int nodeRow);

    /// <summary>The node is unknown and its requirements are known (snapshot).</summary>
    bool InFrontier(int actorRow, int nodeRow);

    /// <summary>The node was opened by the own research of the actor, not by a licence or a grant (snapshot).</summary>
    bool DiscoveredByResearch(int actorRow, int nodeRow);

    /// <summary>The node number of the order of the player for the slot, 0 = none (snapshot).</summary>
    int OrderNode(InstituteHandle slot);
}

/// <summary>A broken institute invariant.</summary>
public sealed class InstituteInvariantException(long month, int actorRow, string message)
    : Exception($"institute invariant violated at month {month}, actor row {actorRow}: {message}");

/// <summary>
/// The institutes of the actors (research-institutes-priorities rev 4 §3, §4, §7). State per actor and slot, columns of the actors table grouped by slot:
/// <c>inst.order.k</c> / <c>inst.stamp.k</c> (the node the player gave the slot and the month: written only by <see cref="Order"/> between ticks),
/// <c>inst.ai.k</c> (the goal the AI chose), <c>inst.cur.k</c> (the goal seen last month, to credit its completion once) and
/// <c>inst.exp.k.discipline</c> (experience; written only by this system). One writer per column.
/// A slot works on its goal: the order of the player while that node is unknown, else the choice of the AI. A completed goal gives the institute one point of
/// experience in the area of the node (also an off-profile institute, #488); the level follows the thresholds proportional to the nodes of the area.
/// There are no losses of experience and no closing of institutes in v0.
/// </summary>
public sealed class InstituteSystem : ITickSystem
{
    public const int DefaultSystemId = 600;

    /// <summary>Most slots one actor may have (the stack buffers of the step).</summary>
    public const int MaxSlotsLimit = 16;

    private readonly InstituteLayout _l;
    private readonly InstituteParams _p;
    private readonly KnowledgeGraph _g;
    private readonly IInstituteInputs _inputs;
    private readonly IntColumn[] _ai, _cur;
    private readonly IntColumn[][] _exp;

    public InstituteSystem(Table table, InstituteLayout layout, InstituteParams parameters, KnowledgeGraph graph,
        IInstituteInputs inputs, int systemId = DefaultSystemId)
    {
        parameters.Validate();
        if (layout.MaxSlots > MaxSlotsLimit)
            throw new ArgumentException($"At most {MaxSlotsLimit} institutes per actor, the data has {layout.MaxSlots}.", nameof(layout));
        if (table.RowCount != layout.ActorCount)
            throw new ArgumentException("The layout does not match the table.", nameof(table));
        _l = layout;
        _p = parameters;
        _g = graph;
        _inputs = inputs;
        SystemId = systemId;
        int d = layout.DisciplineNames.Length;
        _ai = new IntColumn[layout.MaxSlots];
        _cur = new IntColumn[layout.MaxSlots];
        _exp = new IntColumn[layout.MaxSlots][];
        for (int k = 0; k < layout.MaxSlots; k++)
        {
            _ai[k] = table.AddInt($"inst.ai.{k}");
            _cur[k] = table.AddInt($"inst.cur.{k}");
            _exp[k] = new IntColumn[d];
            for (int disc = 0; disc < d; disc++)
            {
                _exp[k][disc] = table.AddInt($"inst.exp.{k}.{layout.DisciplineNames[disc]}");
                var column = new int[layout.ActorCount];
                for (int a = 0; a < layout.ActorCount; a++)
                    column[a] = k < layout.SlotCount(a) ? layout.StartExperience(new InstituteHandle(a, k), disc) : 0;
                _exp[k][disc].Load(column);
            }
        }
    }

    public int SystemId { get; }

    public Cadence Cadence => Cadence.Monthly;

    public InstituteLayout Layout => _l;

    public InstituteParams Parameters => _p;

    // ---- reads of the snapshot

    /// <summary>Experience of the institute in the discipline.</summary>
    public int Experience(InstituteHandle h, int discipline) => _exp[h.Slot][discipline].Current[h.Actor];

    /// <summary>Level 0..4 of the institute in the discipline (0 = no bonus, the area is hidden in the card, #489).</summary>
    public int Level(InstituteHandle h, int discipline) => InstituteMath.LevelOf(Experience(h, discipline), _l.NodesIn[discipline], _p);

    /// <summary>The node number the slot works on now (the order of the player while unknown, else the AI choice), or 0 when the slot is free.</summary>
    public int GoalNode(InstituteHandle h)
    {
        int order = _inputs.OrderNode(h);
        if (order != 0 && !IsKnownNumber(h.Actor, order))
            return order;
        int ai = _ai[h.Slot].Current[h.Actor];
        return ai != 0 && !IsKnownNumber(h.Actor, ai) ? ai : 0;
    }

    /// <summary>
    /// Goals of the actor for the knowledge layer: node rows in slot order and the speed multiplier of each (<c>1 + bonus·level</c> of the institute in the area
    /// of the node). Returns the count; fills at most the length of the spans. A pure read of the snapshot, no allocation.
    /// </summary>
    public int Goals(int actor, Span<int> nodeRows, Span<Fx6> multipliers)
    {
        int n = 0;
        for (int k = 0; k < _l.SlotCount(actor) && n < nodeRows.Length; k++)
        {
            var h = new InstituteHandle(actor, k);
            int number = GoalNode(h);
            if (number == 0)
                continue;
            int row = _g.RowOf(number);
            if (row < 0)
                continue;
            int disc = _g.Discipline[row];
            nodeRows[n] = row;
            multipliers[n] = InstituteMath.LevelMultiplier(disc < 0 ? 0 : Level(h, disc), _p);
            n++;
        }
        return n;
    }

    private bool IsKnownNumber(int actor, int number)
    {
        int row = _g.RowOf(number);
        return row < 0 || _inputs.IsKnown(actor, row);
    }

    // ---- step

    public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd)
    {
        long t = (context.Calendar ?? throw new InvalidOperationException("The institutes are monthly and need a runner calendar.")).MonthIndex(context.Tick);
        Span<int> goal = stackalloc int[MaxSlotsLimit];
        for (int a = rowStart; a < rowEnd; a++)
        {
            int slots = _l.SlotCount(a);
            bool anyFree = false;
            for (int k = 0; k < slots; k++)
            {
                var h = new InstituteHandle(a, k);
                goal[k] = GoalNode(h);
                anyFree |= goal[k] == 0;
                // A goal seen last month that is known now was completed: one point in its area.
                int cur = _cur[k].Current[a];
                if (cur != 0 && IsKnownNumber(a, cur) && _g.RowOf(cur) is >= 0 and int row && _inputs.DiscoveredByResearch(a, row) && _g.Discipline[row] is >= 0 and int disc)
                    _exp[k][disc].Next[a] = _exp[k][disc].Current[a] + 1;
            }
            if (anyFree)
                PickForFreeSlots(a, t, slots, goal);
            for (int k = 0; k < slots; k++)
            {
                _cur[k].Next[a] = goal[k];
                if (goal[k] != 0 && goal[k] != GoalNode(new InstituteHandle(a, k)))
                    _ai[k].Next[a] = goal[k];
            }
        }
    }

    // The AI rule (§7): the heaviest frontier node that no slot works on goes to the free institute with the highest level in its area (ties: the lower slot).
    private void PickForFreeSlots(int a, long t, int slots, Span<int> goal)
    {
        while (true)
        {
            bool free = false;
            for (int k = 0; k < slots; k++)
                free |= goal[k] == 0;
            if (!free)
                return;
            int best = -1;
            Fx6 bestWeight = Fx6.Zero;
            foreach (int r in _g.Order)
            {
                if (!_inputs.InFrontier(a, r) || IsTaken(goal, slots, _g.Number[r]))
                    continue;
                Fx6 w = _inputs.Priority(a, r, t);
                if (w > bestWeight)
                {
                    best = r;
                    bestWeight = w;
                }
            }
            if (best < 0)
                return;
            int disc = _g.Discipline[best];
            int pick = -1, pickLevel = -1;
            for (int k = 0; k < slots; k++)
            {
                if (goal[k] != 0)
                    continue;
                int level = disc < 0 ? 0 : Level(new InstituteHandle(a, k), disc);
                if (level > pickLevel)
                {
                    pick = k;
                    pickLevel = level;
                }
            }
            goal[pick] = _g.Number[best];
        }
    }

    private static bool IsTaken(ReadOnlySpan<int> goal, int slots, int number)
    {
        for (int k = 0; k < slots; k++)
        {
            if (goal[k] == number)
                return true;
        }
        return false;
    }

    public void Merge(in TickContext context, int chunkCount)
    {
    }

    public void Post(in TickContext context)
    {
    }
}
