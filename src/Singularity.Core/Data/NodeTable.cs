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
    /// <summary><c>labour_p</c>: hours per process execution (contract §4.1); zero for non-PRC nodes.</summary>
    public required ImmutableArray<Fx6> LabourP { get; init; }
    public required ImmutableArray<Hazard> Hazard { get; init; }

    /// <summary>Directions of row r (numbers of <see cref="Directions.Ids"/>, the main one first): <c>DirectionStart[r]..DirectionStart[r+1]</c>. Part of the data hash.</summary>
    public required ImmutableArray<int> DirectionStart { get; init; }
    public required ImmutableArray<int> Direction { get; init; }

    /// <summary>Military value word of the node (SCIENCE_SLOTS §3.2); <see cref="MilValue.None"/> when the mod gives none. Part of the data hash; shown on the card, not read by the simulation.</summary>
    public required ImmutableArray<MilValue> MilValue { get; init; }

    /// <summary>research_speed effects of row r: <c>SpeedStart[r]..SpeedStart[r+1]</c>, bit mask of directions and bonus (<see cref="ResearchSpeedEffects"/>). Read by the slot model (R of the research rate); part of the data hash.</summary>
    public required ImmutableArray<int> SpeedStart { get; init; }
    public required ImmutableArray<int> SpeedMask { get; init; }
    public required ImmutableArray<Fx6> SpeedBonus { get; init; }

    public required RequirementLists Requires { get; init; }

    /// <summary>informs edges of row r: <c>InformsStart[r]..InformsStart[r+1]</c>, targets ascending.</summary>
    public required ImmutableArray<int> InformsStart { get; init; }
    public required ImmutableArray<int> InformsTo { get; init; }
    public required ImmutableArray<Fx6> InformsWeight { get; init; }

    public required ImmutableArray<int> AnchorYear { get; init; }
    public required ImmutableArray<bool> AnchorBefore { get; init; }
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

    public int DirectionCount(int row) => DirectionStart[row + 1] - DirectionStart[row];

    /// <summary>The k-th direction number of the node; k = 0 is the main one.</summary>
    public int DirectionAt(int row, int k) => Direction[DirectionStart[row] + k];

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

        var directionStart = new List<int> { 0 };
        var directions = new List<int>();
        foreach (RawNode node in raw)
        {
            directions.AddRange(node.Directions);
            directionStart.Add(directions.Count);
        }

        var speedStart = new List<int> { 0 };
        var speedMask = new List<int>();
        var speedBonus = new List<Fx6>();
        foreach (RawNode node in raw)
        {
            foreach ((int mask, Fx6 bonus) in node.Speed)
            {
                speedMask.Add(mask);
                speedBonus.Add(bonus);
            }
            speedStart.Add(speedMask.Count);
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
            LabourP = [.. raw.Select(r => r.LabourP)],
            Hazard = [.. raw.Select(r => r.Hazard)],
            DirectionStart = directionStart.ToImmutableArray(),
            Direction = directions.ToImmutableArray(),
            MilValue = [.. raw.Select(r => r.MilValue)],
            SpeedStart = speedStart.ToImmutableArray(),
            SpeedMask = speedMask.ToImmutableArray(),
            SpeedBonus = speedBonus.ToImmutableArray(),
            Requires = RequirementLists.Build(requires),
            InformsStart = [.. informsStart],
            InformsTo = [.. informsTo],
            InformsWeight = [.. informsWeight],
            AnchorYear = [.. raw.Select(r => r.AnchorYear)],
            AnchorBefore = [.. raw.Select(r => r.AnchorBefore)],
            AnchorSource = [.. raw.Select(r => r.AnchorSource)],
            AnchorEstimated = [.. raw.Select(r => r.AnchorEstimated)],
            _sortedIds = [.. sortedIds],
            _sortedRows = [.. sortedRows],
        };
        table.Validate(raw);
        return table;
    }
}
