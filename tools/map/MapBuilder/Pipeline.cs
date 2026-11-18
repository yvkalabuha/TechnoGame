using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;

namespace Singularity.MapBuilder;

/// <summary>An intermediate or final province polygon with its provenance.</summary>
internal sealed class Piece
{
    public Geometry Shape = Sph.Gf.CreatePolygon();
    public SortedSet<string> Src = new(StringComparer.Ordinal);
    public string Adm0 = "";
    public string Admin = "";
    public string Name = "";
    public double NameArea;
    public List<string> Cuts = new();
    public List<string> Sides = new();   // "cut_id:hint" of every cut that made this piece (event-line CI)
    public bool ClassA;
    public string? Ref;
    public string RegionHint = "";    // cut rule 'regions': the region this piece must form (#350)
    public string Hint = "";          // naming hint for bootstrap (cut piece suffix or subdivide index)
    public string? SubBase;           // subdivide: base id; id = base_k
    public int SubK;                  // 1-based, by (lat desc, lon asc) of seeds
    public bool Sparse;
    public string Id = "";            // set by anchor assignment
    public long AreaKm2;
}

internal static class Pipeline
{
    public static List<Piece> Initial(List<Atom> atoms) => atoms.Select(a => new Piece
    {
        Shape = a.Shape, Src = new SortedSet<string>(new[] { a.Code }, StringComparer.Ordinal), Adm0 = a.Adm0,
        Admin = a.Admin, Name = a.Name, NameArea = a.AreaM2,
    }).ToList();

    public static List<Piece> Apply(List<Piece> pieces, SpecSet spec, Func<string, OhmShape> ohm)
    {
        foreach (var (file, rule) in spec.Rules)
        {
            if (rule.Merge != null) pieces = DoMerge(pieces, rule.Merge, file);
            else if (rule.Cut != null) pieces = DoCut(pieces, rule.Cut, ohm, file);
            else if (rule.Subdivide != null) pieces = DoSubdivide(pieces, rule.Subdivide, file);
            else if (rule.Keep != null) DoKeep(pieces, rule.Keep, file);
            else if (rule.Isolate != null) Select(pieces, rule.Isolate.Codes, $"{file} isolate");   // checked only; used by bootstrap
            else throw new InvalidDataException($"{file}: empty rule");
        }
        Heal(pieces, spec.Area.KnownGaps);
        return pieces;
    }

