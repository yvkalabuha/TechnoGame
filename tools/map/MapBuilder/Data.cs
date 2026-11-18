using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Linemerge;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;
using NetTopologySuite.Operation.Polygonize;
using NetTopologySuite.Precision;

namespace Singularity.MapBuilder;

internal sealed class Atom
{
    public string Code = "";
    public string Name = "";
    public string Adm0 = "";
    public string Admin = "";
    public double CLon, CLat;
    public Geometry Shape = Sph.Gf.CreatePolygon();
    public double AreaM2;
    public bool Europe = true;   // inside the legacy Europe selection (TASK-0075); false for atoms added by the world build
}

/// <summary>A polygonal cutter from an OHM snapshot plus the licence tag found on it.</summary>
internal sealed class OhmShape
{
    public Geometry Area = Sph.Gf.CreatePolygon();
    public string License = "";
    public int Version;
    public string Name = "";
    /// <summary>Licences found on the relation and its member ways (OHM default CC0-1.0 when a tag is absent).</summary>
    public SortedSet<string> Licenses = new(StringComparer.Ordinal);
    /// <summary>CC BY objects for ATTRIBUTION.md, as "licence|credit|object".</summary>
    public SortedSet<string> Attributions = new(StringComparer.Ordinal);
}

/// <summary>Explicit allow list (ADR-0009): CC0, CC BY 2.0/3.0/4.0 and public domain. CC BY-SA, CC BY-NC and others are rejected.</summary>
internal static class Licenses
{
    public static readonly string[] CcBy = { "CC-BY-2.0", "CC-BY-3.0", "CC-BY-4.0" };
    /// <summary>OHM writes licences freely ("CC BY 4.0", "CC-BY 4.0", "Creative Commons BY 4.0 International"): these map to CC-BY-x.y. Anything else (CC BY-SA, CC BY-NC, unknown) stays as written and is rejected.</summary>
    public static string Normalize(string l)
    {
        var m = System.Text.RegularExpressions.Regex.Match(l.Trim(), @"^(?:cc|creative commons)[ -]?by[ -]?(\d\.\d)(?: international)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? "CC-BY-" + m.Groups[1].Value : l.Trim();
    }
    public static bool Allowed(string l) => l is "CC0-1.0" or "PD" or "public-domain" || CcBy.Contains(l);
    public static bool IsCcBy(string l) => CcBy.Contains(l);
}

internal static class Data
{
    public static List<Atom> LoadNe(byte[] json, AreaSpec? area, HashSet<string>? adm0Seen = null)
    {
        var res = new List<Atom>();
        using var doc = JsonDocument.Parse(json);
        foreach (var f in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            var pr = f.GetProperty("properties");
            string a3 = pr.GetProperty("adm0_a3").GetString()!;
            adm0Seen?.Add(a3);
            string code = pr.GetProperty("adm1_code").GetString()!;
            double lon = pr.GetProperty("longitude").GetDouble(), lat = pr.GetProperty("latitude").GetDouble();
            bool europe = true;
            if (area != null)
            {
                europe = area.Adm0.Contains(a3) && lon >= area.Lon[0] && lon <= area.Lon[1] && lat >= area.Lat[0] && lat <= area.Lat[1] && !area.Drop.Contains(code);
                if (!europe && !(area.World && !area.WorldDropAdm0.Contains(a3))) continue;
            }
            var geo = GeometryPrecisionReducer.Reduce(ReadGeometry(f.GetProperty("geometry")), Sph.Pm);
            res.Add(new Atom
            {
                Code = code, Adm0 = a3, Admin = pr.GetProperty("admin").GetString() ?? a3, CLon = lon, CLat = lat, Shape = geo, AreaM2 = Sph.AreaM2(geo), Europe = europe,
                Name = pr.GetProperty("name").GetString() ?? code,
            });
        }
        return res.OrderBy(a => a.Code, StringComparer.Ordinal).ToList();
    }

    /// <summary>Distinct ADM0_A3 codes of the NE admin-0 map units file (collision check for polity ids).</summary>
    public static SortedSet<string> Adm0Codes(byte[] mapUnits)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(mapUnits);
        foreach (var f in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            var pr = f.GetProperty("properties");
            foreach (var key in new[] { "ADM0_A3", "adm0_a3" })
                if (pr.TryGetProperty(key, out var v) && v.GetString() is { Length: > 0 } s) set.Add(s);
        }
        return set;
    }

    private static Coordinate[] Ring(JsonElement r) =>
        r.EnumerateArray().Select(p => new Coordinate(p[0].GetDouble(), p[1].GetDouble())).ToArray();

