using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Singularity.MapBuilder;

// Spec files are the project's own work (ADR-0009): geometry only, no owners (ADR-0011 §2.2).

internal sealed class AreaSpec
{
    public List<string> Adm0 { get; set; } = new();
    public List<double> Lon { get; set; } = new();
    public List<double> Lat { get; set; } = new();
    public List<string> Drop { get; set; } = new();
    public double MinAreaKm2 { get; set; } = 5000;
    public double MaxAreaKm2 { get; set; } = 150000;
    public double SparseMaxKm2 { get; set; } = 500000;
    /// <summary>World build (TASK-0091): every NE admin-1 object outside <see cref="WorldDropAdm0"/> is an atom; the Europe window above marks the legacy Europe set.</summary>
    public bool World { get; set; }
    public List<string> WorldDropAdm0 { get; set; } = new();
    /// <summary>Region size unit for the clustering of world atoms: k = round(sqrt(area / unit)) per country.</summary>
    public double WorldRegionUnitKm2 { get; set; } = 400000;
    /// <summary>[lat, lon] points inside holes of the NE data that no province covers; Heal accepts a face that holds one of them.</summary>
    public List<List<double>> KnownGaps { get; set; } = new();
}

internal sealed class MergeRule { public List<string> From { get; set; } = new(); }

internal sealed class KeepRule
{
    public List<string> Codes { get; set; } = new();
    public string Class { get; set; } = "";
    public string Ref { get; set; } = "";
    public string Reason { get; set; } = "";
}

internal sealed class CutRule
{
    public string Id { get; set; } = "";
    public List<string> Within { get; set; } = new();
    public List<string> WithinAdm0 { get; set; } = new(); // every piece of these NE countries (used for lines that run through whole countries)
    public double? Parallel { get; set; }
    public double? Meridian { get; set; }
    public string? Ohm { get; set; }          // lock id of an OHM snapshot; the polygon splits the object into inside and outside
    public string? Line { get; set; }         // spec/cuts/<id>.geojson (polygon or lines)
    public List<string> APieces { get; set; } = new(); // hints of the pieces flagged class A; empty = both
    public List<string> Pieces { get; set; } = new(); // naming hints for bootstrap: [south|west|inside, north|east|outside]
    public bool KeepRegions { get; set; }               // a piece that already has a region from an earlier cut keeps it
    public List<string> Regions { get; set; } = new(); // bootstrap: region of each piece ("" = keep the sibling or cluster region); #350 needs one region per side
    public string Source { get; set; } = "";
    public string AsOf { get; set; } = "";
    public string Precision { get; set; } = "";
    public string? Ref { get; set; }
    public double MinPieceKm2 { get; set; } = 40;
}

internal sealed class SubdivideRule
{
    public List<string> Within { get; set; } = new();
    public int Parts { get; set; }
    public string Base { get; set; } = "";
    public string Method { get; set; } = "voronoi";
    public bool Sparse { get; set; }
}

/// <summary>Bootstrap-only: the units holding these codes form (part of) one region. The geometry is not touched.</summary>
internal sealed class IsolateRule
{
    public List<string> Codes { get; set; } = new();
    public string Region { get; set; } = "";
    public string Ref { get; set; } = "";
}

internal sealed class Rule
{
    public IsolateRule? Isolate { get; set; }
    public MergeRule? Merge { get; set; }
    public CutRule? Cut { get; set; }
    public SubdivideRule? Subdivide { get; set; }
    public KeepRule? Keep { get; set; }
}

internal sealed class RuleFile { public List<Rule> Rules { get; set; } = new(); }

internal sealed class IdEntry
{
    public string Id { get; set; } = "";
    public int[] Anchor { get; set; } = Array.Empty<int>(); // [lat_udeg, lon_udeg]
}
internal sealed class IdFile { public List<IdEntry> Ids { get; set; } = new(); }

internal sealed class RegionEntry
{
    public string Id { get; set; } = "";
    public string Capital { get; set; } = "";
    public bool Isolated { get; set; }
}
internal sealed class RegionFile { public List<RegionEntry> Regions { get; set; } = new(); }

internal sealed class StraitEntry
{
    public string A { get; set; } = "";
    public string B { get; set; } = "";
    public string Source { get; set; } = "";
}
internal sealed class StraitFile { public List<StraitEntry> Straits { get; set; } = new(); }

