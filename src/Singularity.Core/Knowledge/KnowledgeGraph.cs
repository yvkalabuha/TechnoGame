using System.Collections.Immutable;
using Singularity.Core.Data;
using Singularity.Kernel;

namespace Singularity.Core.Knowledge;

/// <summary>One node for <see cref="KnowledgeGraph.Create"/>; requirements are groups of node numbers (AND of OR-groups).</summary>
public sealed record KnowledgeNodeSpec(int Number, NodeType Type, int Gen, int Discipline, bool Hazard, Fx6 CostN,
    int[][] Requires, (int From, Fx6 Weight)[]? InformedBy = null);

/// <summary>
/// What the knowledge layer needs from the node data, by storage row. <see cref="Number"/> is the
/// stable node number (RNG keys, journals); <see cref="Order"/> lists rows by ascending number, the
/// order of every sum (spec §3.5). Discipline is an index into <see cref="DisciplineNames"/>, −1 for
/// APP nodes (no discipline, <c>Kmix = 0</c>, §3.5 A14).
/// </summary>
public sealed class KnowledgeGraph
{
    private KnowledgeGraph() { }

    public int Count => Number.Length;
    public required ImmutableArray<int> Number { get; init; }
    public required ImmutableArray<NodeType> Type { get; init; }
    public required ImmutableArray<int> Gen { get; init; }
    public required ImmutableArray<int> Discipline { get; init; }
    public required ImmutableArray<bool> IsHazard { get; init; }
    public required ImmutableArray<Fx6> CostN { get; init; }
    public required RequirementLists Requires { get; init; }

    /// <summary>Incoming informs edges of row r: <c>InformedStart[r]..InformedStart[r+1]</c>.</summary>
    public required ImmutableArray<int> InformedStart { get; init; }
    public required ImmutableArray<int> InformedFrom { get; init; }
    public required ImmutableArray<Fx6> InformedWeight { get; init; }

    public required ImmutableArray<string> DisciplineNames { get; init; }
    public required ImmutableArray<int> Order { get; init; }

    /// <summary>Row of a node number, or −1.</summary>
    public int RowOf(int number)
    {
        for (int r = 0; r < Number.Length; r++)
        {
            if (Number[r] == number)
                return r;
        }
        return -1;
    }

    /// <summary>
    /// Slice graph from loaded data: disciplines in scenario order, hazard = ★ or hz (§1.3),
    /// <c>c_n</c> from <paramref name="p"/> by type and generation (§8.1).
    /// </summary>
    public static KnowledgeGraph FromData(NodeTable nodes, IReadOnlyList<Discipline> disciplines, KnowledgeParams p)
    {
        var specs = new KnowledgeNodeSpec[nodes.Count];
        var informed = new List<(int From, Fx6 Weight)>[nodes.Count];
        for (int r = 0; r < nodes.Count; r++)
            informed[r] = new();
        for (int r = 0; r < nodes.Count; r++)
        {
            for (int e = nodes.InformsStart[r]; e < nodes.InformsStart[r + 1]; e++)
                informed[nodes.InformsTo[e]].Add((nodes.Number[r], nodes.InformsWeight[e]));
        }
        for (int r = 0; r < nodes.Count; r++)
        {
            int disc = -1;
            if (nodes.Type[r] != NodeType.APP)
            {
                disc = IndexOf(disciplines, nodes.Discipline[r]);
                if (disc < 0)
                    throw new ArgumentException($"Node {nodes.Id[r]}: discipline {nodes.Discipline[r]} is not in the scenario.", nameof(disciplines));
            }
            var groups = new int[nodes.Requires.GroupStart[r + 1] - nodes.Requires.GroupStart[r]][];
            for (int g = 0; g < groups.Length; g++)
            {
                ReadOnlySpan<int> members = nodes.Requires.GroupMembers(nodes.Requires.GroupStart[r] + g);
                groups[g] = new int[members.Length];
                for (int m = 0; m < members.Length; m++)
                    groups[g][m] = nodes.Number[members[m]];
            }
            specs[r] = new KnowledgeNodeSpec(nodes.Number[r], nodes.Type[r], nodes.Gen[r], disc, nodes.Hazard[r] != Hazard.None,
                p.CostN(nodes.Type[r], nodes.Gen[r]), groups, [.. informed[r]]);
        }
        string[] names = new string[disciplines.Count];
        for (int d = 0; d < names.Length; d++)
            names[d] = DataNames.NameOf(DataNames.Disciplines, disciplines[d]);
        return Create(specs, names);
    }

