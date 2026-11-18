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
    public double? Parallel { get; set; }
    public double? Meridian { get; set; }
    public string? Ohm { get; set; }          // lock id of an OHM snapshot; the polygon splits the object into inside and outside
    public string? Line { get; set; }         // spec/cuts/<id>.geojson (polygon or lines)
    public List<string> APieces { get; set; } = new(); // hints of the pieces flagged class A; empty = both
    public List<string> Pieces { get; set; } = new(); // naming hints for bootstrap: [south|west|inside, north|east|outside]
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

internal sealed class Rule
{
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

internal sealed class SpecSet
{
    public AreaSpec Area = new();
    public List<(string File, Rule Rule)> Rules = new();
    public List<IdEntry> Ids = new();
    public List<RegionEntry> Regions = new();
    public List<StraitEntry> Straits = new();
    public string Dir = "";

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
            if (name == "area.yaml" || name == "straits.yaml") continue;
            foreach (var r in Read<RuleFile>(f).Rules) s.Rules.Add((name, r));
        }
        foreach (var f in Sorted(Path.Combine(dir, "ids"))) s.Ids.AddRange(Read<IdFile>(f).Ids);
        foreach (var f in Sorted(Path.Combine(dir, "regions"))) s.Regions.AddRange(Read<RegionFile>(f).Regions);
        var st = Path.Combine(dir, "straits.yaml");
        if (File.Exists(st)) s.Straits.AddRange(Read<StraitFile>(st).Straits);
        return s;
    }

    public static IEnumerable<string> Sorted(string dir) =>
        Directory.Exists(dir) ? Directory.GetFiles(dir, "*.yaml").OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal) : Array.Empty<string>();
}
