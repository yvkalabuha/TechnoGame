using System.Globalization;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;

namespace Singularity.MapBuilder;

/// <summary>Fixed precision model and the project's own spherical measures (ADR-0011 §2.4, §2.5). NTS never computes areas.</summary>
internal static class Sph
{
    /// <summary>Authalic radius of WGS84, metres.</summary>
    public const double R = 6371007.181;

    /// <summary>Coordinates are snapped to 1e-6 degrees.</summary>
    public static readonly PrecisionModel Pm = new(1e6);
    public static readonly GeometryFactory Gf = new(Pm, 4326);

    public static int Micro(double deg) => (int)Math.Round(deg * 1e6, MidpointRounding.ToEven);

    /// <summary>Overlay of areas. A touch of the inputs can add lines or points to the result: only its polygons are kept.</summary>
    public static Geometry Overlay(Geometry a, Geometry b, SpatialFunction op)
    {
        var r = OverlayNG.Overlay(a, b, op, Pm);
        return a.Dimension != Dimension.Surface || b.Dimension != Dimension.Surface || r is Polygon or MultiPolygon ? r : FromPolygons(Polygons(r));
    }

    /// <summary>Spherical excess of a ring, Chamberlain and Duquette 2007: R^2/2 * |sum (lon[i+1] - lon[i-1]) * sin(lat[i])|. Square metres.</summary>
    public static double RingArea(Coordinate[] ring)
    {
        int n = ring.Length - 1; // closed ring: last == first
        if (n < 3) return 0;
        // The longitude differences of a closed ring sum to zero, so a constant can be subtracted from sin(lat):
        // it removes most of the floating-point cancellation without changing the formula's value.
        double sinRef = Math.Sin(ring[0].Y * Math.PI / 180.0);
        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            var prev = ring[(i + n - 1) % n];
            var next = ring[(i + 1) % n];
            sum += (next.X - prev.X) * Math.PI / 180.0 * (Math.Sin(ring[i].Y * Math.PI / 180.0) - sinRef);
        }
        return Math.Abs(sum) * R * R / 2.0;
    }

    /// <summary>Polygon area in square metres, holes subtracted.</summary>
    public static double PolygonArea(Polygon p)
    {
        double a = RingArea(p.ExteriorRing.Coordinates);
        for (int i = 0; i < p.NumInteriorRings; i++) a -= RingArea(p.GetInteriorRingN(i).Coordinates);
        return a;
    }

    public static double AreaM2(Geometry g)
    {
        double a = 0;
        for (int i = 0; i < g.NumGeometries; i++)
            if (g.GetGeometryN(i) is Polygon p) a += PolygonArea(p);
        return a;
    }

    /// <summary>Area in km2 as long, banker's rounding (ADR-0011 §2.5).</summary>
    public static long AreaKm2(Geometry g) => (long)Math.Round(AreaM2(g) / 1e6, MidpointRounding.ToEven);

    /// <summary>Great-circle distance in km (haversine) on the authalic sphere.</summary>
    public static double DistKm(double lon0, double lat0, double lon1, double lat1)
    {
        const double d2r = Math.PI / 180.0;
        double dLat = (lat1 - lat0) * d2r, dLon = (lon1 - lon0) * d2r;
        double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat0 * d2r) * Math.Cos(lat1 * d2r) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * R * Math.Asin(Math.Min(1.0, Math.Sqrt(h))) / 1000.0;
    }

    public static IEnumerable<Polygon> Polygons(Geometry g)
    {
        for (int i = 0; i < g.NumGeometries; i++)
            if (g.GetGeometryN(i) is Polygon p && !p.IsEmpty) yield return p;
    }

    public static Geometry FromPolygons(IEnumerable<Polygon> polys)
    {
        var arr = polys.ToArray();
        if (arr.Length == 0) return Gf.CreatePolygon();
        return arr.Length == 1 ? arr[0] : Gf.CreateMultiPolygon(arr);
    }

    public static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
}
