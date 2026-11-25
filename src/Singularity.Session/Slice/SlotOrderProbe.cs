using Singularity.Core.Institutes;

namespace Singularity.Session;

/// <summary>What the player's slot orders ask the world: the snapshot between ticks and the institutes' own decision of the month.</summary>
internal sealed class SlotOrderProbe(SliceWorld world) : ISlotOrderProbe
{
    public bool IsKnown(int actor, int nodeNumber) => world.Graph.RowOf(nodeNumber) is not (>= 0 and int row) || world.Knowledge.IsKnown(actor, row);

    public int ActiveNode(int actor, int slot, long month)
    {
        InstituteSystem institutes = world.Institutes!;
        int slots = institutes.Layout.SlotCount(actor);
        Span<int> node = stackalloc int[slots];
        Span<byte> focus = stackalloc byte[slots];
        institutes.Resolve(actor, month, node, focus);
        return node[slot] > 0 ? node[slot] : 0;
    }
}