    /// <summary>Graph from explicit nodes in any storage order (tests, the reference fixture).</summary>
    public static KnowledgeGraph Create(IReadOnlyList<KnowledgeNodeSpec> nodes, IReadOnlyList<string> disciplineNames)
    {
        int n = nodes.Count;
        int[] numbers = new int[n];
        for (int r = 0; r < n; r++)
        {
            numbers[r] = nodes[r].Number;
            if (numbers[r] < 1 || numbers[r] > KnowledgeRng.MaxNodeNumber)
                throw new ArgumentException($"Node number {numbers[r]} is outside 1..{KnowledgeRng.MaxNodeNumber} (RNG key actor·64 + node).", nameof(nodes));
            if (nodes[r].Discipline < -1 || nodes[r].Discipline >= disciplineNames.Count || (nodes[r].Discipline < 0) != (nodes[r].Type == NodeType.APP))
                throw new ArgumentException($"Node {numbers[r]}: bad discipline index {nodes[r].Discipline}.", nameof(nodes));
            if (nodes[r].CostN <= Fx6.Zero)
                throw new ArgumentException($"Node {numbers[r]}: c_n must be positive.", nameof(nodes));
        }
        int[] order = [.. Enumerable.Range(0, n)];
        int[] sorted = (int[])numbers.Clone();
        Array.Sort(sorted, order);
        for (int i = 1; i < n; i++)
        {
            if (sorted[i] == sorted[i - 1])
                throw new ArgumentException($"Node number {sorted[i]} is used twice.", nameof(nodes));
        }
        int Row(int number)
        {
            int i = Array.BinarySearch(sorted, number);
            return i >= 0 ? order[i] : throw new ArgumentException($"Reference to unknown node number {number}.", nameof(nodes));
        }

        var requires = new List<List<int[]>>(n);
        var informedStart = new List<int> { 0 };
        var informedFrom = new List<int>();
        var informedWeight = new List<Fx6>();
        for (int r = 0; r < n; r++)
        {
            var groups = new List<int[]>();
            foreach (int[] group in nodes[r].Requires)
            {
                int[] rows = new int[group.Length];
                for (int m = 0; m < group.Length; m++)
                    rows[m] = Row(group[m]);
                Array.Sort(rows);
                groups.Add(rows);
            }
            requires.Add(groups);
            var edges = new List<(int From, Fx6 Weight)>();
            foreach ((int from, Fx6 w) in nodes[r].InformedBy ?? [])
                edges.Add((Row(from), w));
            edges.Sort((a, b) => numbers[a.From].CompareTo(numbers[b.From]));
            foreach ((int from, Fx6 w) in edges)
            {
                informedFrom.Add(from);
                informedWeight.Add(w);
            }
            informedStart.Add(informedFrom.Count);
        }

        return new KnowledgeGraph
        {
            Number = [.. numbers],
            Type = [.. nodes.Select(s => s.Type)],
            Gen = [.. nodes.Select(s => s.Gen)],
            Discipline = [.. nodes.Select(s => s.Discipline)],
            IsHazard = [.. nodes.Select(s => s.Hazard)],
            CostN = [.. nodes.Select(s => s.CostN)],
            Requires = RequirementLists.Build(requires),
            InformedStart = [.. informedStart],
            InformedFrom = [.. informedFrom],
            InformedWeight = [.. informedWeight],
            DisciplineNames = [.. disciplineNames],
            Order = [.. order],
        };
    }

    private static int IndexOf(IReadOnlyList<Discipline> list, Discipline d)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == d)
                return i;
        }
        return -1;
    }
}
