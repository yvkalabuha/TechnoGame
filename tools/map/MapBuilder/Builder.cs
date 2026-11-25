using NetTopologySuite.Algorithm;
using NetTopologySuite.Coverage;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;

namespace Singularity.MapBuilder;

internal sealed class BuildOptions
{
    public string Root = "";            // tools/map
    public string Cache = "";
    public string OutDir = "";          // mods/core/map
    public string ScenarioDir = "";     // folder with polities.yaml and owners.yaml
    public double SimplifyTolerance = 0.01;
}

internal sealed class BuildResult
{
    public List<Piece> Pieces = new();          // sorted by id
    public Topology Full = new(), Simple = new();
    public Geometry[] Simplified = Array.Empty<Geometry>();
    public List<bool> Coastal = new();
    public SortedDictionary<(string, string), double> Adjacency = new(new PairComparer());
    public SortedDictionary<(string, string), int> BorderRivers = new(new PairComparer());   // adjacent pair -> Natural Earth rank of the river on the border
    public List<(string River, List<string> Provinces)> Navigable = new();
    public List<(string A, string B)> Straits = new();
    public List<string> Errors = new(), Warnings = new(), Info = new();
    public SourceLock Lock = new();
    public List<PolityEntry> Polities = new();
    public Dictionary<string, Resolved> Owners = new(StringComparer.Ordinal);
    public SpecSet Spec = new();
    public Dictionary<string, OhmShape> Ohm = new(StringComparer.Ordinal);
    public HashSet<string> EuropeCodes = new(StringComparer.Ordinal);   // adm1 codes of the legacy Europe selection
}

internal sealed class PairComparer : IComparer<(string, string)>
{
    public int Compare((string, string) x, (string, string) y)
    {
        int c = string.CompareOrdinal(x.Item1, y.Item1);
        return c != 0 ? c : string.CompareOrdinal(x.Item2, y.Item2);
    }
}

internal static class Builder
{
    public const string NeAdmin1 = "ne_admin1";
    public const string NeUnits = "ne_map_units";
    public const string NeRivers = "ne_rivers";

