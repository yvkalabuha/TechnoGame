using System.Collections.Immutable;
using Singularity.Core.Data.Yaml;

namespace Singularity.Core.Data;

/// <summary>
/// Research institutes of the data (research-institutes-priorities §1.3): one row per institute, sorted by ID. A row names the country (actor ID of the
/// scenario), the founding year, the historical year (<c>ref_year</c>, the price of a later project) and the areas with levels 1..4 (level 0 is not stored).
/// Not part of <see cref="GameData.DataHash"/> (so a run without institutes keeps its data identity); it has its own <see cref="Hash"/>.
/// </summary>
public sealed class InstituteTable
{
    public const int MaxLevel = 4;

    private readonly ImmutableArray<string> _id, _country;
    private readonly ImmutableArray<int> _founded, _refYear, _areaStart;
    private readonly ImmutableArray<string> _areaDiscipline;
    private readonly ImmutableArray<int> _areaLevel;
    private readonly ImmutableArray<int> _dirStart, _dirId, _dirLevel, _foundedMonth;

    private InstituteTable(ImmutableArray<string> id, ImmutableArray<string> country, ImmutableArray<int> founded, ImmutableArray<int> refYear,
        ImmutableArray<int> areaStart, ImmutableArray<string> areaDiscipline, ImmutableArray<int> areaLevel,
        ImmutableArray<int> dirStart, ImmutableArray<int> dirId, ImmutableArray<int> dirLevel, ImmutableArray<int> foundedMonth)
    {
        _foundedMonth = foundedMonth;
        _dirStart = dirStart;
        _dirId = dirId;
        _dirLevel = dirLevel;
        _id = id;
        _country = country;
        _founded = founded;
        _refYear = refYear;
        _areaStart = areaStart;
        _areaDiscipline = areaDiscipline;
        _areaLevel = areaLevel;
        using var w = new CanonicalWriter();
        w.Str("singularity-institutes/2");
        w.Int(id.Length);
        for (int i = 0; i < id.Length; i++)
        {
            w.Str(id[i]);
            w.Str(country[i]);
            w.Int(founded[i]);
            w.Int(refYear[i]);
            w.Int(foundedMonth[i]);
            w.Int(dirStart[i + 1] - dirStart[i]);
            for (int k = dirStart[i]; k < dirStart[i + 1]; k++)
            {
                w.Int(dirId[k]);
                w.Int(dirLevel[k]);
            }
            w.Int(areaStart[i + 1] - areaStart[i]);
            for (int k = areaStart[i]; k < areaStart[i + 1]; k++)
            {
                w.Str(areaDiscipline[k]);
                w.Int(areaLevel[k]);
            }
        }
        Hash = w.FinishHex();
    }

    public int Count => _id.Length;

    /// <summary>SHA-256 (hex) of the canonical form of the table.</summary>
    public string Hash { get; }

    public string Id(int row) => _id[row];

    /// <summary>Actor ID of the owner country.</summary>
    public string Country(int row) => _country[row];

    public int Founded(int row) => _founded[row];

    /// <summary>Month of the founding (1..12), 0 when the data gives only the year (then the institute counts as founded at the start of that year).</summary>
    public int FoundedMonth(int row) => _foundedMonth[row];

    public int RefYear(int row) => _refYear[row];

    public int AreaCount(int row) => _areaStart[row + 1] - _areaStart[row];

    public string AreaDiscipline(int row, int k) => _areaDiscipline[_areaStart[row] + k];

    public int AreaLevel(int row, int k) => _areaLevel[_areaStart[row] + k];

    /// <summary>Directions of the institute (numbers of <see cref="Directions.Ids"/>, ascending) with levels 1..5; empty when the mod gives none. Part of <see cref="Hash"/>.</summary>
    public int DirectionCount(int row) => _dirStart[row + 1] - _dirStart[row];

    public int DirectionAt(int row, int k) => _dirId[_dirStart[row] + k];

    public int DirectionLevel(int row, int k) => _dirLevel[_dirStart[row] + k];

    /// <summary>The level of the institute in a discipline, 0 when the area is not listed.</summary>
    public int LevelOf(int row, string discipline)
    {
        for (int k = _areaStart[row]; k < _areaStart[row + 1]; k++)
        {
            if (_areaDiscipline[k] == discipline)
                return _areaLevel[k];
        }
        return 0;
    }

    internal static InstituteTable Build(List<(string Id, YMap Map)> objects)
    {
        var id = ImmutableArray.CreateBuilder<string>();
        var country = ImmutableArray.CreateBuilder<string>();
        var founded = ImmutableArray.CreateBuilder<int>();
        var refYear = ImmutableArray.CreateBuilder<int>();
        var foundedMonth = ImmutableArray.CreateBuilder<int>();
        var areaStart = ImmutableArray.CreateBuilder<int>();
        var disc = ImmutableArray.CreateBuilder<string>();
        var level = ImmutableArray.CreateBuilder<int>();
        var dirStart = ImmutableArray.CreateBuilder<int>();
        var dirId = ImmutableArray.CreateBuilder<int>();
        var dirLevel = ImmutableArray.CreateBuilder<int>();
        dirStart.Add(0);
        areaStart.Add(0);
        // Objects arrive sorted by ID, so the rows (and the slots of an actor) are in ID order.
        foreach ((string key, YMap map) in objects)
        {
            var m = new MapReader(map);
            m.Str("id");
            if (!ModManifest.IsLocalId(key))
                throw DataLoadException.At(map, $"institute ID '{key}' must match [a-z][a-z0-9_]*");
            id.Add(key);
            country.Add(m.Str("country"));
            int f = m.Int("founded");
            int r = m.Int("ref_year");
            if (r < f)
                throw DataLoadException.At(map, $"institute '{key}': ref_year {r} is before founded {f}");
            founded.Add(f);
            int fm = m.Optional("founded_month") is { } fmNode ? Scalars.Int(fmNode) : 0;
            if (fm is < 0 or > 12)
                throw DataLoadException.At(map, $"institute '{key}': founded_month must be 1..12");
            foundedMonth.Add(fm);
            refYear.Add(r);
            m.Str("source");
            YMap areas = Scalars.Map(m.Required("areas"));
            if (areas.Entries.Count == 0)
                throw DataLoadException.At(areas, $"institute '{key}' has no area");
            // Canonical order (by discipline ID): the key order in the file must not change the hash.
            foreach (KeyValuePair<string, YNode> e in areas.Entries.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                int l = Scalars.Int(e.Value);
                if (l is < 1 or > MaxLevel)
                    throw DataLoadException.At(e.Value, $"institute '{key}': level of '{e.Key}' must be 1..{MaxLevel}");
                disc.Add(e.Key);
                level.Add(l);
            }
            areaStart.Add(disc.Count);
            foreach ((int d, int l) in Directions.ReadLevels(m.Optional("directions"), key))
            {
                dirId.Add(d);
                dirLevel.Add(l);
            }
            dirStart.Add(dirId.Count);
            m.End();
        }
        return new InstituteTable(id.ToImmutable(), country.ToImmutable(), founded.ToImmutable(), refYear.ToImmutable(), areaStart.ToImmutable(), disc.ToImmutable(), level.ToImmutable(),
            dirStart.ToImmutable(), dirId.ToImmutable(), dirLevel.ToImmutable(), foundedMonth.ToImmutable());
    }
}
