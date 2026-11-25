using Singularity.Core.Knowledge;
using Singularity.Kernel;

namespace Singularity.Core.Institutes;

/// <summary>
/// Slot science model of the institutes (SCIENCE_SLOTS.md §2, §4, §8): one standing institute works on one node. What a slot works on is decided by
/// <see cref="Resolve"/>, a pure function of the snapshot that the knowledge layer and this system both call, so the node that makes progress in a month is the one
/// the column `inst.act.k` holds afterwards. The player's lists (active + queue) and focus are columns written between ticks by <see cref="SlotOrders"/>; the AI
/// has no list: it picks for a free slot (PATH, then any frontier node) and sets its focus by the air-power ratio. Experience is credited in proportion to the
/// progress the slot made (O-089d).
/// </summary>
public sealed partial class InstituteSystem
{
    private readonly SlotParams? _slots;
    private IntColumn[] _act = [], _aif = [], _aifn = [];
    private LongColumn[][] _xf = [];

    private void InitSlots(Table table)
    {
        int d = _l.DisciplineNames.Length;
        _act = new IntColumn[_l.MaxSlots];
        _aif = new IntColumn[_l.MaxSlots];
        _aifn = new IntColumn[_l.MaxSlots];
        _xf = new LongColumn[_l.MaxSlots][];
        for (int k = 0; k < _l.MaxSlots; k++)
        {
            _act[k] = table.AddInt($"inst.act.{k}");
            _aif[k] = table.AddInt($"inst.aif.{k}");
            _aifn[k] = table.AddInt($"inst.aifn.{k}");
            _xf[k] = new LongColumn[d];
            for (int disc = 0; disc < d; disc++)
                _xf[k][disc] = table.AddLong($"inst.xf.{k}.{_l.DisciplineNames[disc]}");
        }
    }

    /// <summary>The slot model runs.</summary>
    public bool SlotsOn => _slots is not null;

    public SlotParams SlotParameters => _slots ?? throw new InvalidOperationException("The slot model is off.");

    /// <summary>Entries of a player's list: the active one and the queue.</summary>
    public int ListLength => (_slots ?? throw new InvalidOperationException("The slot model is off.")).QueueLength + 1;

    // The upkeep share of the built and building institutes plus this project.
    private Fx6 UpkeepAfter(InstituteHandle h)
    {
        Fx6 sum = _p.UpkeepPerLevel * Fx6.FromInt(_l.TotalLevels(h));
        for (int k = 0; k < _l.SlotCount(h.Actor); k++)
        {
            var o = new InstituteHandle(h.Actor, k);
            if (k == h.Slot)
                continue;
            if (BuiltByProject(o))
                sum += _p.UpkeepPerLevel * Fx6.FromInt(BuiltLevels(o));
            else if (Building(o))
                sum += _p.UpkeepPerLevel * Fx6.FromInt(_l.TotalLevels(o));
        }
        return sum;
    }

    /// <summary>
    /// Overlap level <c>Lx</c> of the institute with the node (weighted main direction, O-092): the levels of the institute in the directions of the node.
    /// 0 when the node has no direction in common; then <c>m_inst = 1</c> and the node goes at base speed (#572).
    /// </summary>
    public Fx6 Overlap(InstituteHandle h, int nodeRow)
    {
        var dirs = _g.Directions[nodeRow];
        Span<int> levels = stackalloc int[dirs.Length];
        for (int i = 0; i < dirs.Length; i++)
            levels[i] = Level(h, dirs[i]);
        return SlotMath.OverlapLevel(levels, _slots!.MainWeight);
    }

    /// <summary>The speed multiplier of the institute on the node: <c>1 + bonus · Lx</c>.</summary>
    public Fx6 InstituteMultiplier(InstituteHandle h, int nodeRow) => Fx6.One + _p.LevelBonus * Overlap(h, nodeRow);

    /// <summary>Directions the institute has (level above 0).</summary>
    public int DirectionCount(InstituteHandle h)
    {
        int n = 0;
        for (int d = 0; d < _l.DisciplineNames.Length; d++)
            n += Level(h, d) > 0 ? 1 : 0;
        return n;
    }

    // ---- the decision of the month

    private bool Workable(int a, int number, out int row)
    {
        row = _g.RowOf(number);
        return row >= 0 && _inputs.InFrontier(a, row) && !_inputs.Released(a, row);
    }

    private static bool Taken(ReadOnlySpan<int> node, int number)
    {
        for (int k = 0; k < node.Length; k++)
        {
            if (node[k] == number)
                return true;
        }
        return false;
    }

