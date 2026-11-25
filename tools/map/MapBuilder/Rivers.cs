using System.Text;
using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;

namespace Singularity.MapBuilder;

internal sealed class NavigableEntry
{
    public string River { get; set; } = "";
    public List<double>? Bbox { get; set; }          // optional [min_lat, min_lon, max_lat, max_lon]: only this stretch of the river
    public string Note { get; set; } = "";
    public string Source { get; set; } = "";
    public string Accessed { get; set; } = "";
}

internal sealed class RiverSpec
{
    public int BorderMaxRank { get; set; } = 4;       // Natural Earth scalerank 1..4 counts as a border river
    public int BorderTolM { get; set; } = 3000;       // a river lies on a border when within this distance of it
    public int BorderMinSharePct { get; set; } = 50;  // share of the shared border length near the river
    public int BorderMinKm { get; set; } = 5;
    public List<NavigableEntry> Navigable { get; set; } = new();
}

internal sealed class RiverLine
{
    public string Name = "", NameEn = "";
    public int Rank;
    public bool Lake;
    public List<Coordinate[]> Parts = new();
}

/// <summary>Rivers from Natural Earth (public domain): border rivers as an attribute of adjacency pairs (#534) and the provinces along hand-listed navigable rivers.</summary>
internal static class Rivers
{
    public static List<RiverLine> Load(byte[] json)
    {
        var res = new List<RiverLine>();
        using var doc = JsonDocument.Parse(json);
        foreach (var f in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            var pr = f.GetProperty("properties");
            var rl = new RiverLine
            {
                Name = pr.GetProperty("name").GetString() ?? "",
                NameEn = pr.TryGetProperty("name_en", out var en) ? en.GetString() ?? "" : "",
                Rank = (int)pr.GetProperty("scalerank").GetDouble(),
                Lake = pr.GetProperty("featurecla").GetString() == "Lake Centerline",
            };
            var g = f.GetProperty("geometry");
            foreach (var line in g.GetProperty("type").GetString() == "LineString" ? new[] { g.GetProperty("coordinates") } : g.GetProperty("coordinates").EnumerateArray().ToArray())
                rl.Parts.Add(line.EnumerateArray().Select(p => new Coordinate(p[0].GetDouble(), p[1].GetDouble())).ToArray());
            res.Add(rl);
        }
        return res;
    }

    private static double SegDist2(double px, double py, Coordinate a, Coordinate b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
        double t = l2 == 0 ? 0 : Math.Clamp(((px - a.X) * dx + (py - a.Y) * dy) / l2, 0, 1);
        double qx = a.X + t * dx - px, qy = a.Y + t * dy - py;
        return qx * qx + qy * qy;
    }

    /// <summary>Rank (1 = largest) of the river that follows the border of every adjacent pair; pairs without a border river are absent.</summary>
    public static SortedDictionary<(string, string), int> BorderRivers(BuildResult r, List<RiverLine> rivers, RiverSpec spec)
    {
        var tree = new STRtree<(Coordinate A, Coordinate B, int Rank)>();
        foreach (var rv in rivers.Where(x => !x.Lake && x.Rank <= spec.BorderMaxRank))
            foreach (var part in rv.Parts)
                for (int i = 1; i < part.Length; i++)
                {
                    var env = new Envelope(part[i - 1], part[i]);
                    tree.Insert(env, (part[i - 1], part[i], rv.Rank));
                }
        tree.Build();
        double tolDeg = spec.BorderTolM / 111000.0;
        var total = new Dictionary<(string, string), double>();
        var near = new Dictionary<(string, string), double>();
        var rank = new Dictionary<(string, string), int>();
        foreach (var arc in r.Full.Arcs)
        {
            if (arc.Left < 0 || arc.Right < 0 || arc.Left == arc.Right) continue;
            string ia = r.Pieces[arc.Left].Id, ib = r.Pieces[arc.Right].Id;
            var key = string.CompareOrdinal(ia, ib) < 0 ? (ia, ib) : (ib, ia);
            for (int i = 1; i < arc.V.Count; i++)
            {
                var a = arc.V[i - 1]; var b = arc.V[i];
                double len = Sph.DistKm(a.Lon / 1e6, a.Lat / 1e6, b.Lon / 1e6, b.Lat / 1e6);
                double mx = (a.Lon + b.Lon) / 2e6, my = (a.Lat + b.Lat) / 2e6;
                total[key] = total.GetValueOrDefault(key) + len;
                var env = new Envelope(mx, mx, my, my); env.ExpandBy(tolDeg);
                int best = int.MaxValue;
                foreach (var s in tree.Query(env))
                    if (SegDist2(mx, my, s.A, s.B) <= tolDeg * tolDeg) best = Math.Min(best, s.Rank);
                if (best != int.MaxValue)
                {
                    near[key] = near.GetValueOrDefault(key) + len;
                    rank[key] = Math.Min(rank.GetValueOrDefault(key, int.MaxValue), best);
                }
            }
        }
        var res = new SortedDictionary<(string, string), int>(new PairComparer());
        foreach (var (k, n) in near)
            if (r.Adjacency.ContainsKey(k) && n >= spec.BorderMinKm && n * 100 >= total[k] * spec.BorderMinSharePct) res[k] = rank[k];
        return res;
    }

    // Natural Earth lines and admin-1 borders differ by a few km, so a river that runs along a border still counts for both banks.
    private const double BankTolDeg = 3000 / 111000.0;

    /// <summary>Provinces crossed by the river (optionally only inside the bbox), sorted by id.</summary>
    public static List<string> Along(BuildResult r, List<RiverLine> rivers, NavigableEntry e)
    {
        var lines = rivers.Where(x => string.Equals(x.Name, e.River, StringComparison.OrdinalIgnoreCase) || string.Equals(x.NameEn, e.River, StringComparison.OrdinalIgnoreCase)).ToList();
        if (lines.Count == 0) throw new InvalidDataException($"navigable river '{e.River}' is not in the Natural Earth rivers");
        var parts = lines.SelectMany(l => l.Parts).Select(c => (Geometry)Sph.Gf.CreateLineString(c)).ToArray();
        Geometry line = Sph.Gf.CreateMultiLineString(parts.Cast<LineString>().ToArray());
        if (e.Bbox is { Count: 4 })
        {
            var box = Sph.Gf.ToGeometry(new Envelope(e.Bbox[1], e.Bbox[3], e.Bbox[0], e.Bbox[2]));
            line = line.Intersection(box);
        }
        var bank = line.EnvelopeInternal.Copy();
        bank.ExpandBy(BankTolDeg);
        var res = new List<string>();
        if (line.IsEmpty) throw new InvalidDataException($"navigable river '{e.River}': nothing inside the bbox");
        foreach (var p in r.Pieces)
            if (p.Shape.EnvelopeInternal.Intersects(bank) && p.Shape.Buffer(BankTolDeg).Intersection(line).Length > 0.02) res.Add(p.Id);
        return res.OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    public static byte[] Write(IEnumerable<(string River, List<string> Provinces)> rows)
    {
        var sb = new StringBuilder("# Generated by tools/map from spec/rivers.yaml. Provinces along hand-listed navigable rivers (Natural Earth lines, public domain).\nnavigable:\n");
        foreach (var (river, provs) in rows)
            sb.Append(FormattableString.Invariant($"  - {{river: {river}, provinces: [{string.Join(", ", provs)}]}}\n"));
        return new UTF8Encoding(false).GetBytes(sb.ToString());
    }
}
