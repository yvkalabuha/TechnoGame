using System.Collections.Immutable;
using Singularity.Core.Commands;
using Singularity.Kernel;

namespace Singularity.Core.Institutes;

/// <summary>What the orders of the player need from the world to judge a command: the state of the snapshot between ticks.</summary>
public interface ISlotOrderProbe
{
    /// <summary>The node number is known to the actor.</summary>
    bool IsKnown(int actor, int nodeNumber);

    /// <summary>The node number the slot works on in the month the command acts (0 = none).</summary>
    int ActiveNode(int actor, int slot, long month);
}

/// <summary>
/// The player's lists and focus of the slot model: `inst.q.k.j` (j = 0 the active node, 1..5 the queue; node numbers, 0 = empty), `inst.fl.k` / `inst.fn.k` /
/// `inst.fm.k` (focus level, the node it was bought for, month + 1) and `inst.fr.k` (paid from the reserve). Written only here, between ticks (the same way as
/// <see cref="InstituteOrders"/>), read by <see cref="InstituteSystem"/>. A list keeps its order; known nodes are dropped whenever a command touches it and are
/// skipped by every reader until then (S8). A node stands in at most one list of an actor.
/// </summary>
public sealed class SlotOrders
{
    private readonly InstituteLayout _l;
    private readonly SlotParams _p;
    private readonly ISlotOrderProbe _probe;
    private readonly IntColumn[][] _q;
    private readonly IntColumn[] _level, _node, _stamp, _reserve;
    private int[] _copy = [];

    public SlotOrders(Table table, InstituteLayout layout, SlotParams parameters, ISlotOrderProbe probe)
    {
        _l = layout;
        _p = parameters;
        _probe = probe;
        _q = new IntColumn[layout.MaxSlots][];
        _level = new IntColumn[layout.MaxSlots];
        _node = new IntColumn[layout.MaxSlots];
        _stamp = new IntColumn[layout.MaxSlots];
        _reserve = new IntColumn[layout.MaxSlots];
        for (int k = 0; k < layout.MaxSlots; k++)
        {
            _q[k] = new IntColumn[parameters.QueueLength + 1];
            for (int j = 0; j < _q[k].Length; j++)
                _q[k][j] = table.AddInt($"inst.q.{k}.{j}");
            _level[k] = table.AddInt($"inst.fl.{k}");
            _node[k] = table.AddInt($"inst.fn.{k}");
            _stamp[k] = table.AddInt($"inst.fm.{k}");
            _reserve[k] = table.AddInt($"inst.fr.{k}");
        }
    }

    public int Length => _p.QueueLength + 1;

    public int Entry(InstituteHandle h, int index) => _q[h.Slot][index].Current[h.Actor];

    /// <summary>The unknown entries of the list in order (the readers' view of the list).</summary>
    public int Entries(InstituteHandle h, Span<int> into)
    {
        int n = 0;
        for (int j = 0; j < Length; j++)
        {
            int e = Entry(h, j);
            if (e != 0 && !_probe.IsKnown(h.Actor, e))
                into[n++] = e;
        }
        return n;
    }

    /// <summary>The node stands in a list of the actor (any slot); known nodes do not count.</summary>
    public bool Holds(int actor, int node, out int slot)
    {
        for (int k = 0; k < _l.SlotCount(actor); k++)
        {
            for (int j = 0; j < Length; j++)
            {
                if (_q[k][j].Current[actor] == node)
                {
                    slot = k;
                    return !_probe.IsKnown(actor, node);
                }
            }
        }
        slot = -1;
        return false;
    }

    public int FocusLevel(InstituteHandle h) => _level[h.Slot].Current[h.Actor];

    public int FocusNode(InstituteHandle h) => _node[h.Slot].Current[h.Actor];

    /// <summary>Month the focus was set, −1 = never.</summary>
    public long FocusMonth(InstituteHandle h) => _stamp[h.Slot].Current[h.Actor] - 1L;

    public bool FocusFromReserve(InstituteHandle h) => _reserve[h.Slot].Current[h.Actor] != 0;

    private CommandVerdict CheckSlot(int actor, int slot) =>
        (uint)slot >= (uint)_l.SlotCount(actor) ? CommandVerdict.Refuse(CommandRefusal.BadSlot, $"actor {actor} has no institute slot {slot}") : CommandVerdict.Ok;

    private void Write(InstituteHandle h, ReadOnlySpan<int> list)
    {
        for (int j = 0; j < Length; j++)
        {
            int v = j < list.Length ? list[j] : 0;
            if (_q[h.Slot][j].Current[h.Actor] != v)
                Set(_q[h.Slot][j], h.Actor, v);
        }
    }

    /// <summary><c>assign_node</c>: the node goes to the end of the list of the slot (the active node when the list is empty).</summary>
    public CommandVerdict Apply(AssignNode c)
    {
        if (CheckSlot(c.Actor, c.Slot) is { Accepted: false } bad)
            return bad;
        var h = new InstituteHandle(c.Actor, c.Slot);
        if (Holds(c.Actor, c.Node, out _))
            return CommandVerdict.Refuse(CommandRefusal.NodeTaken, $"node {c.Node} stands in a list of actor {c.Actor} already");
        Span<int> list = stackalloc int[Length];
        int n = Entries(h, list);
        if (n >= Length)
            return CommandVerdict.Refuse(CommandRefusal.QueueFull, $"the list of slot {c.Slot} is full ({_p.QueueLength} queued and the active node)");
        list[n++] = c.Node;
        Write(h, list[..n]);
        return CommandVerdict.Ok;
    }

