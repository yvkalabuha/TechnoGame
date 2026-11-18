using Singularity.Kernel;

namespace Singularity.Core.WhyNot;

/// <summary>
/// Requires-closures of spec §5.6 (v0.15, W1) over an abstract graph: <c>cl(n)</c> is empty for a known node, else <c>n</c> and the closure of the
/// chosen member (least closure cost, ties by node number) of every requires-group without a known member. Pure; the graph is acyclic.
/// </summary>
internal sealed class NodeClosure(int count, Func<int, bool> known, Func<int, int[][]> groups, Func<int, Fx6> left, Func<int, int> number)
{
    private readonly bool[]?[] _memo = ClosureArena.TakeMemo(count);

    public bool[] OfNode(int row)
    {
        if (_memo[row] is { } done)
            return done;
        bool[] set = ClosureArena.TakeSet(count);
        if (!known(row))
        {
            set[row] = true;
            Require(set, groups(row), []);
        }
        _memo[row] = set;
        return set;
    }

    /// <summary>Closure of an alternative: the groups without a known member and the nodes that are unknown.</summary>
    public bool[] OfRequirements(int[][] alternativeGroups, IEnumerable<int> nodes)
    {
        bool[] set = ClosureArena.TakeSet(count);
        Require(set, alternativeGroups, nodes);
        return set;
    }

    private void Require(bool[] set, int[][] requirementGroups, IEnumerable<int> nodes)
    {
        foreach (int[] g in requirementGroups)
        {
            if (!g.Any(known))
                Union(set, OfNode(Choose(g)));
        }
        foreach (int n in nodes)
            Union(set, OfNode(n));
    }

    /// <summary>The member of a group with the least closure cost; ties by node number.</summary>
    public int Choose(int[] members)
    {
        int best = -1;
        Fx6 bestCost = Fx6.Zero;
        foreach (int m in members)
        {
            Fx6 cost = Cost(OfNode(m));
            if (best < 0 || cost < bestCost || cost == bestCost && number(m) < number(best))
            {
                best = m;
                bestCost = cost;
            }
        }
        return best;
    }

    /// <summary><c>cost(S) = Σ left(n)</c>: each node of the set once.</summary>
    public Fx6 Cost(bool[] set)
    {
        Fx6 sum = Fx6.Zero;
        for (int r = 0; r < set.Length; r++)
        {
            if (set[r])
                sum += left(r);
        }
        return sum;
    }

    /// <summary>The frontier node of the set with the least <c>left</c> (ties by node number), −1 if none.</summary>
    public int Nearest(bool[] set, Func<int, bool> frontier)
    {
        int best = -1;
        Fx6 bestCost = Fx6.Zero;
        for (int r = 0; r < set.Length; r++)
        {
            if (!set[r] || !frontier(r))
                continue;
            Fx6 cost = left(r);
            if (best < 0 || cost < bestCost || cost == bestCost && number(r) < number(best))
            {
                best = r;
                bestCost = cost;
            }
        }
        return best;
    }

    /// <summary>Choice among the alternatives of a slot: least closure cost, then higher rank, then the smaller <c>alt_id</c> (ordinal).</summary>
    public static bool BetterAlternative(Fx6 cost, int rank, string id, Fx6 bestCost, int bestRank, string bestId) =>
        cost < bestCost || cost == bestCost && (rank > bestRank || rank == bestRank && string.CompareOrdinal(id, bestId) < 0);

    private static void Union(bool[] into, bool[] other)
    {
        for (int i = 0; i < into.Length; i++)
            into[i] |= other[i];
    }
}

/// <summary>
/// Reusable sets of the closures of one why_not analysis, per thread (TASK-0107: one analysis made ~100 KB of garbage per actor and call). Pure
/// scratch, never read across analyses: <see cref="Reset"/> at the start of <c>WhyNotAnalyzer.WhyNot</c> hands every set back, so nothing
/// returned to a caller refers to it (the result is a list of links).
/// </summary>
internal static class ClosureArena
{
    [ThreadStatic] private static List<bool[]>? t_sets;
    [ThreadStatic] private static List<bool[]?[]>? t_memos;
    [ThreadStatic] private static int t_setsUsed, t_memosUsed;

    public static void Reset()
    {
        t_setsUsed = 0;
        t_memosUsed = 0;
    }

    /// <summary>A cleared set of <paramref name="count"/> flags, valid until the next <see cref="Reset"/> on this thread.</summary>
    public static bool[] TakeSet(int count)
    {
        t_sets ??= [];
        if (t_setsUsed == t_sets.Count)
            t_sets.Add(new bool[count]);
        else if (t_sets[t_setsUsed].Length != count)
            t_sets[t_setsUsed] = new bool[count];
        else
            Array.Clear(t_sets[t_setsUsed]);
        return t_sets[t_setsUsed++];
    }

    /// <summary>A cleared memo table of <paramref name="count"/> entries, valid until the next <see cref="Reset"/> on this thread.</summary>
    public static bool[]?[] TakeMemo(int count)
    {
        t_memos ??= [];
        if (t_memosUsed == t_memos.Count)
            t_memos.Add(new bool[]?[count]);
        else if (t_memos[t_memosUsed].Length != count)
            t_memos[t_memosUsed] = new bool[]?[count];
        else
            Array.Clear(t_memos[t_memosUsed]);
        return t_memos[t_memosUsed++];
    }
}
