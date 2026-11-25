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

    private InstituteTable(ImmutableArray<string> id, ImmutableArray<string> country, ImmutableArray<int> founded, ImmutableArray<int> refYear,
        ImmutableArray<int> areaStart, ImmutableArray<string> areaDiscipline, ImmutableArray<int> areaLevel)
    {
        _id = id;
        _country = country;
        _founded = founded;
        _refYear = refYear;
        _areaStart = areaStart;
        _areaDiscipline = areaDiscipline;
        _areaLevel = areaLevel;
        using var w = new CanonicalWriter();
        w.Str("singularity-institutes/1");
        w.Int(id.Length);
        for (int i = 0; i < id.Length; i++)
        {
            w.Str(id[i]);
            w.Str(country[i]);
            w.Int(founded[i]);
            w.Int(refYear[i]);
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

    public int RefYear(int row) => _refYear[row];

    public int AreaCount(int row) => _areaStart[row + 1] - _areaStart[row];

    public string AreaDiscipline(int row, int k) => _areaDiscipline[_areaStart[row] + k];

    public int AreaLevel(int row, int k) => _areaLevel[_areaStart[row] + k];

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
        var areaStart = ImmutableArray.CreateBuilder<int>();
        var disc = ImmutableArray.CreateBuilder<string>();
        var level = ImmutableArray.CreateBuilder<int>();
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
            m.End();
        }
        return new InstituteTable(id.ToImmutable(), country.ToImmutable(), founded.ToImmutable(), refYear.ToImmutable(), areaStart.ToImmutable(), disc.ToImmutable(), level.ToImmutable());
    }
}