    /// <summary>
    /// A cut or Voronoi edge that ends on a shared border adds a vertex to one polygon only, so the neighbour's edge no longer matches.
    /// The modified pieces and their neighbours are re-noded together: all their boundaries are noded at once (snap rounding to the grid)
    /// and polygonised, and each face goes back to the piece that contained it.
    /// </summary>
    internal static void Heal(List<Piece> pieces, List<List<double>>? knownGaps = null)
    {
        var lost = new List<string>();
        var tree = new NetTopologySuite.Index.Strtree.STRtree<int>();
        for (int i = 0; i < pieces.Count; i++) tree.Insert(pieces[i].Shape.EnvelopeInternal, i);
        tree.Build();
        var group = new SortedSet<int>();
        for (int i = 0; i < pieces.Count; i++)
        {
            if (pieces[i].Cuts.Count == 0 && pieces[i].SubBase == null) continue;
            group.Add(i);
            foreach (var j in tree.Query(pieces[i].Shape.EnvelopeInternal))
                if (j != i && pieces[j].Shape.Intersects(pieces[i].Shape)) group.Add(j);
        }
        if (group.Count == 0) return;
        double before = group.Sum(i => Sph.AreaM2(pieces[i].Shape));
        var lines = group.Select(i => pieces[i].Shape.Boundary).ToArray();
        var noded = UnaryUnionNG.Union(lines, Sph.Gf, Sph.Pm);
        var pg = new NetTopologySuite.Operation.Polygonize.Polygonizer();
        pg.Add(noded);
        var faces = new Dictionary<int, List<Polygon>>();
        foreach (Polygon f in pg.GetPolygons())
        {
            var pt = f.InteriorPoint.Coordinate;
            bool owned = false;
            foreach (var i in group)
            {
                if (NetTopologySuite.Algorithm.Locate.SimplePointInAreaLocator.Locate(pt, pieces[i].Shape) == Location.Exterior) continue;
                if (!faces.TryGetValue(i, out var l)) faces[i] = l = new List<Polygon>();
                l.Add(f);
                owned = true;
                break;
            }
            // A face owned by no group piece is either an enclave held by another piece (a hole in the group) or a pre-existing NE gap; a large one means the noding went wrong.
            // A gap listed in area.yaml (known_gaps: [lat, lon] inside it) is a hole of the NE data itself, for instance an undetermined border in an ice field.
            if (!owned && !pieces.Any(q => NetTopologySuite.Algorithm.Locate.SimplePointInAreaLocator.Locate(pt, q.Shape) != Location.Exterior) && Sph.PolygonArea(f) > 5e6
                && !(knownGaps?.Any(g => g.Count == 2 && f.Contains(Sph.Gf.CreatePoint(new Coordinate(g[1], g[0])))) ?? false))
                lost.Add($"{Sph.PolygonArea(f) / 1e6:F1} km2 near {pt.Y:F3},{pt.X:F3}");
        }
        if (lost.Count > 0) throw new InvalidDataException($"Heal dropped a face of {string.Join("; ", lost)} (a gap of the data: list it in area.yaml known_gaps, or the noding went wrong)");
        foreach (var i in group)
        {
            if (!faces.TryGetValue(i, out var l)) throw new InvalidDataException($"Heal: no face found for a piece of {string.Join(",", pieces[i].Src)} (cuts {string.Join(",", pieces[i].Cuts)}, {Sph.AreaM2(pieces[i].Shape) / 1e6:F1} km2, {pieces[i].Shape.NumGeometries} parts, valid {pieces[i].Shape.IsValid})");
            pieces[i].Shape = Sph.FromPolygons(l);
        }
        // Faces that belong to no piece were dropped: the total area must not change beyond rounding.
        double after = group.Sum(i => Sph.AreaM2(pieces[i].Shape));
        if (Math.Abs(after - before) > before * 1e-6)
            throw new InvalidDataException($"Heal changed the area of {group.Count} pieces from {before:F0} to {after:F0} m2");
    }

    private static List<int> Select(List<Piece> pieces, IEnumerable<string> codes, string what)
    {
        var set = codes.ToHashSet(StringComparer.Ordinal);
        var idx = new List<int>();
        for (int i = 0; i < pieces.Count; i++)
            if (pieces[i].Src.Overlaps(set)) idx.Add(i);
        if (idx.Count == 0) throw new InvalidDataException($"{what}: no piece contains any of [{string.Join(", ", set.OrderBy(x => x, StringComparer.Ordinal))}]");
        var found = idx.SelectMany(i => pieces[i].Src).ToHashSet(StringComparer.Ordinal);
        var missing = set.Where(c => !found.Contains(c)).OrderBy(c => c, StringComparer.Ordinal).ToList();
        if (missing.Count > 0) throw new InvalidDataException($"{what}: adm1_code without a piece: {string.Join(", ", missing)}");
        return idx;
    }

    private static List<Piece> DoMerge(List<Piece> pieces, MergeRule r, string file)
    {
        var idx = Select(pieces, r.From, $"{file} merge");
        if (idx.Count == 1) return pieces;
        var group = idx.Select(i => pieces[i]).ToList();
        var big = group.OrderByDescending(p => p.NameArea).ThenBy(p => p.Name, StringComparer.Ordinal).First();
        var merged = new Piece
        {
            Shape = NetTopologySuite.Coverage.CoverageUnion.Union(group.Select(p => p.Shape).ToArray()),
            Adm0 = big.Adm0, Admin = big.Admin, Name = big.Name, NameArea = group.Sum(p => p.NameArea),
        };
        foreach (var p in group) { merged.Src.UnionWith(p.Src); merged.Cuts.AddRange(p.Cuts); merged.Sides.AddRange(p.Sides); }
        merged.Shape = Sph.Overlay(merged.Shape, merged.Shape, SpatialFunction.Union);
        var res = new List<Piece>(pieces.Count);
        var set = idx.ToHashSet();
        for (int i = 0; i < pieces.Count; i++)
        {
            if (i == idx[0]) res.Add(merged);
            else if (!set.Contains(i)) res.Add(pieces[i]);
        }
        return res;
    }

