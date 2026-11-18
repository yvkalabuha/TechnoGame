using System.Text.RegularExpressions;
using NetTopologySuite.Algorithm.Locate;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;

namespace Singularity.MapBuilder;

/// <summary>Anchor-based province ids (ADR-0011 §1): a piece takes the id of the anchor inside it; zero or several anchors are errors.</summary>
internal static class Ids
{
    public static readonly Regex Part = new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled);

    public static bool ValidRegionId(string id) => Part.IsMatch(id);

    public static bool ValidProvinceId(string id)
    {
        int dot = id.IndexOf('.');
        return dot > 0 && ValidRegionId(id[..dot]) && Part.IsMatch(id[(dot + 1)..]) && id.Length - dot - 1 <= 48;
    }

    public static Coordinate AnchorPoint(int[] a) => new(a[1] / 1e6, a[0] / 1e6);

    /// <summary>Returns the errors; fills Piece.Id where the match is unique. Pieces left without id are listed in <paramref name="unanchored"/>.</summary>
    public static List<string> Assign(List<Piece> pieces, List<IdEntry> ids, List<Piece> unanchored)
    {
        var errors = new List<string>();
        var tree = new STRtree<int>();
        for (int i = 0; i < pieces.Count; i++) tree.Insert(pieces[i].Shape.EnvelopeInternal, i);
        tree.Build();
        var hits = new List<int>[pieces.Count];
        for (int i = 0; i < hits.Length; i++) hits[i] = new List<int>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int e = 0; e < ids.Count; e++)
        {
            var id = ids[e];
            if (!ValidProvinceId(id.Id)) errors.Add($"id '{id.Id}': bad format");
            if (!seen.Add(id.Id)) errors.Add($"id '{id.Id}': duplicate");
            if (id.Anchor.Length != 2) { errors.Add($"id '{id.Id}': anchor must be [lat_udeg, lon_udeg]"); continue; }
            var pt = AnchorPoint(id.Anchor);
            var inside = new List<int>();
            foreach (var i in tree.Query(new Envelope(pt)))
                if (SimplePointInAreaLocator.Locate(pt, pieces[i].Shape) != Location.Exterior) inside.Add(i);
            if (inside.Count == 0) errors.Add($"id '{id.Id}': anchor lies in no piece");
            foreach (var i in inside) hits[i].Add(e);
            if (inside.Count > 1) errors.Add($"id '{id.Id}': anchor lies in {inside.Count} pieces (on a border?)");
        }
        for (int i = 0; i < pieces.Count; i++)
        {
            if (hits[i].Count == 1) pieces[i].Id = ids[hits[i][0]].Id;
            else if (hits[i].Count == 0) unanchored.Add(pieces[i]);
            else errors.Add($"piece {Describe(pieces[i])}: {hits[i].Count} anchors ({string.Join(", ", hits[i].Select(h => ids[h].Id))})");
        }
        foreach (var p in unanchored) errors.Add($"piece without anchor: {Describe(p)}");
        return errors;
    }

    public static string Describe(Piece p)
    {
        var ip = p.Shape.InteriorPoint;
        return $"{p.Name} [{string.Join(",", p.Src.Take(3))}{(p.Src.Count > 3 ? ",..." : "")}] hint='{p.Hint}' at [{Sph.Micro(ip.Y)}, {Sph.Micro(ip.X)}]";
    }
}
