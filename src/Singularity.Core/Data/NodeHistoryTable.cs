using System.Collections.Immutable;
using System.Globalization;
using Singularity.Core.Data.Yaml;

namespace Singularity.Core.Data;

/// <summary>How well a source was checked (TASK-0157): the page was read, or only found by a search.</summary>
public enum SourceCheck { Opened, SearchSnippet }

/// <summary>License of a source text; only PD, CC0 and CC-BY may be quoted (ADR-0009).</summary>
public enum SourceLicense { Pd, Cc0, CcBy, Other }

/// <summary>How the source is used: retold in own words, or quoted.</summary>
public enum SourceUse { Paraphrase, Quote }

/// <summary>
/// Display-only history of knowledge nodes (#486): epic flag and sources (the text itself is locale, attribute <c>.history</c>, ADR-0018), as arrays
/// by node row (ADR-0005). No simulation code reads it, so it is NOT part of <see cref="GameData.DataHash"/>.
/// Sources of row r: <c>SourceStart[r]..SourceStart[r+1]</c>, in data order.
/// </summary>
public sealed class NodeHistoryTable
{
    public const int MaxSources = 3;

    public required ImmutableArray<bool> Has { get; init; }
    public required ImmutableArray<bool> Epic { get; init; }

    /// <summary>The history holds facts the research could not confirm (the card shows a mark with a tooltip).</summary>
    public required ImmutableArray<bool> Unconfirmed { get; init; }
    public required ImmutableArray<int> SourceStart { get; init; }
    public required ImmutableArray<string> SourceUrl { get; init; }
    public required ImmutableArray<string> SourceAccessed { get; init; }
    public required ImmutableArray<SourceCheck> SourceCheck { get; init; }
    public required ImmutableArray<SourceLicense> SourceLicense { get; init; }
    public required ImmutableArray<SourceUse> SourceUse { get; init; }

    private static readonly ImmutableArray<(string Name, SourceCheck Value)> Checks =
        [("opened", Data.SourceCheck.Opened), ("search-snippet", Data.SourceCheck.SearchSnippet)];
    private static readonly ImmutableArray<(string Name, SourceLicense Value)> Licenses =
        [("PD", Data.SourceLicense.Pd), ("CC0", Data.SourceLicense.Cc0), ("CC-BY", Data.SourceLicense.CcBy), ("other", Data.SourceLicense.Other)];
    private static readonly ImmutableArray<(string Name, SourceUse Value)> Uses =
        [("paraphrase", Data.SourceUse.Paraphrase), ("quote", Data.SourceUse.Quote)];

    internal static NodeHistoryTable Build(List<(string Id, YMap Map)> objects, NodeTable nodes)
    {
        int n = nodes.Count;
        var has = new bool[n];
        var epic = new bool[n];
        var unconfirmed = new bool[n];
        var sources = new List<(string Url, string Accessed, SourceCheck Check, SourceLicense License, SourceUse Use)>[n];
        foreach ((string id, YMap map) in objects)
        {
            int row = nodes.IndexOf(id);
            if (row < 0)
                throw DataLoadException.At(map, $"node_history of unknown node '{id}'");
            var m = new MapReader(map);
            m.Str("id");
            has[row] = true;
            epic[row] = m.Bool("epic");
            unconfirmed[row] = m.Optional("unconfirmed") is {} u && Scalars.Bool(u);
            YSeq seq = m.Seq("sources");
            if (seq.Items.Count is < 1 or > MaxSources)
                throw DataLoadException.At(seq, $"'{id}' needs 1..{MaxSources} sources, has {seq.Items.Count}");
            sources[row] = [];
            foreach (YNode item in seq.Items)
                sources[row].Add(ReadSource(item));
            m.End();
        }

        var start = new List<int> { 0 };
        var url = new List<string>();
        var accessed = new List<string>();
        var check = new List<SourceCheck>();
        var license = new List<SourceLicense>();
        var use = new List<SourceUse>();
        for (int r = 0; r < n; r++)
        {
            foreach (var s in sources[r] ?? [])
            {
                url.Add(s.Url);
                accessed.Add(s.Accessed);
                check.Add(s.Check);
                license.Add(s.License);
                use.Add(s.Use);
            }
            start.Add(url.Count);
        }
        return new NodeHistoryTable
        {
            Has = has.ToImmutableArray(), Epic = epic.ToImmutableArray(), Unconfirmed = unconfirmed.ToImmutableArray(),
            SourceStart = [.. start], SourceUrl = [.. url], SourceAccessed = [.. accessed],
            SourceCheck = [.. check], SourceLicense = [.. license], SourceUse = [.. use],
        };
    }

    private static (string, string, SourceCheck, SourceLicense, SourceUse) ReadSource(YNode node)
    {
        var m = new MapReader(node);
        string url = m.Str("url");
        if (!url.StartsWith("https://", StringComparison.Ordinal) && !url.StartsWith("http://", StringComparison.Ordinal))
            throw DataLoadException.At(node, $"source url '{url}' must be http(s)");
        string accessed = m.Str("accessed");
        if (!DateOnly.TryParseExact(accessed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw DataLoadException.At(node, $"source access date '{accessed}' must be yyyy-MM-dd");
        SourceCheck check = m.Enum("check", Checks);
        SourceLicense license = m.Enum("license", Licenses);
        SourceUse use = m.Enum("use", Uses);
        m.End();
        if (use == Data.SourceUse.Quote && license == Data.SourceLicense.Other)
            throw DataLoadException.At(node, $"'{url}' may not be quoted: only PD, CC0 and CC-BY text can be (ADR-0009); retell it (use: paraphrase)");
        return (url, accessed, check, license, use);
    }
}
