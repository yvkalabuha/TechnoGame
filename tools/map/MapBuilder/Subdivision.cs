using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;

namespace Singularity.MapBuilder;

/// <summary>Voronoi subdivision (ADR-0011 §2.3): Halton seeds, 10 Lloyd iterations on a fixed sample grid, cells clipped to the object.</summary>
internal static class Subdivision
{
    public const int LloydIterations = 10;
    private const int Grid = 80;

    public static double Halton(int index, int b)
    {
        double f = 1, r = 0;
        for (int i = index; i > 0; i /= b) { f /= b; r += f * (i % b); }
        return r;
    }

    /// <summary>Returns the cells ordered by seed (lat descending, lon ascending). Cell union is exactly the input.</summary>
    public static List<Geometry> Voronoi(Geometry g, int n)
    {
        var env = g.EnvelopeInternal;
        double k = Math.Cos((env.MinY + env.MaxY) / 2 * Math.PI / 180.0);   // equal-area-like x scale
        double x0 = env.MinX * k, x1 = env.MaxX * k, y0 = env.MinY, y1 = env.MaxY;
        bool Inside(double x, double y) => NetTopologySuite.Algorithm.Locate.SimplePointInAreaLocator.Locate(new Coordinate(x / k, y), g) != Location.Exterior;

        var seeds = new List<(double X, double Y)>();
        for (int i = 1; seeds.Count < n; i++)
        {
            if (i > 100000) throw new InvalidOperationException("cannot place Voronoi seeds");
            double x = x0 + Halton(i, 2) * (x1 - x0), y = y0 + Halton(i, 3) * (y1 - y0);
            if (Inside(x, y)) seeds.Add((x, y));
        }

        var samples = new List<(double X, double Y)>();
        for (int i = 0; i < Grid; i++)
            for (int j = 0; j < Grid; j++)
            {
                double x = x0 + (i + 0.5) / Grid * (x1 - x0), y = y0 + (j + 0.5) / Grid * (y1 - y0);
                if (Inside(x, y)) samples.Add((x, y));
            }
        for (int it = 0; it < LloydIterations && samples.Count > 0; it++)
        {
            var sx = new double[n]; var sy = new double[n]; var cnt = new int[n];
            foreach (var s in samples)
            {
                int best = Nearest(seeds, s.X, s.Y);
                sx[best] += s.X; sy[best] += s.Y; cnt[best]++;
            }
            for (int i = 0; i < n; i++) if (cnt[i] > 0) seeds[i] = (sx[i] / cnt[i], sy[i] / cnt[i]);
        }
        var order = Enumerable.Range(0, n).OrderByDescending(i => seeds[i].Y).ThenBy(i => seeds[i].X).ToList();

        // Sequential split: each cut uses one overlay for both halves, so shared edges are identical.
        var res = new List<Geometry>();
        Geometry rest = g;
        for (int oi = 0; oi < n - 1; oi++)
        {
            int i = order[oi];
            var cell = Cell(seeds, i, order.Skip(oi + 1), x0 - 1, y0 - 1, x1 + 1, y1 + 1, k);
            res.Add(Sph.Overlay(rest, cell, SpatialFunction.Intersection));
            rest = Sph.Overlay(rest, cell, SpatialFunction.Difference);
        }
        res.Add(rest);
        if (res.Any(c => c.IsEmpty)) throw new InvalidOperationException("empty Voronoi cell");
        return res;
    }

    private static int Nearest(List<(double X, double Y)> seeds, double x, double y)
    {
        int best = 0; double bd = double.MaxValue;
        for (int i = 0; i < seeds.Count; i++)
        {
            double d = (seeds[i].X - x) * (seeds[i].X - x) + (seeds[i].Y - y) * (seeds[i].Y - y);
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    /// <summary>Cell of seed i against the seeds in <paramref name="others"/> only: points of the plane nearer to i than to each of them.</summary>
    private static Geometry Cell(List<(double X, double Y)> s, int i, IEnumerable<int> others, double x0, double y0, double x1, double y1, double k)
    {
        var poly = new List<(double X, double Y)> { (x0, y0), (x1, y0), (x1, y1), (x0, y1) };
        foreach (var j in others)
        {
            double nx = s[j].X - s[i].X, ny = s[j].Y - s[i].Y;
            double mx = (s[i].X + s[j].X) / 2, my = (s[i].Y + s[j].Y) / 2;
            poly = Clip(poly, nx, ny, nx * mx + ny * my);
        }
        var cs = poly.Select(p => new Coordinate(p.X / k, p.Y)).ToList();
        cs.Add(cs[0]);
        return Sph.Gf.CreatePolygon(cs.ToArray());
    }

    /// <summary>Keeps the half-plane nx*x + ny*y &lt;= c (Sutherland-Hodgman).</summary>
    private static List<(double X, double Y)> Clip(List<(double X, double Y)> poly, double nx, double ny, double c)
    {
        var res = new List<(double X, double Y)>();
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % poly.Count];
            double da = nx * a.X + ny * a.Y - c, db = nx * b.X + ny * b.Y - c;
            if (da <= 0) res.Add(a);
            if (da < 0 && db > 0 || da > 0 && db < 0)
            {
                double t = da / (da - db);
                res.Add((a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y)));
            }
        }
        return res;
    }

    /// <summary>Fallback method: bisection into equal areas along the longer side of the envelope (recursive halving, odd counts split n/2 : n - n/2).</summary>
    public static List<Geometry> Bisect(Geometry g, int n)
    {
        if (n < 2) return new List<Geometry> { g };
        int left = n / 2;
        var env = g.EnvelopeInternal;
        bool byMeridian = (env.MaxX - env.MinX) * Math.Cos((env.MinY + env.MaxY) / 2 * Math.PI / 180.0) >= env.MaxY - env.MinY;
        double total = Sph.AreaM2(g), lo = byMeridian ? env.MinX : env.MinY, hi = byMeridian ? env.MaxX : env.MaxY;
        double target = total * left / n;
        Geometry? a = null, b = null;
        for (int it = 0; it < 60; it++)
        {
            double mid = (lo + hi) / 2;
            var half = byMeridian ? Rect(env.MinX - 1, env.MinY - 1, mid, env.MaxY + 1) : Rect(env.MinX - 1, env.MinY - 1, env.MaxX + 1, mid);
            a = Sph.Overlay(g, half, SpatialFunction.Intersection);
            b = Sph.Overlay(g, half, SpatialFunction.Difference);
            if (Sph.AreaM2(a) < target) lo = mid; else hi = mid;
        }
        var res = Bisect(a!, left);
        res.AddRange(Bisect(b!, n - left));
        return res;
    }

    private static Geometry Rect(double x0, double y0, double x1, double y1) =>
        Sph.Gf.CreatePolygon(new[] { new Coordinate(x0, y0), new Coordinate(x1, y0), new Coordinate(x1, y1), new Coordinate(x0, y1), new Coordinate(x0, y0) });
}