    private bool InAnyList(int a, int slots, int number)
    {
        for (int m = 0; m < slots; m++)
        {
            for (int j = 0; j <= _slots!.QueueLength; j++)
            {
                if (_inputs.QueueEntry(new InstituteHandle(a, m), j) == number)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// What each slot of the actor works on in the month and at which focus: <paramref name="node"/>[k] is a node number, 0 = free, −1 = not built.
    /// A pure function of the snapshot (the table state after the previous month and the player's columns). Order: keep a valid node; the player's list
    /// (first element in the frontier, else an unstarted node borrowed from another slot's queue, O-089c); the AI picks (PATH, then any node of the frontier).
    /// </summary>
    public void Resolve(int a, long t, Span<int> node, Span<byte> focus)
    {
        int slots = _l.SlotCount(a);
        bool player = _inputs.PlayerControlled(a);
        for (int k = 0; k < slots; k++)
        {
            var h = new InstituteHandle(a, k);
            node[k] = Standing(h) ? 0 : -1;
            focus[k] = 0;
        }
        // 1. a node the slot works on stays while it is workable (and, for the player, still in a list).
        for (int k = 0; k < slots; k++)
        {
            if (node[k] != 0)
                continue;
            int cur = _act[k].Current[a];
            if (cur != 0 && Workable(a, cur, out _) && !Taken(node, cur) && (!player || InAnyList(a, slots, cur)))
                node[k] = cur;
        }
        if (player)
        {
            for (int k = 0; k < slots; k++)
            {
                if (node[k] != 0)
                    continue;
                for (int j = 0; j <= _slots!.QueueLength && node[k] == 0; j++)
                {
                    int e = _inputs.QueueEntry(new InstituteHandle(a, k), j);
                    if (e != 0 && Workable(a, e, out _) && !Taken(node, e))
                        node[k] = e;
                }
            }
            for (int k = 0; k < slots; k++)
            {
                if (node[k] != 0)
                    continue;
                for (int m = 0; m < slots && node[k] == 0; m++)
                {
                    for (int j = 0; j <= _slots!.QueueLength && node[k] == 0; j++)
                    {
                        int e = _inputs.QueueEntry(new InstituteHandle(a, m), j);
                        if (m != k && e != 0 && Workable(a, e, out int row) && !Taken(node, e) && _inputs.Progress(a, row) == Fx6.Zero)
                            node[k] = e;
                    }
                }
            }
        }
        else
        {
            PickForFree(a, t, node);
        }
        for (int k = 0; k < slots; k++)
        {
            if (node[k] > 0)
                focus[k] = FocusOf(a, k, node[k], player);
        }
    }

    private byte FocusOf(int a, int k, int number, bool player)
    {
        var h = new InstituteHandle(a, k);
        if (player)
            return (byte)(_inputs.FocusOrder(h, out int level, out _) == number ? level : 0);
        return (byte)(_aifn[k].Current[a] == number ? _aif[k].Current[a] : 0);
    }

    // AI rule (SCIENCE_SLOTS §8): the heaviest node of the path to the next generation goes to the free institute with the largest overlap; with no path node left,
    // any node of the frontier by weight (a node without overlap runs at base speed and builds experience, #572).
    private void PickForFree(int a, long t, Span<int> node)
    {
        int slots = node.Length;
        bool free = false;
        for (int k = 0; k < slots; k++)
            free |= node[k] == 0;
        if (!free)
            return;
        Span<byte> tier = _g.Count <= 512 ? stackalloc byte[_g.Count] : new byte[_g.Count];
        tier.Clear();
        _inputs.GoalPath(a, t, tier);
        while (true)
        {
            int best = -1;
            Fx6 bestWeight = Fx6.Zero;
            for (int pass = 0; pass < 2 && best < 0; pass++)
            {
                foreach (int r in _g.Order)
                {
                    if ((pass == 0 && tier[r] != 1) || !_inputs.InFrontier(a, r) || _inputs.Released(a, r) || Taken(node, _g.Number[r]))
                        continue;
                    Fx6 w = _inputs.Priority(a, r, t);
                    if (w > bestWeight)
                    {
                        best = r;
                        bestWeight = w;
                    }
                }
            }
            if (best < 0)
                return;
            int pick = -1;
            Fx6 pickOverlap = Fx6.FromRaw(-1);
            for (int k = 0; k < slots; k++)
            {
                if (node[k] != 0)
                    continue;
                Fx6 o = Overlap(new InstituteHandle(a, k), best);
                if (o > pickOverlap)
                {
                    pick = k;
                    pickOverlap = o;
                }
            }
            if (pick < 0)
                return;
            node[pick] = _g.Number[best];
        }
    }

    /// <summary>The working slots of the month for the knowledge layer: node row, institute multiplier and focus, in slot order; returns the count.</summary>
    public int Work(int a, long t, Span<SlotWork> work)
    {
        int slots = _l.SlotCount(a);
        Span<int> node = stackalloc int[slots];
        Span<byte> focus = stackalloc byte[slots];
        Resolve(a, t, node, focus);
        int n = 0;
        for (int k = 0; k < slots && n < work.Length; k++)
        {
            if (node[k] <= 0)
                continue;
            int row = _g.RowOf(node[k]);
            work[n++] = new SlotWork(k, row, InstituteMultiplier(new InstituteHandle(a, k), row), focus[k]);
        }
        return n;
    }

    // ---- step

    private void ComputeSlots(long t, int rowStart, int rowEnd)
    {
        Span<int> node = stackalloc int[MaxSlotsLimit];
        Span<byte> focus = stackalloc byte[MaxSlotsLimit];
        for (int a = rowStart; a < rowEnd; a++)
        {
            int slots = _l.SlotCount(a);
            bool player = _inputs.PlayerControlled(a);
            Resolve(a, t, node[..slots], focus[..slots]);
            for (int k = 0; k < slots; k++)
            {
                var h = new InstituteHandle(a, k);
                if (!Standing(h))
                {
                    StepProject(h, t, player);
                    continue;
                }
                if (_act[k].Current[a] != node[k])
                    _act[k].Next[a] = node[k];
                Credit(a, k, h);
            }
            if (!player)
                StepAiFocus(a, t, slots, node[..slots]);
        }
    }

    // Experience in proportion to the progress the slot made last month (up to the node's first full unit), split over the node's directions by weight.
    // An institute with all its directions cannot take a new one (#572): that part of the experience is not kept.
    private void Credit(int a, int k, InstituteHandle h)
    {
        int number = _inputs.SlotProgress(a, k, out Fx6 added);
        if (number == 0 || added <= Fx6.Zero)
            return;
        int row = _g.RowOf(number);
        Fx6 after = _inputs.Progress(a, row);
        Fx6 before = after - added;
        Fx6 room = before >= Fx6.One ? Fx6.Zero : Fx6.One - before;
        Fx6 credit = added < room ? added : room;
        if (credit <= Fx6.Zero)
            return;
        var dirs = _g.Directions[row];
        bool full = DirectionCount(h) >= _slots!.MaxDirections;
        for (int i = 0; i < dirs.Length; i++)
        {
            int d = dirs[i];
            if (full && Level(h, d) == 0)
                continue;
            Fx6 add = credit * SlotMath.DirectionWeight(i, dirs.Length, _slots.MainWeight);
            long frac = _xf[k][d].Current[a] + add.Raw;
            int whole = (int)(frac / Fx6.One.Raw);
            _xf[k][d].Next[a] = frac - whole * (long)Fx6.One.Raw;
            if (whole > 0)
                _exp[k][d].Next[a] = _exp[k][d].Current[a] + whole;
        }
    }

    // The AI buys focus when its air power is behind (SCIENCE_SLOTS §8): level 1 below parity, level 2 well below, on the path node with the most time left,
    // only while the slots leave at least the margin of the money unspent; the focus is dropped as soon as the money no longer covers the asks.
    private void StepAiFocus(int a, long t, int slots, ReadOnlySpan<int> node)
    {
        int want = 0, bestSlot = -1;
        if (_inputs.SurplusShare(a) >= _slots!.AiFocusMargin)
        {
            Fx6 ratio = _inputs.AirPowerRatio(a, t);
            want = ratio < _slots.AiFocusRatio2 ? 2 : ratio < _slots.AiFocusRatio1 ? 1 : 0;
        }
        if (want > 0)
        {
            Span<byte> tier = _g.Count <= 512 ? stackalloc byte[_g.Count] : new byte[_g.Count];
            tier.Clear();
            _inputs.GoalPath(a, t, tier);
            Fx6 mostLeft = Fx6.Zero;
            for (int k = 0; k < slots; k++)
            {
                if (node[k] <= 0)
                    continue;
                int row = _g.RowOf(node[k]);
                if (tier[row] != 1)
                    continue;
                Fx6 left = (Fx6.One - _inputs.Progress(a, row)) * _inputs.NodeMonths(row);
                if (left > mostLeft)
                {
                    mostLeft = left;
                    bestSlot = k;
                }
            }
        }
        for (int k = 0; k < slots; k++)
        {
            int level = k == bestSlot ? want : 0;
            int number = k == bestSlot ? node[k] : 0;
            if (_aif[k].Current[a] != level)
                _aif[k].Next[a] = level;
            if (_aifn[k].Current[a] != number)
                _aifn[k].Next[a] = number;
        }
    }
}
