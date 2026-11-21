#nullable enable
using System;

namespace Singularity.Client.Spike;

/// <summary>Godot-free outlines of the node-type glyphs (offsets from the node centre, y down), so that a test can check that the shapes stay distinguishable at node size.
/// FND triangle, ENG hexagon with a flat top, CMP "house" (apex up, flat base), PRC square, APP diamond; PRN is a circle (null).</summary>
public static class GlyphGeometry
{
    public const float EngScale = 1.3f, CmpScale = 1.35f;   // v0.5d+: ~+18 % over 1.1 / 1.15, to separate ENG and CMP at ~14 px

    static (float X, float Y)[] Reg(float r, int n, double a0)
    {
        var q = new (float, float)[n];
        for (int k = 0; k < n; k++) q[k] = ((float)(Math.Cos(a0 + k * Math.PI * 2 / n) * r), (float)(Math.Sin(a0 + k * Math.PI * 2 / n) * r));
        return q;
    }

    public static (float X, float Y)[]? Outline(string code, float r) => code switch
    {
        "FND" => Reg(r * 1.25f, 3, -Math.PI / 2),
        "ENG" => Reg(r * EngScale, 6, 0),
        "CMP" => new[] { (0f, -r * 1.45f), (r * CmpScale, -r * 0.35f), (r * CmpScale * 0.85f, r * 1.1f), (-r * CmpScale * 0.85f, r * 1.1f), (-r * CmpScale, -r * 0.35f) },
        "PRC" => new[] { (-r, -r), (r, -r), (r, r), (-r, r) },
        "APP" => new[] { (0f, -r * 1.3f), (r * 1.3f, 0f), (0f, r * 1.3f), (-r * 1.3f, 0f) },
        _ => null,
    };

    /// <summary>A2 floor for a meaningful icon: 16 physical px, which is 16 du at 1920x1080 and 100 %.</summary>
    public const float IconFloor = 16f;
    /// <summary>In-node glyph radius as a share of the node plate radius (Icons level). Every glyph spans 2 x this radius in its widest direction.</summary>
    /// <summary>Smallest node plate radius at the Icons level (TechTree.NR starts here).</summary>
    public const float IconsPlateMin = 13f;
    public const float NodeGlyphShare = 0.62f;

    /// <summary>Bounding box of the closed outline of a type at radius r (PRN: circle): min x, min y, max x, max y.</summary>
    public static (float X0, float Y0, float X1, float Y1) Bounds(string code, float r)
    {
        var o = Outline(code, r);
        if (o == null) return (-r, -r, r, r);
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        foreach (var (x, y) in o) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
        return (x0, y0, x1, y1);
    }

    /// <summary>Smallest radius at which both sides of the outline box reach the icon floor (legend swatches use it, so each stays at 16 du and the shapes keep their proportions).</summary>
    public static float LegendRadius(string code)
    {
        var (x0, y0, x1, y1) = Bounds(code, 1f);
        return (IconFloor + 0.1f) / Math.Min(x1 - x0, y1 - y0);
    }

    public static double Area((float X, float Y)[] p)
    {
        double s = 0;
        for (int i = 0; i < p.Length; i++) { var a = p[i]; var b = p[(i + 1) % p.Length]; s += (double)a.X * b.Y - (double)b.X * a.Y; }
        return Math.Abs(s) / 2;
    }

    /// <summary>Number of vertices on the top edge (flat top 2, apex 1).</summary>
    public static int TopVertices((float X, float Y)[] p, float tol = 0.5f)
    {
        float min = float.MaxValue; foreach (var v in p) min = Math.Min(min, v.Y);
        int n = 0; foreach (var v in p) if (v.Y - min <= tol) n++;
        return n;
    }

    /// <summary>Overlap (intersection over union) of two shapes centred on the same point, by a raster of 0.25 px.</summary>
    public static double Overlap((float X, float Y)[] a, (float X, float Y)[] b, float extent)
    {
        int inter = 0, uni = 0;
        for (float y = -extent; y <= extent; y += 0.25f)
            for (float x = -extent; x <= extent; x += 0.25f)
            {
                bool ia = Inside(a, x, y), ib = Inside(b, x, y);
                if (ia && ib) inter++;
                if (ia || ib) uni++;
            }
        return uni == 0 ? 0 : (double)inter / uni;
    }

    static bool Inside((float X, float Y)[] p, float x, float y)
    {
        bool c = false;
        for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
            if ((p[i].Y > y) != (p[j].Y > y) && x < (p[j].X - p[i].X) * (y - p[i].Y) / (p[j].Y - p[i].Y) + p[i].X) c = !c;
        return c;
    }
}

/// <summary>Strip of a foreign power in its tint on paper: a pale tint needs a dark 1 px edge (ΔL* to the paper below <see cref="MinDeltaL"/>).</summary>
public static class TintStrip
{
    public const double MinDeltaL = 15;

    public static double DeltaL(string tintHex, string paperHex) =>
        Math.Abs(ColorMath.Lstar(ColorMath.FromHex(tintHex)) - ColorMath.Lstar(ColorMath.FromHex(paperHex)));

    public static bool NeedsEdge(string tintHex, string paperHex) => DeltaL(tintHex, paperHex) < MinDeltaL;
}
