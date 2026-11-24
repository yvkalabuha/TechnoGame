using Singularity.Core.Commands;
using Singularity.Kernel;

namespace Singularity.Core.Institutes;

/// <summary>
/// The standing orders of the player to the institutes: <c>inst.order.k</c> (node number, 0 = none) and <c>inst.stamp.k</c> (month + 1 of the order) per slot.
/// A column of the entity itself, not a command queue (adr-draft-entity-tables, adr-draft-228 item 11): the order stays until its node is known.
/// Written only by <see cref="Apply"/> between ticks (the same way as the <c>cmd.*</c> columns); read by <see cref="InstituteSystem"/>.
/// </summary>
public sealed class InstituteOrders
{
    private readonly InstituteLayout _l;
    private readonly IntColumn[] _order, _stamp;
    private int[] _copy = [];

    public InstituteOrders(Table table, InstituteLayout layout)
    {
        _l = layout;
        _order = new IntColumn[layout.MaxSlots];
        _stamp = new IntColumn[layout.MaxSlots];
        for (int k = 0; k < layout.MaxSlots; k++)
        {
            _order[k] = table.AddInt($"inst.order.{k}");
            _stamp[k] = table.AddInt($"inst.stamp.{k}");
        }
    }

    public int OrderNode(InstituteHandle h) => _order[h.Slot].Current[h.Actor];

    /// <summary>The month the player gave the order, or −1 when there is none.</summary>
    public long OrderMonth(InstituteHandle h) => _order[h.Slot].Current[h.Actor] == 0 ? -1 : _stamp[h.Slot].Current[h.Actor] - 1;

    /// <summary>Writes the order of the command; the slot must exist (checked by the validator).</summary>
    public CommandVerdict Apply(SetPriorityNode command, long month)
    {
        if ((uint)command.Slot >= (uint)_l.SlotCount(command.Actor))
            return CommandVerdict.Refuse(CommandRefusal.BadSlot, $"actor {command.Actor} has no institute slot {command.Slot}");
        Set(_order[command.Slot], command.Actor, command.Node);
        Set(_stamp[command.Slot], command.Actor, checked((int)month) + 1);
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
