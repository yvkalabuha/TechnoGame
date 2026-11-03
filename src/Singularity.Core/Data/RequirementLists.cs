using System.Collections.Immutable;
using Singularity.Core.Data.Yaml;

namespace Singularity.Core.Data;

/// <summary>
/// Requirements of many owners in CSR form (ADR-0005 adjacency arrays): owner r has groups
/// <c>GroupStart[r]..GroupStart[r+1]</c>; group g is satisfied by any node row in
/// <c>Members[MemberStart[g]..MemberStart[g+1]]</c>. All groups must hold (AND of OR-groups;
/// a plain requirement is a group of one). Members ascend; groups are sorted lexicographically.
/// </summary>
public sealed class RequirementLists
{
    public required ImmutableArray<int> GroupStart { get; init; }
    public required ImmutableArray<int> MemberStart { get; init; }
    public required ImmutableArray<int> Members { get; init; }

    public ReadOnlySpan<int> GroupMembers(int group) =>
        Members.AsSpan()[MemberStart[group]..MemberStart[group + 1]];

    public bool IsSatisfied(int owner, ReadOnlySpan<bool> knownRows)
    {
        for (int g = GroupStart[owner]; g < GroupStart[owner + 1]; g++)
        {
            bool any = false;
            foreach (int member in GroupMembers(g))
                any |= knownRows[member];
            if (!any)
                return false;
        }
        return true;
    }

    /// <summary>Reads a requires list: node IDs (AND) and <c>{any_of: [...]}</c> groups.</summary>
    internal static List<int[]> ReadGroups(YSeq seq, Func<YNode, string, int> resolve)
    {
        var groups = new List<int[]>();
        foreach (YNode item in seq.Items)
        {
            int[] group;
            if (item is YMap)
            {
                var map = new MapReader(item);
                YSeq alternatives = map.Seq("any_of");
                map.End();
                if (alternatives.Items.Count < 2)
                    throw DataLoadException.At(item, "any_of needs at least two node IDs");
                group = [.. alternatives.Items.Select(a => resolve(a, Scalars.Str(a)))];
            }
            else
            {
                group = [resolve(item, Scalars.Str(item))];
            }
            Array.Sort(group);
            for (int i = 0; i < group.Length; i++)
            {
                if ((i > 0 && group[i] == group[i - 1]) || groups.Any(g => Array.IndexOf(g, group[i]) >= 0))
                    throw DataLoadException.At(item, "the same node is required twice");
            }
            groups.Add(group);
        }
        groups.Sort(CompareGroups);
        return groups;
    }

    internal static RequirementLists Build(IReadOnlyList<List<int[]>> perOwner)
    {
        var groupStart = ImmutableArray.CreateBuilder<int>(perOwner.Count + 1);
        var memberStart = ImmutableArray.CreateBuilder<int>();
        var members = ImmutableArray.CreateBuilder<int>();
        groupStart.Add(0);
        memberStart.Add(0);
        foreach (List<int[]> groups in perOwner)
        {
            foreach (int[] group in groups)
            {
                members.AddRange(group);
                memberStart.Add(members.Count);
            }
            groupStart.Add(memberStart.Count - 1);
        }
        return new RequirementLists
        {
            GroupStart = groupStart.MoveToImmutable(),
            MemberStart = memberStart.ToImmutable(),
            Members = members.ToImmutable(),
        };
    }

    internal void WriteCanonical(CanonicalWriter w, int owner, NodeTable nodes)
    {
        w.Int(GroupStart[owner + 1] - GroupStart[owner]);
        for (int g = GroupStart[owner]; g < GroupStart[owner + 1]; g++)
        {
            ReadOnlySpan<int> group = GroupMembers(g);
            w.Int(group.Length);
            foreach (int member in group)
                w.Str(nodes.Id[member]);
        }
    }

    private static int CompareGroups(int[] a, int[] b)
    {
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            if (a[i] != b[i])
                return a[i].CompareTo(b[i]);
        }
        return a.Length.CompareTo(b.Length);
    }
}