    public static BuildResult Build(BuildOptions o)
    {
        var res = new BuildResult();
        res.Lock = Sources.Load(Path.Combine(o.Root, "sources.lock.yaml"));
        res.Spec = SpecSet.Load(Path.Combine(o.Root, "spec"));
        var ne = Sources.ReadVerified(res.Lock, o.Cache, NeAdmin1);
        var units = Sources.ReadVerified(res.Lock, o.Cache, NeUnits);

        OhmShape Ohm(string id)
        {
            if (!res.Ohm.TryGetValue(id, out var s))
            {
                s = Data.LoadOhm(Sources.ReadVerified(res.Lock, o.Cache, id), id);
                res.Ohm[id] = s;
            }
            return s;
        }

        var atoms = Data.LoadNe(ne, res.Spec.Area);
        res.Info.Add($"atoms: {atoms.Count}");
        res.EuropeCodes = atoms.Where(a => a.Europe).Select(a => a.Code).ToHashSet(StringComparer.Ordinal);
        var pieces = Pipeline.Apply(Pipeline.Initial(atoms), res.Spec, Ohm);
        var unanchored = new List<Piece>();
        res.Errors.AddRange(Ids.Assign(pieces, res.Spec.Ids, unanchored));
        foreach (var id in res.Spec.Ids.Select(i => i.Id).Where(id => !pieces.Any(p => p.Id == id)))
            res.Errors.Add($"anchor '{id}' has no piece");
        if (res.Errors.Count > 0) { res.Pieces = pieces; return res; }

        pieces = pieces.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
        foreach (var p in pieces) p.AreaKm2 = Sph.AreaKm2(p.Shape);
        res.Pieces = pieces;
        var geoms = pieces.Select(p => p.Shape).ToArray();

        // Coverage: no overlaps, no mismatched edges, no gaps.
        var inv = CoverageValidator.Validate(geoms);
        for (int i = 0; i < inv.Length; i++)
            if (inv[i] != null && !inv[i].IsEmpty) res.Errors.Add($"coverage: {pieces[i].Id} has {inv[i].NumGeometries} invalid edge(s) (overlap or mismatch)");
        var gaps = CoverageGapFinder.FindGaps(geoms, 0.01);
        for (int g = 0; g < (gaps?.NumGeometries ?? 0); g++)
        {
            var gg = gaps!.GetGeometryN(g);
            var c = gg.Centroid;
            res.Warnings.Add($"coverage gap of width < 0.01 deg near lat {Sph.F(c.Y)} lon {Sph.F(c.X)} (length {Sph.F(gg.Length)} deg)");
        }

        res.Full = Topology.Build(geoms);
        res.Simplified = CoverageSimplifier.Simplify(geoms, o.SimplifyTolerance);
        var inv2 = CoverageValidator.Validate(res.Simplified);
        if (inv2.Any(x => x != null && !x.IsEmpty)) res.Errors.Add("coverage: simplified coverage is invalid");
        res.Simple = Topology.Build(res.Simplified);
        // Rings rebuilt from the shared arcs must give back every simplified polygon (no gaps, no overlaps in arcs.bin).
        for (int p = 0; p < pieces.Count; p++)
        {
            double a = 0;
            foreach (var ring in res.Simple.Rings[p])
            {
                var cs = res.Simple.RingCoordinates(ring);
                a += Orientation.IsCCW(cs.ToArray()) ? Sph.RingArea(cs.ToArray()) : -Sph.RingArea(cs.ToArray());
            }
            if (Math.Abs(a - Sph.AreaM2(res.Simplified[p])) > Sph.AreaM2(res.Simplified[p]) * 1e-9 + 1)
            {
                var g0 = res.Simplified[p];
                res.Errors.Add($"arcs: rings of {pieces[p].Id} do not rebuild its polygon ({a} vs {Sph.AreaM2(g0)})");
            }
        }

        // Adjacency: shared border length >= 1 km (a touch point does not count).
        var len = new SortedDictionary<(string, string), double>(new PairComparer());
        foreach (var a in res.Full.Arcs)
        {
            if (a.Left < 0 || a.Right < 0 || a.Left == a.Right) continue;
            var k = string.CompareOrdinal(pieces[a.Left].Id, pieces[a.Right].Id) < 0 ? (pieces[a.Left].Id, pieces[a.Right].Id) : (pieces[a.Right].Id, pieces[a.Left].Id);
            len[k] = len.GetValueOrDefault(k) + a.LengthKm;
        }
        foreach (var kv in len) if (kv.Value >= 1.0) res.Adjacency[kv.Key] = kv.Value;
        var riverSpecPath = Path.Combine(o.Root, "spec", "rivers.yaml");
        var riverSpec = File.Exists(riverSpecPath) ? SpecSet.Read<RiverSpec>(riverSpecPath) : new RiverSpec();
        var rivers = Rivers.Load(Sources.ReadVerified(res.Lock, o.Cache, NeRivers));
        res.BorderRivers = Rivers.BorderRivers(res, rivers, riverSpec);
        foreach (var n in riverSpec.Navigable.OrderBy(n => n.River, StringComparer.Ordinal))
        {
            if (n.Source.Length == 0 || n.Accessed.Length == 0) res.Errors.Add($"rivers: navigable '{n.River}' has no source or access date");
            else res.Navigable.Add((n.River, Rivers.Along(res, rivers, n)));
        }
        foreach (var s in res.Spec.Straits)
            res.Straits.Add(string.CompareOrdinal(s.A, s.B) < 0 ? (s.A, s.B) : (s.B, s.A));
        res.Straits = res.Straits.OrderBy(s => s, new PairComparer()).ToList();

        res.Coastal = Coastal(res, Data.LoadNe(ne, null).Where(a => !atoms.Any(x => x.Code == a.Code)).ToList());

        var polFile = Path.Combine(o.ScenarioDir, "polities.yaml");
        var ownFile = Path.Combine(o.ScenarioDir, "owners.yaml");
        if (File.Exists(polFile)) res.Polities = SpecSet.Read<PolityFile>(polFile).Polities;
        var adm0 = Data.Adm0Codes(units);
        OwnerFile? owners = File.Exists(ownFile) ? SpecSet.Read<OwnerFile>(ownFile) : null;
        Validator.Run(res, atoms, adm0, owners, o);
        return res;
    }

    /// <summary>A province is coastal if an arc between it and "outside" is at least 1 km long and is not next to land outside the selected area.</summary>
    private static List<bool> Coastal(BuildResult r, List<Atom> otherLand)
    {
        var tree = new STRtree<Geometry>();
        foreach (var a in otherLand) tree.Insert(a.Shape.EnvelopeInternal, a.Shape);
        tree.Build();
        var km = new double[r.Pieces.Count];
        foreach (var arc in r.Full.Arcs)
        {
            if (arc.Left >= 0 == arc.Right >= 0) continue;
            int p = arc.Left >= 0 ? arc.Left : arc.Right;
            double sea = 0;
            for (int i = 1; i < arc.V.Count; i++)
            {
                var a = arc.V[i - 1]; var b = arc.V[i];
                var mid = new Coordinate((a.Lon + b.Lon) / 2e6, (a.Lat + b.Lat) / 2e6);
                var env = new Envelope(mid); env.ExpandBy(0.003);
                var pt = Sph.Gf.CreatePoint(mid);
                bool land = tree.Query(env).Any(g => g.IsWithinDistance(pt, 0.003));
                if (!land) sea += Sph.DistKm(a.Lon / 1e6, a.Lat / 1e6, b.Lon / 1e6, b.Lat / 1e6);
            }
            km[p] += sea;
        }
        return km.Select(v => v >= 1.0).ToList();
    }
}
