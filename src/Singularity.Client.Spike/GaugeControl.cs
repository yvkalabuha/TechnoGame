using System;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>The game-speed gauge (UI_INSTRUMENTS §3, #404, #409): a layer-2 instrument drawn procedurally, no assets. Three looks by game year: E1 needle dial (1945–69),
/// E2 lit segment block (1970–99), E3 plain scale with a cursor (2000–2100). Level 0 of the setting «Приборы-органы» gives the plain four-cell meter («before»).
/// The value is shown by the needle position, the number of lit cells or the cursor, and by the tempo word beside it, never by colour alone. Not interactive: the keys and tooltips stay with the buttons.</summary>
public sealed partial class GaugeControl : Control
{
    static readonly int[] Ink = { Ui.Teal, Ui.Olive, Ui.Plum, Ui.Sand };
    const float InstrW = 84, InstrH = 44, BeforeW = 46, BeforeH = 28, MoveSeconds = 0.12f;

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

    // E2 fills sit one step below the buttons: saturation cut by 15 %.
    static readonly Color[] E2Fill = BuildE2();
    static Color[] BuildE2()
    {
        var r = new Color[3];
        for (int i = 0; i < 3; i++) { var c = Ui.InkLine[Ink[i]]; r[i] = Color.FromHsv(c.H, c.S * 0.85f, c.V); }
        return r;
    }

    // Weak: ink and grey only. Normal: the tempo inks (teal, olive, plum).
    static Color Col(int level, int i) => level == 2 ? Ui.InkLine[Ink[i]] : Ui.Ink2;

    readonly Vector2[] _tri = new Vector2[3];   // reused by the E3 cursor, no per-frame allocation

    public override void _Draw()
    {
        if (_level == 0) { DrawBefore(); return; }
        var o = new Vector2(0, Size.Y - InstrH);
        if (_era == 1) DrawDial(o); else if (_era == 2) DrawBlock(o); else DrawScale(o);
    }

    // E1: four step ticks (pause, day, week, month) and a dashed reserve tick on a half dial; a 2 px needle with a counterweight and the step number. No arc.
    // DEBT-0059: at 100 % the dial is larger (r 33) and every tick is a full-ink stroke (7:1 on paper); the tempo ink rides on a bead beyond the tick end, so the value never rests on the ink colour.
    static readonly double[] TickDeg = { -60, -30, 0, 30, 60 };
    static readonly Vector2[] TickDir = BuildTickDir();
    static Vector2[] BuildTickDir()
    {
        var r = new Vector2[TickDeg.Length];
        for (int i = 0; i < r.Length; i++) { double a = TickDeg[i] * Math.PI / 180; r[i] = new Vector2((float)Math.Sin(a), -(float)Math.Cos(a)); }
        return r;
    }

    void DrawDial(Vector2 o)
    {
        var c = o + new Vector2(42, InstrH - 3);
        const float r = InstrumentLogic.DialRadius, len = InstrumentLogic.DialTickLength, w = InstrumentLogic.DialTickWidth;
        for (int i = 0; i < 5; i++)
        {
            var dir = TickDir[i];
            if (i == 4) { DrawDashedLine(c + dir * (r - len + 3), c + dir * r, Ui.Ink2, 1.5f, 2f); continue; }
            DrawLine(c + dir * (r - len), c + dir * r, Ui.Ink, w);
            if (i > 0) DrawCircle(c + dir * (r + 4), 3, Col(_level, i - 1));
        }
        double ang = NeedleAngle() * Math.PI / 180; var nd = new Vector2((float)Math.Sin(ang), -(float)Math.Cos(ang));
        DrawLine(c - nd * 6, c + nd * (r - 4), Ui.Ink, 2f);
        DrawCircle(c - nd * 7, 2.5f, Ui.Ink);   // counterweight
        DrawCircle(c, 3, Ui.Ink);
        DrawString(ThemeDB.FallbackFont, c + new Vector2(9, 0), Filled.ToString(), HorizontalAlignment.Left, -1, 12, Ui.Ink);
    }

    double NeedleAngle()
    {
        int i = Math.Clamp((int)Math.Floor(_pos), 0, 2); float t = Math.Clamp(_pos - i, 0f, 1f);
        return InstrumentLogic.NeedleDeg[i] + (InstrumentLogic.NeedleDeg[i + 1] - InstrumentLogic.NeedleDeg[i]) * t;
    }

    // E2: four columns of three lit segments, filled up to the tempo; the reserve column is dashed.
    void DrawBlock(Vector2 o)
    {
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 3; j++)
            {
                var r = new Rect2(o.X + 4 + i * 20, o.Y + 26 - j * 8, 16, 6);
                if (i < Filled && i < 3) DrawRect(r, _level == 2 ? E2Fill[i] : Ui.Ink2);
                if (i == 3) DrawDashedRect(r, Ui.Ink3); else DrawRect(r, Ui.Ink, false, 1f);
            }
    }

    // E3: a plain scale, hairline stops, a 2 px tick on the current step and a small cursor triangle over it.
    void DrawScale(Vector2 o)
    {
        DrawLine(o + new Vector2(6, 26), o + new Vector2(78, 26), Ui.Ink, 1f);
        for (int i = 0; i < 4; i++)
        {
            float x = o.X + 12 + i * 20;
            DrawLine(new Vector2(x, o.Y + 20), new Vector2(x, o.Y + 32), i == 3 ? Ui.Ink3 : Ui.Ink2, 1f);
        }
        float cx = o.X + 12 + _pos * 20;
        DrawLine(new Vector2(cx, o.Y + 18), new Vector2(cx, o.Y + 34), Filled > 0 && Filled < 4 ? Col(_level, Filled - 1) : Ui.Ink, 2f);
        _tri[0] = new Vector2(cx, o.Y + 17); _tri[1] = new Vector2(cx - 3, o.Y + 11); _tri[2] = new Vector2(cx + 3, o.Y + 11);
        DrawColoredPolygon(_tri, Ui.Ink);
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
