#nullable enable
using System;

namespace Singularity.Client.Spike;

public enum PrimKind { Polyline, Closed, Fill, Ring, Dot }

/// <summary>One drawn primitive of a glyph. Polyline and Closed: points and a stroke; Fill: a filled polygon; Ring: P[0] is the centre, R the radius, stroke; Dot: P[0] centre, R radius, filled.</summary>
public readonly record struct Prim(PrimKind Kind, (float X, float Y)[] P, float Stroke, float R = 0);

/// <summary>Godot-free source of every glyph the client draws (node glyphs at the Icons level, legend swatches, time buttons, the menu button): the drawing code and the tests read the same primitives, so a test measures what is drawn.
/// UI_DESIGN_SYSTEM A2 at 1920x1080, 100 %: a meaningful icon is at least 16 x 16 px incl. its stroke; an arrow, pointer or tick is at least 2 px thick; a functional line at least 1 px.</summary>
public static class GlyphPrims
{
    public const float IconFloor = 16f, ArrowStroke = 2f, LineStroke = 1f;

    /// <summary>Glyph of a node type inside its plate at the Icons level, centred on (0, 0); rg is the glyph radius.</summary>
    public static Prim[] NodeGlyph(string code, float rg)
    {
        switch (code)
        {
            case "FND":   // foundation: three bars widening downwards, so that the stack follows the triangle plate
                return new[]
                {
                    new Prim(PrimKind.Polyline, new[] { (-0.35f * rg, -0.9f * rg), (0.35f * rg, -0.9f * rg) }, ArrowStroke),
                    new Prim(PrimKind.Polyline, new[] { (-0.65f * rg, 0f), (0.65f * rg, 0f) }, ArrowStroke),
                    new Prim(PrimKind.Polyline, new[] { (-rg, 0.9f * rg), (rg, 0.9f * rg) }, ArrowStroke),
                };
            case "PRN": return new[] { new Prim(PrimKind.Ring, new[] { (0f, 0f) }, ArrowStroke, rg), new Prim(PrimKind.Dot, new[] { (0f, 0f) }, 0, 1.8f) };
            case "ENG":
            case "CMP": return new[] { new Prim(PrimKind.Closed, GlyphGeometry.Outline(code, rg * 0.85f)!, ArrowStroke) };
            case "PRC":   // process: an arrow
                return new[]
                {
                    new Prim(PrimKind.Polyline, new[] { (-rg, 0f), (rg, 0f) }, ArrowStroke),
                    new Prim(PrimKind.Polyline, new[] { (0.3f * rg, -0.95f * rg), (rg, 0f), (0.3f * rg, 0.95f * rg) }, ArrowStroke),
                };
            default: return new[] { new Prim(PrimKind.Fill, new[] { (0f, -rg), (rg, 0f), (0f, rg), (-rg, 0f) }, 0) };   // product: a solid diamond
        }
    }

    /// <summary>Legend swatch of a node type, centred on the middle of its box (not on the circumcentre).</summary>
    public static Prim[] LegendSwatch(string code)
    {
        float r = GlyphGeometry.LegendRadius(code);
        var o = GlyphGeometry.Outline(code, r);
        if (o == null) return new[] { new Prim(PrimKind.Ring, new[] { (0f, 0f) }, 1.8f, r) };
        var (x0, y0, x1, y1) = GlyphGeometry.Bounds(code, r);
        float cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
        var pts = new (float, float)[o.Length];
        for (int i = 0; i < o.Length; i++) pts[i] = (o[i].X - cx, o[i].Y - cy);
        return new[] { new Prim(PrimKind.Closed, pts, 1.8f) };
    }

    /// <summary>Menu button glyph: three bars centred on (0, 0).</summary>
    public static Prim[] MenuGlyph()
    {
        var l = new Prim[3];
        for (int i = 0; i < 3; i++) l[i] = new Prim(PrimKind.Polyline, new[] { (-9f, (i - 1) * 7f), (9f, (i - 1) * 7f) }, ArrowStroke);
        return l;
    }

    /// <summary>Ink box of a set of primitives including strokes: a segment is a rectangle of the stroke width (butt caps), a ring and a dot grow by the radius.</summary>
    public static (float X0, float Y0, float X1, float Y1) Ink(Prim[] prims)
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        void Add(float x, float y) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
        foreach (var p in prims)
        {
            switch (p.Kind)
            {
                case PrimKind.Ring: Add(p.P[0].X - p.R - p.Stroke / 2, p.P[0].Y - p.R - p.Stroke / 2); Add(p.P[0].X + p.R + p.Stroke / 2, p.P[0].Y + p.R + p.Stroke / 2); break;
                case PrimKind.Dot: Add(p.P[0].X - p.R, p.P[0].Y - p.R); Add(p.P[0].X + p.R, p.P[0].Y + p.R); break;
                case PrimKind.Fill: foreach (var (x, y) in p.P) Add(x, y); break;
                default:
                    int n = p.Kind == PrimKind.Closed ? p.P.Length : p.P.Length - 1;
                    for (int i = 0; i < n; i++)
                    {
                        var a = p.P[i]; var b = p.P[(i + 1) % p.P.Length];
                        float dx = b.X - a.X, dy = b.Y - a.Y, len = MathF.Sqrt(dx * dx + dy * dy);
                        float nx = -dy / len * p.Stroke / 2, ny = dx / len * p.Stroke / 2;   // half stroke along the normal
                        Add(a.X + nx, a.Y + ny); Add(a.X - nx, a.Y - ny); Add(b.X + nx, b.Y + ny); Add(b.X - nx, b.Y - ny);
                    }
                    break;
            }
        }
        return (x0, y0, x1, y1);
    }

    /// <summary>Thinnest stroke among the primitives that carry meaning (everything stroked); 0 when none is stroked.</summary>
    public static float MinStroke(Prim[] prims)
    {
        float m = float.MaxValue;
        foreach (var p in prims) if (p.Kind is PrimKind.Polyline or PrimKind.Closed or PrimKind.Ring) m = Math.Min(m, p.Stroke);
        return m == float.MaxValue ? 0 : m;
    }
}
