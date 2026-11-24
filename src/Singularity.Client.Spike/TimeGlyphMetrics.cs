using System;

namespace Singularity.Client.Spike;

/// <summary>Pure layout of the time group (TASK-0139 spec): glyph shapes of the three buttons in button coordinates and the geometry of the speed gauge. Godot-free, so that a test checks the
/// optical centre of each glyph, the A2 floor (a meaningful glyph is at least 16 x 16 physical px at 1920x1080, 100 %) and the symmetry of the active frame; the drawing code uses the same numbers.
/// All coordinates are whole du, so a 2 du stroke on a whole centre line covers whole pixels at 100 %.</summary>
public static class TimeGlyphMetrics
{
    public const int Button = 32, Gap = 4, Stroke = 2;
    // Centre lines of the 16 x 16 ink box: stroke 2 puts the ink at 8..24 around the button centre 16.
    const int Lo = 9, Mid = 16, Hi = 23, TriLo = 8, TriHi = 24;   // triangles are 2 du taller: their slanted edges add no butt-end height to the ink box

    /// <summary>Whole-du shift of a glyph along x that moves its centre of mass to the button centre: a triangle pair keeps its mass at the base side, so pointing right it moves right.</summary>
    public static int OpticalShiftX(int kind) => kind switch { 1 => -1, 2 => 1, _ => 0 };

    /// <summary>Closed polylines of a glyph in button coordinates: pause = two bars, slower = two triangles pointing left, faster = two pointing right (optical shift already applied).</summary>
    public static (double X, double Y)[][] Shapes(int kind)
    {
        int s = OpticalShiftX(kind);
        (double, double)[] Tri(int baseX, int tipX) => new (double, double)[] { (baseX + s, TriLo), (tipX + s, Mid), (baseX + s, TriHi), (baseX + s, TriLo) };
        return kind switch
        {
            0 => new[] { new (double, double)[] { (Lo, Lo), (13, Lo), (13, Hi), (Lo, Hi), (Lo, Lo) }, new (double, double)[] { (19, Lo), (Hi, Lo), (Hi, Hi), (19, Hi), (19, Lo) } },
            1 => new[] { Tri(24, 18), Tri(14, 8) },
            _ => new[] { Tri(8, 14), Tri(18, 24) },
        };
    }

    /// <summary>Stroke model of a glyph in button coordinates, for measuring (InkBox). TimeControl draws the same Shapes as pixel fills for straight edges and antialiased paths for slanted ones (IconButton.BuildParts),
    /// so the box is a close bound: miters at the sharp tips reach about 1 du further.</summary>
    public static Prim[] Prims(int kind)
    {
        var shapes = Shapes(kind); var res = new Prim[shapes.Length];
        for (int i = 0; i < res.Length; i++)
        {
            var pts = new (float, float)[shapes[i].Length];
            for (int k = 0; k < pts.Length; k++) pts[k] = ((float)shapes[i][k].X, (float)shapes[i][k].Y);
            res[i] = new Prim(PrimKind.Polyline, pts, Stroke);
        }
        return res;
    }

    /// <summary>Visible ink box of the drawn primitives in button coordinates: x, y, width, height.</summary>
    public static (double X, double Y, double W, double H) InkBox(int kind)
    {
        var (x0, y0, x1, y1) = GlyphPrims.Ink(Prims(kind));
        return (x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>Centre of mass of the stroke (every edge weighs its length) in button coordinates: the optical centre that must sit at the button centre (16, 16) within 0.5 du.</summary>
    public static (double X, double Y) StrokeCentroid(int kind)
    {
        double sx = 0, sy = 0, sl = 0;
        foreach (var poly in Shapes(kind))
            for (int i = 0; i + 1 < poly.Length; i++)
            {
                double dx = poly[i + 1].X - poly[i].X, dy = poly[i + 1].Y - poly[i].Y, len = Math.Sqrt(dx * dx + dy * dy);
                sx += len * (poly[i].X + poly[i + 1].X) / 2; sy += len * (poly[i].Y + poly[i + 1].Y) / 2; sl += len;
            }
        return (sx / sl, sy / sl);
    }
}

/// <summary>Whole-physical-pixel snapping of the time group at any UI scale s (physical px per du). An edge is rounded once in absolute pixels, so it is never more than half a pixel (at most 0.5 du) from its du position;
/// a glyph is snapped symmetrically around the rounded button centre, so a symmetric glyph has its centre of mass exactly on that centre.</summary>
public static class TimeSnap
{
    static double R(double v) => Math.Round(v, MidpointRounding.AwayFromZero);

    /// <summary>Absolute pixel of a du coordinate u measured from a control whose origin is at originPx.</summary>
    public static double Edge(double originPx, double u, double s) => R(originPx + u * s);

    /// <summary>Pixel of a glyph coordinate u (button du): mirrored around the rounded button centre.</summary>
    public static double GlyphEdge(double originPx, double u, double s)
    {
        double mid = TimeGlyphMetrics.Button / 2.0, d = u - mid;
        return Edge(originPx, mid, s) + Math.Sign(d) * R(Math.Abs(d) * s);
    }

    /// <summary>A stroke of du width in whole pixels (at least 1).</summary>
    public static int StrokePx(double du, double s) => Math.Max(1, (int)R(du * s));
}

/// <summary>Geometry of the four-step speed gauge in du inside its 60 x 32 box: bars 6 wide on a 14 step, heights 10/14/18/22, the group centred both ways; the active step wears a 2 du frame
/// 1 du outside its bar, so the frame is the bar grown by 3 on every side and is symmetric around the bar.</summary>
public static class TimeGaugeGeometry
{
    public const int Width = 60, Height = 32, BarW = 6, Step = 14, FrameGrow = 3, FrameStroke = 2, Bars = 4;
    static readonly int[] Heights = { 10, 14, 18, 22 };
    const int Left = 6, Bottom = 27;   // (60 - (3 * 14 + 6)) / 2 and (32 + 22) / 2

    public static (int X, int Y, int W, int H) Bar(int i) => (Left + i * Step, Bottom - Heights[i], BarW, Heights[i]);

    /// <summary>Bar i in absolute pixels for a gauge whose corner is at (ox, oy): x0, y0 and the exclusive ends x1, y1.</summary>
    public static (double X0, double Y0, double X1, double Y1) BarPx(int i, double ox, double oy, double s)
    {
        var b = Bar(i);
        return (TimeSnap.Edge(ox, b.X, s), TimeSnap.Edge(oy, b.Y, s), TimeSnap.Edge(ox, b.X + b.W, s), TimeSnap.Edge(oy, b.Y + b.H, s));
    }

    /// <summary>Outer rectangle of the active frame of step i in pixels: the pixel bar grown by the same whole number of pixels on every side.</summary>
    public static (double X0, double Y0, double X1, double Y1) FramePx(int i, double ox, double oy, double s)
    {
        var p = BarPx(i, ox, oy, s); int g = TimeSnap.StrokePx(FrameGrow, s);
        return (p.X0 - g, p.Y0 - g, p.X1 + g, p.Y1 + g);
    }

    /// <summary>Outer rectangle of the active frame of step i.</summary>
    public static (int X, int Y, int W, int H) Frame(int i)
    {
        var b = Bar(i);
        return (b.X - FrameGrow, b.Y - FrameGrow, b.W + 2 * FrameGrow, b.H + 2 * FrameGrow);
    }
}
