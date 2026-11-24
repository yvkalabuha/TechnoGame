using System.Collections.Generic;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>Root of the "why not" (TASK-0136 spec 2.1): a red filled square with a white "!" and a black frame with the top-right corner cut off. The shape is the second channel (A5).
/// Used on the planisphere, in the legend and in the goal panel, so one sign has one meaning (C2). Drawn with lines, no text.</summary>
public static class RootMark
{
    static readonly Dictionary<float, (Vector2[] Poly, Vector2[] Loop)> Cache = new();

    static (Vector2[] Poly, Vector2[] Loop) For(float side)
    {
        if (Cache.TryGetValue(side, out var c)) return c;
        var f = TreeClarity.RootFrame(side);
        var poly = new Vector2[f.Length]; var loop = new Vector2[f.Length + 1];
        for (int k = 0; k < f.Length; k++) loop[k] = poly[k] = new Vector2(f[k].X, f[k].Y);
        loop[f.Length] = loop[0];
        return Cache[side] = (poly, loop);
    }

    /// <summary>basis: the transform the owner canvas item runs under (the clipped planisphere view shifts its origin).</summary>
    public static void Draw(CanvasItem ci, Vector2 centre, float side, Vector2 basis)
    {
        var (poly, loop) = For(side);
        ci.DrawSetTransform(centre + basis);
        ci.DrawColoredPolygon(poly, Ui.Alarm);
        ci.DrawPolyline(loop, Ui.Ink, 2f);
        ci.DrawLine(new Vector2(0, -side * 0.30f), new Vector2(0, side * 0.08f), Colors.White, 2f);
        ci.DrawRect(new Rect2(-1, side * 0.2f, 2, 2), Colors.White);
        ci.DrawSetTransform(basis);
    }
}

/// <summary>The root mark as a control (goal panel line).</summary>
public sealed partial class RootMarkIcon : Control
{
    const float Side = 14;
    public RootMarkIcon() { CustomMinimumSize = new Vector2(Side + 4, Side + 4); MouseFilter = MouseFilterEnum.Ignore; SizeFlagsVertical = SizeFlags.ShrinkCenter; }
    public override void _Draw() => RootMark.Draw(this, Size / 2f, Side, Vector2.Zero);
}

/// <summary>Plain stroke icon in front of an inspector card heading: what the card is about, in shape rather than colour (spec 2.6).</summary>
public sealed partial class CardIcon : Control
{
    public enum Kind { Touches, Raises, Opens, Needs }
    readonly Kind _kind;
    public CardIcon(Kind kind) { _kind = kind; CustomMinimumSize = new Vector2(16, 16); MouseFilter = MouseFilterEnum.Ignore; SizeFlagsVertical = SizeFlags.ShrinkCenter; }

    public override void _Draw()
    {
        var c = Size / 2f; var ink = Ui.Ink2;
        switch (_kind)
        {
            case Kind.Touches: DrawArc(c, 4f, 0, Mathf.Tau, 16, ink, 2f, true); break;
            case Kind.Raises: Arrow(c, new Vector2(0, -1), ink); break;
            case Kind.Opens: Arrow(c, new Vector2(1, 0), ink); break;
            default: Arrow(c, new Vector2(-1, 0), ink); break;
        }
    }

    void Arrow(Vector2 c, Vector2 d, Color ink)
    {
        var n = new Vector2(-d.Y, d.X);
        DrawLine(c - d * 6, c + d * 6, ink, 2f, true);
        DrawLine(c + d * 6, c + d * 1.5f + n * 4.5f, ink, 2f, true);
        DrawLine(c + d * 6, c + d * 1.5f - n * 4.5f, ink, 2f, true);
    }
}
