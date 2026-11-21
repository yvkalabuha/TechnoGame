using Godot;

namespace Singularity.Client.Spike;

/// <summary>Draws the Godot-free primitives of GlyphPrims on a canvas item, offset by o.</summary>
public static class GlyphDraw
{
    public static void Draw(CanvasItem ci, Prim[] prims, Vector2 o, Color c)
    {
        foreach (var p in prims)
        {
            switch (p.Kind)
            {
                case PrimKind.Ring: ci.DrawArc(o + new Vector2(p.P[0].X, p.P[0].Y), p.R, 0, Mathf.Tau, 24, c, p.Stroke, true); break;
                case PrimKind.Dot: ci.DrawCircle(o + new Vector2(p.P[0].X, p.P[0].Y), p.R, c); break;
                default:
                    int n = p.P.Length + (p.Kind == PrimKind.Closed ? 1 : 0);
                    var pts = new Vector2[n];
                    for (int i = 0; i < n; i++) pts[i] = o + new Vector2(p.P[i % p.P.Length].X, p.P[i % p.P.Length].Y);
                    if (p.Kind == PrimKind.Fill) ci.DrawColoredPolygon(pts, c); else ci.DrawPolyline(pts, c, p.Stroke, true);
                    break;
            }
        }
    }
}