    private static void DoKeep(List<Piece> pieces, KeepRule k, string file)
    {
        if (k.Class != "A" || string.IsNullOrWhiteSpace(k.Ref)) throw new InvalidDataException($"{file} keep: class A and ref are required");
        foreach (var i in Select(pieces, k.Codes, $"{file} keep")) { pieces[i].ClassA = true; pieces[i].Ref = k.Ref; }
    }

    // ---- cuts ----

    private static Geometry Rect(double x0, double y0, double x1, double y1) =>
        Sph.Gf.CreatePolygon(new[] { new Coordinate(x0, y0), new Coordinate(x1, y0), new Coordinate(x1, y1), new Coordinate(x0, y1), new Coordinate(x0, y0) });

    /// <summary>First piece = south / west / inside, second = north / east / outside.</summary>
    private static Geometry Cutter(CutRule c, Func<string, OhmShape> ohm, string file)
    {
        if (c.Parallel is double phi) return Rect(-181, -91, 181, phi);
        if (c.Meridian is double lam) return Rect(-181, -91, lam, 91);
        if (c.Ohm != null)
        {
            var area = ohm(c.Ohm).Area;
            // A snapshot without outer ways (for instance a 'chronology' relation that only lists other relations) would cut nothing, silently.
            if (Sph.AreaM2(area) <= 0) throw new InvalidDataException($"{file} cut {c.Id}: OHM snapshot {c.Ohm} has no area");
            return area;
        }
        throw new InvalidDataException($"{file} cut {c.Id}: one of parallel, meridian, ohm is required ('line' files are not used yet)");
    }

    private static List<Piece> DoCut(List<Piece> pieces, CutRule c, Func<string, OhmShape> ohm, string file)
    {
        if (c.Id == "" || c.Source == "" || c.AsOf == "" || c.Precision is not ("A" or "B" or "C"))
            throw new InvalidDataException($"{file} cut {c.Id}: id, source, as_of and precision A/B/C are required");
        if (c.Precision == "A" && string.IsNullOrWhiteSpace(c.Ref))
            throw new InvalidDataException($"{file} cut {c.Id}: class A needs a ref");
        var idx = c.Within.Count > 0 ? Select(pieces, c.Within, $"{file} cut {c.Id}") : new List<int>();
        if (c.WithinAdm0.Count > 0)
        {
            var extra = Enumerable.Range(0, pieces.Count).Where(i => c.WithinAdm0.Contains(pieces[i].Adm0)).ToList();
            if (extra.Count == 0) throw new InvalidDataException($"{file} cut {c.Id}: no piece in {string.Join(", ", c.WithinAdm0)}");
            idx = idx.Union(extra).OrderBy(i => i).ToList();
        }
        if (idx.Count == 0) throw new InvalidDataException($"{file} cut {c.Id}: within or within_adm0 is required");
        var cutter = Cutter(c, ohm, file);
        // a_pieces must name existing hints: a typo would silently leave the cut without class A.
        var hints = c.Pieces.Count > 0 ? c.Pieces : new List<string> { "a", "b" };
        foreach (var h in c.APieces)
            if (!hints.Contains(h)) throw new InvalidDataException($"{file} cut {c.Id}: a_pieces '{h}' is not one of the pieces ({string.Join(", ", hints)})");
        var res = new List<Piece>(pieces.Count + 4);
        var set = idx.ToHashSet();
        for (int i = 0; i < pieces.Count; i++)
        {
            if (!set.Contains(i)) { res.Add(pieces[i]); continue; }
            var p = pieces[i];
            var a = Sph.Overlay(p.Shape, cutter, SpatialFunction.Intersection);
            var b = Sph.Overlay(p.Shape, cutter, SpatialFunction.Difference);
            (a, b) = AbsorbSlivers(a, b, c.MinPieceKm2, p.Shape);
            if (a.IsEmpty || b.IsEmpty)
            {
                // The piece lies wholly on one side: it keeps its geometry but is still a member of that side of the line (event-line CI, region hint).
                int side = a.IsEmpty ? 1 : 0;
                var hint = c.Pieces.Count > side ? c.Pieces[side] : (side == 0 ? "a" : "b");
                p.Sides.Add($"{c.Id}:{hint}");
                if (c.Regions.Count > side && c.Regions[side] != "" && !(c.KeepRegions && p.RegionHint != "")) p.RegionHint = c.Regions[side];
                res.Add(p);
                continue;
            }
            for (int k = 0; k < 2; k++)
            {
                var q = Clone(p, k == 0 ? a : b);
                q.Cuts.Add(c.Id);
                q.Hint = c.Pieces.Count > k ? c.Pieces[k] : (k == 0 ? "a" : "b");
                if (c.Regions.Count > k && c.Regions[k] != "" && !(c.KeepRegions && q.RegionHint != "")) q.RegionHint = c.Regions[k];
                q.Sides.Add($"{c.Id}:{q.Hint}");
                // Class A marks only the piece(s) the cut was made for (a_pieces; both when absent).
                if (c.Precision == "A" && (c.APieces.Count == 0 || c.APieces.Contains(q.Hint))) { q.ClassA = true; q.Ref = c.Ref; }
                res.Add(q);
            }
        }
        return res;
    }

