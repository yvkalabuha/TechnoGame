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

    /// <summary>The share of the planned research money the actor really funds, 0..1 (snapshot): a project advances by this part of a month, so a project without money does not finish.</summary>
    Fx6 BuildFunding(int actorRow);

    /// <summary>
    /// The path of the actor to the next generation of the product (ai-institute-goals.md §2), a pure function of the snapshot: per node row,
    /// 1 = unknown and needed for the next generation g* the actor cannot yet compose, 0 = not (g*+1 is not computed: the PATH rule does not use it).
    /// <paramref name="tier"/> has one entry per node row and is zeroed by the caller.
    /// </summary>
    void GoalPath(int actorRow, long month, Span<byte> tier);

    // ---- slot model (SCIENCE_SLOTS.md); defaults keep the old model's inputs unchanged

    /// <summary>Entry <paramref name="index"/> (0 = the active one, 1..5 the queue) of the player's list of the slot: a node number, 0 = empty (snapshot).</summary>
    int QueueEntry(InstituteHandle slot, int index) => 0;

    /// <summary>The focus the player bought for the slot: the level 0..2 and the node number it was bought for (0 = none); the month it was set, −1 = never.</summary>
    int FocusOrder(InstituteHandle slot, out int level, out long month)
    {
        level = 0;
        month = -1;
        return 0;
    }

    /// <summary>The node number the slot worked on in the last step and the progress it added (0 = none).</summary>
    int SlotProgress(int actorRow, int slot, out Fx6 added)
    {
        added = Fx6.Zero;
        return 0;
    }

    /// <summary>Progress of the node for the actor (snapshot).</summary>
    Fx6 Progress(int actorRow, int nodeRow) => Fx6.Zero;

    /// <summary>A hazard node at its progress cap, which left its slot (O-092).</summary>
    bool Released(int actorRow, int nodeRow) => false;

    /// <summary>Own air power over the best rival's (#565); 1 when there is none to compare.</summary>
    Fx6 AirPowerRatio(int actorRow, long month) => Fx6.One;

    /// <summary>Share of the research money the slots left unused in the last step (returned to the budget).</summary>
    Fx6 SurplusShare(int actorRow) => Fx6.Zero;

    /// <summary><c>T_n</c> of the node in months.</summary>
    Fx6 NodeMonths(int nodeRow) => Fx6.One;
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

    /// <summary>Slot model (#574): the upkeep of the institutes, this one included, would pass its share of the research money.</summary>
    UpkeepCeiling,
}

/// <summary>
/// The institutes of the actors (research-institutes-priorities rev 4 §3, §4, §7, §8). State per actor and slot, columns of the actors table grouped by slot:
/// <c>inst.order.k</c> / <c>inst.stamp.k</c> / <c>inst.build.k</c> (the orders of the player: written only by <see cref="InstituteOrders"/> between ticks),
/// <c>inst.ai.k</c> (the goal the AI chose), <c>inst.abuild.k</c> (the start of a project the AI ordered), <c>inst.cur.k</c> (the goal seen last month, to credit
/// its completion once), <c>inst.done.k</c> (month + 1 the institute opened, 1 for those standing at the start, 0 = not built) and
/// <c>inst.prog.k</c> (raw Fx6 months of a running project paid so far) and <c>inst.exp.k.discipline</c> (experience); the last six are written only by this system. One writer per column.
/// A slot works on its goal: the order of the player while that node is unknown, else the choice of the AI. A completed goal found by the own research gives
/// the institute one point of experience in the area of the node (also an off-profile institute, #488). A project takes a share of the research money while it
/// runs; a built institute adds capacity and takes upkeep (<see cref="BudgetFactor"/>); the institutes of 1945 are in the base money. No losses of experience
/// and no closing of institutes in v0.
/// </summary>
public sealed partial class InstituteSystem : ITickSystem
{
    public const int DefaultSystemId = 700;   // not 600: the intelligence system has it, and the write trace tells systems apart by this id

    /// <summary>Most slots one actor may have (the stack buffers of the step).</summary>
    public const int MaxSlotsLimit = 16;

    private readonly InstituteLayout _l;
    private readonly InstituteParams _p;
    private readonly KnowledgeGraph _g;
    private readonly IInstituteInputs _inputs;
    private readonly IntColumn[] _ai, _cur, _done, _abuild, _prog;
    private readonly IntColumn[][] _exp;