    /// <summary><c>unassign</c>: the node leaves the list; when it was the one being worked this is a replacement: the next entry takes over and the focus ends (§4.2).</summary>
    public CommandVerdict Apply(Unassign c)
    {
        if (CheckSlot(c.Actor, c.Slot) is { Accepted: false } bad)
            return bad;
        var h = new InstituteHandle(c.Actor, c.Slot);
        Span<int> list = stackalloc int[Length];
        int n = Entries(h, list);
        int at = list[..n].IndexOf(c.Node);
        if (at < 0)
            return CommandVerdict.Refuse(CommandRefusal.NotInList, $"node {c.Node} is not in the list of slot {c.Slot}");
        for (int i = at; i + 1 < n; i++)
            list[i] = list[i + 1];
        Write(h, list[..(n - 1)]);
        return CommandVerdict.Ok;
    }

    /// <summary><c>reorder_queue</c>: the new order is a permutation of the queued nodes (the active one stays first).</summary>
    public CommandVerdict Apply(ReorderQueue c, long month)
    {
        if (CheckSlot(c.Actor, c.Slot) is { Accepted: false } bad)
            return bad;
        var h = new InstituteHandle(c.Actor, c.Slot);
        Span<int> list = stackalloc int[Length];
        int n = Entries(h, list);
        if (c.Order.IsDefault || c.Order.Length != n)
            return CommandVerdict.Refuse(CommandRefusal.NotInList, $"the order names {(c.Order.IsDefault ? 0 : c.Order.Length)} nodes, the list of slot {c.Slot} holds {n}");
        for (int i = 0; i < n; i++)
        {
            if (list[..n].IndexOf(c.Order[i]) < 0 || c.Order.IndexOf(c.Order[i]) != i)
                return CommandVerdict.Refuse(CommandRefusal.NotInList, $"node {c.Order[i]} is not in the list of slot {c.Slot} or repeats");
        }
        c.Order.CopyTo(list);
        Write(h, list[..n]);
        return CommandVerdict.Ok;
    }

    /// <summary><c>set_priority_node</c> of the slot model: the node becomes the active one of the slot, the node it replaces leaves the list.</summary>
    public CommandVerdict Apply(SetPriorityNode c, long month)
    {
        if (CheckSlot(c.Actor, c.Slot) is { Accepted: false } bad)
            return bad;
        var h = new InstituteHandle(c.Actor, c.Slot);
        int active = _probe.ActiveNode(c.Actor, c.Slot, month);
        if (Holds(c.Actor, c.Node, out int where) && where != c.Slot)
        {
            var other = new InstituteHandle(c.Actor, where);
            Span<int> o = stackalloc int[Length];
            int on = Entries(other, o);
            int at = o[..on].IndexOf(c.Node);
            for (int i = at; i + 1 < on; i++)
                o[i] = o[i + 1];
            Write(other, o[..(on - 1)]);
        }
        Span<int> list = stackalloc int[Length + 1];
        int n = 0;
        list[n++] = c.Node;
        Span<int> old = stackalloc int[Length];
        int on2 = Entries(h, old);
        for (int i = 0; i < on2 && n < Length; i++)
        {
            if (old[i] != c.Node && old[i] != active)
                list[n++] = old[i];
        }
        Write(h, list[..n]);
        return CommandVerdict.Ok;
    }

    /// <summary><c>set_focus</c>: level 0 switches the focus off; 1 and 2 buy it for the node the slot works on now (the focus ends with the node).</summary>
    public CommandVerdict Apply(SetFocus c, long month)
    {
        if (CheckSlot(c.Actor, c.Slot) is { Accepted: false } bad)
            return bad;
        var h = new InstituteHandle(c.Actor, c.Slot);
        int active = _probe.ActiveNode(c.Actor, c.Slot, month);
        if (c.Level == 0)
        {
            Set(_level[c.Slot], c.Actor, 0);
            Set(_node[c.Slot], c.Actor, 0);
            return CommandVerdict.Ok;
        }
        if (active == 0)
            return CommandVerdict.Refuse(CommandRefusal.NoActiveNode, $"slot {c.Slot} works on no node: nothing to focus on");
        bool same = FocusNode(h) == active && FocusLevel(h) == c.Level;
        if (same && FocusFromReserve(h) == c.FromReserve)
            return CommandVerdict.Ok;
        long last = FocusMonth(h);
        if (FocusLevel(h) > 0 && FocusNode(h) == active && last >= 0 && month - last < _p.FocusRepeatMonths)
            return CommandVerdict.Refuse(CommandRefusal.FocusTooSoon, $"the focus of slot {c.Slot} was set in month {last}; the next change is allowed in month {last + _p.FocusRepeatMonths}");
        Set(_level[c.Slot], c.Actor, c.Level);
        Set(_node[c.Slot], c.Actor, active);
        Set(_stamp[c.Slot], c.Actor, checked((int)month) + 1);
        Set(_reserve[c.Slot], c.Actor, c.FromReserve ? 1 : 0);
        return CommandVerdict.Ok;
    }

    private void Set(IntColumn column, int row, int value)
    {
        if (_copy.Length != column.Length)
            _copy = new int[column.Length];
        column.Current.CopyTo(_copy);
        _copy[row] = value;
        column.Load(_copy);
    }
}