    private static Piece Clone(Piece p, Geometry g) => new()
    {
        Shape = g, Src = new SortedSet<string>(p.Src, StringComparer.Ordinal), Adm0 = p.Adm0, Admin = p.Admin, Name = p.Name, NameArea = p.NameArea,
        Cuts = new List<string>(p.Cuts), Sides = new List<string>(p.Sides), RegionHint = p.RegionHint, Sparse = p.Sparse, SubBase = p.SubBase, SubK = p.SubK,
    };

    /// <summary>Polygons smaller than the threshold move to the other side so that a cut never leaves slivers.</summary>
    private static (Geometry, Geometry) AbsorbSlivers(Geometry a, Geometry b, double minKm2, Geometry whole)
    {
        var pa = Sph.Polygons(a).ToList();
        var pb = Sph.Polygons(b).ToList();
        bool Small(Polygon p) => Sph.PolygonArea(p) / 1e6 < minKm2;
        // A side made only of small polygons is the noise of a line that does not cross the piece: the piece stays whole on the other side.
        if (pa.All(Small)) return (Sph.Gf.CreatePolygon(), whole);
        if (pb.All(Small)) return (whole, Sph.Gf.CreatePolygon());
        var moveA = pa.Where(Small).ToList();
        var moveB = pb.Where(Small).ToList();
        if (moveA.Count == 0 && moveB.Count == 0) return (a, b);
        var na = pa.Where(p => !Small(p)).Concat(moveB).ToList();
        var nb = pb.Where(p => !Small(p)).Concat(moveA).ToList();
        return (UnionAll(na), UnionAll(nb));
    }

    internal static Geometry UnionAll(List<Polygon> polys)
    {
        if (polys.Count == 0) return Sph.Gf.CreatePolygon();
        Geometry acc = polys[0];
        for (int i = 1; i < polys.Count; i++) acc = Sph.Overlay(acc, polys[i], SpatialFunction.Union);
        return acc;
    }

    // ---- subdivide ----

    private static List<Piece> DoSubdivide(List<Piece> pieces, SubdivideRule r, string file)
    {
        if (r.Parts < 2 || r.Base == "") throw new InvalidDataException($"{file} subdivide: parts >= 2 and base are required");
        var idx = Select(pieces, r.Within, $"{file} subdivide");
        if (idx.Count != 1) throw new InvalidDataException($"{file} subdivide: expected exactly one piece, found {idx.Count}");
        var p = pieces[idx[0]];
        var cells = r.Method == "bisect" ? Subdivision.Bisect(p.Shape, r.Parts) : Subdivision.Voronoi(p.Shape, r.Parts);
        var res = new List<Piece>(pieces.Count + r.Parts);
        for (int i = 0; i < pieces.Count; i++)
        {
            if (i != idx[0]) { res.Add(pieces[i]); continue; }
            for (int k = 0; k < cells.Count; k++)
            {
                var q = Clone(p, cells[k]);
                q.SubBase = r.Base; q.SubK = k + 1; q.Hint = $"{r.Base}_{k + 1}"; q.Sparse = r.Sparse;
                res.Add(q);
            }
        }
        return res;
    }
}
