using System.Collections.Immutable;
using Singularity.Core.Data;
using Singularity.Kernel;

namespace Singularity.Core.Industry;

/// <summary>One PRC for <see cref="IndustryLayout.Create"/>: stable node number, cluster, discipline index, tier, <c>r_d</c> (0 = none).</summary>
public sealed record IndustryProcessSpec(int Number, Cluster Cluster, int Discipline, int Tier, Dims3 Req);

/// <summary>
/// Immutable shape of the capability layer: the site clusters (ascending <see cref="Cluster"/>), the
/// processes by storage row (<see cref="Order"/> = rows by ascending number, the order of every sum and
/// merge key), the owner actor row of each site, the workforce categories and requester ids (contract §4.6),
/// and the alloy melt grades (spec §1.4).
/// </summary>
public sealed class IndustryLayout
{
    /// <summary>Melt processes of <c>s5.cmp.heat_resistant_alloy</c>, worst to best grade (spec §1.4).</summary>
    public static readonly ImmutableArray<int> SliceAlloyGrades = [39, 40, 41];

    private IndustryLayout() { }

    public required ImmutableArray<Cluster> Clusters { get; init; }

    public int ProcessCount => Number.Length;
    public required ImmutableArray<int> Number { get; init; }
    public required ImmutableArray<int> ClusterIndex { get; init; }
    public required ImmutableArray<int> Discipline { get; init; }
    public required ImmutableArray<int> Tier { get; init; }
    public required ImmutableArray<Dims3> Req { get; init; }
    public required ImmutableArray<int> Order { get; init; }

    public int SiteCount => SiteOwner.Length;

    /// <summary>Actor row that owns each site (contract §1: capability is keyed by site → owner_actor).</summary>
    public required ImmutableArray<int> SiteOwner { get; init; }

    public required int WorkforceCategories { get; init; }

    /// <summary>Requester ids are 0…<c>Requesters − 1</c>, one id space for bureaus and programmes (contract §4.6).</summary>
    public required int Requesters { get; init; }

    /// <summary>Process rows of the alloy melts, worst to best grade; empty when the layout has no alloy chain.</summary>
    public required ImmutableArray<int> AlloyGrades { get; init; }

    /// <summary>Row of a process number, or −1.</summary>
    public int RowOf(int number)
    {
        for (int r = 0; r < Number.Length; r++)
        {
            if (Number[r] == number)
                return r;
        }
        return -1;
    }

    public static IndustryLayout Create(IReadOnlyList<Cluster> clusters, IReadOnlyList<IndustryProcessSpec> processes,
        IReadOnlyList<int> siteOwner, int workforceCategories, int requesters, IReadOnlyList<int>? alloyGrades = null)
    {
        for (int i = 1; i < clusters.Count; i++)
        {
            if (clusters[i] <= clusters[i - 1])
                throw new ArgumentException("Clusters must be distinct and in ascending order.", nameof(clusters));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(workforceCategories);
        ArgumentOutOfRangeException.ThrowIfNegative(requesters);
        var cluster = new int[processes.Count];
        for (int r = 0; r < processes.Count; r++)
        {
            IndustryProcessSpec p = processes[r];
            cluster[r] = IndexOf(clusters, p.Cluster);
            if (cluster[r] < 0)
                throw new ArgumentException($"Process {p.Number}: cluster {p.Cluster} is not a site cluster.", nameof(processes));
            if (p.Tier < 3)
                throw new ArgumentException($"Process {p.Number}: tier {p.Tier} is below 3.", nameof(processes));
            for (int q = 0; q < r; q++)
            {
                if (processes[q].Number == p.Number)
                    throw new ArgumentException($"Process number {p.Number} is used twice.", nameof(processes));
            }
        }
        int[] order = [.. Enumerable.Range(0, processes.Count)];
        Array.Sort(order, (a, b) => processes[a].Number.CompareTo(processes[b].Number));
        var grades = new int[alloyGrades?.Count ?? 0];
        for (int i = 0; i < grades.Length; i++)
        {
            grades[i] = -1;
            for (int r = 0; r < processes.Count; r++)
            {
                if (processes[r].Number == alloyGrades![i])
                    grades[i] = r;
            }
            if (grades[i] < 0)
                throw new ArgumentException($"Alloy grade {alloyGrades![i]} is not a process of the layout.", nameof(alloyGrades));
        }
        return new IndustryLayout
        {
            Clusters = [.. clusters],
            Number = [.. processes.Select(p => p.Number)],
            ClusterIndex = [.. cluster],
            Discipline = [.. processes.Select(p => p.Discipline)],
            Tier = [.. processes.Select(p => p.Tier)],
            Req = [.. processes.Select(p => p.Req)],
            Order = [.. order],
            SiteOwner = [.. siteOwner],
            WorkforceCategories = workforceCategories,
            Requesters = requesters,
            AlloyGrades = [.. grades],
        };
    }

    /// <summary>
    /// Slice layout from loaded data: every PRC node in node-table order, the scenario clusters (sorted) and
    /// disciplines, one site per actor (site row = actor row, spec §1.1), the slice alloy grades.
    /// </summary>
    public static IndustryLayout FromData(NodeTable nodes, ScenarioData scenario, int workforceCategories, int requesters)
    {
        var specs = new List<IndustryProcessSpec>();
        for (int r = 0; r < nodes.Count; r++)
        {
            if (nodes.Type[r] != NodeType.PRC)
                continue;
            int disc = scenario.Disciplines.IndexOf(nodes.Discipline[r]);
            if (disc < 0)
                throw new ArgumentException($"Process {nodes.Id[r]}: discipline {nodes.Discipline[r]} is not in the scenario.", nameof(scenario));
            specs.Add(new IndustryProcessSpec(nodes.Number[r], nodes.Cluster[r], disc, nodes.Tier[r],
                new Dims3(nodes.Requirement(r, Dimension.Precision), nodes.Requirement(r, Dimension.Purity),
                    nodes.Requirement(r, Dimension.QualityControl))));
        }
        Cluster[] clusters = [.. scenario.Clusters];
        Array.Sort(clusters);
        return Create(clusters, specs, [.. Enumerable.Range(0, scenario.ActorCount)], workforceCategories, requesters, SliceAlloyGrades);
    }

    private static int IndexOf(IReadOnlyList<Cluster> clusters, Cluster c)
    {
        for (int i = 0; i < clusters.Count; i++)
        {
            if (clusters[i] == c)
                return i;
        }
        return -1;
    }
}
