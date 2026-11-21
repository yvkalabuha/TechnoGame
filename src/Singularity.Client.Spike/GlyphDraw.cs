using Godot;

namespace Singularity.Client.Spike;

/// <summary>A glyph ready to draw: the primitives and their point arrays, built once (closed outlines repeat the first point), so that _Draw allocates nothing.</summary>
public sealed class GlyphSet
{
    public readonly Prim[] Prims;
    public readonly Vector2[][] Pts;

    public GlyphSet(Prim[] prims)
    {
        Prims = prims; Pts = new Vector2[prims.Length][];
        for (int i = 0; i < prims.Length; i++)
        {
            var p = prims[i]; int n = p.P.Length + (p.Kind == PrimKind.Closed ? 1 : 0);
            Pts[i] = new Vector2[n];
            for (int k = 0; k < n; k++) Pts[i][k] = new Vector2(p.P[k % p.P.Length].X, p.P[k % p.P.Length].Y);
        }
    }
}

/// <summary>Draws a GlyphSet on a canvas item at offset o. A set built at unit size is scaled by `scale` through the canvas transform; the strokes and dots are divided by it so that they keep their size in du.</summary>
public static class GlyphDraw
{
    public static void Draw(CanvasItem ci, GlyphSet g, Vector2 o, Color c, float scale = 1f)
    {
        ci.DrawSetTransform(o, 0, new Vector2(scale, scale));
        for (int i = 0; i < g.Prims.Length; i++)
        {
            var p = g.Prims[i]; var pts = g.Pts[i];
            switch (p.Kind)
            {
                case PrimKind.Ring: ci.DrawArc(pts[0], p.R, 0, Mathf.Tau, 24, c, p.Stroke / scale, true); break;
                case PrimKind.Dot: ci.DrawCircle(pts[0], p.R / scale, c); break;
                case PrimKind.Fill: ci.DrawColoredPolygon(pts, c); break;
                default: ci.DrawPolyline(pts, c, p.Stroke / scale, true); break;
            }
        }
        ci.DrawSetTransform(Vector2.Zero);
    }
}
