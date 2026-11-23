using System.Collections;

namespace Singularity.Session;

/// <summary>
/// The nodes of the knowledge graph as one actor sees them, in graph row order. A compact block instead of one object per node (TASK-0103 R1):
/// bit masks for <c>Known</c> and <c>ByLicence</c>, one <see cref="ProcessView"/> per process node, <c>KnownSince</c> only where it is tracked. A
/// <see cref="NodeView"/> is assembled by the indexer and is a value, so reading allocates nothing. Immutable once published; every array is below
/// the large-object limit, and the node numbers are shared by all actors.
/// </summary>
public readonly struct NodeList : IReadOnlyList<NodeView>, IEquatable<NodeList>
{
    private readonly NodeBlock? _block;

    internal NodeList(NodeBlock block) => _block = block;

    /// <summary>
    /// False for an actor the view does not carry the nodes of (not subscribed, ADR-0025): no block, <see cref="Count"/> 0, the indexer throws.
    /// An actor of the slice that is subscribed always has a present list, because the graph shape is common to all actors.
    /// </summary>
    public bool IsPresent => _block is not null;

    /// <summary>Identity of the block, for tests of the reuse (R2).</summary>
    internal object? BlockIdentity => _block;

    public int Count => _block?.Shape.Numbers.Length ?? 0;

    /// <summary>Same as <see cref="Count"/> (the type replaces an array).</summary>
    public int Length => Count;

    public NodeView this[int row]
    {
        get
        {
            NodeBlock b = _block ?? throw new InvalidOperationException("actor not subscribed");
            NodeShape sh = b.Shape;
            int p = sh.ProcessIndex[row];
            return new NodeView(sh.Numbers[row], Bit(b.Known, row), Bit(b.ByLicence, row), p < 0 ? null : b.Processes[p], b.KnownSince?[row]);
        }
    }

    private static bool Bit(ulong[] mask, int i) => (mask[i >> 6] & (1UL << (i & 63))) != 0;

    /// <summary>Builds a list from views (tests and tools); the live views are built by <c>ViewBuilder</c>.</summary>
    public static NodeList Create(ReadOnlySpan<NodeView> nodes)
    {
        var numbers = new int[nodes.Length];
        var processIndex = new int[nodes.Length];
        int processes = 0;
        bool since = false;
        for (int i = 0; i < nodes.Length; i++)
        {
            numbers[i] = nodes[i].Number;
            processIndex[i] = nodes[i].Process is null ? -1 : processes++;
            since |= nodes[i].KnownSince is not null;
        }
        var block = new NodeBlock(new NodeShape(numbers, processIndex, processIndex), since);
        for (int i = 0; i < nodes.Length; i++)
        {
            if (nodes[i].Known)
                block.Known[i >> 6] |= 1UL << (i & 63);
            if (nodes[i].ByLicence)
                block.ByLicence[i >> 6] |= 1UL << (i & 63);
            if (nodes[i].Process is { } pv)
                block.Processes[processIndex[i]] = pv;
            if (block.KnownSince is { } ks)
                ks[i] = nodes[i].KnownSince ?? 0;
        }
        return new NodeList(block);
    }

    public Enumerator GetEnumerator() => new(this);

    IEnumerator<NodeView> IEnumerable<NodeView>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Element-wise equality, so views compare by content as the arrays they replace did.</summary>
    public bool Equals(NodeList other)
    {
        if (ReferenceEquals(_block, other._block))
            return true;
        if (IsPresent != other.IsPresent)
            return false;   // "not subscribed" is not an empty list of nodes
        if (Count != other.Count)
            return false;
        for (int i = 0; i < Count; i++)
        {
            if (this[i] != other[i])
                return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is NodeList o && Equals(o);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(IsPresent);
        h.Add(Count);
        for (int i = 0; i < Count; i += 61)
            h.Add(this[i]);
        return h.ToHashCode();
    }

    public static bool operator ==(NodeList a, NodeList b) => a.Equals(b);

    public static bool operator !=(NodeList a, NodeList b) => !a.Equals(b);

    public struct Enumerator : IEnumerator<NodeView>
    {
        private readonly NodeList _list;
        private int _i;

        internal Enumerator(NodeList list)
        {
            _list = list;
            _i = -1;
        }

        public readonly NodeView Current => _list[_i];

        readonly object IEnumerator.Current => Current;

        public bool MoveNext() => ++_i < _list.Count;

        public void Reset() => _i = -1;

        public readonly void Dispose()
        {
        }
    }
}

/// <summary>What is common to the nodes of all actors of a world: the node numbers and where a node's <see cref="ProcessView"/> sits in a block. Built once.</summary>
internal sealed class NodeShape(int[] numbers, int[] processIndex, int[] layoutRow)
{
    public int[] Numbers { get; } = numbers;

    /// <summary>Node row → index into <see cref="NodeBlock.Processes"/>, or −1 for a node that is not a process.</summary>
    public int[] ProcessIndex { get; } = processIndex;

    /// <summary>Node row → row of the process in the industry layout, or −1; used by the builder only.</summary>
    public int[] LayoutRow { get; } = layoutRow;

    public int ProcessCount { get; } = processIndex.Count(i => i >= 0);
}

/// <summary>The nodes of one actor. Written only while built, then shared (the reuse of an unchanged block hands out the same one).</summary>
internal sealed class NodeBlock
{
    public NodeBlock(NodeShape shape, bool tracksKnownSince)
    {
        Shape = shape;
        int words = (shape.Numbers.Length + 63) >> 6;
        Known = new ulong[words];
        ByLicence = new ulong[words];
        Processes = new ProcessView[shape.ProcessCount];
        KnownSince = tracksKnownSince ? new long[shape.Numbers.Length] : null;
    }

    public NodeShape Shape { get; }

    public ulong[] Known { get; }

    public ulong[] ByLicence { get; }

    public ProcessView[] Processes { get; }

    public long[]? KnownSince { get; }
}

/// <summary>
/// The node blocks of the last snapshot and the state they were read from: the month of the snapshot (<c>next</c>) and the version stamp of the columns
/// (<see cref="SliceWorld.NodeStamp"/>). A snapshot of the same month and stamp takes the blocks as they are (R2, TASK-0103). Used by one thread.
/// Holds the blocks of the subscribed actors only (ADR-0025).
/// Invariant: the blocks also read <c>Knowledge.LastLicenceGrants</c>, which is not a column; it is rewritten only by the monthly merge, which changes the month of the key.
/// </summary>
internal sealed class NodeBlockCache
{
    private long _month = long.MinValue, _stamp;
    private NodeList[] _blocks = [];

    /// <summary>Starts a snapshot: keeps the blocks if the state is the one they were read from, else drops them.</summary>
    public void Begin(long month, long stamp, int actors, bool reuse)
    {
        if (reuse && _month == month && _stamp == stamp && _blocks.Length == actors)
            return;
        _blocks = new NodeList[actors];
        _month = reuse ? month : long.MinValue;
        _stamp = stamp;
    }

    public bool TryGet(int actor, out NodeList list)
    {
        list = _blocks[actor];
        return list.IsPresent;
    }

    public void Put(int actor, NodeList list) => _blocks[actor] = list;

    /// <summary>The cache holds blocks of subscribed actors only (ADR-0025 п. 8).</summary>
    public void Drop(int actor) => _blocks[actor] = default;
}
