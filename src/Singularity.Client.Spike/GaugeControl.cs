using System;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>The game-speed meter: four bars 10/14/18/22 high (day/s, 3 days/s, week/s, month/s; none filled = pause, #481). No text beside it (#443): the tooltip and the accessible name carry the rate with its unit.
/// Geometry is TimeGaugeGeometry; every edge is a whole-pixel fill, so the active frame stays symmetric around its bar. Only a tooltip reaches it; the keys stay with the buttons.</summary>
public sealed partial class GaugeControl : Control
{
    static readonly int[] Ink = { Ui.Teal, Ui.Olive, Ui.Plum, Ui.Sand };
    static readonly Color HatchColor = new(Palette.GaugeHatch);
    public int Filled;               // speed step 0..4; 0 = pause, nothing filled and no frame

    public GaugeControl()
    {
        MouseFilter = MouseFilterEnum.Pass; SizeFlagsVertical = SizeFlags.ShrinkCenter;
        CustomMinimumSize = new Vector2(TimeGaugeGeometry.Width, TimeGaugeGeometry.Height);
    }

    void Hatch(Rect2 r, Color col)
    {
        for (float a = r.Position.X - r.Size.Y; a < r.End.X; a += 4f)
        {
            float s0 = Math.Max(0, r.Position.X - a), s1 = Math.Min(r.Size.Y, r.End.X - a);
            if (s0 < s1) DrawLine(new Vector2(a + s0, r.End.Y - s0), new Vector2(a + s1, r.End.Y - s1), col, 1f);
        }
    }

    // A rectangle outline of stroke t inside r, as four fills (no half-pixel blur).
    void Outline(Rect2 r, float t, Color c)
    {
        DrawRect(new Rect2(r.Position.X, r.Position.Y, r.Size.X, t), c); DrawRect(new Rect2(r.Position.X, r.End.Y - t, r.Size.X, t), c);
        DrawRect(new Rect2(r.Position.X, r.Position.Y + t, t, r.Size.Y - 2 * t), c); DrawRect(new Rect2(r.End.X - t, r.Position.Y + t, t, r.Size.Y - 2 * t), c);
    }

    public override void _Draw()
    {
        // Bars and frame are snapped to whole physical pixels at the current UI scale (TimeSnap, TimeGaugeGeometry), then drawn back in du.
        var xf = GetViewport().GetFinalTransform() * GetGlobalTransformWithCanvas(); float s = xf.X.Length(); var o = xf.Origin;
        float one = TimeSnap.StrokePx(1, s) / s;
        for (int i = 0; i < TimeGaugeGeometry.Bars; i++)
        {
            var p = TimeGaugeGeometry.BarPx(i, o.X, o.Y, s);
            var r = new Rect2((float)((p.X0 - o.X) / s), (float)((p.Y0 - o.Y) / s), (float)((p.X1 - p.X0) / s), (float)((p.Y1 - p.Y0) / s));
            if (i < Filled) DrawRect(r, Ui.InkLine[Ink[i]]); else Hatch(r.Grow(-one), HatchColor);   // inside the outline: an antialiased line must not spill over the bar edge
            Outline(r, one, Ui.Ink);
            if (i == Filled - 1)
            {
                var f = TimeGaugeGeometry.FramePx(i, o.X, o.Y, s);
                Outline(new Rect2((float)((f.X0 - o.X) / s), (float)((f.Y0 - o.Y) / s), (float)((f.X1 - f.X0) / s), (float)((f.Y1 - f.Y0) / s)), TimeSnap.StrokePx(TimeGaugeGeometry.FrameStroke, s) / s, Ui.Ink);
            }
        }
    }
}
