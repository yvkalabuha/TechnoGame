using System;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>The game-speed gauge (UI_INSTRUMENTS §3, #404, #409): a layer-2 instrument drawn procedurally, no assets. Three looks by game year: E1 needle dial (1945–69),
/// E2 lit segment block (1970–99), E3 plain scale with a cursor (2000–2100). Level 0 of the setting «Приборы-органы» gives the plain four-cell meter («before»).
/// The value is shown by the needle position, the number of lit cells or the cursor, and by the tempo word beside it, never by colour alone. Not interactive: the keys and tooltips stay with the buttons.</summary>
public sealed partial class GaugeControl : Control
{
    static readonly int[] Ink = { Ui.Teal, Ui.Olive, Ui.Plum, Ui.Sand };
    const float InstrW = 84, InstrH = 36, BeforeW = 46, BeforeH = 28, MoveSeconds = 0.12f;

    public int Filled;               // speed step 0..3
    public int Year = 1945;
    int _era, _level = -1;
    float _pos;                      // displayed step; the needle travels to Filled in 120 ms (at once with «reduce motion»)

    public GaugeControl() { MouseFilter = MouseFilterEnum.Ignore; Sync(); }

    void Sync()
    {
        int level = InstrumentLogic.OrganLevel, era = InstrumentLogic.Era(Year);
        if (level == _level && era == _era) return;
        _level = level; _era = era;
        CustomMinimumSize = level == 0 ? new Vector2(BeforeW, BeforeH) : new Vector2(InstrW, InstrH);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        Sync();
        if (Mathf.IsEqualApprox(_pos, Filled)) return;
        _pos = Settings.Current.Animations ? Mathf.MoveToward(_pos, Filled, (float)delta * 3f / MoveSeconds) : Filled;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_level == 0) { DrawBefore(); return; }
        // Weak: ink and grey only. Normal: the three inks of the tempo steps (teal, olive, plum; sand is the reserve).
        Color Col(int i) => _level == 2 ? Ui.InkLine[Ink[i]] : Ui.Ink2;
        var o = new Vector2(0, Size.Y - InstrH);
        if (_era == 1) DrawDial(o, Col); else if (_era == 2) DrawBlock(o, Col); else DrawScale(o, Col);
    }

    // E1: arc of four segments, ticks every 12 degrees, a needle on a hub. Centre (42, 33), radius 30.
    void DrawDial(Vector2 o, Func<int, Color> col)
    {
        var c = o + new Vector2(42, 33);
        const float r = 30;
        Vector2 At(double deg, float rad) { double a = deg * Math.PI / 180; return c + new Vector2((float)Math.Sin(a), -(float)Math.Cos(a)) * rad; }
        (double a0, double a1)[] seg = { (-72, -36), (-34, 2), (4, 38), (40, 72) };
        for (int i = 0; i < 4; i++)
        {
            if (i < 3) DrawArc(c, r - 2, (float)((seg[i].a0 - 90) * Math.PI / 180), (float)((seg[i].a1 - 90) * Math.PI / 180), 14, col(i), 4f);
            else   // reserve step: dashed, inactive
                for (double d = seg[i].a0; d < seg[i].a1; d += 8)
                    DrawArc(c, r - 2, (float)((d - 90) * Math.PI / 180), (float)((Math.Min(d + 4, seg[i].a1) - 90) * Math.PI / 180), 3, Ui.Ink3, 3f);
        }
        for (int d = -72; d <= 72; d += 12) DrawLine(At(d, r + 1), At(d, r - 6), Ui.Ink2, 1f);
        double ang = NeedleAngle();
        DrawLine(c, At(ang, r - 3), Ui.Ink, 2f);
        DrawCircle(c, 3, Ui.Ink);
    }

    double NeedleAngle()
    {
        int i = Math.Clamp((int)Math.Floor(_pos), 0, 2); float t = Math.Clamp(_pos - i, 0f, 1f);
        return InstrumentLogic.NeedleDeg[i] + (InstrumentLogic.NeedleDeg[i + 1] - InstrumentLogic.NeedleDeg[i]) * t;
    }

    // E2: four columns of three lit segments, filled up to the tempo; the reserve column is dashed.
    void DrawBlock(Vector2 o, Func<int, Color> col)
    {
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 3; j++)
            {
                var r = new Rect2(o.X + 4 + i * 20, o.Y + 26 - j * 8, 16, 6);
                bool on = i < Filled && i < 3;
                if (on) DrawRect(r, col(i));
                if (i == 3) DrawDashedRect(r, Ui.Ink3); else DrawRect(r, Ui.Ink, false, 1f);
            }
    }

    // E3: a plain scale with four stops and a cursor triangle over the current one.
    void DrawScale(Vector2 o, Func<int, Color> col)
    {
        DrawLine(o + new Vector2(6, 26), o + new Vector2(78, 26), Ui.Ink, 1.5f);
        for (int i = 0; i < 4; i++)
        {
            float x = o.X + 12 + i * 20;
            DrawLine(new Vector2(x, o.Y + 19), new Vector2(x, o.Y + 33), i < 3 ? col(i) : Ui.Ink3, i < 3 ? 4f : 2f);
        }
        float cx = o.X + 12 + _pos * 20;
        DrawColoredPolygon(new[] { new Vector2(cx, o.Y + 17), new Vector2(cx - 5, o.Y + 7), new Vector2(cx + 5, o.Y + 7) }, Ui.Ink);
    }

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

    void DrawBefore()
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
