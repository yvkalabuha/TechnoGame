using System;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>The game-speed meter: four bars 10/14/18/22 high (pause, day/s, week/s, month/s; the fourth is the dashed reserve, #331). No text beside it (#443): the tooltip and the accessible name carry the rate with its unit.
/// Geometry is TimeGaugeGeometry; every edge is a whole-pixel fill, so the active frame stays symmetric around its bar. Only a tooltip reaches it; the keys stay with the buttons.</summary>
public sealed partial class GaugeControl : Control
{
    static readonly int[] Ink = { Ui.Teal, Ui.Olive, Ui.Plum, Ui.Sand };
    static readonly Color HatchColor = new(Palette.GaugeHatch);
    public int Filled;               // speed step 0..3; 0 = pause, nothing filled and no frame

    public GaugeControl()
    {
        MouseFilter = MouseFilterEnum.Pass; SizeFlagsVertical = SizeFlags.ShrinkCenter;
        CustomMinimumSize = new Vector2(TimeGaugeGeometry.Width, TimeGaugeGeometry.Height);
    }

    // A 1 du line on whole pixels: the dashed rectangle runs on the pixel centres.
    void DrawDashedRect(Rect2 r, Color c)
    {
        r = new Rect2(r.Position + new Vector2(0.5f, 0.5f), r.Size - Vector2.One);
        DrawDashedLine(r.Position, new Vector2(r.End.X, r.Position.Y), c, 1f, 2f); DrawDashedLine(new Vector2(r.End.X, r.Position.Y), r.End, c, 1f, 2f);
        DrawDashedLine(r.End, new Vector2(r.Position.X, r.End.Y), c, 1f, 2f); DrawDashedLine(new Vector2(r.Position.X, r.End.Y), r.Position, c, 1f, 2f);
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
        for (int i = 0; i < TimeGaugeGeometry.Bars; i++)
        {
            var (x, y, w, h) = TimeGaugeGeometry.Bar(i);
            var r = new Rect2(x, y, w, h);
            if (i == 3) { DrawDashedRect(r, Ui.Ink3); continue; }
            if (i < Filled) DrawRect(r, Ui.InkLine[Ink[i]]); else Hatch(r, HatchColor);
            Outline(r, 1f, Ui.Ink);
            if (i == Filled - 1)
            {
                var (fx, fy, fw, fh) = TimeGaugeGeometry.Frame(i);
                Outline(new Rect2(fx, fy, fw, fh), TimeGaugeGeometry.FrameStroke, Ui.Ink);
            }
        }
    }
}