    public InstituteSystem(Table table, InstituteLayout layout, InstituteParams parameters, KnowledgeGraph graph,
        IInstituteInputs inputs, int systemId = DefaultSystemId, SlotParams? slots = null)
    {
        parameters.Validate();
        slots?.Validate();
        _slots = slots;
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
        _prog = new IntColumn[layout.MaxSlots];
        _exp = new IntColumn[layout.MaxSlots][];
        for (int k = 0; k < layout.MaxSlots; k++)
        {
            _ai[k] = table.AddInt($"inst.ai.{k}");
            _cur[k] = table.AddInt($"inst.cur.{k}");
            _abuild[k] = table.AddInt($"inst.abuild.{k}");
            _prog[k] = table.AddInt($"inst.prog.{k}");
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
        if (slots is not null)
            InitSlots(table);
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
    public Fx6 BuildRate(InstituteHandle h, long month) =>
        InstituteMath.BuildRate(_l.TotalLevels(h), _l.RefYear(h) - _l.YearOf(ExpectedOpening(h, month)), _p);

    /// <summary>
    /// Month the running project opens if it keeps the funding of the snapshot (step of <paramref name="month"/> included): the stretched project pays the price of this
    /// year, not of the year it was started for, and the price follows the funding when it changes. Without funding it never opens: the price is the one of the historical year.
    /// </summary>
    public long ExpectedOpening(InstituteHandle h, long month)
    {
        Fx6 funding = _inputs.BuildFunding(h.Actor);
        long left = BuildMonths(h) * (long)Fx6.One.Raw - _prog[h.Slot].Current[h.Actor];
        if (funding <= Fx6.Zero)
            return _l.FirstMonthOf(_l.RefYear(h));   // never opens: the price of opening on time (x1), no overflow of the year
        long steps = (left + funding.Raw - 1) / funding.Raw;
        return month + (steps < 1 ? 1 : steps) - 1;
    }

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
        if (_slots is not null && UpkeepAfter(h) > _slots.UpkeepCeilingShare)
            return BuildCheck.UpkeepCeiling;
        return BuildCheck.Ok;
    }

    /// <summary>
    /// The factor on the research money of the actor: <c>(1 − running projects − upkeep of built institutes)·(1 + capacity of built institutes)</c>; exactly 1
    /// without projects (the institutes of the start are in the base money).
    /// </summary>
    public Fx6 BudgetFactor(int actor, long month)
    {
        Fx6 take = Take(actor, month, out Fx6 give, out bool any);
        if (!any)
            return Fx6.One;
        Fx6 keep = Fx6.One - take;
        return keep > Fx6.Zero ? keep * (Fx6.One + give) : Fx6.Zero;
    }

    // The sum of the shares of the research money that running projects and upkeep take, and the capacity of the built institutes.
    private Fx6 Take(int actor, long month, out Fx6 give, out bool any)
    {
        Fx6 take = Fx6.Zero;
        give = Fx6.Zero;
        any = false;
        for (int k = 0; k < _l.SlotCount(actor); k++)
        {
            var h = new InstituteHandle(actor, k);
            if (BuiltByProject(h))
            {
                int levels = BuiltLevels(h);
                take += _p.UpkeepPerLevel * Fx6.FromInt(levels);
                give += _p.CapacityPerLevel * Fx6.FromInt(levels);
                any = true;
            }
            else if (Building(h))
            {
                take += BuildRate(h, month);
                any = true;
            }
        }
        return take;
    }

    /// <summary>Sum of the current levels of a built institute (they grow with experience; at the opening they equal the levels of the data).</summary>
    private int BuiltLevels(InstituteHandle h)
    {
        int sum = 0;
        for (int d = 0; d < _l.DisciplineNames.Length; d++)
            sum += Level(h, d);
        return sum;
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
        if (_slots is not null)
        {
            ComputeSlots(t, rowStart, rowEnd);
            return;
        }
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
                var h = new InstituteHandle(a, k);
                int working = GoalNode(h);
                // Credit only a goal the slot worked on during this tick (the one of the snapshot); a goal picked now acts from the next tick.
                _cur[k].Next[a] = working;
                if (goal[k] > 0 && goal[k] != working)
                    _ai[k].Next[a] = goal[k];
                else if (working != 0 && working != _ai[k].Current[a] && _ai[k].Current[a] != 0)
                    _ai[k].Next[a] = 0;   // a player order took the slot: the old AI choice must not come back and duplicate another slot's goal
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
            // A month advances the project by the funded part of it; no money, no progress (the term stretches, rev 4 §8).
            // Safety rule: if the projects and the upkeep ever take more than all the money, every project slows by min(1, 1/sum of the shares).
            Fx6 funding = _inputs.BuildFunding(h.Actor) * InstituteMath.OverloadScale(Take(h.Actor, t, out _, out _));
            long paid = _prog[h.Slot].Current[h.Actor] + funding.Raw;
            if (paid < months * (long)Fx6.One.Raw)
            {
                _prog[h.Slot].Next[h.Actor] = checked((int)paid);
                return;
            }
            _prog[h.Slot].Next[h.Actor] = 0;
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

    // The AI rule (§7 with the path rule PATH of ai-institute-goals.md §2, #559): of the frontier nodes on the path to the next product generation, the heaviest
    // that no slot works on goes to the free institute with the highest level in its area (ties: the lower slot). No node of the path in the frontier: the slot has no goal.
    private void PickForFreeSlots(int a, long t, int slots, Span<int> goal)
    {
        Span<byte> tier = _g.Count <= 512 ? stackalloc byte[_g.Count] : new byte[_g.Count];   // heap only above 512 nodes, as in KnowledgeSystem
        tier.Clear();
        _inputs.GoalPath(a, t, tier);
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
                if (tier[r] != 1 || !_inputs.InFrontier(a, r) || IsTaken(goal, slots, _g.Number[r]))
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
