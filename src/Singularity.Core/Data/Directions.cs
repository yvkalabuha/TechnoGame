using System.Collections.Immutable;
using Singularity.Core.Data.Yaml;

namespace Singularity.Core.Data;

/// <summary>
/// The 12 science directions (docs/research/notes/directions-nodes-institutes.md, O-091) and the checks of the <c>directions</c> field of nodes and institutes.
/// Data only: the core does not read the field yet. An absent field is an empty list (a mod may omit it); a present one is validated.
/// </summary>
public static class Directions
{
    public const int MinPerEntry = 2;
    public const int MaxPerEntry = 4;
    public const int MaxLevel = 5;

    /// <summary>Direction IDs; the index in this array is the direction number.</summary>
    public static readonly ImmutableArray<string> Ids =
        ["aero", "prop", "met", "mat", "chem", "struct", "mfg", "elec", "sens", "ctrl", "nuc", "ord"];

    /// <summary>Node field: a list of 2..4 distinct known direction IDs, the main one first. Returns direction numbers in file order.</summary>
    internal static int[] ReadForNode(YNode? node, string owner)
    {
        if (node is null)
            return [];
        YSeq seq = Scalars.Seq(node);
        CheckCount(node, owner, seq.Items.Count);
        var result = new int[seq.Items.Count];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = Resolve(seq.Items[i], owner);
            if (Array.IndexOf(result, result[i], 0, i) >= 0)
                throw DataLoadException.At(seq.Items[i], $"'{owner}': direction '{Ids[result[i]]}' is listed twice");
        }
        return result;
    }

    /// <summary>Institute field: a map of 2..4 direction IDs to levels 1..5. Returns (direction number, level) sorted by direction number.</summary>
    internal static (int Direction, int Level)[] ReadLevels(YNode? node, string owner)
    {
        if (node is null)
            return [];
        YMap map = Scalars.Map(node);
        CheckCount(node, owner, map.Entries.Count);
        var result = new List<(int Direction, int Level)>();
        foreach (KeyValuePair<string, YNode> e in map.Entries)
        {
            int d = Ids.IndexOf(e.Key);
            if (d < 0)
                throw DataLoadException.At(e.Value, $"'{owner}': unknown direction '{e.Key}'");
            int l = Scalars.Int(e.Value);
            if (l is < 1 or > MaxLevel)
                throw DataLoadException.At(e.Value, $"'{owner}': level of direction '{e.Key}' must be 1..{MaxLevel}");
            result.Add((d, l));   // duplicate keys are rejected by the YAML reader
        }
        result.Sort((a, b) => a.Direction.CompareTo(b.Direction));
        return [.. result];
    }

    private static void CheckCount(YNode at, string owner, int count)
    {
        if (count is < MinPerEntry or > MaxPerEntry)
            throw DataLoadException.At(at, $"'{owner}': {count} directions, expected {MinPerEntry}..{MaxPerEntry}");
    }

    private static int Resolve(YNode item, string owner)
    {
        string id = Scalars.Str(item);
        int d = Ids.IndexOf(id);
        return d >= 0 ? d : throw DataLoadException.At(item, $"'{owner}': unknown direction '{id}'");
    }
}
