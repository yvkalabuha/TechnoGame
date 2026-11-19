using System;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>The game-speed meter: four cells 7/11/15/19 high (pause, day/s, week/s, month/s; the fourth is the dashed reserve, #331). The same plain control at every organ level (O-008: the era-skinned gauge was not shipped);
/// the value is read from the tempo word with its unit next to it, which TimeControl sets in full ink. Not interactive: the keys and tooltips stay with the buttons.</summary>
public sealed partial class GaugeControl : Control
{
    static readonly int[] Ink = { Ui.Teal, Ui.Olive, Ui.Plum, Ui.Sand };
    public int Filled;               // speed step 0..3

    public GaugeControl() { MouseFilter = MouseFilterEnum.Ignore; CustomMinimumSize = new Vector2(46, 28); }

    void DrawDashedRect(Rect2 r, Color c)
    {
        DrawDashedLine(r.Position, new Vector2(r.End.X, r.Position.Y), c, 1f, 2f); DrawDashedLine(new Vector2(r.End.X, r.Position.Y), r.End, c, 1f, 2f);
        DrawDashedLine(r.End, new Vector2(r.Position.X, r.End.Y), c, 1f, 2f); DrawDashedLine(new Vector2(r.Position.X, r.End.Y), r.Position, c, 1f, 2f);
    }

    // «Before» (CLIENT_V05C_SPEC §2): four cells 7/11/15/19 high; filled in their own ink, the current one ringed, empty hatched at 45 degrees; the fourth is the dashed reserve.
    static readonly float[] Heights = { 7, 11, 15, 19 };

    void Hatch(Rect2 r, Color col)
    {
        for (float a = r.Position.X - r.Size.Y; a < r.End.X; a += 4f)
        {
            float s0 = Math.Max(0, r.Position.X - a), s1 = Math.Min(r.Size.Y, r.End.X - a);
            if (s0 < s1) DrawLine(new Vector2(a + s0, r.End.Y - s0), new Vector2(a + s1, r.End.Y - s1), col, 1f);
        }
    }

    public override void _Draw()
    {
        for (int i = 0; i < 4; i++)
        {
            var r = new Rect2(4 + i * 10, Size.Y - 4 - Heights[i], 8, Heights[i]);
            if (i == 3) { DrawDashedRect(r, Ui.Ink3); continue; }
            if (i < Filled) DrawRect(r, Ui.InkLine[Ink[i]]); else Hatch(r, new Color(Ui.Ink, 0.45f));
            DrawRect(r, Ui.Ink, false, 1f);
            if (i == Filled - 1) DrawRect(r.Grow(2), Ui.Ink, false, 1.5f);
        }
    }
}