    private static Polygon Poly(JsonElement p)
    {
        var rings = p.EnumerateArray().ToList();
        return Sph.Gf.CreatePolygon(Sph.Gf.CreateLinearRing(Ring(rings[0])),
            rings.Skip(1).Select(r => Sph.Gf.CreateLinearRing(Ring(r))).ToArray());
    }

    public static Geometry ReadGeometry(JsonElement g)
    {
        var c = g.GetProperty("coordinates");
        return g.GetProperty("type").GetString() == "Polygon"
            ? Poly(c)
            : Sph.Gf.CreateMultiPolygon(c.EnumerateArray().Select(Poly).ToArray());
    }

    /// <summary>Builds the area of an OHM relation (outer ways minus inner ways). Open rings are a loud error.</summary>
    public static OhmShape LoadOhm(byte[] json, string lockId)
    {
        using var doc = JsonDocument.Parse(json);
        var rel = doc.RootElement.EnumerateArray().First(e => e.GetProperty("type").GetString() == "relation");
        var shape = new OhmShape { Version = rel.GetProperty("version").GetInt32() };
        // The lock id "ohm:relation/<id>@v<version>" must describe this snapshot.
        var idm = System.Text.RegularExpressions.Regex.Match(lockId, @"^ohm:relation/(\d+)@v(\d+)$");
        if (!idm.Success || idm.Groups[1].Value != rel.GetProperty("id").GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture)
            || idm.Groups[2].Value != shape.Version.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidDataException($"lock id '{lockId}' does not match the snapshot (relation {rel.GetProperty("id").GetInt64()} version {shape.Version})");
        void Tag(JsonElement e, string what)
        {
            // No licence tag means the OHM default (CC0-1.0, ADR-0011 §2.1).
            var lic = "CC0-1.0";
            string credit = "";
            if (e.TryGetProperty("tags", out var t))
            {
                if (t.TryGetProperty("license", out var l)) lic = Licenses.Normalize(l.GetString()!);
                foreach (var key in new[] { "attribution", "source" })
                    if (credit == "" && t.TryGetProperty(key, out var c)) credit = c.GetString()!;
            }
            shape.Licenses.Add(lic);
            if (Licenses.IsCcBy(lic)) shape.Attributions.Add($"{lic}|{(credit == "" ? "OpenHistoricalMap contributors" : credit)}|{what}");
        }
        Tag(rel, "relation/" + rel.GetProperty("id").GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var w in doc.RootElement.EnumerateArray().Where(e => e.GetProperty("type").GetString() == "way"))
            Tag(w, "way/" + w.GetProperty("id").GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (rel.TryGetProperty("tags", out var tags)) shape.Name = tags.TryGetProperty("name", out var n) ? n.GetString()! : "";
        shape.License = shape.Licenses.FirstOrDefault(x => !Licenses.Allowed(x)) ?? (shape.Licenses.Count == 1 ? shape.Licenses.First() : "CC-BY-mixed");
        var outer = new List<Geometry>();
        var inner = new List<Geometry>();
        foreach (var m in rel.GetProperty("members").EnumerateArray())
        {
            if (m.GetProperty("type").GetString() != "way") continue;
            var cs = m.GetProperty("geometry").EnumerateArray().Select(p =>
            {
                var c = new Coordinate(p.GetProperty("lon").GetDouble(), p.GetProperty("lat").GetDouble());
                Sph.Pm.MakePrecise(c);
                return c;
            }).ToArray();
            if (cs.Length < 2) continue;
            (m.GetProperty("role").GetString() == "inner" ? inner : outer).Add(Sph.Gf.CreateLineString(cs));
        }
        var o = Faces(outer);
        shape.Area = inner.Count == 0 ? o : Sph.Overlay(o, Faces(inner), SpatialFunction.Difference);
        return shape;
    }

    private static Geometry Faces(List<Geometry> lines)
    {
        var merger = new LineMerger();
        foreach (var l in lines) merger.Add(l);
        var pg = new Polygonizer();
        pg.Add(merger.GetMergedLineStrings().Cast<Geometry>().ToList());
        if (pg.GetDangles().Count > 0 || pg.GetCutEdges().Count > 0)
            throw new InvalidDataException("OHM relation does not form closed rings (dangles or cut edges)");
        var polys = pg.GetPolygons().Cast<Polygon>().ToArray();
        // Nested rings of one role are islands inside holes only in malformed data; take the union of the faces.
        return GeometryPrecisionReducer.Reduce(Sph.Gf.CreateMultiPolygon(polys).Buffer(0), Sph.Pm);
    }
}