/// <summary>One side of an ownership-event line: provinces that change owner or controller together (ADR-0020 §2.9).</summary>
internal sealed class EventSet
{
    public string Name { get; set; } = "";
    public List<string> Adm0 { get; set; } = new();     // NE ADM0_A3 of the province
    public List<string> Ids { get; set; } = new();      // province ids; a trailing '*' is a prefix (depends on the region id: use codes or cuts for lines inside a country)
    public List<string> Codes { get; set; } = new();    // NE adm1 codes the province was made from (Piece.Src): independent of the region the province ended up in
    public List<string> Cuts { get; set; } = new();     // "cut_id:hint" of a cut piece
    public List<string> ExceptIds { get; set; } = new(); // province ids (trailing * = prefix) removed from the selection above
    public List<string> ExceptCuts { get; set; } = new();  // "cut_id:piece" removed from the selection above
    public List<string> ExceptCodes { get; set; } = new(); // NE adm1 codes removed from the selection above
}

internal sealed class EventEntry
{
    public string Id { get; set; } = "";
    public string Date { get; set; } = "";
    public string Rr { get; set; } = "";                // RR-0020 row
    public List<EventSet> Sets { get; set; } = new();
    public string? NotDrawn { get; set; }               // reason: the line is not on the map (no acceptable source)
}
internal sealed class EventFile { public List<EventEntry> Events { get; set; } = new(); }

/// <summary>A small territory with ownership but no geometry (ADR-0020 §1.4): own row in the province table, attached to a region.</summary>
internal sealed class TerritoryEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public double ApproxKm2 { get; set; }
    public string Ref { get; set; } = "";
}
internal sealed class TerritoryFile { public List<TerritoryEntry> Territories { get; set; } = new(); }

internal sealed class SpecSet
{
    public List<EventEntry> Events = new();
    public List<TerritoryEntry> Territories = new();
    public AreaSpec Area = new();
    public List<(string File, Rule Rule)> Rules = new();
    public List<IdEntry> Ids = new();
    public List<RegionEntry> Regions = new();
    public List<StraitEntry> Straits = new();
    public string Dir = "";
    public GreatPowerFile? GreatPowers;

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance).Build();

    public static T Read<T>(string path) => Yaml.Deserialize<T>(File.ReadAllText(path));

    /// <summary>Files are applied in ordinal file-name order, then in rule order (ADR-0011 §2.2).</summary>
    public static SpecSet Load(string dir)
    {
        var s = new SpecSet { Dir = dir, Area = Read<AreaSpec>(Path.Combine(dir, "area.yaml")) };
        foreach (var f in Directory.GetFiles(dir, "*.yaml").OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal))
        {
            var name = Path.GetFileName(f);
            if (name is "area.yaml" or "straits.yaml" or "territories.yaml" or "great_powers.yaml" or "rivers.yaml" or "terrain.yaml" or "terrain_1945.yaml") continue;
            foreach (var r in Read<RuleFile>(f).Rules) s.Rules.Add((name, r));
        }
        foreach (var f in Sorted(Path.Combine(dir, "ids"))) s.Ids.AddRange(Read<IdFile>(f).Ids);
        foreach (var f in Sorted(Path.Combine(dir, "regions"))) s.Regions.AddRange(Read<RegionFile>(f).Regions);
        foreach (var f in Sorted(Path.Combine(dir, "events"))) s.Events.AddRange(Read<EventFile>(f).Events);
        var tf = Path.Combine(dir, "territories.yaml");
        if (File.Exists(tf)) s.Territories.AddRange(Read<TerritoryFile>(tf).Territories);
        var gp = Path.Combine(dir, "great_powers.yaml");
        if (File.Exists(gp)) s.GreatPowers = Read<GreatPowerFile>(gp);
        var st = Path.Combine(dir, "straits.yaml");
        if (File.Exists(st)) s.Straits.AddRange(Read<StraitFile>(st).Straits);
        return s;
    }

    public static IEnumerable<string> Sorted(string dir) =>
        Directory.Exists(dir) ? Directory.GetFiles(dir, "*.yaml").OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal) : Array.Empty<string>();
}
