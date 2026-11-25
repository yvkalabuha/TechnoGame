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

    /// <summary>The month the player started the project of the slot, −1 = none (snapshot).</summary>
    long PlayerBuildMonth(InstituteHandle slot);

    /// <summary>The actor is the one the player controls: the AI neither sets its goals nor starts its projects, it only proposes (O-067, #479).</summary>
    bool PlayerControlled(int actorRow);
}

/// <summary>Why a project cannot start now.</summary>
public enum BuildCheck
{
    Ok = 0,
    AlreadyStanding,
    AlreadyBuilding,

    /// <summary>The window opens in <c>ref_year − window</c>.</summary>
    TooEarly,

    /// <summary>The project of a small country has more levels than the cap.</summary>
    SmallCountryCap,
}

/// <summary>
/// The institutes of the actors (research-institutes-priorities rev 4 §3, §4, §7, §8). State per actor and slot, columns of the actors table grouped by slot:
/// <c>inst.order.k</c> / <c>inst.stamp.k</c> / <c>inst.build.k</c> (the orders of the player: written only by <see cref="InstituteOrders"/> between ticks),
/// <c>inst.ai.k</c> (the goal the AI chose), <c>inst.abuild.k</c> (the start of a project the AI ordered), <c>inst.cur.k</c> (the goal seen last month, to credit
/// its completion once), <c>inst.done.k</c> (month + 1 the institute opened, 1 for those standing at the start, 0 = not built) and
/// <c>inst.exp.k.discipline</c> (experience); the last five are written only by this system. One writer per column.
/// A slot works on its goal: the order of the player while that node is unknown, else the choice of the AI. A completed goal found by the own research gives
/// the institute one point of experience in the area of the node (also an off-profile institute, #488). A project takes a share of the research money while it
/// runs; a built institute adds capacity and takes upkeep (<see cref="BudgetFactor"/>); the institutes of 1945 are in the base money. No losses of experience
/// and no closing of institutes in v0.
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
    private readonly IntColumn[] _ai, _cur, _done, _abuild;
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
        _done = new IntColumn[layout.MaxSlots];
        _abuild = new IntColumn[layout.MaxSlots];
        _exp = new IntColumn[layout.MaxSlots][];
        for (int k = 0; k < layout.MaxSlots; k++)
        {
            _ai[k] = table.AddInt($"inst.ai.{k}");
            _cur[k] = table.AddInt($"inst.cur.{k}");
            _abuild[k] = table.AddInt($"inst.abuild.{k}");
            _done[k] = table.AddInt($"inst.done.{k}");
            var standing = new int[layout.ActorCount];
            for (int a = 0; a < layout.ActorCount; a++)
                standing[a] = k < layout.StartingCount(a) ? 1 : 0;
            _done[k].Load(standing);
            _exp[k] = new IntColumn[d];
            for (int disc = 0; disc < d; disc++)
            {
                _exp[k][disc] = table.AddInt($"inst.exp.{k}.{layout.DisciplineNames[disc]}");
                var column = new int[layout.ActorCount];
                for (int a = 0; a < layout.ActorCount; a++)
                    column[a] = k < layout.StartingCount(a) ? layout.StartExperience(new InstituteHandle(a, k), disc) : 0;
                _exp[k][disc].Load(column);
            }
        }
    }

    public int SystemId { get; }

    public Cadence Cadence => Cadence.Monthly;

    public InstituteLayout Layout => _l;

    public InstituteParams Parameters => _p;

    // ---- reads of the snapshot

    /// <summary>The institute stands (it stood at the start or its project is finished).</summary>
    public bool Standing(InstituteHandle h) => _done[h.Slot].Current[h.Actor] != 0;

    /// <summary>The institute was built by a project: its capacity and upkeep are in the budget factor.</summary>
    public bool BuiltByProject(InstituteHandle h) => _done[h.Slot].Current[h.Actor] >= 2;

    /// <summary>Month the project of the slot started (the player's order first, else the AI's), or −1.</summary>
    public long BuildStart(InstituteHandle h)
    {
        long player = _inputs.PlayerBuildMonth(h);
        return player >= 0 ? player : _abuild[h.Slot].Current[h.Actor] - 1L;
    }

    /// <summary>A project of the slot runs.</summary>
    public bool Building(InstituteHandle h) => !Standing(h) && BuildStart(h) >= 0;

    /// <summary>Month the institute opened (a project's last month), or −1 when it does not stand or stood at the start.</summary>
    public long OpenedMonth(InstituteHandle h) => BuiltByProject(h) ? _done[h.Slot].Current[h.Actor] - 1L : -1;

    /// <summary>Months the project of the slot takes.</summary>
    public int BuildMonths(InstituteHandle h) => InstituteMath.BuildMonths(_l.TotalLevels(h), _p);

    /// <summary>
    /// Share of the research money the running project takes per month; the price grows with the years from the opening year of the project to the historical year
    /// (opening on time = ×1, O-067).
    /// </summary>
    public Fx6 BuildRate(InstituteHandle h, long startMonth) =>
        InstituteMath.BuildRate(_l.TotalLevels(h), _l.RefYear(h) - _l.YearOf(startMonth + BuildMonths(h) - 1), _p);

    /// <summary>Whether a project of the slot may start in the month (§8): not standing or running, inside the window, within the cap of a small country.</summary>
    public BuildCheck CanBuild(InstituteHandle h, long month)
    {
        if (Standing(h))
            return BuildCheck.AlreadyStanding;
        if (BuildStart(h) >= 0)
            return BuildCheck.AlreadyBuilding;
        if (_l.YearOf(month) < _l.RefYear(h) - _p.WindowYears)
            return BuildCheck.TooEarly;
        if (_l.IsSmallCountry(h.Actor) && _l.TotalLevels(h) > _p.SmallCountryLevelCap)
            return BuildCheck.SmallCountryCap;
        return BuildCheck.Ok;
    }

    /// <summary>
    /// The factor on the research money of the actor: <c>(1 − running projects − upkeep of built institutes)·(1 + capacity of built institutes)</c>; exactly 1
    /// without projects (the institutes of the start are in the base money).
    /// </summary>
    public Fx6 BudgetFactor(int actor)
    {
        Fx6 take = Fx6.Zero, give = Fx6.Zero;
        bool any = false;
        for (int k = 0; k < _l.SlotCount(actor); k++)
        {
            var h = new InstituteHandle(actor, k);
            if (BuiltByProject(h))
            {
                int levels = _l.TotalLevels(h);
                take += _p.UpkeepPerLevel * Fx6.FromInt(levels);
                give += _p.CapacityPerLevel * Fx6.FromInt(levels);
                any = true;
            }
            else if (Building(h))
            {
                take += BuildRate(h, BuildStart(h));
                any = true;
            }
        }
        if (!any)
            return Fx6.One;
        Fx6 keep = Fx6.One - take;
        return keep > Fx6.Zero ? keep * (Fx6.One + give) : Fx6.Zero;
    }

    /// <summary>Experience of the institute in the discipline.</summary>
    public int Experience(InstituteHandle h, int discipline) => _exp[h.Slot][discipline].Current[h.Actor];

    /// <summary>Level 0..4 of the institute in the discipline (0 = no bonus, the area is hidden in the card, #489).</summary>
    public int Level(InstituteHandle h, int discipline) => InstituteMath.LevelOf(Experience(h, discipline), _l.NodesIn[discipline], _p);

    /// <summary>The node number the slot works on now (the order of the player while unknown, else the AI choice), or 0 when the slot is free or not built.</summary>
    public int GoalNode(InstituteHandle h)
    {
        if (!Standing(h))
            return 0;
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
            bool player = _inputs.PlayerControlled(a);
            bool anyFree = false;
            for (int k = 0; k < slots; k++)
            {
                var h = new InstituteHandle(a, k);
                if (!Standing(h))
                {
                    goal[k] = -1;   // not built: no goal, never free
                    StepProject(h, t, player);
                    continue;
                }
                goal[k] = GoalNode(h);
                anyFree |= goal[k] == 0;
                // A goal seen last month that is known now was completed: one point in its area, if the own research found it.
                int cur = _cur[k].Current[a];
                if (cur != 0 && IsKnownNumber(a, cur) && _g.RowOf(cur) is >= 0 and int row && _inputs.DiscoveredByResearch(a, row) && _g.Discipline[row] is >= 0 and int disc)
                    _exp[k][disc].Next[a] = _exp[k][disc].Current[a] + 1;
            }
            if (anyFree && !player)
                PickForFreeSlots(a, t, slots, goal);
            for (int k = 0; k < slots; k++)
            {
                _cur[k].Next[a] = goal[k] > 0 ? goal[k] : 0;
                if (goal[k] > 0 && goal[k] != GoalNode(new InstituteHandle(a, k)))
                    _ai[k].Next[a] = goal[k];
            }
        }
    }

    // A project that has run its months opens the institute with the experience of its levels; the AI starts the projects of the historical ones so that they
    // open about the historical year (§7), by the same checks as the player.
    private void StepProject(InstituteHandle h, long t, bool player)
    {
        long start = BuildStart(h);
        int months = BuildMonths(h);
        if (start >= 0)
        {
            if (t - start + 1 < months)
                return;
            _done[h.Slot].Next[h.Actor] = checked((int)t) + 1;
            for (int d = 0; d < _l.DisciplineNames.Length; d++)
                _exp[h.Slot][d].Next[h.Actor] = _l.StartExperience(h, d);
            return;
        }
        // The project is seen (and pays) from the next month, so that month is its start: the stamp is start + 1.
        if (!player && AiWouldStart(h, t))
            _abuild[h.Slot].Next[h.Actor] = checked((int)t) + 2;
    }

    // The AI rule (§7): the project of a historical candidate starts so that the institute opens about its historical year.
    private bool AiWouldStart(InstituteHandle h, long t) =>
        CanBuild(h, t) == BuildCheck.Ok && t + 1 >= _l.FirstMonthOf(_l.RefYear(h)) - BuildMonths(h);

    /// <summary>
    /// What the AI would do for the actor in the month, as a proposal for the player (the adviser, #479): the goal for each free standing slot (0 = none) in
    /// <paramref name="goals"/> and, per slot, whether it would start the project now in <paramref name="startsProject"/>. A pure read; writes nothing.
    /// </summary>
    public void Propose(int actor, long month, Span<int> goals, Span<bool> startsProject)
    {
        int slots = _l.SlotCount(actor);
        Span<int> work = stackalloc int[MaxSlotsLimit];
        for (int k = 0; k < slots; k++)
        {
            var h = new InstituteHandle(actor, k);
            work[k] = Standing(h) ? GoalNode(h) : -1;
            startsProject[k] = !Standing(h) && AiWouldStart(h, month);
        }
        PickForFreeSlots(actor, month, slots, work);
        for (int k = 0; k < slots; k++)
            goals[k] = work[k] > 0 && GoalNode(new InstituteHandle(actor, k)) == 0 ? work[k] : 0;
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
