using System.Collections.Immutable;
using Singularity.Core.Data.Yaml;
using Singularity.Kernel;

namespace Singularity.Core.Data;

/// <summary>
/// Knowledge nodes as an immutable struct-of-arrays table (ADR-0005). Row r of every column is
/// the same node; rows are ordered by <see cref="Number"/> (the stable node number used in RNG
/// keys, slice spec §3.5 A7). <c>capability_req</c> is sparse: <see cref="Fx6.Zero"/> means
/// "dimension not required"; <see cref="Tier"/> is 0 for non-PRC nodes.
/// </summary>
public sealed partial class NodeTable
{
    public int Count => Id.Length;

    public required ImmutableArray<string> Id { get; init; }
    public required ImmutableArray<int> Number { get; init; }
    public required ImmutableArray<NodeType> Type { get; init; }
    public required ImmutableArray<int> Gen { get; init; }
    public required ImmutableArray<Discipline> Discipline { get; init; }
    public required ImmutableArray<Cluster> Cluster { get; init; }
    public required ImmutableArray<int> Tier { get; init; }
    public required ImmutableArray<Fx6> ReqPrecision { get; init; }
    public required ImmutableArray<Fx6> ReqPurity { get; init; }
    public required ImmutableArray<Fx6> ReqQualityControl { get; init; }
    public required ImmutableArray<Hazard> Hazard { get; init; }
    public required RequirementLists Requires { get; init; }

    /// <summary>informs edges of row r: <c>InformsStart[r]..InformsStart[r+1]</c>, targets ascending.</summary>
    public required ImmutableArray<int> InformsStart { get; init; }
    public required ImmutableArray<int> InformsTo { get; init; }
    public required ImmutableArray<Fx6> InformsWeight { get; init; }

    public required ImmutableArray<int> AnchorYear { get; init; }
    public required ImmutableArray<bool> AnchorBefore { get; init; }
    public required ImmutableArray<string> AnchorEvent { get; init; }
    public required ImmutableArray<string> AnchorSource { get; init; }
    public required ImmutableArray<bool> AnchorEstimated { get; init; }

    // IDs sorted ordinally with their rows, for binary-search lookup.
    private ImmutableArray<string> _sortedIds;
    private ImmutableArray<int> _sortedRows;

    /// <summary>Row of a node ID, or -1.</summary>
    public int IndexOf(string id)
    {
        int i = _sortedIds.AsSpan().BinarySearch(id, StringComparer.Ordinal);
        return i >= 0 ? _sortedRows[i] : -1;
    }

    public Fx6 Requirement(int row, Dimension dim) => dim switch
    {
        Dimension.Precision => ReqPrecision[row],
        Dimension.Purity => ReqPurity[row],
        _ => ReqQualityControl[row],
    };

    internal static NodeTable Build(List<(string Id, YMap Map)> objects)
    {
        var raw = objects.Select(o => RawNode.Read(o.Map)).ToList();
        raw.Sort((a, b) => a.Number.CompareTo(b.Number));
        for (int i = 1; i < raw.Count; i++)
        {
            if (raw[i].Number == raw[i - 1].Number)
                throw DataLoadException.At(raw[i].Map, $"node number {raw[i].Number} is used by '{raw[i - 1].Id}' and '{raw[i].Id}'");
        }

        string[] sortedIds = [.. raw.Select(r => r.Id)];
        int[] sortedRows = [.. Enumerable.Range(0, raw.Count)];
        Array.Sort(sortedIds, sortedRows, StringComparer.Ordinal);
        int Resolve(YNode at, string id)
        {
            int i = Array.BinarySearch(sortedIds, id, StringComparer.Ordinal);
            return i >= 0 ? sortedRows[i] : throw DataLoadException.At(at, $"reference to unknown node '{id}'");
        }

        var requires = new List<List<int[]>>(raw.Count);
        var informsStart = new List<int> { 0 };
        var informsTo = new List<int>();
        var informsWeight = new List<Fx6>();
        foreach (RawNode node in raw)
        {
            requires.Add(RequirementLists.ReadGroups(node.Requires, Resolve));
            var edges = new List<(int To, Fx6 Weight, YNode At)>();
            foreach (YNode item in node.Informs?.Items ?? [])
            {
                var edge = new MapReader(item);
                YNode to = edge.Required("to");
                edges.Add((Resolve(to, Scalars.Str(to)), edge.Fixed("weight"), item));
                edge.End();
            }
            edges.Sort((a, b) => a.To.CompareTo(b.To));
            foreach ((int to, Fx6 weight, YNode at) in edges)
            {
                if (informsTo.Count > informsStart[^1] && informsTo[^1] == to)
                    throw DataLoadException.At(at, $"duplicate informs edge to '{raw[to].Id}'");
                informsTo.Add(to);
                informsWeight.Add(weight);
            }
            informsStart.Add(informsTo.Count);
        }

        var table = new NodeTable
        {
            Id = [.. raw.Select(r => r.Id)],
            Number = [.. raw.Select(r => r.Number)],
            Type = [.. raw.Select(r => r.Type)],
            Gen = [.. raw.Select(r => r.Gen)],
            Discipline = [.. raw.Select(r => r.Discipline)],
            Cluster = [.. raw.Select(r => r.Cluster)],
            Tier = [.. raw.Select(r => r.Tier)],
            ReqPrecision = [.. raw.Select(r => r.Req[0])],
            ReqPurity = [.. raw.Select(r => r.Req[1])],
            ReqQualityControl = [.. raw.Select(r => r.Req[2])],
            Hazard = [.. raw.Select(r => r.Hazard)],
            Requires = RequirementLists.Build(requires),
            InformsStart = [.. informsStart],
            InformsTo = [.. informsTo],
            InformsWeight = [.. informsWeight],
            AnchorYear = [.. raw.Select(r => r.AnchorYear)],
            AnchorBefore = [.. raw.Select(r => r.AnchorBefore)],
            AnchorEvent = [.. raw.Select(r => r.AnchorEvent)],
            AnchorSource = [.. raw.Select(r => r.AnchorSource)],
            AnchorEstimated = [.. raw.Select(r => r.AnchorEstimated)],
            _sortedIds = [.. sortedIds],
            _sortedRows = [.. sortedRows],
        };
        table.Validate(raw);
        return table;
    }
}
