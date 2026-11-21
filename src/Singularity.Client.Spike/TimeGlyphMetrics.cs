using System;

namespace Singularity.Client.Spike;

/// <summary>Glyph boxes of the time buttons (pause, slower, faster) in du, relative to the 28 du button. Godot-free so that a test can check the visible ink against the
/// UI_DESIGN_SYSTEM A2 floor (a meaningful glyph is at least 16 x 16 physical px at 1920x1080, 100 %) and the 5 du edge band (T6).</summary>
public static class TimeGlyphMetrics
{
    public const double Button = 28, Stroke = 2, GlyphSize = 15;   // the chevrons and bars are arrows and pointers: 2 px (A2)
    /// <summary>Top-left corner of the glyph box in the button.</summary>
    public const double Origin = (Button - GlyphSize) / 2.0;

    const double H = GlyphSize / 2.0;

    /// <summary>Closed polylines of a glyph in glyph coordinates (0..GlyphSize): pause = two bars, slower = two triangles pointing left, faster = two pointing right.</summary>
    public static (double X, double Y)[][] Shapes(int kind) => kind switch
    {
        0 => new[] { new[] { (0.0, 0.0), (5.0, 0.0), (5.0, GlyphSize), (0.0, GlyphSize), (0.0, 0.0) }, new[] { (10.0, 0.0), (GlyphSize, 0.0), (GlyphSize, GlyphSize), (10.0, GlyphSize), (10.0, 0.0) } },
        1 => new[] { new[] { (H, 0.0), (0.0, H), (H, GlyphSize), (H, 0.0) }, new[] { (GlyphSize, 0.0), (H, H), (GlyphSize, GlyphSize), (GlyphSize, 0.0) } },
        _ => new[] { new[] { (0.0, 0.0), (H, H), (0.0, GlyphSize), (0.0, 0.0) }, new[] { (H, 0.0), (GlyphSize, H), (H, GlyphSize), (H, 0.0) } },
    };

    /// <summary>The primitives of a glyph in button coordinates: what TimeControl draws and what the tests measure.</summary>
    public static Prim[] Prims(int kind)
    {
        var shapes = Shapes(kind); var res = new Prim[shapes.Length];
        for (int i = 0; i < res.Length; i++)
        {
            var pts = new (float, float)[shapes[i].Length];
            for (int k = 0; k < pts.Length; k++) pts[k] = ((float)(Origin + shapes[i][k].X), (float)(Origin + shapes[i][k].Y));
            res[i] = new Prim(PrimKind.Polyline, pts, (float)Stroke);
        }
        return res;
    }

    /// <summary>Visible ink box of the drawn primitives in button coordinates: x, y, width, height.</summary>
    public static (double X, double Y, double W, double H) InkBox(int kind)
    {
        var (x0, y0, x1, y1) = GlyphPrims.Ink(Prims(kind));
        return (x0, y0, x1 - x0, y1 - y0);
    }
}
