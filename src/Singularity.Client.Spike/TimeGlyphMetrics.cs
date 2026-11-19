using System;

namespace Singularity.Client.Spike;

/// <summary>Glyph boxes of the time buttons (pause, slower, faster) in du, relative to the 28 du button. Godot-free so that a test can check the visible ink against the
/// UI_DESIGN_SYSTEM A2 floor (a meaningful glyph is at least 16 x 16 physical px at 1920x1080, 100 %) and the 5 du edge band (T6).</summary>
public static class TimeGlyphMetrics
{
    public const double Button = 28, Stroke = 1.5, GlyphSize = 15;
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

    /// <summary>Visible ink box (geometry grown by half the stroke) in button coordinates: x, y, width, height.</summary>
    public static (double X, double Y, double W, double H) InkBox(int kind)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var s in Shapes(kind)) foreach (var (x, y) in s) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
        double g = Stroke / 2;
        return (Origin + x0 - g, Origin + y0 - g, x1 - x0 + Stroke, y1 - y0 + Stroke);
    }
}
